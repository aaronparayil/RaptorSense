using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;

namespace RaptorSense.Helper;

/// <summary>
/// Installs the helper as an automatic Windows service. The service runs as SYSTEM, so its files are copied to
/// Program Files first: if it ran from a folder the user can write to, any program could replace it and gain SYSTEM.
/// </summary>
static class ServiceInstaller
{
    public const string ServiceName = "RaptorSenseHelper";
    const string DisplayName = "RaptorSense Helper";
    const string Description = "Fans and power modes for RaptorSense. Returns the fans to Auto when stopped.";
    const string ExeName = "RaptorSense.Helper.exe";

    static readonly string InstallDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RaptorSense", "Helper");

    public static int Install()
    {
        if (!IsAdministrator()) return Fail("Run this from an administrator terminal.");
        try
        {
            StopAndDelete();   // reinstalling over an old copy
            CopyFiles();

            var exe = Path.Combine(InstallDir, ExeName);
            if (!CreateService(exe)) return Fail("Could not create the service.");
            Sc("description", ServiceName, Description);
            Sc("failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/30000//");   // restart after a crash
            if (Sc("start", ServiceName) != 0) return Fail("Installed, but the service did not start. See Event Viewer > Application.");
            Console.WriteLine($"{DisplayName} is installed in {InstallDir} and running.");
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            return Fail($"Install failed: {e.Message}");
        }
    }

    public static int Uninstall()
    {
        if (!IsAdministrator()) return Fail("Run this from an administrator terminal.");
        try
        {
            if (!StopAndDelete()) Console.WriteLine($"{DisplayName} was not installed.");
            else Console.WriteLine($"{DisplayName} is removed. Stopping it put the fans back on Auto.");

            if (IsInstallDir(AppContext.BaseDirectory))
            {
                Console.WriteLine($"Delete {InstallDir} yourself: this program is running from it.");
                return 0;
            }
            if (Directory.Exists(InstallDir)) Retry(() => Directory.Delete(InstallDir, recursive: true));
            var parent = Path.GetDirectoryName(InstallDir)!;
            if (Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()) Directory.Delete(parent);
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.ServiceProcess.TimeoutException)
        {
            return Fail($"Uninstall failed: {e.Message}");
        }
    }

    // Waits for the stop: until then the files are locked and the service name can't be reused.
    static bool StopAndDelete()
    {
        var service = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName.Equals(ServiceName, StringComparison.OrdinalIgnoreCase));
        if (service is null) return false;
        using (service)
        {
            if (service.Status == ServiceControllerStatus.Running) service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        }
        return Sc("delete", ServiceName) == 0;
    }

    static void CopyFiles()
    {
        var source = AppContext.BaseDirectory;
        if (IsInstallDir(source)) return;   // reinstalling from the installed copy
        if (Directory.Exists(InstallDir)) Retry(() => Directory.Delete(InstallDir, recursive: true));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(InstallDir, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    static bool IsInstallDir(string directory) =>
        Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).Equals(InstallDir, StringComparison.OrdinalIgnoreCase);

    // A just-deleted service lingers for a moment ("marked for deletion"), so try a few times.
    static bool CreateService(string exe)
    {
        const int MarkedForDeletion = 1072;
        for (int attempt = 0; ; attempt++)
        {
            int code = Sc("create", ServiceName, "binPath=", $"\"{exe}\"", "start=", "auto", "DisplayName=", DisplayName);
            if (code == 0) return true;
            if (code != MarkedForDeletion || attempt == 9) return false;
            Thread.Sleep(1000);
        }
    }

    // A stopped service's process can hold its files a moment longer.
    static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 9) { Thread.Sleep(500); }
        }
    }

    static int Sc(params string[] args)
    {
        var start = new ProcessStartInfo("sc.exe") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) Console.Error.WriteLine(output.Trim());
        return process.ExitCode;
    }

    static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
