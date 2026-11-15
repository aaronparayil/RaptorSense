using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RaptorSense.App;

/// <summary>
/// Brings a window in front of whatever the user is working in. Windows only lets the app that last received a key
/// or a click do that by itself, and the PREDATOR key reaches RaptorSense as a report from the keyboard, not as a key
/// press: without this the window would open behind the others and only flash in the taskbar.
/// </summary>
[SupportedOSPlatform("windows")]
static class WindowActivation
{
    public static void BringToFront(IntPtr window)
    {
        IntPtr front = GetForegroundWindow();
        if (window == IntPtr.Zero || front == window) return;

        // For the moment the two threads share their input state, Windows treats the request as coming from the app in front.
        uint frontThread = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, out _);
        uint ours = GetCurrentThreadId();
        bool joined = frontThread != 0 && frontThread != ours && AttachThreadInput(ours, frontThread, true);
        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            if (joined) AttachThreadInput(ours, frontThread, false);
        }
    }

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint thread, uint attachTo, bool attach);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
