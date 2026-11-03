using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;

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
    /// <summary>"status", "profile" or "fans".</summary>
    public string Command { get; init; } = "status";

    public PowerProfile? Profile { get; init; }

    public FanMode? Mode { get; init; }

    public int? Cpu { get; init; }

    public int? Gpu { get; init; }
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
        "The RaptorSense helper isn't running. Fans and power modes need it (see README: Fans and power modes).";
}
