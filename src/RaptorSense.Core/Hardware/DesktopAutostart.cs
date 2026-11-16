namespace RaptorSense.Hardware;

/// <summary>
/// "Start when you sign in" on Linux desktops: a <c>.desktop</c> file in the user's autostart folder, which GNOME,
/// KDE and the others read. No special rights, and deleting the file undoes it.
/// </summary>
public static class DesktopAutostart
{
    const string FileName = "raptorsense.desktop";

    /// <summary><c>$XDG_CONFIG_HOME/autostart</c>, or <c>~/.config/autostart</c> when that isn't set.</summary>
    public static string DefaultFolder
    {
        get
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(config))
                config = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            return Path.Combine(config, "autostart");
        }
    }

    /// <summary>True when the file is there and starts this program (not a copy that has since moved).</summary>
    public static bool IsEnabled(string folder, string exePath)
    {
        var file = Path.Combine(folder, FileName);
        string start = ExecLine(exePath, null);
        return File.Exists(file) && File.ReadLines(file).Any(line => line == start || line.StartsWith(start + " ", StringComparison.Ordinal));
    }

    public static void Enable(string folder, string exePath, string argument)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, FileName), string.Join('\n',
            "[Desktop Entry]",
            "Type=Application",
            "Name=RaptorSense",
            "Comment=Keyboard lighting for Acer Predator and Nitro laptops",
            ExecLine(exePath, argument),
            "Terminal=false",
            "X-GNOME-Autostart-enabled=true",
            ""));
    }

    public static void Disable(string folder)
    {
        var file = Path.Combine(folder, FileName);
        if (File.Exists(file)) File.Delete(file);
    }

    // The path goes in double quotes, with the four characters the desktop-entry rules name escaped; % is doubled so
    // it isn't read as a field code.
    static string ExecLine(string exePath, string? argument)
    {
        var quoted = exePath.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%");
        return $"Exec=\"{quoted}\"" + (argument is null ? "" : " " + argument);
    }
}
