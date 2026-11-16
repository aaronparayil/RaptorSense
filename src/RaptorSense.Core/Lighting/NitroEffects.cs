using RaptorSense.Audio;
using RaptorSense.Hardware;
using RaptorSense.Layout;

namespace RaptorSense.Lighting;

// Effects made for the Nitro 5's keyboard: four zones, left to right, updated a few times a second through the helper.
// Per-key effects shrink to a blur of averages there, so these think in zones instead: each picks one colour per zone,
// and every key in a zone gets it (which is also exactly what the zone averages back to). They move slowly and
// smoothly, because the keyboard only takes about ten updates a second.

/// <summary>An effect that sets one colour for each of the four zones.</summary>
public abstract class ZoneEffect : IEffect
{
    protected const int Zones = FourZoneProtocol.Zones;

    readonly Rgb[] zones = new Rgb[Zones];

    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual string Category => EffectCategory.Animated;
    public virtual bool UsesAudio => false;
    public virtual bool UsesKeys => false;
    public virtual bool WorksAsOverlay => false;

    /// <summary>Fills <paramref name="zones"/> (left to right), which start black.</summary>
    protected abstract void RenderZones(Rgb[] zones, in EffectContext ctx);

    public void Render(Frame frame, in EffectContext ctx)
    {
        Array.Fill(zones, Rgb.Black);
        RenderZones(zones, ctx);
        foreach (int cell in KeyGeometry.Cells)
        {
            int zone = FourZoneLayout.ZoneOf(cell);
            if (zone >= 0) frame[cell] = zones[zone];
        }
    }

    /// <summary>The zone's centre, 0 at the left edge to 1 at the right.</summary>
    protected static double U(int zone) => (zone + 0.5) / Zones;

    protected static double Smooth(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }
}

// ---- Animated ----

/// <summary>Each zone drifts slowly through the rainbow, a little ahead of its neighbour on the right.</summary>
public sealed class ColourDriftEffect : ZoneEffect
{
    public override string Id => "nitro-drift";
    public override string Name => "Colour drift";

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        for (int z = 0; z < Zones; z++) zones[z] = Rgb.FromHsv(ctx.Time * 24 - z * 35);
    }
}

/// <summary>Colour 1 and colour 2 slide across the keyboard and back, with a soft edge between them.</summary>
public sealed class TwoToneSweepEffect : ZoneEffect
{
    public override string Id => "nitro-sweep";
    public override string Name => "Two-tone sweep";

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        double edge = (Math.Sin(ctx.Time * 0.9) + 1) / 2 * (Zones + 1) - 0.5;   // -0.5 .. 4.5: all one colour at each end
        for (int z = 0; z < Zones; z++)
            zones[z] = Rgb.Lerp(ctx.Settings.Color2, ctx.Primary(U(z)), Smooth(edge - z));
    }
}

/// <summary>One bright zone runs left and right with a fading tail, like a scanner.</summary>
public sealed class ScannerEffect : ZoneEffect
{
    public override string Id => "nitro-scanner";
    public override string Name => "Scanner";

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        const double Period = 2.4;
        double phase = ctx.Time / Period % 1;
        double position = (phase < 0.5 ? phase * 2 : 2 - phase * 2) * (Zones - 1);   // 0 → 3 → 0
        bool goingRight = phase < 0.5;
        for (int z = 0; z < Zones; z++)
        {
            double behind = goingRight ? position - z : z - position;   // > 0: the scanner has passed this zone
            double level = behind >= 0 ? Math.Exp(-behind * 1.6) : Math.Max(0, 1 + behind * 2.5);
            zones[z] = ctx.Primary(U(z)).Scale(Math.Max(0.04, level));
        }
    }
}

/// <summary>The zones fill up one by one, then empty, alternating colour 1 and colour 2.</summary>
public sealed class FillEffect : ZoneEffect
{
    public override string Id => "nitro-fill";
    public override string Name => "Fill and empty";

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        double step = ctx.Time * 1.6;
        int cycle = (int)Math.Floor(step / (2 * Zones));
        double phase = step - cycle * 2 * Zones;   // 0..8: filling for the first half, emptying for the second
        var color = cycle % 2 == 0 ? ctx.Settings.Color : ctx.Settings.Color2;
        for (int z = 0; z < Zones; z++)
        {
            double level = phase < Zones ? Smooth(phase - z) : 1 - Smooth(phase - Zones - z);
            zones[z] = (ctx.Settings.AutoColor ? Rgb.FromHsv(cycle * 70 + z * 20) : color).Scale(level);
        }
    }
}

/// <summary>Deep blue water swelling into teal and, now and then, foam.</summary>
public sealed class OceanEffect : ZoneEffect
{
    static readonly Rgb Deep = new(0, 20, 110), Teal = new(0, 170, 190), Foam = new(190, 255, 255);

    public override string Id => "nitro-ocean";
    public override string Name => "Ocean swell";
    public override string Category => EffectCategory.Nature;

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        for (int z = 0; z < Zones; z++)
        {
            double swell = EffectMath.Noise(z * 0.55, ctx.Time * 0.35);
            double crest = Math.Max(0, EffectMath.Noise(z * 0.9 + 20, ctx.Time * 0.7) - 0.72) / 0.28;
            zones[z] = Rgb.Lerp(Rgb.Lerp(Deep, Teal, swell), Foam, crest * 0.7);
        }
    }
}

/// <summary>A bed of embers: each zone glows and flickers on its own.</summary>
public sealed class EmbersEffect : ZoneEffect
{
    public override string Id => "nitro-embers";
    public override string Name => "Embers";
    public override string Category => EffectCategory.Nature;

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        for (int z = 0; z < Zones; z++)
        {
            double heat = 0.42 + 0.38 * EffectMath.Noise(z * 1.3, ctx.Time * 1.1) + 0.18 * EffectMath.Noise(z * 2.7 + 9, ctx.Time * 4.5);
            zones[z] = EffectMath.Fire(heat);
        }
    }
}

/// <summary>Colour 1 and colour 2 blend into each other like wax in a lava lamp.</summary>
public sealed class LavaLampEffect : ZoneEffect
{
    public override string Id => "nitro-lava";
    public override string Name => "Lava lamp";

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        for (int z = 0; z < Zones; z++)
        {
            double mix = Smooth((EffectMath.Noise(z * 0.6 + 4, ctx.Time * 0.22) - 0.2) / 0.6);
            double glow = 0.55 + 0.45 * EffectMath.Noise(z * 0.8 + 30, ctx.Time * 0.3);
            zones[z] = Rgb.Lerp(ctx.Primary(U(z)), ctx.Settings.Color2, mix).Scale(glow);
        }
    }
}

/// <summary>Zones pop into random bright colours, one after another, and fade.</summary>
public sealed class PartyEffect : ZoneEffect
{
    const double StepSeconds = 0.45;

    readonly Rgb[] colors = new Rgb[Zones];
    readonly double[] litAt = new double[Zones];
    readonly Random random = new();
    long lastStep = -1;
    double lastTime;

    public override string Id => "nitro-party";
    public override string Name => "Party";

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        if (ctx.Time < lastTime) { lastStep = -1; Array.Clear(litAt); }
        lastTime = ctx.Time;
        long step = (long)Math.Floor(ctx.Time / StepSeconds);
        if (step != lastStep)
        {
            lastStep = step;
            int zone = random.Next(Zones);
            colors[zone] = Rgb.FromHsv(random.NextDouble() * 360);
            litAt[zone] = ctx.Time;
        }
        for (int z = 0; z < Zones; z++)
        {
            double age = ctx.Time - litAt[z];
            zones[z] = colors[z].Scale(0.18 + 0.82 * Math.Exp(-age * 2.2));
        }
    }
}

/// <summary>A day in a minute: night, sunrise, daylight and sunset, moving from left to right.</summary>
public sealed class DayAndNightEffect : ZoneEffect
{
    static readonly (double At, Rgb Color)[] Sky =
    [
        (0.00, new Rgb(8, 8, 50)),       // night
        (0.20, new Rgb(40, 10, 90)),
        (0.30, new Rgb(255, 80, 20)),    // sunrise
        (0.40, new Rgb(255, 190, 90)),
        (0.55, new Rgb(120, 200, 255)),  // day
        (0.70, new Rgb(255, 150, 60)),
        (0.80, new Rgb(200, 30, 110)),   // sunset
        (0.90, new Rgb(30, 10, 80)),
        (1.00, new Rgb(8, 8, 50)),
    ];

    public override string Id => "nitro-day";
    public override string Name => "Day and night";
    public override string Category => EffectCategory.Nature;

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        const double DaySeconds = 60;
        for (int z = 0; z < Zones; z++)
        {
            double t = (ctx.Time / DaySeconds - z * 0.03) % 1;   // the left of the keyboard sees the sun first
            if (t < 0) t += 1;
            zones[z] = SkyAt(t);
        }
    }

    public static Rgb SkyAt(double t)
    {
        for (int i = 1; i < Sky.Length; i++)
        {
            if (t > Sky[i].At) continue;
            var (fromAt, from) = Sky[i - 1];
            return Rgb.Lerp(from, Sky[i].Color, (t - fromAt) / (Sky[i].At - fromAt));
        }
        return Sky[^1].Color;
    }
}

/// <summary>Red on the left, blue on the right, flashing in turn like a siren.</summary>
public sealed class SirenEffect : ZoneEffect
{
    static readonly Rgb Red = new(255, 0, 0), Blue = new(0, 40, 255);

    public override string Id => "nitro-siren";
    public override string Name => "Siren";

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        const double Period = 1.0;
        double phase = ctx.Time / Period % 1;
        // Each side flashes twice in its half of the period.
        static double Flashes(double p) => p < 0.5 && (p % 0.25) < 0.14 ? 1 : 0.06;
        double left = Flashes(phase), right = Flashes((phase + 0.5) % 1);
        zones[0] = zones[1] = Red.Scale(left);
        zones[2] = zones[3] = Blue.Scale(right);
    }
}

// ---- Music ----

/// <summary>Bass on the left, treble on the right: each zone lights with its part of the music.</summary>
public sealed class MusicZonesEffect : ZoneEffect
{
    readonly double[] shown = new double[Zones];

    public override string Id => "nitro-music-zones";
    public override string Name => "Music zones";
    public override string Category => EffectCategory.Music;
    public override bool UsesAudio => true;

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        if (ctx.Audio is null) return;
        var bands = ctx.Audio.GetBands(Zones);
        double fall = Math.Clamp(ctx.DeltaTime * 3, 0, 1);   // rises at once, falls over about a third of a second
        for (int z = 0; z < Zones; z++)
        {
            double level = Math.Clamp(bands[z], 0, 1);
            shown[z] = level >= shown[z] ? level : shown[z] + (level - shown[z]) * fall;
            zones[z] = Rgb.Lerp(ctx.Primary(U(z)), ctx.Settings.Color2, shown[z]).Scale(0.08 + 0.92 * shown[z]);
        }
    }
}

/// <summary>The whole keyboard flashes on every beat and steps to a new colour.</summary>
public sealed class BeatColourEffect : ZoneEffect
{
    readonly BeatDetector beats = new();
    double hue, flash;

    public override string Id => "nitro-beat";
    public override string Name => "Beat colour";
    public override string Category => EffectCategory.Music;
    public override bool UsesAudio => true;

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        if (ctx.Audio is null) return;
        ctx.Audio.GetBands(Zones);   // refreshes Bass
        if (beats.Update(ctx.Audio.Bass, ctx.DeltaTime))
        {
            hue = (hue + 67) % 360;
            flash = 1;
        }
        flash = Math.Max(0, flash - ctx.DeltaTime * 2.5);
        double level = 0.15 + 0.85 * flash;
        for (int z = 0; z < Zones; z++)
            zones[z] = (ctx.Settings.AutoColor ? Rgb.FromHsv(hue + z * 12) : Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, hue / 360)).Scale(level);
    }
}

// ---- Typing ----

/// <summary>The zone you type in flashes, and fades back to dark. Also works as an overlay.</summary>
public sealed class ZoneFlashEffect : ZoneEffect
{
    const double FadeSeconds = 0.6;

    readonly double[] level = new double[Zones];
    readonly double[] hues = new double[Zones];
    readonly Random random = new();

    public override string Id => "nitro-type-flash";
    public override string Name => "Zone flash";
    public override string Category => EffectCategory.Typing;
    public override bool UsesKeys => true;
    public override bool WorksAsOverlay => true;

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        foreach (int cell in ctx.Presses)
        {
            int zone = FourZoneLayout.ZoneOf(cell);
            if (zone < 0) continue;
            level[zone] = 1;
            hues[zone] = random.NextDouble() * 360;
        }
        double fade = ctx.DeltaTime / FadeSeconds;
        for (int z = 0; z < Zones; z++)
        {
            level[z] = Math.Max(0, level[z] - fade);
            zones[z] = ctx.Accent(hues[z]).Scale(level[z]);
        }
    }
}

/// <summary>A key press sends a wave from its zone out to the others. Also works as an overlay.</summary>
public sealed class TypingRippleEffect : ZoneEffect
{
    const double ZoneDelay = 0.12, Width = 0.18, Lifetime = 1.0;
    const int MaxActive = 16;

    readonly List<(int Zone, double Start, double Hue)> active = [];
    readonly Random random = new();
    double lastTime;

    public override string Id => "nitro-type-ripple";
    public override string Name => "Ripple across";
    public override string Category => EffectCategory.Typing;
    public override bool UsesKeys => true;
    public override bool WorksAsOverlay => true;

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        if (ctx.RealTime < lastTime) active.Clear();
        lastTime = ctx.RealTime;
        foreach (int cell in ctx.Presses)
        {
            int zone = FourZoneLayout.ZoneOf(cell);
            if (zone >= 0) active.Add((zone, ctx.RealTime, random.NextDouble() * 360));
        }
        double now = ctx.RealTime;
        active.RemoveAll(a => now - a.Start > Lifetime);
        if (active.Count > MaxActive) active.RemoveRange(0, active.Count - MaxActive);

        foreach (var (origin, start, hue) in active)
        {
            double age = ctx.RealTime - start;
            var color = ctx.Accent(hue);
            for (int z = 0; z < Zones; z++)
            {
                double arrival = Math.Abs(z - origin) * ZoneDelay;
                double d = (age - arrival) / Width;
                double level = Math.Exp(-d * d) * (1 - age / Lifetime);
                if (level > 0) zones[z] = EffectMath.Max(zones[z], color.Scale(Math.Min(1, level)));
            }
        }
    }
}

/// <summary>Each zone warms up as you type in it and cools down when you stop. Also works as an overlay.</summary>
public sealed class ZoneHeatEffect : ZoneEffect
{
    const double PerPress = 0.07, CoolingPerSecond = 0.12;

    readonly double[] heat = new double[Zones];

    public override string Id => "nitro-type-heat";
    public override string Name => "Zone heat";
    public override string Category => EffectCategory.Typing;
    public override bool UsesKeys => true;
    public override bool WorksAsOverlay => true;

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        foreach (int cell in ctx.Presses)
        {
            int zone = FourZoneLayout.ZoneOf(cell);
            if (zone >= 0) heat[zone] = Math.Min(1, heat[zone] + PerPress);
        }
        double cooling = Math.Clamp(ctx.DeltaTime, 0, 0.1) * CoolingPerSecond;
        for (int z = 0; z < Zones; z++)
        {
            heat[z] = Math.Max(0, heat[z] - cooling);
            if (heat[z] <= 0) continue;
            zones[z] = ctx.Settings.AutoColor ? Rgb.FromHsv(240 - 240 * heat[z]).Scale(Math.Min(1, heat[z] * 2)) : EffectMath.Fire(heat[z]);
        }
    }
}

/// <summary>The faster you type, the hotter the whole keyboard: calm colour 1 when idle, colour 2 at full speed.</summary>
public sealed class TypingSpeedEffect : ZoneEffect
{
    /// <summary>Keys a second that count as full speed (about 100 words a minute).</summary>
    public const double FullSpeed = 8;

    const double WindowSeconds = 2.5;

    readonly Queue<double> presses = new();
    double shown;

    public override string Id => "nitro-type-speed";
    public override string Name => "Typing speed";
    public override string Category => EffectCategory.Typing;
    public override bool UsesKeys => true;

    /// <summary>0 idle .. 1 at <see cref="FullSpeed"/>, as shown now.</summary>
    public double Intensity => shown;

    protected override void RenderZones(Rgb[] zones, in EffectContext ctx)
    {
        double now = ctx.RealTime;
        foreach (int _ in ctx.Presses) presses.Enqueue(now);
        while (presses.Count > 0 && now - presses.Peek() > WindowSeconds) presses.Dequeue();
        double target = Math.Min(1, presses.Count / WindowSeconds / FullSpeed);
        shown += (target - shown) * Math.Clamp(ctx.DeltaTime * 3, 0, 1);

        var color = ctx.Settings.AutoColor ? Rgb.FromHsv(200 - 200 * shown) : Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, shown);
        double level = 0.3 + 0.7 * shown;
        for (int z = 0; z < Zones; z++) zones[z] = color.Scale(level);
    }
}
