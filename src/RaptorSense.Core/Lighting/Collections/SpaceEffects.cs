using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// Space: stars, planets and things far away, mostly on black.

static class Sky
{
    /// <summary>A faint, slowly twinkling star on some keys and black on the rest.</summary>
    public static Rgb Stars(int cell, double time, double density = 0.25)
    {
        if (Hash(cell + 4321) > density) return Rgb.Black;
        double twinkle = 0.25 + 0.75 * Wave(time * (0.6 + Hash(cell + 7) * 1.4) + cell);
        return new Rgb(200, 210, 255).Scale(0.18 * twinkle);
    }
}

/// <summary>Stars streaking out of the middle of the keyboard, as if jumping to light speed.</summary>
public sealed class WarpSpeedEffect : SteppedEffect
{
    readonly List<(double Angle, double R, double Hue)> stars = [];
    double spawn;

    public override string Id => "spc-warp";
    public override string Name => "Warp speed";
    public override string Category => EffectCategory.Space;

    protected override void Reset() => stars.Clear();

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        for (spawn += dt * 30; spawn >= 1; spawn--) stars.Add((Random.NextDouble() * 2 * Math.PI, 0.3 + Random.NextDouble(), Random.NextDouble() * 360));
        for (int i = 0; i < stars.Count; i++) { var s = stars[i]; stars[i] = s with { R = s.R + dt * (1 + s.R * 1.6) }; }
        stars.RemoveAll(s => s.R > 13);

        double cx = KeyGeometry.Width / 2, cy = KeyGeometry.Height / 2;
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            var color = Rgb.Black;
            foreach (var s in stars)
            {
                double c = Math.Cos(s.Angle), si = Math.Sin(s.Angle) * 0.45;
                double tail = Math.Max(0.2, s.R * 0.25);
                double d = ToSegment(x, y, cx + c * s.R, cy + si * s.R, cx + c * (s.R - tail), cy + si * (s.R - tail));
                double level = (1 - d / 0.5) * Math.Min(1, s.R / 4);
                if (level <= 0) continue;
                var tint = ctx.Settings.AutoColor ? Rgb.FromHsv(s.Hue) : Rgb.Lerp(Rgb.White, ctx.Settings.Color, 0.35);
                color = EffectMath.Max(color, tint.Scale(level));
            }
            frame[cell] = color;
        }
    }
}

/// <summary>A spiral galaxy turning slowly, with a bright core and blue arms.</summary>
public sealed class GalaxyEffect : KeyShader
{
    static readonly Rgb Core = new(255, 230, 170), Arm = new(90, 120, 255), Dust = new(180, 60, 220);

    public override string Id => "spc-galaxy";
    public override string Name => "Galaxy";
    public override string Category => EffectCategory.Space;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double dx = x - KeyGeometry.Width / 2, dy = (y - KeyGeometry.Height / 2) * 2.4;
        double r = Math.Sqrt(dx * dx + dy * dy) + 0.01, angle = Math.Atan2(dy, dx);
        double arms = Math.Pow(Wave(2 * (angle - Math.Log(r) * 2.2 + ctx.Time * 0.35)), 3) * Math.Exp(-r / 6);
        var color = Rgb.Lerp(Arm, Dust, Wave(r * 0.6)).Scale(Math.Min(1, arms * 1.6));
        color = EffectMath.Max(color, Core.Scale(Glow(r, 1.4)));
        return EffectMath.Max(color, Sky.Stars(cell, ctx.Time));
    }
}

/// <summary>Clouds of glowing gas in pink, violet and teal, drifting among the stars.</summary>
public sealed class NebulaEffect : KeyShader
{
    public override string Id => "spc-nebula";
    public override string Name => "Nebula";
    public override string Category => EffectCategory.Space;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double a = Cloud(x * 0.2 + ctx.Time * 0.06, y * 0.3);
        double b = Cloud(x * 0.25 - ctx.Time * 0.05 + 20, y * 0.35 + 9);
        var gas = Shaping.Add(new Rgb(255, 40, 160).Scale(Smooth(0.45, 0.8, a) * 0.8), new Rgb(40, 180, 220).Scale(Smooth(0.5, 0.85, b) * 0.7));
        gas = Shaping.Add(gas, new Rgb(90, 20, 160).Scale(0.25));
        return EffectMath.Max(gas, Sky.Stars(cell, ctx.Time, 0.3));
    }
}

/// <summary>A starry night where now and then a shooting star streaks across.</summary>
public sealed class ShootingStarsEffect : SteppedEffect
{
    readonly List<(double X, double Y, double Age)> meteors = [];
    double next, time;

    public override string Id => "spc-shooting-stars";
    public override string Name => "Shooting stars";
    public override string Category => EffectCategory.Space;

    protected override void Reset() { meteors.Clear(); next = 0; time = 0; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        time += dt;
        if (time >= next)
        {
            meteors.Add((Random.NextDouble() * KeyGeometry.Width * 0.7 - 2, -0.5 + Random.NextDouble() * 1.5, 0));
            next = time + 0.6 + Random.NextDouble() * 1.4;
        }
        for (int i = 0; i < meteors.Count; i++) { var m = meteors[i]; meteors[i] = m with { Age = m.Age + dt }; }
        meteors.RemoveAll(m => m.Age > 1.3);

        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            var color = Sky.Stars(cell, time, 0.35);
            foreach (var m in meteors)
            {
                double hx = m.X + m.Age * 13, hy = m.Y + m.Age * 4.5;
                double d = ToSegment(x, y, hx, hy, hx - 3.6, hy - 1.25);
                double along = Math.Clamp(Math.Sqrt((x - hx) * (x - hx) + (y - hy) * (y - hy)) / 3.8, 0, 1);
                double level = (1 - d / 0.55) * (1 - along) * (1 - m.Age / 1.3);
                if (level <= 0) continue;
                var tint = ctx.Settings.AutoColor ? Rgb.FromHsv(m.X * 30) : Rgb.Lerp(Rgb.White, ctx.Settings.Color, along);
                color = EffectMath.Max(color, tint.Scale(level));
            }
            frame[cell] = color;
        }
    }
}

/// <summary>Planets circling a glowing sun, each on its own faint orbit.</summary>
public sealed class OrbitsEffect : KeyShader
{
    static readonly (double Radius, double Speed, Rgb Color)[] Planets =
        [(2.6, 0.9, new Rgb(255, 140, 60)), (4.6, 0.55, new Rgb(60, 160, 255)), (7.2, 0.32, new Rgb(200, 90, 255))];
    readonly (double X, double Y)[] at = new (double, double)[Planets.Length];

    public override string Id => "spc-orbits";
    public override string Name => "Orbits";
    public override string Category => EffectCategory.Space;

    protected override void Prepare(in EffectContext ctx)
    {
        for (int i = 0; i < Planets.Length; i++)
        {
            double a = ctx.Time * Planets[i].Speed + i * 2;
            at[i] = (KeyGeometry.Width / 2 + Math.Cos(a) * Planets[i].Radius, KeyGeometry.Height / 2 + Math.Sin(a) * Planets[i].Radius * 0.36);
        }
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double dx = x - KeyGeometry.Width / 2, dy = (y - KeyGeometry.Height / 2) / 0.36;
        double r = Math.Sqrt(dx * dx + dy * dy);
        var color = new Rgb(255, 200, 60).Scale(Glow(Math.Sqrt(dx * dx + dy * dy * 0.13), 1.2));
        for (int i = 0; i < Planets.Length; i++)
        {
            var (radius, _, tint) = Planets[i];
            var planet = tint;
            color = EffectMath.Max(color, planet.Scale(0.07 * Glow(r - radius, 0.45)));
            double d = Math.Sqrt((x - at[i].X) * (x - at[i].X) + (y - at[i].Y) * (y - at[i].Y));
            color = EffectMath.Max(color, planet.Scale(Glow(d, 0.75)));
        }
        return color;
    }
}

/// <summary>A black hole: a dark centre with a hot, swirling ring of matter round it.</summary>
public sealed class BlackHoleEffect : KeyShader
{
    public override string Id => "spc-black-hole";
    public override string Name => "Black hole";
    public override string Category => EffectCategory.Space;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double dx = x - KeyGeometry.Width / 2, dy = (y - KeyGeometry.Height / 2) * 1.9;
        double r = Math.Sqrt(dx * dx + dy * dy), angle = Math.Atan2(dy, dx);
        if (r < 1.1) return Rgb.Black;
        double swirl = Wave(angle * 3 - ctx.Time * 2.6 + r * 1.6);
        double disc = Glow(r - 2.6, 1.3) * (0.45 + 0.55 * swirl);
        double edge = Glow(r - 1.3, 0.25);
        var hot = EffectMath.Fire(0.55 + 0.45 * Glow(r - 1.6, 0.9));
        var color = EffectMath.Max(hot.Scale(Math.Min(1, disc)), new Rgb(255, 240, 220).Scale(edge * 0.9));
        return EffectMath.Max(color, Sky.Stars(cell, ctx.Time, 0.2));
    }
}

/// <summary>A spinning star sweeping two beams of light round, like a lighthouse in space.</summary>
public sealed class PulsarEffect : KeyShader
{
    public override string Id => "spc-pulsar";
    public override string Name => "Pulsar";
    public override string Category => EffectCategory.Space;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double dx = x - KeyGeometry.Width / 2, dy = (y - KeyGeometry.Height / 2) * 2;
        double r = Math.Sqrt(dx * dx + dy * dy), angle = Math.Atan2(dy, dx);
        double beam = Math.Pow(Math.Abs(Math.Cos(angle - ctx.Time * 1.4)), 14) * Math.Min(1, 0.3 + r / 6);
        var tint = ctx.Primary(angle / (2 * Math.PI) + 0.5);
        var color = Rgb.Lerp(tint, Rgb.White, 0.3).Scale(beam);
        color = EffectMath.Max(color, Rgb.White.Scale(Glow(r, 0.9) * (0.75 + 0.25 * Wave(ctx.Time * 2))));
        return EffectMath.Max(color, Sky.Stars(cell, ctx.Time, 0.2));
    }
}

/// <summary>The moon slides across the sun, and the sun's corona shines out round the edge.</summary>
public sealed class EclipseEffect : KeyShader
{
    const double SunRadius = 2.1, Cycle = 18;
    double moonX;

    public override string Id => "spc-eclipse";
    public override string Name => "Eclipse";
    public override string Category => EffectCategory.Space;

    protected override void Prepare(in EffectContext ctx) =>
        moonX = KeyGeometry.Width / 2 + (Fract(ctx.Time / Cycle) * 2 - 1) * 8;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double cx = KeyGeometry.Width / 2, cy = KeyGeometry.Height / 2;
        double sun = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy) * 1.8);
        double moon = Math.Sqrt((x - moonX) * (x - moonX) + (y - cy) * (y - cy) * 1.8);
        double cover = Smooth(SunRadius * 1.8, 0, Math.Abs(moonX - cx));   // 1 when the moon is right in front
        var gold = new Rgb(255, 190, 60);
        var color = gold.Scale(Smooth(SunRadius + 0.5, SunRadius - 0.5, sun) * (1 - 0.6 * cover));
        double corona = Glow(sun - SunRadius, 0.9) * cover;
        color = EffectMath.Max(color, Rgb.Lerp(gold, Rgb.White, 0.6).Scale(corona));
        if (moon < SunRadius * 0.95) color = Rgb.Lerp(color, Rgb.Black, Smooth(SunRadius * 0.95, SunRadius * 0.7, moon));
        return EffectMath.Max(color, Sky.Stars(cell, ctx.Time, 0.15).Scale(cover));
    }
}

/// <summary>Three star patterns, their stars and the lines between them fading in and out in turn.</summary>
public sealed class ConstellationsEffect : KeyShader
{
    // Star positions in key pitches.
    static readonly (double X, double Y)[][] Shapes =
    [
        [(1.5, 1.2), (2.8, 2.1), (4.2, 1.6), (5.4, 2.6), (4.6, 4.0)],
        [(7.6, 3.9), (8.3, 2.2), (9.6, 1.3), (10.9, 2.3), (11.5, 4.1)],
        [(13.0, 1.0), (14.6, 1.8), (15.4, 3.3), (16.8, 2.4), (17.6, 4.2)],
    ];
    readonly double[] shown = new double[Shapes.Length];

    public override string Id => "spc-constellations";
    public override string Name => "Constellations";
    public override string Category => EffectCategory.Space;

    protected override void Prepare(in EffectContext ctx)
    {
        for (int i = 0; i < Shapes.Length; i++) shown[i] = Smooth(0.15, 0.6, Wave(ctx.Time * 0.5 - i * 2.1));
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        var color = Sky.Stars(cell, ctx.Time, 0.3);
        for (int i = 0; i < Shapes.Length; i++)
        {
            if (shown[i] <= 0.01) continue;
            var stars = Shapes[i];
            double line = 0, star = 0;
            for (int j = 0; j < stars.Length; j++)
            {
                star = Math.Max(star, Glow(Math.Sqrt((x - stars[j].X) * (x - stars[j].X) + (y - stars[j].Y) * (y - stars[j].Y)), 0.5));
                if (j > 0) line = Math.Max(line, Glow(ToSegment(x, y, stars[j - 1].X, stars[j - 1].Y, stars[j].X, stars[j].Y), 0.32));
            }
            var tint = ctx.Settings.AutoColor ? Rgb.FromHsv(i * 120 + ctx.Time * 10) : ctx.Settings.Color;
            color = EffectMath.Max(color, EffectMath.Max(tint.Scale(line * 0.45), Rgb.White.Scale(star)).Scale(shown[i]));
        }
        return color;
    }
}

/// <summary>Close up on the sun: boiling cells of orange and yellow, with brighter flares rolling through.</summary>
public sealed class SolarSurfaceEffect : KeyShader
{
    public override string Id => "spc-sun-surface";
    public override string Name => "Solar surface";
    public override string Category => EffectCategory.Space;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double granules = EffectMath.Noise(x * 1.1 + ctx.Time * 0.4, y * 1.1 - ctx.Time * 0.3);
        double flare = Smooth(0.6, 0.9, EffectMath.Noise(x * 0.25 - ctx.Time * 0.15, y * 0.3 + 5));
        double heat = 0.5 + 0.3 * granules + 0.2 * flare;
        var color = EffectMath.Fire(heat);
        return color;
    }
}
