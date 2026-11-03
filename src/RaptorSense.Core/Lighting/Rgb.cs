using System.Globalization;

namespace RaptorSense.Lighting;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);

    /// <summary>The keyboard's factory colour.</summary>
    public static readonly Rgb PredatorTeal = new(0x00, 0xAE, 0xC7);

    /// <summary>Hue in degrees (any value, wraps), saturation and value 0..1.</summary>
    public static Rgb FromHsv(double hue, double saturation = 1, double value = 1)
    {
        hue = ((hue % 360) + 360) % 360;
        double c = value * saturation;
        double x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        double m = value - c;
        (double r, double g, double b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return new Rgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    public static Rgb Parse(string hex)
    {
        if (!TryParse(hex, out var rgb)) throw new FormatException($"Colour must be RRGGBB, got '{hex}'.");
        return rgb;
    }

    public static bool TryParse(string? hex, out Rgb rgb)
    {
        rgb = Black;
        if (hex is null) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v)) return false;
        rgb = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(ToByte((a.R + (b.R - a.R) * t) / 255), ToByte((a.G + (b.G - a.G) * t) / 255), ToByte((a.B + (b.B - a.B) * t) / 255));
    }

    public Rgb Scale(double factor) => new(ToByte(R * factor / 255), ToByte(G * factor / 255), ToByte(B * factor / 255));

    public override string ToString() => $"{R:X2}{G:X2}{B:X2}";

    static byte ToByte(double unit) => (byte)Math.Clamp(Math.Round(unit * 255), 0, 255);
}
