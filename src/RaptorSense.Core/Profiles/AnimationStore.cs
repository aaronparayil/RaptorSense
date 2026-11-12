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

    /// <summary><paramref name="stem"/> if no animation has that name yet, otherwise "stem 2", "stem 3" and so on.</summary>
    public static string UniqueName(string stem, string? folder = null)
    {
        stem = stem.Trim();
        if (!Exists(stem, folder)) return stem;
        for (int i = 2; ; i++)
        {
            if (!Exists($"{stem} {i}", folder)) return $"{stem} {i}";
        }
    }

    /// <summary>Writes one animation to a file to share. Only colours and times go in it.</summary>
    public static void Export(KeyAnimation animation, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(animation.Cleaned(), Json));

    /// <summary>
    /// Adds an animation file someone shared. It is read as data and cleaned, and never replaces an animation that is
    /// already here: a clashing name gets a number. Throws <see cref="InvalidDataException"/>, with a message fit to
    /// show, if the file isn't an animation.
    /// </summary>
    public static KeyAnimation Import(string path, string? folder = null)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new InvalidDataException("That file isn't there any more.");
        if (file.Length > MaxImportBytes) throw new InvalidDataException("That file is too big to be a RaptorSense animation.");

        KeyAnimation? animation;
        try { animation = JsonSerializer.Deserialize<KeyAnimation>(File.ReadAllText(path), Json); }
        catch (Exception e) when (e is JsonException or NotSupportedException) { animation = null; }
        if (animation?.Frames is not { Count: > 0 } || animation.Frames.All(f => f is null))
            throw new InvalidDataException("That file isn't a RaptorSense animation.");

        var name = CleanName(animation.Name);
        if (name.Length == 0) name = CleanName(Path.GetFileNameWithoutExtension(path));
        if (name.Length == 0) name = "Animation";
        var cleaned = animation.Cleaned() with { Name = UniqueName(name, folder) };
        Save(cleaned, folder);
        return cleaned;
    }

    /// <summary>The largest file <see cref="Import"/> reads. 500 full frames come to well under 2 MB.</summary>
    public const long MaxImportBytes = 5 * 1024 * 1024;

    const int MaxNameLength = 60;

    static string CleanName(string? name)
    {
        var clean = new string((name ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        return clean.Length > MaxNameLength ? clean[..MaxNameLength].Trim() : clean;
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
