using RaptorSense.Layout;

namespace RaptorSense.Lighting;

/// <summary>Key positions for effects, in key pitches (1 = one key), from <see cref="PhysicalLayout"/>.</summary>
public static class KeyGeometry
{
    /// <summary>Cells that have a key, left to right within each row, top row first.</summary>
    public static IReadOnlyList<int> Cells { get; }

    public static double Width { get; } = PhysicalLayout.Width / PhysicalLayout.Pitch;
    public static double Height { get; } = PhysicalLayout.Height / PhysicalLayout.Pitch;

    static readonly double[] xs = new double[Frame.CellCount], ys = new double[Frame.CellCount];

    static KeyGeometry()
    {
        var cells = new List<int>();
        for (int cell = 0; cell < Frame.CellCount; cell++)
        {
            if (PhysicalLayout.ForCell(cell) is not { } r) continue;
            xs[cell] = r.CenterX / PhysicalLayout.Pitch;
            ys[cell] = r.CenterY / PhysicalLayout.Pitch;
            cells.Add(cell);
        }
        Cells = cells.OrderBy(c => Math.Round(ys[c], 1)).ThenBy(c => xs[c]).ToList();
    }

    public static double X(int cell) => xs[cell];
    public static double Y(int cell) => ys[cell];

    /// <summary>0 at the left edge, 1 at the right.</summary>
    public static double U(int cell) => xs[cell] / Width;

    /// <summary>0 at the top (F row), 1 at the bottom (space row).</summary>
    public static double V(int cell) => ys[cell] / Height;

    public static double Distance(int cell, double x, double y) => Math.Sqrt((xs[cell] - x) * (xs[cell] - x) + (ys[cell] - y) * (ys[cell] - y));
}

public static class EffectMath
{
    /// <summary>Smooth 2D value noise in 0..1.</summary>
    public static double Noise(double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        double sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        double a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        return a + (b - a) * sx + (c - a) * sy + (a - b - c + d) * sx * sy;
    }

    static double Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) / (double)uint.MaxValue;
        }
    }

    /// <summary>Black, red, orange, yellow, white as heat goes 0..1.</summary>
    public static Rgb Fire(double heat)
    {
        heat = Math.Clamp(heat, 0, 1);
        if (heat < 0.35) return Rgb.Lerp(Rgb.Black, new Rgb(200, 10, 0), heat / 0.35);
        if (heat < 0.65) return Rgb.Lerp(new Rgb(200, 10, 0), new Rgb(255, 110, 0), (heat - 0.35) / 0.3);
        if (heat < 0.9) return Rgb.Lerp(new Rgb(255, 110, 0), new Rgb(255, 220, 40), (heat - 0.65) / 0.25);
        return Rgb.Lerp(new Rgb(255, 220, 40), Rgb.White, (heat - 0.9) / 0.1);
    }

    /// <summary>Brightest-wins blend, so overlapping lights don't wash each other out.</summary>
    public static Rgb Max(Rgb a, Rgb b) => new(Math.Max(a.R, b.R), Math.Max(a.G, b.G), Math.Max(a.B, b.B));
}
