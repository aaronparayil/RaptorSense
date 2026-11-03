using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using RaptorSense.Layout;

namespace RaptorSense.Input;

/// <summary>
/// Low-level keyboard hook on its own thread with a message loop. It reads only the scan code of each key event,
/// turns it into a lighting cell and passes the key on untouched. Install it only while a reactive effect runs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsKeyHook : IKeyPressSource
{
    const int WH_KEYBOARD_LL = 13, WM_QUIT = 0x0012;
    const uint LLKHF_EXTENDED = 0x01, LLKHF_UP = 0x80;

    // AltGr on some layouts sends a fake left Ctrl with this scan code; it would light the wrong key.
    const uint AltGrFakeCtrl = 0x21D;

    readonly Thread thread;
    readonly HashSet<int> held = [];
    LowLevelKeyboardProc? callback;   // kept in a field so the GC cannot collect the delegate while Windows calls it
    IntPtr hook;
    uint threadId;

    public event Action<int>? CellPressed;

    public WindowsKeyHook()
    {
        using var ready = new ManualResetEventSlim();
        int error = 0;
        thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            callback = OnKey;
            hook = SetWindowsHookEx(WH_KEYBOARD_LL, callback, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) error = Marshal.GetLastWin32Error();
            ready.Set();
            if (hook == IntPtr.Zero) return;
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            UnhookWindowsHookEx(hook);
        })
        { IsBackground = true, Name = "RaptorSense key hook" };
        thread.Start();
        ready.Wait();
        if (hook == IntPtr.Zero) throw new Win32Exception(error, "Could not install the keyboard hook");
    }

    IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var key = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(data);
            if (key.scanCode != AltGrFakeCtrl && ScanCodes.CellFor((int)(key.scanCode & 0xFF), (key.flags & LLKHF_EXTENDED) != 0) is int cell)
            {
                if ((key.flags & LLKHF_UP) != 0) held.Remove(cell);
                else if (held.Add(cell)) CellPressed?.Invoke(cell);
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    public void Dispose()
    {
        if (threadId != 0) PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(1000);
    }

    delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr extraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int x, y; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, LowLevelKeyboardProc proc, IntPtr module, uint threadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint threadId, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
}
