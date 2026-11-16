using Microsoft.Win32;
using RaptorSense.Hardware;

namespace RaptorSense.App;

/// <summary>
/// Starting hidden when the person signs in. On Windows through the per-user Run key, on Linux through a file in the
/// desktop's autostart folder (<see cref="DesktopAutostart"/>): no administrator rights either way, and removed
/// cleanly when turned off.
/// </summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "RaptorSense";

    public const string MinimizedArgument = "--minimized";

    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>What the switch says while it is off, and while it is on.</summary>
    public static string OffLabel => OperatingSystem.IsWindows() ? "Start with Windows" : "Start when you sign in";
    public static string OnLabel => OperatingSystem.IsWindows() ? "Starts with Windows" : "Starts when you sign in";

    public static bool IsEnabled
    {
        get
        {
            if (OperatingSystem.IsLinux()) return DesktopAutostart.IsEnabled(DesktopAutostart.DefaultFolder, ExePath);
            if (!OperatingSystem.IsWindows()) return false;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command && command.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            if (OperatingSystem.IsLinux())
            {
                if (value) DesktopAutostart.Enable(DesktopAutostart.DefaultFolder, ExePath, MinimizedArgument);
                else DesktopAutostart.Disable(DesktopAutostart.DefaultFolder);
                return;
            }
            if (!OperatingSystem.IsWindows()) return;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{ExePath}\" {MinimizedArgument}");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
}
