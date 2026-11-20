using System.Runtime.Versioning;
using Microsoft.Win32;
using RaptorSense.Profiles;

namespace RaptorSense.App;

/// <summary>
/// The website's "Add to RaptorSense" button opens raptorsense://add-animation/&lt;name&gt;. Windows hands such a link
/// to the program registered for it, which is this app, with the link after <see cref="Argument"/>.
/// </summary>
/// <remarks>
/// The registration is per user (HKCU\Software\Classes\raptorsense), needs no administrator rights, and is written
/// again when the app moves. A copy that is already running gets the request through a small file in the settings
/// folder, holding only the animation's plain name, and the same signal a second launch already sends.
/// </remarks>
static class AnimationLinks
{
    public const string Argument = "--add-animation";

    const string ClassesKey = @"Software\Classes\" + SharedAnimations.Scheme;

    static string Inbox => Path.Combine(ProfileStore.Root, "incoming");

    /// <summary>The animation the command line asks for, or null: the link after <see cref="Argument"/>, or a bare link.</summary>
    public static string? FromArguments(string[] args)
    {
        int at = Array.IndexOf(args, Argument);
        if (at >= 0 && at + 1 < args.Length) return SharedAnimations.SlugFromLink(args[at + 1]);
        return args.Select(SharedAnimations.SlugFromLink).FirstOrDefault(slug => slug is not null);
    }

    /// <summary>Makes raptorsense:// links open this copy of the app. Does nothing when that is already so.</summary>
    public static void Register()
    {
        if (!OperatingSystem.IsWindows() || Environment.ProcessPath is not { } exe) return;
        try { RegisterOnWindows(exe); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException) { }
    }

    [SupportedOSPlatform("windows")]
    static void RegisterOnWindows(string exe)
    {
        string command = $"\"{exe}\" {Argument} \"%1\"";
        using (var existing = Registry.CurrentUser.OpenSubKey(ClassesKey + @"\shell\open\command"))
        {
            if (existing?.GetValue(null) as string == command) return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(ClassesKey);
        key.SetValue(null, "URL:RaptorSense animation");
        key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon")) icon.SetValue(null, $"\"{exe}\",0");
        using var open = key.CreateSubKey(@"shell\open\command");
        open.SetValue(null, command);
    }

    /// <summary>Leaves a request for the copy that is already running. Only a checked plain name is written.</summary>
    public static void Leave(string slug)
    {
        if (!SharedAnimations.IsSlug(slug)) return;
        try
        {
            Directory.CreateDirectory(Inbox);
            File.WriteAllText(Path.Combine(Inbox, $"{DateTime.UtcNow.Ticks}-{Guid.NewGuid():N}.txt"), slug);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>The requests left since the last look, oldest first, each taken away as it is read.</summary>
    public static List<string> Take()
    {
        var slugs = new List<string>();
        try
        {
            if (!Directory.Exists(Inbox)) return slugs;
            foreach (var file in Directory.EnumerateFiles(Inbox, "*.txt").Order(StringComparer.Ordinal))
            {
                try
                {
                    var info = new FileInfo(file);
                    var text = info.Length <= 200 ? File.ReadAllText(file).Trim() : "";
                    File.Delete(file);
                    if (SharedAnimations.IsSlug(text) && !slugs.Contains(text)) slugs.Add(text);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return slugs;
    }
}
