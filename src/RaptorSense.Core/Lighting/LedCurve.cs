namespace RaptorSense.Lighting;

/// <summary>
/// Turns the colours effects draw, which are meant for a screen, into what the keyboard's LEDs are given.
/// </summary>
/// <remarks>
/// A screen darkens low values on purpose (its gamma), so a key drawn at (10, 0, 5) looks black in the preview. An LED
/// is driven straight: 10 out of 255 is a clearly visible glow, especially in a dark room. Without this curve every
/// "dark" background and the tail of every fade glowed on the keys, and mid tones looked washed out.
/// Raising each channel to the power 2.2, as a screen does, makes the keys match the preview: what looks black goes
/// fully off (everything up to 20 out of 255), full colours stay full, and pastels keep their colour.
/// </remarks>
public static class LedCurve
{
    public const double Exponent = 2.2;

    static readonly byte[] table = Build();

    static byte[] Build()
    {
        var t = new byte[256];
        // Rounded down, so the darkest tones reach 0 rather than a faint 1 that an LED still shows.
        for (int i = 0; i < 256; i++) t[i] = (byte)Math.Floor(255 * Math.Pow(i / 255.0, Exponent) + 1e-9);
        return t;
    }

    public static byte Apply(byte value) => table[value];

    public static Rgb Apply(Rgb color) => new(table[color.R], table[color.G], table[color.B]);
}
