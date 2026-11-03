using System.Diagnostics;
using System.Security.Principal;

namespace RaptorSense.Helper;

/// <summary>Installs the helper as an automatic Windows service, using sc.exe.</summary>
static class ServiceInstaller
{
    public const string ServiceName = "RaptorSenseHelper";
    const string DisplayName = "RaptorSense Helper";
    const string Description = "Fans and power modes for RaptorSense. Returns the fans to Auto when stopped.";

    public static int Install()
    {
        if (!IsAdministrator()) return Fail("Run this from an administrator terminal.");
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        Sc("stop", ServiceName);   // reinstalling over an old copy: ignore errors
        Sc("delete", ServiceName);
        if (Sc("create", ServiceName, "binPath=", $"\"{exe}\"", "start=", "auto", "DisplayName=", DisplayName) != 0)
            return Fail("Could not create the service.");
        Sc("description", ServiceName, Description);
        Sc("failure", ServiceName, "reset=", "86400", "actions=", "restart/5000/restart/30000//");   // restart after a crash
        if (Sc("start", ServiceName) != 0) return Fail("Installed, but the service did not start. See Event Viewer > Application.");
        Console.WriteLine($"{DisplayName} is installed and running.");
        return 0;
    }

    public static int Uninstall()
    {
        if (!IsAdministrator()) return Fail("Run this from an administrator terminal.");
        Sc("stop", ServiceName);
        if (Sc("delete", ServiceName) != 0) return Fail("Could not remove the service (is it installed?).");
        Console.WriteLine($"{DisplayName} is removed. Stopping it put the fans back on Auto.");
        return 0;
    }

    static int Sc(params string[] args)
    {
        var start = new ProcessStartInfo("sc.exe") { UseShellExecute = false, RedirectStandardOutput = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
