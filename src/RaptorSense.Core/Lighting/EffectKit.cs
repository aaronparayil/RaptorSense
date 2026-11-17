namespace RaptorSense.Lighting;

// Shared pieces for the effect collections (Ambient, Patterns, Space and the rest). Positions are in key pitches,
// from KeyGeometry; times are the speed-scaled ctx.Time unless a comment says otherwise.
//
// Colours: the abstract effects (patterns, sci-fi, retro, music, typing) use colour 1 and colour 2, or the rainbow
// with Auto colour. The ones that picture something (a sunset, the ocean, a candle, a festival) keep its own colours
// whatever the colour setting, because a purple ocean or green lava just looks wrong.

/// <summary>An effect that works out each key's colour from where it is and the time, with no memory between frames.</summary>
public abstract class KeyShader : IEffect
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Category { get; }
    public virtual bool UsesAudio => false;
    public virtual bool UsesKeys => false;
    public virtual bool WorksAsOverlay => false;

    public void Render(Frame frame, in EffectContext ctx)
    {
        Prepare(ctx);
        foreach (int cell in KeyGeometry.Cells) frame[cell] = Shade(cell, KeyGeometry.X(cell), KeyGeometry.Y(cell), ctx);
    }

    /// <summary>Runs once per frame before the keys are shaded.</summary>
    protected virtual void Prepare(in EffectContext ctx) { }

    protected abstract Rgb Shade(int cell, double x, double y, in EffectContext ctx);
}

/// <summary>
/// An effect that keeps state from frame to frame (particles, a game board). It starts again when the clock goes
/// backwards, which is what a new engine run looks like, and steps by the frame time with the speed applied.
/// </summary>
public abstract class SteppedEffect : IEffect
{
    protected readonly Random Random = new();
    double lastTime = double.NegativeInfinity;

    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Category { get; }
    public virtual bool UsesAudio => false;
    public virtual bool UsesKeys => false;
    public virtual bool WorksAsOverlay => false;

    public void Render(Frame frame, in EffectContext ctx)
    {
        if (ctx.Time < lastTime) Reset();
        lastTime = ctx.Time;
        Step(frame, ctx, Math.Clamp(ctx.DeltaTime * ctx.Settings.Speed, 0, 0.1));
    }

    /// <summary>Forgets everything, as at the very start.</summary>
    protected virtual void Reset() { }

    /// <param name="dt">Seconds since the last frame, times the speed setting, at most 0.1.</param>
    protected abstract void Step(Frame frame, in EffectContext ctx, double dt);

    protected int RandomCell() => KeyGeometry.Cells[Random.Next(KeyGeometry.Cells.Count)];
}

/// <summary>
/// The keys as a coarse grid of 19 columns by 6 rows, for effects that think in pixels (games, bars). Row 0 is the
/// F row. A wide key takes the pixel under its middle, and keys that share a pixel show the same thing.
/// </summary>
public static class KeyGrid
{
    public const int Columns = 19, Rows = 6;

    static readonly int[] columns = new int[Frame.CellCount], rows = new int[Frame.CellCount];

    static KeyGrid()
    {
        foreach (int cell in KeyGeometry.Cells)
        {
            columns[cell] = Math.Clamp((int)Math.Floor(KeyGeometry.X(cell)), 0, Columns - 1);
            rows[cell] = Math.Clamp((int)Math.Round(KeyGeometry.Y(cell)), 0, Rows - 1);
        }
    }

    public static int Column(int cell) => columns[cell];
    public static int Row(int cell) => rows[cell];
}

public static class Shaping
{
    /// <summary>0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, smooth in between.</summary>
    public static double Smooth(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>A soft round light: 1 at the centre, falling off over about <paramref name="radius"/>.</summary>
    public static double Glow(double distance, double radius) => Math.Exp(-(distance * distance) / (radius * radius));

    public static double Fract(double x) => x - Math.Floor(x);

    /// <summary>0.5 + 0.5·sin, so 0..1.</summary>
    public static double Wave(double phase) => 0.5 + 0.5 * Math.Sin(phase);

    /// <summary>A steady random number 0..1 for a whole number, the same every time.</summary>
    public static double Hash(int n)
    {
        unchecked
        {
            uint h = (uint)n * 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return h / (double)uint.MaxValue;
        }
    }

    /// <summary>Two octaves of <see cref="EffectMath.Noise"/>, still 0..1, with more detail.</summary>
    public static double Cloud(double x, double y) => EffectMath.Noise(x, y) * 0.65 + EffectMath.Noise(x * 2.1 + 31, y * 2.1 + 17) * 0.35;

    /// <summary>Distance from (px, py) to the segment from (ax, ay) to (bx, by).</summary>
    public static double ToSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay, length = dx * dx + dy * dy;
        double t = length == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / length, 0, 1);
        double cx = ax + t * dx - px, cy = ay + t * dy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }

    /// <summary>Picks along a list of colours, <paramref name="t"/> 0..1, blending between neighbours.</summary>
    public static Rgb Palette(double t, params Rgb[] stops)
    {
        t = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        int i = Math.Min((int)t, stops.Length - 2);
        return Rgb.Lerp(stops[i], stops[i + 1], t - i);
    }

    /// <summary>Like <see cref="Palette"/>, but going round: past the last colour comes the first again.</summary>
    public static Rgb Cycle(double t, params Rgb[] stops)
    {
        t = Fract(t) * stops.Length;
        int i = (int)t % stops.Length;
        return Rgb.Lerp(stops[i], stops[(i + 1) % stops.Length], t - Math.Floor(t));
    }

    /// <summary>The colour with its hue turned by <paramref name="degrees"/>.</summary>
    public static Rgb Turn(Rgb color, double degrees)
    {
        var (h, s, v) = color.ToHsv();
        return Rgb.FromHsv(h + degrees, s, v);
    }

    /// <summary>Adds two lights, each channel capped at full.</summary>
    public static Rgb Add(Rgb a, Rgb b) => new((byte)Math.Min(255, a.R + b.R), (byte)Math.Min(255, a.G + b.G), (byte)Math.Min(255, a.B + b.B));
}
