using Microsoft.Win32;

namespace RaptorSense.App;

/// <summary>"Start with Windows" through the per-user Run key: no administrator rights, removed cleanly when turned off.</summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "RaptorSense";

    public const string MinimizedArgument = "--minimized";

    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>What the switch says while it is off, and while it is on (on Linux it starts at sign-in).</summary>
    public static string OffLabel => OperatingSystem.IsWindows() ? L("Start with Windows") : L("Start when you sign in");
    public static string OnLabel => OperatingSystem.IsWindows() ? L("Starts with Windows") : L("Starts when you sign in");

    public static bool IsEnabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string command && command.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
        set
        {
            if (!OperatingSystem.IsWindows()) return;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(ValueName, $"\"{ExePath}\" {MinimizedArgument}");
            else key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
}
