namespace RaptorSense.Lighting;

/// <summary>
/// Ready-made animations to start from instead of a blank keyboard. They are ordinary frames, built from real key
/// positions, so everything about them can be repainted or retimed in the editor afterwards.
/// </summary>
public static class AnimationStarters
{
    public static IReadOnlyList<string> Names { get; } = ["Sweep", "Pulse", "Ripple", "Waterfall", "Rainbow drift"];

    /// <summary>Builds the starter called <paramref name="name"/> in <paramref name="color"/>, or null if there is none.</summary>
    public static KeyAnimation? Create(string name, Rgb color)
    {
        var frames = name switch
        {
            "Sweep" => Sweep(color),
            "Pulse" => Pulse(color),
            "Ripple" => Ripple(color),
            "Waterfall" => Waterfall(color),
            "Rainbow drift" => RainbowDrift(),
            _ => null,
        };
        return frames is null ? null : new KeyAnimation { Name = name, Frames = frames };
    }

    /// <summary>A soft column of light crossing the keyboard left to right.</summary>
    static List<AnimationFrame> Sweep(Rgb color)
    {
        var frames = new List<AnimationFrame>();
        for (double at = -1; at <= KeyGeometry.Width + 1; at += 1)
            frames.Add(Paint(cell => color.Scale(Glow(KeyGeometry.X(cell) - at, 1.6)), hold: 0.03, fade: 0.05));
        return frames;
    }

    /// <summary>The whole keyboard breathing between bright and dim.</summary>
    static List<AnimationFrame> Pulse(Rgb color) =>
    [
        Paint(_ => color, hold: 0.15, fade: 1.2),
        Paint(_ => color.Scale(0.12), hold: 0.15, fade: 1.2),
    ];

    /// <summary>A ring spreading out from the middle of the keyboard, then a short rest.</summary>
    static List<AnimationFrame> Ripple(Rgb color)
    {
        double centreX = KeyGeometry.Width / 2, centreY = KeyGeometry.Height / 2;
        double reach = Math.Sqrt(centreX * centreX + centreY * centreY) + 1;
        var frames = new List<AnimationFrame>();
        for (double radius = 0; radius <= reach; radius += 0.75)
        {
            double strength = 1 - 0.6 * radius / reach;   // fades as it spreads
            double r = radius;
            frames.Add(Paint(cell => color.Scale(strength * Glow(KeyGeometry.Distance(cell, centreX, centreY) - r, 1.3)), hold: 0.03, fade: 0.06));
        }
        frames.Add(Paint(_ => Rgb.Black, hold: 0.5, fade: 0));
        return frames;
    }

    /// <summary>A band of light falling from the F row to the space row.</summary>
    static List<AnimationFrame> Waterfall(Rgb color)
    {
        var frames = new List<AnimationFrame>();
        for (double at = -0.5; at <= KeyGeometry.Height + 0.5; at += 0.5)
            frames.Add(Paint(cell => color.Scale(Glow(KeyGeometry.Y(cell) - at, 1.1)), hold: 0.04, fade: 0.06));
        return frames;
    }

    /// <summary>A rainbow across the keyboard that slides sideways. Every frame is pure fade, so it never jumps.</summary>
    static List<AnimationFrame> RainbowDrift()
    {
        const int Steps = 12;
        var frames = new List<AnimationFrame>();
        for (int i = 0; i < Steps; i++)
        {
            double shift = i * 360.0 / Steps;
            frames.Add(Paint(cell => Rgb.FromHsv(KeyGeometry.U(cell) * 360 - shift), hold: 0, fade: 0.25));
        }
        return frames;
    }

    /// <summary>1 at the centre of a glow, falling to 0 at <paramref name="width"/> key pitches away.</summary>
    static double Glow(double distance, double width) => Math.Max(0, 1 - Math.Abs(distance) / width);

    static AnimationFrame Paint(Func<int, Rgb> colorOf, double hold, double fade)
    {
        var cells = new Rgb[Frame.CellCount];
        foreach (int cell in KeyGeometry.Cells) cells[cell] = colorOf(cell);
        return AnimationFrame.FromCells(cells, hold, fade);
    }
}
