using RaptorSense.Layout;

namespace RaptorSense.Lighting;

public static class Effects
{
    /// <summary>The one painted layout the UI edits. Declared before <see cref="All"/>, which includes it.</summary>
    public static PaintedEffect Painted { get; } = new();

    public static IReadOnlyList<IEffect> All { get; } =
    [
        new StaticEffect(),
        Painted,
        new BreathingEffect(),
        new RainbowWaveEffect(),
        new SpectrumEffect(),
        new BassPulseEffect(),
        new ReactiveKeysEffect(),
        new RippleEffect(),
    ];

    public static IEffect? Find(string id) => All.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

public sealed class StaticEffect : IEffect
{
    public string Id => "static";
    public string Name => "Static colour";

    public void Render(Frame frame, in EffectContext ctx) => frame.Fill(ctx.Settings.Color);
}

/// <summary>A colour per key, painted by the user. Black means off.</summary>
public sealed class PaintedEffect : IEffect
{
    // Written by the UI thread, read by the engine; a torn read only shows for one frame.
    readonly Rgb[] cells = new Rgb[Frame.CellCount];

    public string Id => "custom";
    public string Name => "Custom (paint keys)";

    public Rgb this[int cell]
    {
        get => cells[cell];
        set => cells[cell] = value;
    }

    public void Fill(Rgb color) => Array.Fill(cells, color);

    public void Render(Frame frame, in EffectContext ctx)
    {
        for (int cell = 0; cell < Frame.CellCount; cell++) frame[cell] = cells[cell];
    }
}

public sealed class BreathingEffect : IEffect
{
    public string Id => "breathing";
    public string Name => "Breathing";

    public void Render(Frame frame, in EffectContext ctx)
    {
        double level = 0.5 - 0.5 * Math.Cos(ctx.Time * 2 * Math.PI / 3);   // one breath per 3 s at speed 1
        frame.Fill(ctx.Settings.Color.Scale(level));
    }
}

public sealed class RainbowWaveEffect : IEffect
{
    public string Id => "rainbow";
    public string Name => "Rainbow wave";

    public void Render(Frame frame, in EffectContext ctx)
    {
        for (int cell = 0; cell < Frame.CellCount; cell++)
        {
            int column = cell / KeyMap.Rows;
            frame[cell] = Rgb.FromHsv(column * 300.0 / KeyMap.Columns - ctx.Time * 120);
        }
    }
}

/// <summary>Music spectrum: one frequency band per column, bars rising from the bottom row.</summary>
public sealed class SpectrumEffect : IEffect
{
    public string Id => "spectrum";
    public string Name => "Music spectrum";
    public bool UsesAudio => true;

    public void Render(Frame frame, in EffectContext ctx)
    {
        if (ctx.Audio is null) return;
        var bands = ctx.Audio.GetBands(KeyMap.Columns);
        for (int column = 0; column < KeyMap.Columns; column++)
        {
            double height = bands[column] * KeyMap.Rows;
            for (int row = 0; row < KeyMap.Rows; row++)
            {
                int cell = column * KeyMap.Rows + row;
                if (cell >= Frame.CellCount) break;
                double lit = Math.Clamp(height - row, 0, 1);   // the top segment fades in
                if (lit <= 0) continue;
                var color = Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, row / (double)(KeyMap.Rows - 1));
                frame[cell] = color.Scale(lit);
            }
        }
    }
}

/// <summary>Pressed keys flash colour 2 and fade back to colour 1.</summary>
public sealed class ReactiveKeysEffect : IEffect
{
    const double FadeSeconds = 0.8;

    readonly double[] heat = new double[Frame.CellCount];

    public string Id => "reactive";
    public string Name => "Reactive keys";
    public bool UsesKeys => true;

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int cell in ctx.Presses) heat[cell] = 1;
        double cooling = ctx.DeltaTime * ctx.Settings.Speed / FadeSeconds;
        for (int cell = 0; cell < Frame.CellCount; cell++)
        {
            heat[cell] = Math.Max(0, heat[cell] - cooling);
            frame[cell] = Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, heat[cell]);
        }
    }
}

/// <summary>Each press sends a ring of colour 2 across a dimmed colour 1.</summary>
public sealed class RippleEffect : IEffect
{
    const double CellsPerSecond = 14, LifetimeSeconds = 1.2, RingWidth = 1.1, Background = 0.2;
    const int MaxRipples = 32;

    readonly List<(int Column, int Row, double Start)> ripples = [];

    public string Id => "ripple";
    public string Name => "Ripple";
    public bool UsesKeys => true;

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int cell in ctx.Presses) ripples.Add((cell / KeyMap.Rows, cell % KeyMap.Rows, ctx.Time));
        double now = ctx.Time;
        ripples.RemoveAll(r => now - r.Start > LifetimeSeconds);
        if (ripples.Count > MaxRipples) ripples.RemoveRange(0, ripples.Count - MaxRipples);

        var background = ctx.Settings.Color.Scale(Background);
        for (int cell = 0; cell < Frame.CellCount; cell++)
        {
            int column = cell / KeyMap.Rows, row = cell % KeyMap.Rows;
            double intensity = 0;
            foreach (var r in ripples)
            {
                double age = now - r.Start;
                double distance = Math.Sqrt((column - r.Column) * (column - r.Column) + (row - r.Row) * (row - r.Row));
                double ring = 1 - Math.Abs(distance - age * CellsPerSecond) / RingWidth;
                if (ring > 0) intensity = Math.Max(intensity, ring * (1 - age / LifetimeSeconds));
            }
            frame[cell] = Rgb.Lerp(background, ctx.Settings.Color2, intensity);
        }
    }
}

/// <summary>Whole keyboard pulses with the bass, shifting from colour 1 to colour 2 on hits.</summary>
public sealed class BassPulseEffect : IEffect
{
    public string Id => "pulse";
    public string Name => "Bass pulse";
    public bool UsesAudio => true;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double bass = ctx.Audio?.Bass ?? 0;
        var color = Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, bass);
        frame.Fill(color.Scale(0.12 + 0.88 * bass));
    }
}
