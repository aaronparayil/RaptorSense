using System.Text.Json;
using System.Text.Json.Serialization;
using RaptorSense.Profiles;

namespace RaptorSense.Automation;

public sealed record AutomationSettings
{
    public bool Enabled { get; init; } = true;

    public IReadOnlyList<AutomationRule> Rules { get; init; } = [];
}

/// <summary>The rules, as readable JSON beside the profiles.</summary>
public static class AutomationStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath { get; } = Path.Combine(ProfileStore.Root, "automations.json");

    /// <summary>The saved settings, or empty ones if there are none or the file can't be read.</summary>
    public static AutomationSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try { return File.Exists(path) ? JsonSerializer.Deserialize<AutomationSettings>(File.ReadAllText(path), Json) ?? new() : new(); }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return new(); }
    }

    public static void Save(AutomationSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Json));
    }
}
