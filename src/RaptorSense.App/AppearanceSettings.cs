using System.Text.Json;
using RaptorSense.Profiles;

namespace RaptorSense.App;

/// <summary>
/// How the window looks and is drawn, kept in %APPDATA%\RaptorSense\appearance.json as plain name/value pairs.
/// Nothing from Avalonia is used here, because the drawing choice is read before Avalonia starts.
/// </summary>
static class AppearanceSettings
{
    const string RenderingKey = "Rendering", GraphicsCard = "gpu";

    static string FilePath => Path.Combine(ProfileStore.Root, "appearance.json");

    /// <summary>
    /// True to draw the window with the graphics card. Off unless the user asks: that way of drawing loads the
    /// graphics driver and a Direct3D device into the app, about 100 MB that stays for as long as it runs, tray
    /// included. Read once at start; a change applies the next time the app starts.
    /// </summary>
    public static bool UseGraphicsCard
    {
        get => Get(RenderingKey) == GraphicsCard;
        set => Set(RenderingKey, value ? GraphicsCard : null);
    }

    /// <summary>
    /// The language picked in the app, such as "de", or null to follow the system's. Read once at start; a change
    /// applies the next time the app starts.
    /// </summary>
    public static string? Language
    {
        get => Get(LanguageKey);
        set => Set(LanguageKey, value);
    }

    const string LanguageKey = "Language";

    public static string? Get(string key) => Read().GetValueOrDefault(key);

    /// <summary>Saves one value and keeps the others. Null removes it.</summary>
    public static void Set(string key, string? value)
    {
        var all = Read();
        if (value is null) all.Remove(key);
        else all[key] = value;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static Dictionary<string, string> Read()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        return [];
    }
}
