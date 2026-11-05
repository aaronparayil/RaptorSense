using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using RaptorSense.Hardware;

namespace RaptorSense.Thermal;

/// <summary>
/// The app and CLI talk to the elevated helper over a local named pipe, one JSON object per line.
/// The helper accepts only these commands; it never runs arbitrary firmware calls.
/// </summary>
public static class ThermalProtocol
{
    public const string PipeName = "RaptorSense.Thermal";

    public static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record ThermalRequest
{
    /// <summary>"status", "profile", "fans" or "lightbar".</summary>
    public string Command { get; init; } = "status";

    public PowerProfile? Profile { get; init; }

    public FanMode? Mode { get; init; }

    public int? Cpu { get; init; }

    public int? Gpu { get; init; }

    public LightBarRequest? LightBar { get; init; }
}

/// <summary>
/// A light bar change. <see cref="Colors"/> are RRGGBB strings: one for the whole bar, or three (left, centre, right).
/// With a <see cref="Mode"/> the bar plays that animation in the first colour; without one the colours are static.
/// </summary>
public sealed record LightBarRequest
{
    public IReadOnlyList<string> Colors { get; init; } = [];

    public int Brightness { get; init; } = LightBarProtocol.MaxBrightness;

    public LightBarMode? Mode { get; init; }

    public int Speed { get; init; } = LightBarProtocol.DefaultSpeed;

    /// <summary>Checks the request and sends it. Throws <see cref="ArgumentException"/> for anything the helper must not pass on.</summary>
    public void ApplyTo(ILightBar bar)
    {
        var zones = ParseZones();
        if (Mode is { } mode && !Enum.IsDefined(mode)) throw new ArgumentException($"Unknown light bar mode {(int)mode}.");
        int brightness = Math.Clamp(Brightness, 0, LightBarProtocol.MaxBrightness);

        if (Mode is { } animation) bar.SetMode(animation, zones[0], Speed, brightness);
        else bar.SetStatic(zones, brightness);
    }

    /// <summary>
    /// The colours as one per zone (a single colour fills the bar), or false if the request is malformed.
    /// For settings read from a profile file, which people edit by hand.
    /// </summary>
    public bool TryGetZones(out Lighting.Rgb[] zones)
    {
        try
        {
            zones = ParseZones();
            return Mode is not { } mode || Enum.IsDefined(mode);
        }
        catch (ArgumentException)
        {
            zones = [];
            return false;
        }
    }

    Lighting.Rgb[] ParseZones()
    {
        if (Colors.Count is not (1 or 3)) throw new ArgumentException("Give one colour for the whole bar or three (left, centre, right).");
        var zones = new Lighting.Rgb[LightBarProtocol.Zones];
        for (int zone = 0; zone < zones.Length; zone++)
        {
            string text = Colors[Colors.Count == 1 ? 0 : zone];
            if (!Lighting.Rgb.TryParse(text, out zones[zone])) throw new ArgumentException($"Colour must be RRGGBB, got '{text}'.");
        }
        return zones;
    }
}

public sealed record ThermalStatus
{
    public bool Ok { get; init; }

    public string? Error { get; init; }

    public ThermalReading? Reading { get; init; }

    public PowerProfile? Profile { get; init; }

    public IReadOnlyList<PowerProfile> SupportedProfiles { get; init; } = [];

    public FanMode FanMode { get; init; }

    public int CpuPercent { get; init; }

    public int GpuPercent { get; init; }

    public bool SafetyOverride { get; init; }

    public static ThermalStatus Failed(string error) => new() { Ok = false, Error = error };
}

/// <summary>Client side of the pipe. Each call opens a short connection, so a restarted helper is picked up.</summary>
public static class ThermalClient
{
    public static Task<ThermalStatus> StatusAsync(CancellationToken cancel = default) => SendAsync(new ThermalRequest(), cancel);

    public static Task<ThermalStatus> SetProfileAsync(PowerProfile profile, CancellationToken cancel = default) =>
        SendAsync(new ThermalRequest { Command = "profile", Profile = profile }, cancel);

    public static Task<ThermalStatus> SetFansAsync(FanMode mode, int cpu = 50, int gpu = 50, CancellationToken cancel = default) =>
        SendAsync(new ThermalRequest { Command = "fans", Mode = mode, Cpu = cpu, Gpu = gpu }, cancel);

    public static Task<ThermalStatus> SetLightBarAsync(LightBarRequest request, CancellationToken cancel = default) =>
        SendAsync(new ThermalRequest { Command = "lightbar", LightBar = request }, cancel);

    public static async Task<ThermalStatus> SendAsync(ThermalRequest request, CancellationToken cancel = default)
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", ThermalProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(500, cancel);
            using var reader = new StreamReader(pipe);
            await using var writer = new StreamWriter(pipe) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request, ThermalProtocol.Json));
            var line = await reader.ReadLineAsync(cancel);
            return line is null
                ? ThermalStatus.Failed("The helper closed the connection.")
                : JsonSerializer.Deserialize<ThermalStatus>(line, ThermalProtocol.Json) ?? ThermalStatus.Failed("Empty reply.");
        }
        catch (TimeoutException)
        {
            return ThermalStatus.Failed(HelperMissing);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return ThermalStatus.Failed($"Could not talk to the helper: {e.Message}");
        }
    }

    public const string HelperMissing =
        "The RaptorSense helper isn't running. Fans, power modes and the light bar need it (see README: Fans and power modes).";
}
