using System.Text.Json;

namespace RaptorSense.Profiles;

/// <summary>Profiles in %APPDATA%\RaptorSense\profiles (Windows) or ~/.config/RaptorSense/profiles (Linux).</summary>
public static class ProfileStore
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RaptorSense");

    public static string Folder { get; } = Path.Combine(Root, "profiles");

    static string LastPath => Path.Combine(Root, "last.json");

    static string SwatchesPath => Path.Combine(Root, "swatches.json");

    /// <summary>The user's saved colours, as RRGGBB.</summary>
    public static IReadOnlyList<string> LoadSwatches()
    {
        try { return File.Exists(SwatchesPath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(SwatchesPath)) ?? [] : []; }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return []; }
    }

    public static void SaveSwatches(IEnumerable<string> swatches)
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(SwatchesPath, JsonSerializer.Serialize(swatches.ToList(), Json));
    }

    public static IReadOnlyList<string> List()
    {
        if (!Directory.Exists(Folder)) return [];
        return Directory.EnumerateFiles(Folder, "*.json")
            .Select(path => Read(path)?.Name)
            .OfType<string>()
            .Where(name => name.Length > 0)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static Profile? Load(string name) => Read(PathFor(name));

    public static void Save(Profile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("A profile needs a name.");
        Write(PathFor(profile.Name), profile);
    }

    public static void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>The setup the app had when it last closed.</summary>
    public static Profile? LoadLast() => Read(LastPath);

    public static void SaveLast(Profile profile) => Write(LastPath, profile);

    static string PathFor(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return Path.Combine(Folder, safe + ".json");
    }

    static Profile? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Json) : null; }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    static void Write(string path, Profile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(profile, Json));
    }
}
