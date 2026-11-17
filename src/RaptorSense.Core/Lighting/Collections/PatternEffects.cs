using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// Patterns: shapes and colour fields in motion. They blend colour 1 and colour 2, or run through the rainbow with
// Auto colour.

static class Centre
{
    public static double X => KeyGeometry.Width / 2;
    public static double Y => KeyGeometry.Height / 2;
}

/// <summary>Old-school plasma: overlapping waves of colour folding into each other.</summary>
public sealed class PlasmaEffect : KeyShader
{
    public override string Id => "pat-plasma";
    public override string Name => "Plasma";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double t = ctx.Time * 0.8;
        double dx = x - Centre.X, dy = y - Centre.Y;
        double v = Math.Sin(x * 0.55 + t) + Math.Sin(y * 0.9 - t * 1.3) + Math.Sin((x + y) * 0.35 + t * 0.7)
                   + Math.Sin(Math.Sqrt(dx * dx + dy * dy * 2) * 0.75 - t * 1.1);
        if (ctx.Settings.AutoColor) return Rgb.FromHsv(v * 50 + ctx.Time * 25);
        return ctx.Blend(Wave(v * Math.PI / 2), KeyGeometry.U(cell));
    }
}

/// <summary>A mirrored, turning pattern around the middle of the keyboard, like looking into a kaleidoscope.</summary>
public sealed class KaleidoscopeEffect : KeyShader
{
    const double Slice = Math.PI / 3;

    public override string Id => "pat-kaleido";
    public override string Name => "Kaleidoscope";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double dx = x - Centre.X, dy = (y - Centre.Y) * 1.6;
        double r = Math.Sqrt(dx * dx + dy * dy);
        double angle = Math.Atan2(dy, dx) + ctx.Time * 0.25;
        double folded = Math.Abs((angle % Slice + Slice) % Slice - Slice / 2);   // mirrored inside each slice
        double v = EffectMath.Noise(folded * 4 + 3, r * 0.45 - ctx.Time * 0.6);
        double level = 0.25 + 0.75 * Smooth(0.25, 0.75, v);
        var color = ctx.Settings.AutoColor ? Rgb.FromHsv(v * 260 + r * 12 + ctx.Time * 30) : ctx.Blend(v, folded);
        return color.Scale(level);
    }
}

/// <summary>Spiral arms turning round the centre.</summary>
public sealed class SpiralEffect : KeyShader
{
    public override string Id => "pat-spiral";
    public override string Name => "Spiral";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double dx = x - Centre.X, dy = (y - Centre.Y) * 1.5;
        double r = Math.Sqrt(dx * dx + dy * dy), angle = Math.Atan2(dy, dx);
        double s = angle / (2 * Math.PI) * 3 + r * 0.28 - ctx.Time * 0.5;
        double arm = Math.Pow(Wave(2 * Math.PI * s), 2);
        var color = ctx.Settings.AutoColor ? Rgb.FromHsv(s * 120) : ctx.Blend(Fract(s * 0.5) < 0.5 ? 0 : 1, r / 9);
        return color.Scale(0.06 + 0.94 * arm);
    }
}

/// <summary>Rings rushing out of the middle, as if flying down a tunnel.</summary>
public sealed class TunnelEffect : KeyShader
{
    public override string Id => "pat-tunnel";
    public override string Name => "Tunnel";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double dx = x - Centre.X, dy = (y - Centre.Y) * 2.2;
        double r = Math.Sqrt(dx * dx + dy * dy);
        double depth = Math.Log(r + 0.6) * 3 - ctx.Time * 1.4;
        int ring = (int)Math.Floor(depth);
        double edge = Smooth(0, 0.25, Fract(depth)) * Smooth(1, 0.75, Fract(depth));
        double near = Math.Clamp(r / 7, 0.15, 1);
        var color = ctx.Settings.AutoColor ? Rgb.FromHsv(ring * 40) : ctx.Blend((ring & 1) == 0 ? 0 : 1, 0.5);
        return color.Scale(near * (0.35 + 0.65 * edge));
    }
}

/// <summary>A checkerboard of the two colours, its squares swapping in a wave from left to right.</summary>
public sealed class CheckerboardEffect : KeyShader
{
    public override string Id => "pat-checker";
    public override string Name => "Checkerboard";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        bool even = ((KeyGrid.Column(cell) + KeyGrid.Row(cell)) & 1) == 0;
        double swap = Smooth(-0.6, 0.6, Math.Sin(2 * Math.PI * (ctx.Time / 4 - x / 25)));
        double t = even ? swap : 1 - swap;
        return ctx.Blend(t, KeyGeometry.U(cell));
    }
}

/// <summary>Bold diagonal stripes sliding across.</summary>
public sealed class StripesEffect : KeyShader
{
    public override string Id => "pat-stripes";
    public override string Name => "Diagonal stripes";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double s = (x + y * 0.9) * 0.3 - ctx.Time * 0.55;
        double band = Smooth(0.35, 0.65, Wave(2 * Math.PI * s));
        return ctx.Blend(band, KeyGeometry.U(cell));
    }
}

/// <summary>Two sets of rings drifting over each other, making shifting moiré shapes.</summary>
public sealed class MoireEffect : KeyShader
{
    double ax, ay, bx, by;

    public override string Id => "pat-moire";
    public override string Name => "Moiré";
    public override string Category => EffectCategory.Patterns;

    protected override void Prepare(in EffectContext ctx)
    {
        ax = Centre.X + 5 * Math.Sin(ctx.Time * 0.31);
        ay = Centre.Y + 2 * Math.Cos(ctx.Time * 0.43);
        bx = Centre.X - 5 * Math.Sin(ctx.Time * 0.27 + 1);
        by = Centre.Y - 2 * Math.Cos(ctx.Time * 0.37 + 2);
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double d1 = Math.Sqrt((x - ax) * (x - ax) + (y - ay) * (y - ay));
        double d2 = Math.Sqrt((x - bx) * (x - bx) + (y - by) * (y - by));
        double v = Math.Cos(d1 * 2.1) * Math.Cos(d2 * 2.1);
        var color = ctx.Settings.AutoColor ? Rgb.FromHsv(d1 * 25 + ctx.Time * 30) : ctx.Blend(Wave(d2), KeyGeometry.U(cell));
        return color.Scale(0.1 + 0.9 * Smooth(-0.2, 0.8, v));
    }
}

/// <summary>Cells of colour that drift and push against each other, with dark walls between them.</summary>
public sealed class LivingCellsEffect : KeyShader
{
    const int Seeds = 9;
    readonly (double X, double Y)[] seeds = new (double, double)[Seeds];

    public override string Id => "pat-cells";
    public override string Name => "Living cells";
    public override string Category => EffectCategory.Patterns;

    protected override void Prepare(in EffectContext ctx)
    {
        for (int i = 0; i < Seeds; i++)
        {
            double a = Hash(i + 7), b = Hash(i + 99);
            seeds[i] = (KeyGeometry.Width * (0.5 + 0.48 * Math.Sin(ctx.Time * (0.11 + 0.1 * a) + a * 40)),
                        KeyGeometry.Height * (0.5 + 0.5 * Math.Sin(ctx.Time * (0.13 + 0.1 * b) + b * 40)));
        }
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double first = double.MaxValue, second = double.MaxValue;
        int owner = 0;
        for (int i = 0; i < Seeds; i++)
        {
            double d = Math.Sqrt((x - seeds[i].X) * (x - seeds[i].X) + (y - seeds[i].Y) * (y - seeds[i].Y) * 1.5);
            if (d < first) { second = first; first = d; owner = i; }
            else if (d < second) second = d;
        }
        double wall = Smooth(0.15, 0.7, second - first);
        var color = ctx.Settings.AutoColor ? Rgb.FromHsv(owner * 40 + ctx.Time * 8) : ctx.Blend(Hash(owner + 3), 0.5);
        return color.Scale(0.1 + 0.9 * wall);
    }
}

/// <summary>Arrow shapes marching to the right.</summary>
public sealed class ChevronsEffect : KeyShader
{
    public override string Id => "pat-chevrons";
    public override string Name => "Chevrons";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double s = (x - Math.Abs(y - Centre.Y) * 1.1) * 0.28 - ctx.Time * 0.7;
        double band = Smooth(0.4, 0.6, Fract(s));
        return ctx.Blend(band, KeyGeometry.U(cell)).Scale(0.3 + 0.7 * band);
    }
}

/// <summary>Diamond rings growing out of the middle.</summary>
public sealed class DiamondsEffect : KeyShader
{
    public override string Id => "pat-diamonds";
    public override string Name => "Diamonds";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double s = (Math.Abs(x - Centre.X) + Math.Abs(y - Centre.Y) * 1.7) * 0.32 - ctx.Time * 0.6;
        double ring = Math.Pow(Wave(2 * Math.PI * s), 1.5);
        var color = ctx.Settings.AutoColor ? Rgb.FromHsv(Math.Floor(s) * 50) : ctx.Blend(Fract(s * 0.5) < 0.5 ? 0 : 1, 0.5);
        return color.Scale(0.08 + 0.92 * ring);
    }
}

/// <summary>Waves from two points, adding up where they meet and cancelling out between.</summary>
public sealed class InterferenceEffect : KeyShader
{
    public override string Id => "pat-interference";
    public override string Name => "Interference";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double sx = 3.5 * Math.Sin(ctx.Time * 0.2);
        double d1 = Math.Sqrt((x - 3 - sx) * (x - 3 - sx) + (y - Centre.Y) * (y - Centre.Y));
        double d2 = Math.Sqrt((x - KeyGeometry.Width + 3 - sx) * (x - KeyGeometry.Width + 3 - sx) + (y - Centre.Y) * (y - Centre.Y));
        double sum = (Math.Sin(d1 * 1.5 - ctx.Time * 2.5) + Math.Sin(d2 * 1.5 - ctx.Time * 2.5)) / 2;
        double level = sum * sum;
        return EffectMath.Max(ctx.Secondary(KeyGeometry.U(cell)).Scale(0.08), ctx.Primary(KeyGeometry.U(cell)).Scale(level));
    }
}

/// <summary>Keys turn from colour 1 to colour 2 one at a time in a scattered order, then back again.</summary>
public sealed class DissolveEffect : KeyShader
{
    public override string Id => "pat-dissolve";
    public override string Name => "Dissolve";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double progress = 0.5 - 0.5 * Math.Cos(2 * Math.PI * ctx.Time / 10);
        double threshold = 0.05 + 0.9 * Hash(cell + 500);
        return ctx.Blend(Smooth(threshold - 0.04, threshold + 0.04, progress), KeyGeometry.U(cell));
    }
}

/// <summary>A gradient from colour 1 to colour 2 that slowly turns round the keyboard.</summary>
public sealed class TurningGradientEffect : KeyShader
{
    double cos, sin;

    public override string Id => "pat-turning-gradient";
    public override string Name => "Turning gradient";
    public override string Category => EffectCategory.Patterns;

    protected override void Prepare(in EffectContext ctx)
    {
        cos = Math.Cos(ctx.Time * 0.45);
        sin = Math.Sin(ctx.Time * 0.45);
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double along = ((x - Centre.X) * cos + (y - Centre.Y) * 2.5 * sin) / KeyGeometry.Width + 0.5;
        return ctx.Settings.AutoColor ? Rgb.FromHsv(along * 300) : Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, along);
    }
}

/// <summary>Three ribbons of light weaving across the keyboard.</summary>
public sealed class RibbonsEffect : KeyShader
{
    public override string Id => "pat-ribbons";
    public override string Name => "Ribbons";
    public override string Category => EffectCategory.Patterns;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        var color = Rgb.Black;
        double u = KeyGeometry.U(cell);
        for (int i = 0; i < 3; i++)
        {
            double centre = Centre.Y + 1.9 * Math.Sin(x * (0.3 + 0.07 * i) + ctx.Time * (0.7 + 0.25 * i) + i * 2.1);
            double level = Glow(y - centre, 0.6);
            var tint = ctx.Settings.AutoColor ? Rgb.FromHsv(i * 120 + ctx.Time * 20) : ctx.Blend(i / 2.0, u);
            color = EffectMath.Max(color, tint.Scale(level));
        }
        return color;
    }
}
