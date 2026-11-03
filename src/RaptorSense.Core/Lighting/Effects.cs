using RaptorSense.Layout;

namespace RaptorSense.Lighting;

public static class Effects
{
    public static IReadOnlyList<IEffect> All { get; } =
    [
        new StaticEffect(),
        new BreathingEffect(),
        new RainbowWaveEffect(),
        new SpectrumEffect(),
        new BassPulseEffect(),
    ];

    public static IEffect? Find(string id) => All.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

public sealed class StaticEffect : IEffect
{
    public string Id => "static";
    public string Name => "Static colour";

    public void Render(Frame frame, in EffectContext ctx) => frame.Fill(ctx.Settings.Color);
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
