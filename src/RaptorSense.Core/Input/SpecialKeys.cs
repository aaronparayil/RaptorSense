using System.Runtime.Versioning;
using RaptorSense.Hardware;

namespace RaptorSense.Input;

/// <summary>Keys the keyboard reports on a collection of its own, outside normal typing.</summary>
public enum SpecialKey { None, Predator, BrightnessDown, BrightnessUp }

/// <summary>
/// The reports of the keyboard's special-key collection (usage page 0x88, three bytes, report id 4). Confirmed on a
/// PH16-71 (2026-11-15): pressed, the PREDATOR key sends <c>04 81 FF</c>, Fn+F11 (dimmer) <c>04 7D FF</c> and Fn+F12
/// (brighter) <c>04 7C FF</c>; letting go of any of them sends <c>04 00 00</c>.
/// </summary>
public static class SpecialKeyReports
{
    public const ushort UsagePage = 0x0088;

    const byte ReportId = 4, Pressed = 0xFF;

    /// <summary>The key this report says was just pressed, or <see cref="SpecialKey.None"/> for a release or anything else.</summary>
    public static SpecialKey Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < 3 || report[0] != ReportId || report[2] != Pressed) return SpecialKey.None;
        return report[1] switch
        {
            0x81 => SpecialKey.Predator,
            0x7D => SpecialKey.BrightnessDown,
            0x7C => SpecialKey.BrightnessUp,
            _ => SpecialKey.None,
        };
    }
}

/// <summary>
/// The brightness levels the keyboard's own Fn+F11 and Fn+F12 move between (seen on a PH16-71). The keyboard steps
/// its level by itself when those keys are pressed, but every frame RaptorSense sends carries a brightness and puts
/// it straight back. So RaptorSense takes the same step, and the two agree.
/// </summary>
public static class BrightnessSteps
{
    public static readonly IReadOnlyList<int> Levels = [0, 12, 25, 37, 50];

    /// <summary>The next level above or below <paramref name="current"/>, which need not be one of the levels itself.</summary>
    public static int Next(int current, bool brighter)
    {
        if (brighter)
        {
            foreach (int level in Levels) if (level > current) return level;
            return Levels[^1];
        }
        for (int i = Levels.Count - 1; i >= 0; i--) if (Levels[i] < current) return Levels[i];
        return Levels[0];
    }
}

/// <summary>
/// Waits for the keyboard's special keys on a thread of its own. It costs nothing while no such key is pressed, sees
/// no typing (that travels on other collections, which Windows keeps to itself) and needs no administrator rights.
/// If the keyboard goes away, it looks for it again every few seconds.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SpecialKeyWatcher : IDisposable
{
    static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(5);

    readonly Thread thread;
    readonly ManualResetEventSlim stop = new();
    readonly object readerLock = new();
    WindowsHidTransport.InputReader? reader;

    /// <summary>Raised on the watcher's own thread.</summary>
    public event Action<SpecialKey>? Pressed;

    public SpecialKeyWatcher()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "RaptorSense special keys" };
        thread.Start();
    }

    void Run()
    {
        while (!stop.IsSet)
        {
            WindowsHidTransport.InputReader? opened = null;
            try { opened = WindowsHidTransport.OpenInput(Ph16Keyboard.VendorId, Ph16Keyboard.ProductIds, SpecialKeyReports.UsagePage); }
            catch (System.ComponentModel.Win32Exception) { }
            if (opened is null)
            {
                stop.Wait(RetryAfter);
                continue;
            }

            lock (readerLock) reader = opened;
            while (!stop.IsSet)
            {
                var report = opened.Read();
                if (report.IsEmpty) break;   // cancelled, or the keyboard went away
                if (SpecialKeyReports.Parse(report) is var key and not SpecialKey.None) Pressed?.Invoke(key);
            }
            lock (readerLock) reader = null;
            opened.Dispose();
            stop.Wait(RetryAfter);
        }
    }

    public void Dispose()
    {
        stop.Set();
        // The thread is usually waiting inside Read; cancel until it has noticed, in case it was just about to start waiting.
        for (int attempt = 0; attempt < 20 && !thread.Join(50); attempt++)
        {
            lock (readerLock) reader?.Cancel();
        }
    }
}
