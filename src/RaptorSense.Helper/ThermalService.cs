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
    IFourZoneKeyboard? keyboard;   // only on a laptop whose 4-zone keyboard has been checked, whatever the fans' state
    FanController? fans;
    IReadOnlyList<PowerProfile> supported = [];
    PowerProfile? knownProfile;
    long profileReadAt;
    string? model;
    string? refusal;   // set on a laptop the helper only reads on

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        using var wmi = AcerGamingWmi.Open();
        model = LaptopModels.ReadModel();
        if (LaptopModels.IsVerified(model))
        {
            firmware = wmi;
            lightBar = wmi;
        }
        else
        {
            // Nothing that writes is handed out: the fan rules, the light bar and every request see only reads.
            refusal = LaptopModels.Refusal(model);
            firmware = new ReadOnlyFirmware(wmi, refusal);
        }
        if (LaptopModels.HasFourZoneKeyboard(model)) keyboard = wmi;
        fans = new FanController(firmware);
        lock (firmwareLock)
        {
            supported = firmware.SupportedProfiles();
            if (refusal is null) fans.RecoverOnStart();
        }
        if (refusal is null)
            log.LogInformation("RaptorSense helper started on {Model}. Profiles: {Profiles}", model, string.Join(", ", supported));
        else
            log.LogWarning("RaptorSense helper started on {Model}, which hasn't been tested: it only reads here.", LaptopModels.Describe(model));

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
            // The keyboard is the one change allowed on a read-only laptop, and only when it has been checked there.
            bool allowed = request.Command == "status" || (request.Command == "keyboard" && keyboard is not null);
            if (refusal is not null && !allowed) return Status(refusal) with { Ok = false };
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
                    case "keyboard" when request.Keyboard is { } zones && keyboard is not null:
                        zones.ApplyTo(keyboard);
                        // The app's effects stream frames several times a second: those aren't worth a log line each.
                        if (zones.Confirm) log.LogInformation("Keyboard set: {Mode}, colours {Colours}, brightness {Brightness}",
                            zones.Mode ?? FourZoneMode.Static, string.Join(" ", zones.Colors), zones.Brightness);
                        // On a read-only laptop the usual error is the refusal, which this change didn't hit.
                        return Status(refusal is null ? fans.LastError : null);
                    case "keyboard" when keyboard is null:
                        return Status("This laptop has no 4-zone keyboard that RaptorSense has been tested on.") with { Ok = false };
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
        Model = model,
        ReadOnly = refusal is not null,
        HelperRevision = ThermalProtocol.HelperRevision,
        FourZoneKeyboard = keyboard is not null,
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
