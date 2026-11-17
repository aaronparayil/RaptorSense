using RaptorSense.Layout;
using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// Handy: layouts that do a job. They light chosen keys by name (KeyMap labels).

static class KeyLabels
{
    public static int[] Cells(params string[] labels) =>
        labels.Select(KeyMap.Find).Where(k => k is not null).Select(k => k!.Cell).ToArray();

    public static HashSet<int> Set(params string[] labels) => [.. Cells(labels)];
}

/// <summary>
/// The time on the keys: F1 to F12 count the hour, the number row the minutes in fives, and a light steps along the
/// Q row with the seconds. Esc glows in the afternoon and evening.
/// </summary>
public sealed class ClockEffect : IEffect
{
    static readonly int[] Hours = KeyLabels.Cells("F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12");
    static readonly int[] Minutes = KeyLabels.Cells("1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=");
    static readonly int[] Seconds = KeyLabels.Cells("Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P", "[", "]");
    static readonly int Afternoon = KeyMap.Find("Esc")!.Cell;

    /// <summary>The time shown; the real clock unless a test sets it.</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    public string Id => "hnd-clock";
    public string Name => "Clock";
    public string Category => EffectCategory.Handy;

    public void Render(Frame frame, in EffectContext ctx)
    {
        var now = Now();
        foreach (int cell in KeyGeometry.Cells) frame[cell] = ctx.Secondary(0.5).Scale(0.03);
        int hour = now.Hour % 12 == 0 ? 12 : now.Hour % 12;
        for (int i = 0; i < hour; i++) frame[Hours[i]] = ctx.Primary(i / 12.0).Scale(i == hour - 1 ? 1 : 0.45);
        double fives = now.Minute / 5.0 + now.Second / 300.0;
        for (int i = 0; i < 12; i++) frame[Minutes[i]] = ctx.Secondary(i / 12.0).Scale(Math.Clamp(fives - i, 0, 1) * 0.9);
        double second = now.Second / 5.0 + now.Millisecond / 5000.0;
        for (int i = 0; i < 12; i++) frame[Seconds[i]] = Rgb.White.Scale(0.6 * Math.Max(0, 1 - Math.Abs(second - i - 0.5)));
        if (now.Hour >= 12) frame[Afternoon] = ctx.Secondary(0).Scale(0.6);
    }
}

/// <summary>WASD, the arrows and the keys games use most, lit up; everything else dimmed.</summary>
public sealed class GamingKeysEffect : KeyShader
{
    static readonly HashSet<int> Movement = KeyLabels.Set("W", "A", "S", "D");
    static readonly HashSet<int> Actions = KeyLabels.Set("Q", "E", "R", "F", "C", "Space", "Shift", "Ctrl", "Tab", "Esc", "Alt", "1", "2", "3", "4", "5",
                                                    "Up", "Down", "Left", "Right", "G", "V", "Z", "X");

    public override string Id => "hnd-gaming";
    public override string Name => "Gaming keys";
    public override string Category => EffectCategory.Handy;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        if (Movement.Contains(cell)) return ctx.Secondary(0.5).Scale(0.85 + 0.15 * Wave(ctx.Time * 2));
        if (Actions.Contains(cell)) return ctx.Primary(KeyGeometry.U(cell));
        return ctx.Primary(KeyGeometry.U(cell)).Scale(0.06);
    }
}

/// <summary>For learning to touch-type: each finger's keys in its own colour, the home row bright, F and J brightest.</summary>
public sealed class TypingGuideEffect : KeyShader
{
    static readonly string[][] Fingers =
    [
        ["`", "1", "Q", "A", "Z", "Tab", "Caps", "Shift", "Ctrl", "Esc"],
        ["2", "W", "S", "X"],
        ["3", "E", "D", "C"],
        ["4", "5", "R", "T", "F", "G", "V", "B"],
        ["6", "7", "Y", "U", "H", "J", "N", "M"],
        ["8", "I", "K", ","],
        ["9", "O", "L", "."],
        ["0", "-", "=", "P", "[", "]", "\\", ";", "'", "/", "Enter", "RShift", "Bksp"],
        ["Space"],
    ];
    static readonly Rgb[] Colours =
    [
        new(255, 60, 60), new(255, 150, 0), new(240, 220, 0), new(60, 220, 60),
        new(0, 200, 200), new(40, 120, 255), new(150, 70, 255), new(255, 60, 200), new(200, 200, 200),
    ];
    static readonly HashSet<int> HomeRow = KeyLabels.Set("A", "S", "D", "F", "J", "K", "L", ";");
    static readonly HashSet<int> Bumps = KeyLabels.Set("F", "J");
    static readonly Dictionary<int, int> FingerOf = Fingers.SelectMany((labels, finger) => KeyLabels.Cells(labels).Select(cell => (cell, finger)))
        .ToDictionary(p => p.cell, p => p.finger);

    public override string Id => "hnd-typing-guide";
    public override string Name => "Typing guide";
    public override string Category => EffectCategory.Handy;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        if (!FingerOf.TryGetValue(cell, out int finger)) return Rgb.Black;
        var color = Colours[finger];
        if (Bumps.Contains(cell)) return Rgb.Lerp(color, Rgb.White, 0.45);
        return color.Scale(HomeRow.Contains(cell) ? 1 : 0.3);
    }
}

/// <summary>
/// A focus timer: 25 minutes of work, then a 5-minute break. The number row fills up as the time passes, in colour 1
/// while working and green on the break, and the keyboard glows softly when it is time to switch. It counts real
/// minutes, whatever the speed, from when it was picked.
/// </summary>
public sealed class FocusTimerEffect : IEffect
{
    public const double FocusMinutes = 25, BreakMinutes = 5;
    static readonly int[] Bar = KeyLabels.Cells("`", "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=");
    static readonly Rgb Rest = new(60, 220, 90);
    double start = double.NaN, last;

    public string Id => "hnd-focus";
    public string Name => "Focus timer";
    public string Category => EffectCategory.Handy;

    public void Render(Frame frame, in EffectContext ctx)
    {
        if (double.IsNaN(start) || ctx.RealTime < last) start = ctx.RealTime;
        last = ctx.RealTime;
        double cycle = (FocusMinutes + BreakMinutes) * 60;
        double t = (ctx.RealTime - start) % cycle;
        bool working = t < FocusMinutes * 60;
        double progress = working ? t / (FocusMinutes * 60) : (t - FocusMinutes * 60) / (BreakMinutes * 60);
        double sincePhase = working ? t : t - FocusMinutes * 60;
        var color = working ? ctx.Primary(0.5) : Rest;
        double greet = sincePhase < 4 ? Math.Sin(Math.PI * sincePhase / 4) : 0;   // one soft swell when the phase changes

        foreach (int cell in KeyGeometry.Cells) frame[cell] = color.Scale(0.05 + 0.35 * greet);
        double filled = progress * Bar.Length;
        for (int i = 0; i < Bar.Length; i++) frame[Bar[i]] = color.Scale(0.12 + 0.88 * Math.Clamp(filled - i, 0, 1));
    }
}

/// <summary>Modifier keys in colour 1 and the letters of everyday shortcuts (copy, paste, undo, save...) in colour 2.</summary>
public sealed class ShortcutKeysEffect : KeyShader
{
    static readonly HashSet<int> Modifiers = KeyLabels.Set("Ctrl", "RCtrl", "Alt", "AltGr", "Shift", "RShift", "Win", "Fn", "Tab", "Esc", "Enter", "Bksp", "Del");
    static readonly HashSet<int> Letters = KeyLabels.Set("C", "V", "X", "Z", "Y", "A", "S", "F", "T", "W", "N", "P", "R");

    public override string Id => "hnd-shortcuts";
    public override string Name => "Shortcut keys";
    public override string Category => EffectCategory.Handy;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        if (Modifiers.Contains(cell)) return ctx.Primary(KeyGeometry.U(cell));
        if (Letters.Contains(cell)) return ctx.Secondary(KeyGeometry.U(cell));
        return ctx.Primary(KeyGeometry.U(cell)).Scale(0.04);
    }
}

/// <summary>Every key a dim, warm amber: enough to find the keys at night without lighting up the room.</summary>
public sealed class NightLightEffect : KeyShader
{
    public override string Id => "hnd-night-light";
    public override string Name => "Night light";
    public override string Category => EffectCategory.Handy;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        var amber = new Rgb(255, 110, 20);
        return (amber).Scale(0.28);
    }
}
