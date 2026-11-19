using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// More Typing effects. Except Shadow keys, they draw on black, so each also works as a typing overlay.

/// <summary>Every key you press lights in the next colour of the rainbow, then fades.</summary>
public sealed class RainbowKeysEffect : PressEffect<(int Cell, double Hue)>
{
    double hue;

    public override string Id => "typ-rainbow";
    public override string Name => "Rainbow keys";
    protected override double Lifetime => 1.2;
    protected override int MaxActive => 40;

    protected override (int Cell, double Hue) Spawn(int cell) => (cell, hue += 31);

    protected override void Draw(Frame frame, in EffectContext ctx, (int Cell, double Hue) press, double age, Rgb color)
    {
        double level = 1 - age / Lifetime;
        Light(frame, press.Cell, Rgb.FromHsv(press.Hue), level * level);
    }
}

/// <summary>A glowing line joins each key you press to the one before it.</summary>
public sealed class LightTrailEffect : PressEffect<(double X0, double Y0, double X1, double Y1)>
{
    double lastX = double.NaN, lastY;

    public override string Id => "typ-trail";
    public override string Name => "Light trail";
    protected override double Lifetime => 0.9;

    protected override (double X0, double Y0, double X1, double Y1) Spawn(int cell)
    {
        double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
        var line = double.IsNaN(lastX) ? (x, y, x, y) : (lastX, lastY, x, y);
        (lastX, lastY) = (x, y);
        return line;
    }

    protected override void Draw(Frame frame, in EffectContext ctx, (double X0, double Y0, double X1, double Y1) line, double age, Rgb color)
    {
        double fade = 1 - age / Lifetime;
        foreach (int cell in KeyGeometry.Cells)
        {
            double d = ToSegment(KeyGeometry.X(cell), KeyGeometry.Y(cell), line.X0, line.Y0, line.X1, line.Y1);
            Light(frame, cell, color, (1 - d / 0.55) * fade);
        }
    }
}

/// <summary>A beam of light shoots up the column from the key you press.</summary>
public sealed class ColumnBeamEffect : PressEffect<(double X, double Y)>
{
    public override string Id => "typ-column";
    public override string Name => "Column beam";
    protected override double Lifetime => 0.7;

    protected override (double X, double Y) Spawn(int cell) => (KeyGeometry.X(cell), KeyGeometry.Y(cell));

    protected override void Draw(Frame frame, in EffectContext ctx, (double X, double Y) at, double age, Rgb color)
    {
        double front = at.Y - age * 16, fade = 1 - age / Lifetime;
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            if (Math.Abs(x - at.X) > 0.75 || y > at.Y + 0.2 || y < front - 0.3) continue;
            double head = Glow(y - front, 0.6);
            Light(frame, cell, Rgb.Lerp(color, Rgb.White, head * 0.5), (0.45 + 0.55 * head) * fade * (1 - Math.Abs(x - at.X) / 0.9));
        }
    }
}

/// <summary>Light runs out left and right along the row of the key you press.</summary>
public sealed class RowSweepEffect : PressEffect<(double X, int Row)>
{
    public override string Id => "typ-row-sweep";
    public override string Name => "Row sweep";
    protected override double Lifetime => 0.9;

    protected override (double X, int Row) Spawn(int cell) => (KeyGeometry.X(cell), KeyGrid.Row(cell));

    protected override void Draw(Frame frame, in EffectContext ctx, (double X, int Row) at, double age, Rgb color)
    {
        double fade = 1 - age / Lifetime;
        foreach (int cell in KeyGeometry.Cells)
        {
            if (KeyGrid.Row(cell) != at.Row) continue;
            double d = Math.Abs(KeyGeometry.X(cell) - at.X);
            double ring = Math.Max(1 - Math.Abs(d - age * 18) / 1.3, d < 0.6 ? 1 - age * 3 : 0);
            Light(frame, cell, color, ring * fade);
        }
    }
}

/// <summary>Each key you press bursts into sparks that arc out and fall.</summary>
public sealed class KeyFireworksEffect : PressEffect<(double X, double Y, (double Vx, double Vy, double Hue)[] Sparks)>
{
    public override string Id => "typ-fireworks";
    public override string Name => "Key fireworks";
    protected override double Lifetime => 1.0;
    protected override int MaxActive => 12;

    protected override (double X, double Y, (double Vx, double Vy, double Hue)[] Sparks) Spawn(int cell)
    {
        var sparks = new (double, double, double)[9];
        double hue = Random.NextDouble() * 360;
        for (int i = 0; i < sparks.Length; i++)
        {
            double a = i * Math.PI * 2 / sparks.Length + Random.NextDouble() * 0.4, speed = 3 + Random.NextDouble() * 2.5;
            sparks[i] = (Math.Cos(a) * speed, Math.Sin(a) * speed * 0.7 - 1.5, hue + Random.NextDouble() * 40);
        }
        return (KeyGeometry.X(cell), KeyGeometry.Y(cell), sparks);
    }

    protected override void Draw(Frame frame, in EffectContext ctx, (double X, double Y, (double Vx, double Vy, double Hue)[] Sparks) burst, double age, Rgb color)
    {
        double fade = 1 - age / Lifetime;
        foreach (var (vx, vy, hue) in burst.Sparks)
        {
            double sx = burst.X + vx * age, sy = burst.Y + vy * age + 4 * age * age;
            var tint = ctx.Settings.AutoColor ? Rgb.FromHsv(hue) : Rgb.Lerp(color, Rgb.FromHsv(hue), 0.3);
            foreach (int cell in KeyGeometry.Cells)
            {
                double level = 1 - KeyGeometry.Distance(cell, sx, sy) / 0.6;
                if (level > 0) Light(frame, cell, tint, level * fade * fade);
            }
        }
    }
}

/// <summary>Pressed keys freeze icy white, with a touch of frost on their neighbours, then thaw.</summary>
public sealed class FrostKeysEffect : PressEffect<(double X, double Y)>
{
    static readonly Rgb Ice = new(200, 235, 255);

    public override string Id => "typ-frost";
    public override string Name => "Frost keys";
    protected override double Lifetime => 2.5;
    protected override int MaxActive => 40;

    protected override (double X, double Y) Spawn(int cell) => (KeyGeometry.X(cell), KeyGeometry.Y(cell));

    protected override void Draw(Frame frame, in EffectContext ctx, (double X, double Y) at, double age, Rgb color)
    {
        double thaw = 1 - age / Lifetime;
        var ice = ctx.Settings.AutoColor ? Rgb.Lerp(Ice, color, 0.5) : Ice;
        foreach (int cell in KeyGeometry.Cells)
        {
            double d = KeyGeometry.Distance(cell, at.X, at.Y);
            if (d > 1.5) continue;
            Light(frame, cell, d < 0.5 ? ice : Rgb.Lerp(ice, new Rgb(40, 120, 220), 0.6), (d < 0.5 ? 1 : 0.45 * (1.5 - d)) * thaw);
        }
    }
}

/// <summary>Your typing paints the keyboard: each key you press keeps its colour, fading only slowly.</summary>
public sealed class PaintAsYouTypeEffect : PressEffect<(int Cell, double Hue)>
{
    double hue;

    public override string Id => "typ-paint";
    public override string Name => "Paint as you type";
    protected override double Lifetime => 20;
    protected override int MaxActive => 300;

    protected override (int Cell, double Hue) Spawn(int cell) => (cell, hue += 13);

    protected override void Draw(Frame frame, in EffectContext ctx, (int Cell, double Hue) press, double age, Rgb color)
    {
        var paint = ctx.Settings.AutoColor ? Rgb.FromHsv(press.Hue) : Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, Wave(press.Hue * 0.05));
        Light(frame, press.Cell, paint, Math.Min(1, age * 20) * (1 - age / Lifetime));
    }
}

/// <summary>A soft glow blooms out round the key you press, then fades.</summary>
public sealed class BloomEffect : PressEffect<(double X, double Y)>
{
    public override string Id => "typ-bloom";
    public override string Name => "Bloom";
    protected override double Lifetime => 1.6;

    protected override (double X, double Y) Spawn(int cell) => (KeyGeometry.X(cell), KeyGeometry.Y(cell));

    protected override void Draw(Frame frame, in EffectContext ctx, (double X, double Y) at, double age, Rgb color)
    {
        double radius = 0.5 + 2 * Smooth(0, 0.6, age);
        double fade = age < 0.6 ? 1 : 1 - (age - 0.6) / (Lifetime - 0.6);
        foreach (int cell in KeyGeometry.Cells)
        {
            double d = KeyGeometry.Distance(cell, at.X, at.Y);
            if (d < radius * 2) Light(frame, cell, color, Glow(d, radius) * fade);
        }
    }
}

/// <summary>An electric arc crackles between the last two keys you pressed.</summary>
public sealed class ElectricArcEffect : PressEffect<ElectricArcEffect.Arc>
{
    public readonly record struct Arc((double X, double Y)[] Points);

    static readonly Rgb Spark = new(170, 200, 255);
    double lastX = double.NaN, lastY;

    public override string Id => "typ-arc";
    public override string Name => "Electric arc";
    protected override double Lifetime => 0.45;
    protected override int MaxActive => 10;

    protected override Arc Spawn(int cell)
    {
        double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
        double fromX = double.IsNaN(lastX) ? x - 1 : lastX, fromY = double.IsNaN(lastX) ? y : lastY;
        (lastX, lastY) = (x, y);
        const int Points = 7;
        var points = new (double X, double Y)[Points];
        double nx = -(y - fromY), ny = x - fromX, length = Math.Max(0.01, Math.Sqrt(nx * nx + ny * ny));
        for (int i = 0; i < Points; i++)
        {
            double t = i / (double)(Points - 1), jag = i is 0 or Points - 1 ? 0 : (Random.NextDouble() - 0.5) * 1.1;
            points[i] = (fromX + (x - fromX) * t + nx / length * jag, fromY + (y - fromY) * t + ny / length * jag);
        }
        return new Arc(points);
    }

    protected override void Draw(Frame frame, in EffectContext ctx, Arc arc, double age, Rgb color)
    {
        var points = arc.Points;
        double fade = 1 - age / Lifetime;
        var tint = ctx.Settings.AutoColor ? color : Rgb.Lerp(Spark, ctx.Settings.Color2, 0.35);
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell), d = double.MaxValue;
            for (int i = 1; i < points.Length; i++) d = Math.Min(d, ToSegment(x, y, points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y));
            Light(frame, cell, Rgb.Lerp(tint, Rgb.White, 0.3), (1 - d / 0.5) * fade);
        }
    }
}

/// <summary>The light of the key you press drops down to the bottom row and splashes out sideways.</summary>
public sealed class DroppingKeysEffect : PressEffect<(double X, double Y)>
{
    public override string Id => "typ-gravity";
    public override string Name => "Dropping keys";
    protected override double Lifetime => 1.3;

    protected override (double X, double Y) Spawn(int cell) => (KeyGeometry.X(cell), KeyGeometry.Y(cell));

    protected override void Draw(Frame frame, in EffectContext ctx, (double X, double Y) at, double age, Rgb color)
    {
        double floor = KeyGeometry.Height - 0.45;
        double fallTime = Math.Sqrt(Math.Max(0, floor - at.Y) / 12);
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            double level;
            if (age < fallTime) level = 1 - KeyGeometry.Distance(cell, at.X, at.Y + 12 * age * age) / 0.65;
            else
            {
                double since = age - fallTime, spread = since * 7;
                level = KeyGrid.Row(cell) == KeyGrid.Rows - 1 ? (1 - Math.Abs(Math.Abs(x - at.X) - spread) / 0.9) * (1 - since / (Lifetime - fallTime + 0.01)) : 0;
            }
            Light(frame, cell, color, level);
        }
    }
}

/// <summary>The keyboard glows in colour 1; the keys you press go dark and slowly light up again.</summary>
public sealed class ShadowKeysEffect : IEffect
{
    const double ReturnSeconds = 1.5;
    readonly double[] dark = new double[Frame.CellCount];

    public string Id => "typ-shadow";
    public string Name => "Shadow keys";
    public string Category => EffectCategory.Typing;
    public bool UsesKeys => true;

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int cell in ctx.Presses) if (cell is >= 0 and < Frame.CellCount) dark[cell] = 1;
        double recover = Math.Clamp(ctx.DeltaTime * ctx.Settings.Speed, 0, 0.1) / ReturnSeconds;
        foreach (int cell in KeyGeometry.Cells)
        {
            dark[cell] = Math.Max(0, dark[cell] - recover);
            frame[cell] = Rgb.Lerp(ctx.Primary(KeyGeometry.U(cell)), ctx.Secondary(KeyGeometry.U(cell)).Scale(0.15), Math.Sqrt(dark[cell]));
        }
    }
}

/// <summary>A press knocks on to the keys around it, which knock on to theirs, each a little weaker, like dominoes.</summary>
public sealed class ChainReactionEffect : PressEffect<(double X, double Y)>
{
    const double StepSeconds = 0.07, KeyFade = 0.35;

    public override string Id => "typ-chain";
    public override string Name => "Chain reaction";
    protected override double Lifetime => 1.5;

    protected override (double X, double Y) Spawn(int cell) => (KeyGeometry.X(cell), KeyGeometry.Y(cell));

    protected override void Draw(Frame frame, in EffectContext ctx, (double X, double Y) at, double age, Rgb color)
    {
        foreach (int cell in KeyGeometry.Cells)
        {
            int steps = (int)Math.Round(KeyGeometry.Distance(cell, at.X, at.Y) / 1.05);
            double since = age - steps * StepSeconds;
            if (since < 0 || since > KeyFade) continue;
            Light(frame, cell, color, Math.Pow(0.72, steps) * (1 - since / KeyFade));
        }
    }
}
