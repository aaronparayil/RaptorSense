using System.Runtime.InteropServices;

namespace RaptorSense.Hardware;

/// <summary>
/// Sub-millisecond waits for frame pacing. On Windows this is a high-resolution waitable timer: Windows 11 ignores
/// timeBeginPeriod for processes without a visible window, which made Thread.Sleep round up to ~16 ms.
/// </summary>
public sealed class PreciseTimer : IDisposable
{
    readonly IntPtr handle;

    public PreciseTimer()
    {
        if (OperatingSystem.IsWindows())
            handle = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
    }

    public void Wait(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return;
        if (handle == IntPtr.Zero) { Thread.Sleep(duration); return; }
        long due = -duration.Ticks;   // negative = relative, in 100 ns units
        if (SetWaitableTimer(handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) WaitForSingleObject(handle, INFINITE);
        else Thread.Sleep(duration);
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero) CloseHandle(handle);
    }

    const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x2, TIMER_ALL_ACCESS = 0x1F0003, INFINITE = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);

    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
}
