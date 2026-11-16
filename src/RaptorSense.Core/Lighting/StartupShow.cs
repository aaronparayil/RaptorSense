using RaptorSense.Layout;

namespace RaptorSense.Lighting;

/// <summary>
/// A few seconds of party lights when RaptorSense starts (which is when the laptop starts, with "Start with
/// Windows" on): a spinning rainbow, strobes, a rainbow scanner, colour pops and a white finale, then the
/// user's own effect. Works on both keyboards: by key position on the Helios, by zone on the Nitro, with steps no
/// faster than the Nitro's ten updates a second.
/// </summary>
public sealed class StartupShow(LaptopLayout layout) : IEffect
{
    /// <summary>How long the show runs, in seconds.</summary>
    public const double Duration = 6.6;

    // When each part starts, in seconds.
    const double Strobe = 1.5, Scanner = 2.7, Pops = 4.2, Finale = 5.4;

    double? start;

    public string Id => "startup-show";
    public string Name => "Start-up show";

    /// <summary>Starts the show again from the beginning at the next frame.</summary>
    public void Restart() => start = null;

    /// <summary>Seconds into the show at the last frame drawn (0 before the first).</summary>
    public double Elapsed { get; private set; }

    public bool Finished => Elapsed >= Duration;

    public void Render(Frame frame, in EffectContext ctx)
    {
        start ??= ctx.RealTime;
        Elapsed = ctx.RealTime - start.Value;
        double t = Elapsed;

        if (layout == LaptopLayout.Nitro)
        {
            foreach (int cell in KeyGeometry.Cells)
            {
                int zone = FourZoneLayout.ZoneOf(cell);
                if (zone >= 0) frame[cell] = ColorAt((zone + 0.5) / 4, zone, t);
            }
            return;
        }
        // On the Helios, 22 columns across: the same show, in finer steps.
        foreach (int cell in KeyGeometry.Cells)
        {
            double u = KeyGeometry.U(cell);
            frame[cell] = ColorAt(u, (int)Math.Floor(u * 22), t);
        }
    }

    /// <summary>
    /// The show's colour at <paramref name="u"/> (0 left .. 1 right) and <paramref name="t"/> seconds in.
    /// <paramref name="slot"/> is which zone (or column) it is, for the random pops.
    /// </summary>
    public static Rgb ColorAt(double u, int slot, double t)
    {
        if (t < 0 || t >= Duration) return Rgb.Black;

        if (t < Strobe)
        {
            // Spinning rainbow that speeds up, fading in over the first half second.
            double spin = t * 300 + t * t * 160;
            return Rgb.FromHsv(u * 360 - spin).Scale(Math.Min(1, t / 0.5));
        }
        if (t < Scanner)
        {
            // Strobe: the two halves flash in turn, each flash a new colour.
            int step = (int)Math.Floor((t - Strobe) / 0.15);
            bool left = u < 0.5;
            bool lit = step % 2 == 0 ? left : !left;
            return lit ? Rgb.FromHsv(step * 53) : Rgb.Black;
        }
        if (t < Pops)
        {
            // A rainbow scanner bouncing end to end, twice a second.
            double phase = (t - Scanner) / 0.75 % 1;
            double position = phase < 0.5 ? phase * 2 : 2 - phase * 2;
            double level = Math.Exp(-Math.Abs(u - position) * 5);
            return Rgb.FromHsv(t * 320 + u * 90).Scale(Math.Max(0.05, level));
        }
        if (t < Finale)
        {
            // Party pops: every step, each slot is a random bright colour or dark.
            int step = (int)Math.Floor((t - Pops) / 0.12);
            uint h = Hash(step, slot);
            return (h & 3) == 0 ? Rgb.Black : Rgb.FromHsv(h % 360);
        }
        // Finale: two white flashes, then a rainbow that fades out into the user's effect.
        double f = t - Finale;
        if (f < 0.12 || (f >= 0.3 && f < 0.42)) return Rgb.White;
        if (f < 0.6) return Rgb.Black;
        double fade = 1 - (f - 0.6) / (Duration - Finale - 0.6);
        return Rgb.FromHsv(u * 360 + f * 200).Scale(Math.Clamp(fade, 0, 1));
    }

    static uint Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 374761393 + b * 668265263 + 1442695041);
            h = (h ^ (h >> 13)) * 1274126177;
            return h ^ (h >> 16);
        }
    }
}
