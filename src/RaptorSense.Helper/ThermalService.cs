using System.IO.Pipes;
using System.Management;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using RaptorSense.Hardware;
using RaptorSense.Thermal;

namespace RaptorSense.Helper;

/// <summary>
/// Owns the gaming firmware: checks the fan safety rules every second and answers the app and CLI over a
/// named pipe. Only the fixed commands in <see cref="ThermalRequest"/> are accepted.
/// </summary>
sealed class ThermalService(ILogger<ThermalService> log) : BackgroundService
{
    const int MaxRequestChars = 4096;
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    // Asking the firmware for the power profile stalls the whole machine for about 6 ms (measured on a PH16-71).
    // The app asks for a status every second while its Performance tab shows, so replies reuse the answer this long.
    static readonly TimeSpan ProfileMaxAge = TimeSpan.FromSeconds(5);

    readonly object firmwareLock = new();
    IGamingFirmware? firmware;
    ILightBar? lightBar;
    FanController? fans;
    IReadOnlyList<PowerProfile> supported = [];
    PowerProfile? knownProfile;
    long profileReadAt;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var wmi = AcerGamingWmi.Open();
        firmware = wmi;
        lightBar = wmi;
        fans = new FanController(wmi);
        lock (firmwareLock)
        {
            supported = wmi.SupportedProfiles();
            fans.RecoverOnStart();
        }
        log.LogInformation("RaptorSense helper started. Profiles: {Profiles}", string.Join(", ", supported));

        var pipe = ServePipeAsync(stop);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                try { lock (firmwareLock) fans.Tick(); }
                catch (Exception e) { log.LogWarning("Fan check failed: {Error}", e.Message); }
                await Task.Delay(TimeSpan.FromSeconds(1), stop);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lock (firmwareLock) fans.Release();   // fans back to Auto when the helper stops
            log.LogInformation("RaptorSense helper stopped; fans are on Auto.");
        }
        await pipe;
    }

    async Task ServePipeAsync(CancellationToken stop)
    {
        // FirstPipeInstance on the first one: if another program already owns the name, fail instead of sharing it.
        var options = PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance;
        while (!stop.IsCancellationRequested)
        {
            NamedPipeServerStream server;
            try
            {
                server = NamedPipeServerStreamAcl.Create(ThermalProtocol.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options, 0, 0, PipeAccess());
                options &= ~PipeOptions.FirstPipeInstance;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.LogError("Could not create the pipe '{Pipe}': {Error}", ThermalProtocol.PipeName, e.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stop).ContinueWith(_ => { });
                continue;
            }

            try { await server.WaitForConnectionAsync(stop); }
            catch (OperationCanceledException) { await server.DisposeAsync(); break; }
            _ = HandleConnectionAsync(server, stop);
        }
    }

    async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        await using (pipe)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(RequestTimeout);
                using var reader = new StreamReader(pipe);
                await using var writer = new StreamWriter(pipe) { AutoFlush = true };
                var line = await reader.ReadLineAsync(timeout.Token);
                var reply = line is null || line.Length > MaxRequestChars
                    ? ThermalStatus.Failed("Bad request.")
                    : Handle(JsonSerializer.Deserialize<ThermalRequest>(line, ThermalProtocol.Json));
                await writer.WriteLineAsync(JsonSerializer.Serialize(reply, ThermalProtocol.Json));
            }
            catch (Exception e) when (e is IOException or JsonException or OperationCanceledException)
            {
                log.LogDebug("Pipe request dropped: {Error}", e.Message);
            }
        }
    }

    ThermalStatus Handle(ThermalRequest? request)
    {
        if (request is null || firmware is null || fans is null) return ThermalStatus.Failed("Bad request.");
        lock (firmwareLock)
        {
            try
            {
                switch (request.Command)
                {
                    case "status":
                        break;
                    case "profile" when request.Profile is { } profile:
                        firmware.SetProfile(profile);
                        knownProfile = null;   // read it back for the reply
                        log.LogInformation("Power profile set to {Profile}", profile);
                        break;
                    case "fans" when request.Mode is FanMode.Auto:
                        fans.SetAuto();
                        break;
                    case "fans" when request.Mode is FanMode.Max:
                        fans.SetMax();
                        break;
                    case "fans" when request.Mode is FanMode.Custom:
                        fans.SetCustom(request.Cpu ?? 50, request.Gpu ?? 50);
                        log.LogInformation("Fans set to custom {Cpu}% / {Gpu}%", fans.CpuPercent, fans.GpuPercent);
                        break;
                    case "lightbar" when request.LightBar is { } bar && lightBar is not null:
                        bar.ApplyTo(lightBar);
                        log.LogInformation("Light bar set: {Mode}, colours {Colours}, brightness {Brightness}",
                            bar.Mode?.ToString() ?? "static", string.Join(" ", bar.Colors), bar.Brightness);
                        break;
                    default:
                        return ThermalStatus.Failed($"Unknown request '{request.Command}'.");
                }
                return Status(fans.LastError);
            }
            catch (Exception e) when (e is FirmwareException or ManagementException or ArgumentException)
            {
                log.LogWarning("Request {Command} failed: {Error}", request.Command, e.Message);
                return Status(e.Message) with { Ok = false };
            }
        }
    }

    ThermalStatus Status(string? error) => new()
    {
        Ok = true,
        Error = error,
        Reading = fans!.LastReading ?? Try(firmware!.ReadSensors),
        Profile = CurrentProfile(),
        SupportedProfiles = supported,
        FanMode = fans.RequestedMode,
        CpuPercent = fans.CpuPercent,
        GpuPercent = fans.GpuPercent,
        SafetyOverride = fans.SafetyOverride,
    };

    // Called with the firmware lock held. A failed read is not kept, so the next reply tries again.
    PowerProfile? CurrentProfile()
    {
        if (knownProfile is null || Environment.TickCount64 - profileReadAt >= ProfileMaxAge.TotalMilliseconds)
        {
            knownProfile = Try(firmware!.GetProfile);
            profileReadAt = Environment.TickCount64;
        }
        return knownProfile;
    }

    // The firmware fails reads for a moment after a change; the status reply shouldn't fail with it.
    static T? Try<T>(Func<T> read) where T : struct
    {
        try { return read(); }
        catch (FirmwareException) { return null; }
    }

    // Signed-in users may connect (the app runs without elevation); only the helper itself can create instances.
    static PipeSecurity PipeAccess()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
