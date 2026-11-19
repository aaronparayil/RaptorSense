using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// Ambient: slow and calm. Nothing here changes faster than about once a second across the whole keyboard.

/// <summary>Warm candle flames on every key, each flickering gently on its own.</summary>
public sealed class CandlelightEffect : KeyShader
{
    static readonly Rgb Deep = new(150, 40, 0), Warm = new(255, 120, 20), Bright = new(255, 190, 90);

    public override string Id => "amb-candle";
    public override string Name => "Candlelight";
    public override string Category => EffectCategory.Ambient;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double flicker = 0.55 + 0.3 * EffectMath.Noise(x * 0.45, ctx.Time * 1.4) + 0.15 * EffectMath.Noise(cell * 1.7, ctx.Time * 3.5);
        var color = Palette(flicker, Deep, Warm, Bright);
        return color.Scale(0.35 + 0.65 * flicker);
    }
}

/// <summary>Colour 1 rises and falls like the tide, with small waves along its edge.</summary>
public sealed class TideEffect : KeyShader
{
    public override string Id => "amb-tide";
    public override string Name => "Tide";
    public override string Category => EffectCategory.Ambient;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double height = KeyGeometry.Height * (0.5 + 0.32 * Math.Sin(ctx.Time * 2 * Math.PI / 9));
        double surface = KeyGeometry.Height - height + 0.3 * Math.Sin(x * 0.8 + ctx.Time * 1.6);
        double under = Smooth(surface - 0.4, surface + 0.4, y);
        double depth = Math.Clamp((y - surface) / KeyGeometry.Height, 0, 1);
        var water = ctx.Primary(KeyGeometry.U(cell)).Scale(1 - 0.55 * depth);
        var foam = Rgb.Lerp(water, Rgb.White, 0.5).Scale(Glow(y - surface, 0.35));
        return EffectMath.Max(Rgb.Lerp(ctx.Secondary(KeyGeometry.U(cell)).Scale(0.08), water, under), foam.Scale(0.6));
    }
}

/// <summary>Soft clouds drift across a sky of colour 1.</summary>
public sealed class DriftingCloudsEffect : KeyShader
{
    public override string Id => "amb-clouds";
    public override string Name => "Drifting clouds";
    public override string Category => EffectCategory.Ambient;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double cloud = Smooth(0.45, 0.78, Cloud(x * 0.22 - ctx.Time * 0.25, y * 0.35 + 3));
        var sky = ctx.Primary(KeyGeometry.U(cell)).Scale(0.3);
        var white = Rgb.Lerp(Rgb.White, ctx.Secondary(KeyGeometry.U(cell)), 0.15);
        return Rgb.Lerp(sky, white.Scale(0.85), cloud);
    }
}

/// <summary>Violet at the top to gold at the bottom, swaying slowly like an evening sky.</summary>
public sealed class SunsetEffect : KeyShader
{
    static readonly Rgb[] Sky = [new(40, 0, 90), new(170, 0, 120), new(255, 40, 60), new(255, 120, 0), new(255, 200, 60)];

    public override string Id => "amb-sunset";
    public override string Name => "Sunset glow";
    public override string Category => EffectCategory.Ambient;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double t = KeyGeometry.V(cell) + 0.12 * Math.Sin(x * 0.35 + ctx.Time * 0.5) + 0.06 * Math.Sin(ctx.Time * 0.21);
        var color = Palette(t, Sky);
        return color;
    }
}

/// <summary>Fireflies wander in the dark, each glowing and dimming at its own pace.</summary>
public sealed class FirefliesEffect : KeyShader
{
    const int Count = 11;
    readonly (double X, double Y, double Glow)[] flies = new (double, double, double)[Count];

    public override string Id => "amb-fireflies";
    public override string Name => "Fireflies";
    public override string Category => EffectCategory.Ambient;

    protected override void Prepare(in EffectContext ctx)
    {
        for (int i = 0; i < Count; i++)
        {
            double a = Hash(i * 3 + 1), b = Hash(i * 3 + 2), c = Hash(i * 3 + 3);
            double x = KeyGeometry.Width * (0.5 + 0.47 * Math.Sin(ctx.Time * (0.13 + 0.12 * a) + a * 20));
            double y = KeyGeometry.Height * (0.5 + 0.45 * Math.Sin(ctx.Time * (0.17 + 0.15 * b) + b * 20));
            double glow = Math.Pow(Wave(ctx.Time * (1.2 + c) + c * 30), 3);
            flies[i] = (x, y, glow);
        }
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        var color = new Rgb(0, 10, 3);
        for (int i = 0; i < Count; i++)
        {
            var f = flies[i];
            double level = f.Glow * Glow(Math.Sqrt((x - f.X) * (x - f.X) + (y - f.Y) * (y - f.Y)), 0.9);
            if (level < 0.02) continue;
            var fly = new Rgb(190, 255, 60);
            color = EffectMath.Max(color, fly.Scale(level));
        }
        return color;
    }
}

/// <summary>Breathing that rolls across the keyboard from left to right, colour 1 into colour 2.</summary>
public sealed class BreathingWaveEffect : KeyShader
{
    public override string Id => "amb-breath-wave";
    public override string Name => "Breathing wave";
    public override string Category => EffectCategory.Ambient;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double u = KeyGeometry.U(cell);
        double level = 0.5 - 0.5 * Math.Cos(2 * Math.PI * (ctx.Time / 4.5 - u * 0.7));
        return ctx.Blend(u, u).Scale(0.08 + 0.92 * level);
    }
}

/// <summary>Deep blue water with sunlight rippling across it from above.</summary>
public sealed class DeepSeaEffect : KeyShader
{
    static readonly Rgb Abyss = new(0, 6, 35), Shallow = new(0, 90, 150), Light = new(120, 220, 255);

    public override string Id => "amb-deep-sea";
    public override string Name => "Deep sea";
    public override string Category => EffectCategory.Ambient;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double v = KeyGeometry.V(cell);
        var water = Rgb.Lerp(Shallow, Abyss, v);
        double a = EffectMath.Noise(x * 0.55 + ctx.Time * 0.25, y * 0.55), b = EffectMath.Noise(x * 0.55 - ctx.Time * 0.2 + 7, y * 0.55 + 4);
        double caustic = Math.Pow(Math.Max(0, 1 - Math.Abs(a - b) * 5), 3) * (1 - 0.6 * v);
        var color = EffectMath.Max(water, Light.Scale(caustic * 0.8));
        return color;
    }
}

/// <summary>Raked sand around two stones, the lines slowly drifting outwards.</summary>
public sealed class ZenGardenEffect : KeyShader
{
    static readonly Rgb Sand = new(200, 160, 105);
    static readonly (double X, double Y)[] Stones = [(4.6, 2.4), (12.6, 3.6)];

    public override string Id => "amb-zen";
    public override string Name => "Zen garden";
    public override string Category => EffectCategory.Ambient;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double near = double.MaxValue;
        foreach (var (sx, sy) in Stones) near = Math.Min(near, Math.Sqrt((x - sx) * (x - sx) + (y - sy) * (y - sy) * 1.6));
        if (near < 0.7) return ctx.Primary(KeyGeometry.U(cell)).Scale(0.8);
        double rake = 0.5 + 0.5 * Math.Cos(2 * Math.PI * (near / 1.5 - ctx.Time * 0.12));
        var sand = Sand;
        return sand.Scale(0.18 + 0.45 * rake);
    }
}

/// <summary>Dim blue night, with the moon's glow passing slowly over the keys.</summary>
public sealed class MoonlightEffect : KeyShader
{
    double moonX, moonY;

    public override string Id => "amb-moonlight";
    public override string Name => "Moonlight";
    public override string Category => EffectCategory.Ambient;

    protected override void Prepare(in EffectContext ctx)
    {
        double t = ctx.Time / 40 * 2 * Math.PI;
        moonX = KeyGeometry.Width * (0.5 - 0.55 * Math.Cos(t));
        moonY = KeyGeometry.Height * (0.55 - 0.35 * Math.Sin(t));
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        var night = Rgb.Lerp(new Rgb(4, 8, 30), ctx.Secondary(KeyGeometry.U(cell)), 0.08);
        double haze = 0.12 * EffectMath.Noise(x * 0.3 + ctx.Time * 0.05, y * 0.4);
        double glow = Glow(Math.Sqrt((x - moonX) * (x - moonX) + (y - moonY) * (y - moonY)), 3.2);
        var moon = Rgb.Lerp(new Rgb(220, 230, 255), ctx.Primary(KeyGeometry.U(cell)), 0.35);
        return Rgb.Lerp(night, moon, Math.Min(1, glow * 0.85 + haze));
    }
}

/// <summary>Blobs of colour 1 rise and sink through colour 2, merging like a lava lamp.</summary>
public sealed class LavaBlobsEffect : KeyShader
{
    const int Blobs = 6;
    readonly (double X, double Y, double R)[] blobs = new (double, double, double)[Blobs];

    public override string Id => "amb-lava";
    public override string Name => "Lava lamp";
    public override string Category => EffectCategory.Ambient;

    protected override void Prepare(in EffectContext ctx)
    {
        for (int i = 0; i < Blobs; i++)
        {
            double a = Hash(i + 40), b = Hash(i + 80);
            double x = KeyGeometry.Width * (0.08 + 0.84 * (i + 0.5) / Blobs) + 1.2 * Math.Sin(ctx.Time * 0.21 + a * 9);
            double y = KeyGeometry.Height * (0.5 + 0.62 * Math.Sin(ctx.Time * (0.25 + 0.2 * b) + b * 12));
            blobs[i] = (x, y, 1.3 + 0.7 * a);
        }
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double field = 0;
        foreach (var (bx, by, r) in blobs) field += r * r / ((x - bx) * (x - bx) + (y - by) * (y - by) * 1.8 + 0.2);
        double inside = Smooth(0.8, 1.3, field);
        double u = KeyGeometry.U(cell);
        var lava = Rgb.Lerp(ctx.Primary(u), Rgb.White, Smooth(2.5, 5, field) * 0.35);
        return Rgb.Lerp(ctx.Secondary(u).Scale(0.12), lava, inside);
    }
}
