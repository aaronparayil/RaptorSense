using System.ComponentModel;
using System.Diagnostics;

namespace RaptorSense.App;

/// <summary>
/// Installs or updates the fan helper from the app, the same way <c>Install fan control.cmd</c> does: it runs the
/// helper's own install command, and Windows asks the person for administrator rights first.
/// </summary>
static class HelperSetup
{
    const string ExeName = "RaptorSense.Helper.exe";
    const int CancelledByUser = 1223;

    /// <summary>The helper next to the app (the download's <c>helper</c> folder, or a source build's), or null.</summary>
    public static string? Find()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "helper", ExeName);
        if (File.Exists(packaged)) return packaged;

        // A source build: src\RaptorSense.App\bin\<config>\<framework>\ next to src\RaptorSense.Helper\bin\<config>\net10.0-windows\.
        var framework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var config = framework.Parent;
        var src = config?.Parent?.Parent?.Parent;
        if (config is null || src is null) return null;
        var built = Path.Combine(src.FullName, "RaptorSense.Helper", "bin", config.Name, "net10.0-windows", ExeName);
        return File.Exists(built) ? built : null;
    }

    /// <summary>Runs the install and waits for it. Returns what to tell the person.</summary>
    public static string Install(string helper)
    {
        try
        {
            using var install = Process.Start(new ProcessStartInfo(helper, "install")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
            if (install is null) return "Windows didn't start the install.";
            install.WaitForExit();
            return install.ExitCode == 0
                ? "Fan control is installed."
                : $"The install didn't finish (code {install.ExitCode}). Try Install fan control.cmd in the RaptorSense folder, which shows what went wrong.";
        }
        catch (Win32Exception e) when (e.NativeErrorCode == CancelledByUser)
        {
            return "Cancelled: fan control needs administrator rights once, to install.";
        }
        catch (Win32Exception e)
        {
            return $"Couldn't start the install: {e.Message}";
        }
    }
}
