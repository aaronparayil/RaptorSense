using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RaptorSense.Automation;

/// <summary>
/// Tells when a different program comes to the front, using a Windows event rather than checking repeatedly. Only the
/// program's name is read, and it is never stored. Install this only while a rule depends on an app.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ForegroundAppWatcher : IDisposable
{
    const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    const int WM_QUIT = 0x0012;

    readonly Thread thread;
    WinEventProc? callback;   // kept in a field so the GC cannot collect the delegate while Windows calls it
    uint threadId;

    /// <summary>Raised on the watcher's own thread with the program's name, without ".exe".</summary>
    public event Action<string>? AppInFront;

    public ForegroundAppWatcher()
    {
        using var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            callback = OnForegroundChanged;
            var hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            ready.Set();
            if (hook == IntPtr.Zero) return;
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            UnhookWinEvent(hook);
        })
        { IsBackground = true, Name = "RaptorSense foreground watcher" };
        thread.Start();
        ready.Wait();
    }

    void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint time)
    {
        GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0) return;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            AppInFront?.Invoke(process.ProcessName);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { }   // it has already closed
    }

    public void Dispose()
    {
        if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
    }

    delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint eventThread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc proc, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
