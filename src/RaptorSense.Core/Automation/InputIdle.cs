using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RaptorSense.Automation;

/// <summary>How long it has been since a key, the mouse or the touchpad was last used. Windows keeps that time; nothing is hooked.</summary>
[SupportedOSPlatform("windows")]
public static class InputIdle
{
    /// <summary>Zero if Windows can't say.</summary>
    public static TimeSpan Time
    {
        get
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            // Both are milliseconds since Windows started, as 32-bit counters, so the subtraction survives the counter wrapping.
            return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime)) : TimeSpan.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize, dwTime; }

    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
}

/// <summary>When to look at the idle time next, so that an idle rule costs next to nothing while the laptop is in use.</summary>
public static class IdleSchedule
{
    /// <summary>While a rule is active, how often to look for the user coming back.</summary>
    public static readonly TimeSpan WhileIdle = TimeSpan.FromMilliseconds(250);

    static readonly TimeSpan Margin = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// With no rule active yet, the earliest moment one could become active if nothing is touched from now on: there
    /// is no point looking before then. <paramref name="limits"/> are the idle times the rules wait for.
    /// </summary>
    public static TimeSpan NextCheck(TimeSpan idle, IReadOnlyCollection<TimeSpan> limits)
    {
        if (limits.Count == 0 || limits.Any(limit => idle >= limit)) return WhileIdle;
        var wait = limits.Min() - idle + Margin;
        return wait > WhileIdle ? wait : WhileIdle;
    }
}
