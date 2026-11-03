using RaptorSense.Hardware;
using RaptorSense.Layout;

namespace RaptorSense.Lighting;

public static class Effects
{
    /// <summary>The one painted layout the UI edits. Declared before <see cref="All"/>, which includes it.</summary>
    public static PaintedEffect Painted { get; } = new();

    public static IReadOnlyList<IEffect> All { get; } =
    [
        // Basic
        new StaticEffect(), Painted, new GradientEffect(), new BreathingEffect(),
        // Animated
        new RainbowWaveEffect(), new ColorWaveEffect(), new NeonEffect(), new SnakeEffect(),
        new RadarEffect(), new SpotlightEffect(), new HeartbeatEffect(), new AuroraEffect(),
        // Nature
        new RipplesEffect(), new RainEffect(), new MatrixEffect(), new SnowEffect(),
        new StarsEffect(), new LightningEffect(), new FireEffect(), new FireballEffect(),
        // Music
        new SpectrumEffect(), new BassPulseEffect(),
        // Typing
        new ReactiveKeysEffect(), new RippleEffect(), new TypingLightningEffect(), new TypingFireballEffect(),
        new CometEffect(), new BurstEffect(), new SplashEffect(), new CrosshairEffect(),
        new HeatmapEffect(), new ConfettiEffect(), new SparkleEffect(),
        // Run by the keyboard itself; previewed with the closest software effect
        new KeyboardEffect(BuiltInEffect.Static, "static"), new KeyboardEffect(BuiltInEffect.Breathing, "breathing"),
        new KeyboardEffect(BuiltInEffect.Wave, "rainbow"), new KeyboardEffect(BuiltInEffect.Snake, "snake"),
        new KeyboardEffect(BuiltInEffect.Ripple, "ripples"), new KeyboardEffect(BuiltInEffect.Neon, "neon"),
        new KeyboardEffect(BuiltInEffect.Raindrop, "raindrop"), new KeyboardEffect(BuiltInEffect.Lightning, "lightning"),
        new KeyboardEffect(BuiltInEffect.Spot, "spotlight"), new KeyboardEffect(BuiltInEffect.Star, "stars"),
        new KeyboardEffect(BuiltInEffect.Fireball, "fireball"), new KeyboardEffect(BuiltInEffect.Snow, "snow"),
        new KeyboardEffect(BuiltInEffect.Heartbeat, "heartbeat"),
    ];

    public static IEffect? Find(string id) => All.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One of the keyboard's own effects. The engine hands the keyboard over to it instead of streaming frames, so it
/// keeps running when RaptorSense is closed and costs no CPU. The preview shows the closest software effect.
/// </summary>
public sealed class KeyboardEffect(BuiltInEffect effect, string previewId) : IEffect
{
    public BuiltInEffect Effect => effect;
    public string Id => "kb-" + effect.ToString().ToLowerInvariant();
    public string Name => effect == BuiltInEffect.Raindrop ? "Rain drop" : effect.ToString();
    public string Category => EffectCategory.Keyboard;

    /// <summary>Wave and neon pick their own colours.</summary>
    public bool UsesColor => effect is not (BuiltInEffect.Wave or BuiltInEffect.Neon);

    public void Render(Frame frame, in EffectContext ctx) => Effects.Find(previewId)?.Render(frame, ctx);
}

public sealed class StaticEffect : IEffect
{
    public string Id => "static";
    public string Name => "Static colour";

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int cell in KeyGeometry.Cells)
            frame[cell] = ctx.Settings.AutoColor ? Rgb.FromHsv(KeyGeometry.U(cell) * 300) : ctx.Settings.Color;
    }
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
        foreach (int cell in KeyGeometry.Cells) frame[cell] = ctx.Primary(KeyGeometry.U(cell)).Scale(level);
    }
}

public sealed class RainbowWaveEffect : IEffect
{
    public string Id => "rainbow";
    public string Name => "Rainbow wave";
    public string Category => EffectCategory.Animated;

    public void Render(Frame frame, in EffectContext ctx)
    {
        for (int cell = 0; cell < Frame.CellCount; cell++)
        {
            // Physical position makes the sweep even across wide keys; cells without a key fall back to the grid.
            double x = PhysicalLayout.ForCell(cell) is { } rect
                ? rect.CenterX / PhysicalLayout.Width
                : (double)(cell / KeyMap.Rows) / KeyMap.Columns;
            frame[cell] = Rgb.FromHsv(x * 300 - ctx.Time * 120);
        }
    }
}

/// <summary>Music spectrum: one frequency band per column, bars rising from the bottom row.</summary>
public sealed class SpectrumEffect : IEffect
{
    public string Id => "spectrum";
    public string Name => "Music spectrum";
    public string Category => EffectCategory.Music;
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
    public string Category => EffectCategory.Typing;
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

/// <summary>Each press sends a ring of colour 2 across dark keys, spreading in real key distances.</summary>
public sealed class RippleEffect : IEffect
{
    // Distances are in key pitches.
    const double KeysPerSecond = 12, LifetimeSeconds = 1.2, RingWidth = 1.0;
    const int MaxRipples = 32;

    readonly List<(double X, double Y, double Start)> ripples = [];

    public string Id => "ripple";
    public string Name => "Typing ripple";
    public string Category => EffectCategory.Typing;
    public bool UsesKeys => true;
    public bool WorksAsOverlay => true;

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int cell in ctx.Presses)
        {
            if (PhysicalLayout.ForCell(cell) is { } origin)
                ripples.Add((origin.CenterX / PhysicalLayout.Pitch, origin.CenterY / PhysicalLayout.Pitch, ctx.Time));
        }
        double now = ctx.Time;
        ripples.RemoveAll(r => now - r.Start > LifetimeSeconds);
        if (ripples.Count > MaxRipples) ripples.RemoveRange(0, ripples.Count - MaxRipples);

        for (int cell = 0; cell < Frame.CellCount; cell++)
        {
            if (PhysicalLayout.ForCell(cell) is not { } rect) continue;   // no key there
            double x = rect.CenterX / PhysicalLayout.Pitch, y = rect.CenterY / PhysicalLayout.Pitch;
            double intensity = 0;
            foreach (var r in ripples)
            {
                double age = now - r.Start;
                double distance = Math.Sqrt((x - r.X) * (x - r.X) + (y - r.Y) * (y - r.Y));
                double ring = 1 - Math.Abs(distance - age * KeysPerSecond) / RingWidth;
                if (ring > 0) intensity = Math.Max(intensity, ring * (1 - age / LifetimeSeconds));
            }
            frame[cell] = ctx.Settings.Color2.Scale(intensity);
        }
    }
}

/// <summary>Whole keyboard pulses with the bass, shifting from colour 1 to colour 2 on hits.</summary>
public sealed class BassPulseEffect : IEffect
{
    public string Id => "pulse";
    public string Name => "Bass pulse";
    public string Category => EffectCategory.Music;
    public bool UsesAudio => true;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double bass = ctx.Audio?.Bass ?? 0;
        var color = Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, bass);
        frame.Fill(color.Scale(0.12 + 0.88 * bass));
    }
}
