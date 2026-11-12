using System.Text.Json;
using RaptorSense.Lighting;

namespace RaptorSense.Profiles;

/// <summary>Key animations in %APPDATA%\RaptorSense\animations, one JSON file each.</summary>
public static class AnimationStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultFolder { get; } = Path.Combine(ProfileStore.Root, "animations");

    public static IReadOnlyList<string> List(string? folder = null)
    {
        folder ??= DefaultFolder;
        if (!Directory.Exists(folder)) return [];
        return Directory.EnumerateFiles(folder, "*.json")
            .Select(path => Read(path)?.Name)
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static KeyAnimation? Load(string name, string? folder = null) => Read(PathFor(name, folder));

    public static bool Exists(string name, string? folder = null) => File.Exists(PathFor(name, folder));

    public static void Save(KeyAnimation animation, string? folder = null)
    {
        if (string.IsNullOrWhiteSpace(animation.Name)) throw new ArgumentException("An animation needs a name.");
        var path = PathFor(animation.Name, folder);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write a temporary file first, so a crash mid-save never leaves half an animation behind.
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(animation, Json));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public static void Delete(string name, string? folder = null)
    {
        var path = PathFor(name, folder);
        if (File.Exists(path)) File.Delete(path);
    }

    static string PathFor(string name, string? folder)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return Path.Combine(folder ?? DefaultFolder, safe + ".json");
    }

    static KeyAnimation? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<KeyAnimation>(File.ReadAllText(path), Json) : null; }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) { return null; }
    }
}
