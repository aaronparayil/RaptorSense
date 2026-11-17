using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// Celebrations: festive lights. Bursts grow and fade over a good fraction of a second rather than flashing.

/// <summary>Rockets climb from the bottom row and burst into falling sparks.</summary>
public sealed class FireworksEffect : SteppedEffect
{
    readonly List<(double X, double Y, double Vy, double Hue)> rockets = [];
    readonly List<(double X, double Y, double Vx, double Vy, double Age, double Hue)> sparks = [];
    double next, time;

    public override string Id => "cel-fireworks";
    public override string Name => "Fireworks";
    public override string Category => EffectCategory.Celebrations;

    protected override void Reset() { rockets.Clear(); sparks.Clear(); next = time = 0; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        time += dt;
        if (time >= next)
        {
            rockets.Add((1.5 + Random.NextDouble() * (KeyGeometry.Width - 3), KeyGeometry.Height + 0.3, -(6 + Random.NextDouble() * 2),
                         Random.NextDouble() * 360));
            next = time + 0.7 + Random.NextDouble() * 0.7;
        }
        for (int i = rockets.Count - 1; i >= 0; i--)
        {
            var r = rockets[i];
            r = r with { Y = r.Y + r.Vy * dt, Vy = r.Vy + 5.5 * dt };
            if (r.Vy >= -1 || r.Y < 0.8)
            {
                for (int k = 0; k < 14; k++)
                {
                    double a = k * Math.PI * 2 / 14 + Random.NextDouble() * 0.3, speed = 3 + Random.NextDouble() * 1.5;
                    sparks.Add((r.X, r.Y, Math.Cos(a) * speed, Math.Sin(a) * speed * 0.7, 0, r.Hue + Random.NextDouble() * 30));
                }
                rockets.RemoveAt(i);
            }
            else rockets[i] = r;
        }
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var s = sparks[i];
            double drag = Math.Exp(-dt * 2);
            s = (s.X + s.Vx * dt, s.Y + s.Vy * dt, s.Vx * drag, s.Vy * drag + 2.5 * dt, s.Age + dt, s.Hue);
            if (s.Age > 1.5) sparks.RemoveAt(i);
            else sparks[i] = s;
        }

        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            var color = new Rgb(2, 2, 10);
            foreach (var r in rockets)
            {
                double level = 1 - ToSegment(x, y, r.X, r.Y, r.X, r.Y + 0.9) / 0.45;
                if (level > 0) color = EffectMath.Max(color, new Rgb(255, 210, 150).Scale(level * 0.8));
            }
            foreach (var s in sparks)
            {
                double grow = Math.Min(1, s.Age / 0.15), fade = 1 - s.Age / 1.5;   // eases in rather than flashing on
                double level = (1 - Math.Sqrt((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y)) / 0.6) * grow * fade * fade;
                if (level <= 0) continue;
                color = EffectMath.Max(color, Rgb.FromHsv(s.Hue, 0.85).Scale(level));   // fireworks are every colour
            }
            frame[cell] = color;
        }
    }
}

/// <summary>Strings of coloured bulbs along the rows, each twinkling gently on its own.</summary>
public sealed class FestiveLightsEffect : KeyShader
{
    static readonly Rgb[] Bulbs = [new(255, 20, 20), new(20, 220, 40), new(255, 180, 20), new(30, 90, 255), new(255, 60, 200)];

    public override string Id => "cel-festive-lights";
    public override string Name => "Festive lights";
    public override string Category => EffectCategory.Celebrations;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        int index = (int)Math.Round(x * 1.2) + KeyGrid.Row(cell);
        var bulb = Bulbs[(index % Bulbs.Length + Bulbs.Length) % Bulbs.Length];
        double twinkle = 0.45 + 0.55 * Math.Pow(Wave(ctx.Time * (1.2 + Hash(cell) * 1.4) + Hash(cell + 3) * 20), 2);
        return bulb.Scale(twinkle);
    }
}

/// <summary>Little oil lamps flickering warmly across a deep dusk, as at Diwali.</summary>
public sealed class DiyasEffect : KeyShader
{
    static readonly Rgb Dusk = new(28, 4, 22), Flame = new(255, 140, 20), Tip = new(255, 220, 120);

    public override string Id => "cel-diyas";
    public override string Name => "Diyas";
    public override string Category => EffectCategory.Celebrations;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        bool lamp = (KeyGrid.Column(cell) + KeyGrid.Row(cell) * 2) % 3 == 0 && Hash(cell + 61) > 0.25;
        if (!lamp)
        {
            double glow = 0.15 * EffectMath.Noise(x * 0.6, ctx.Time * 0.5);
            return Rgb.Lerp(Dusk, Flame, glow);
        }
        double flicker = 0.7 + 0.2 * EffectMath.Noise(cell * 2.3, ctx.Time * 3) + 0.1 * EffectMath.Noise(cell * 5.1, ctx.Time * 7);
        var color = Rgb.Lerp(Flame, Tip, flicker - 0.6);
        return (color).Scale(flicker);
    }
}

/// <summary>Clouds of bright coloured powder bloom across the keys and mix, as at Holi.</summary>
public sealed class ColourFestivalEffect : SteppedEffect
{
    static readonly Rgb[] Powders = [new(255, 20, 140), new(40, 220, 60), new(255, 220, 0), new(30, 120, 255), new(255, 100, 0), new(170, 40, 255)];
    readonly Rgb[] keys = new Rgb[Frame.CellCount];
    readonly List<(double X, double Y, double Age, Rgb Color)> puffs = [];
    double next, time;

    public override string Id => "cel-colour-festival";
    public override string Name => "Colour festival";
    public override string Category => EffectCategory.Celebrations;

    protected override void Reset() { Array.Clear(keys); puffs.Clear(); next = time = 0; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        time += dt;
        if (time >= next)
        {
            var color = ctx.Settings.AutoColor ? Rgb.FromHsv(Random.NextDouble() * 360) : Powders[Random.Next(Powders.Length)];
            puffs.Add((Random.NextDouble() * KeyGeometry.Width, Random.NextDouble() * KeyGeometry.Height, 0, color));
            next = time + 0.45 + Random.NextDouble() * 0.5;
        }
        for (int i = 0; i < puffs.Count; i++) { var p = puffs[i]; puffs[i] = p with { Age = p.Age + dt }; }
        puffs.RemoveAll(p => p.Age > 1.2);

        foreach (int cell in KeyGeometry.Cells)
        {
            var color = keys[cell].Scale(Math.Exp(-dt * 0.25));   // what is already there fades slowly
            foreach (var p in puffs)
            {
                double radius = 0.6 + 2.6 * Math.Sqrt(p.Age / 1.2);
                double cover = Smooth(radius, radius * 0.4, KeyGeometry.Distance(cell, p.X, p.Y)) * dt * 3;
                color = Rgb.Lerp(color, p.Color, cover);
            }
            keys[cell] = color;
            frame[cell] = color;
        }
    }
}

/// <summary>Red and white stripes winding round, like a candy cane.</summary>
public sealed class CandyCaneEffect : KeyShader
{
    public override string Id => "cel-candy-cane";
    public override string Name => "Candy cane";
    public override string Category => EffectCategory.Celebrations;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double s = (x * 0.55 - y * 0.9) * 0.4 - ctx.Time * 0.5;
        double white = Smooth(0.42, 0.58, Wave(2 * Math.PI * s));
        var red = new Rgb(230, 0, 20);
        return Rgb.Lerp(red, new Rgb(255, 235, 235), white);
    }
}

/// <summary>Hearts floating up the keyboard, glowing pink and red.</summary>
public sealed class HeartsEffect : SteppedEffect
{
    readonly List<(double X, double Y, double Size, double Speed, double Hue)> hearts = [];
    double spawn;

    public override string Id => "cel-hearts";
    public override string Name => "Hearts";
    public override string Category => EffectCategory.Celebrations;

    protected override void Reset() => hearts.Clear();

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        if (hearts.Count == 0) spawn = 1;
        for (spawn += dt * 0.9; spawn >= 1; spawn--)
            hearts.Add((1.5 + Random.NextDouble() * (KeyGeometry.Width - 3), KeyGeometry.Height + 1.5, 1.2 + Random.NextDouble() * 0.6,
                        0.8 + Random.NextDouble() * 0.5, 330 + Random.NextDouble() * 40));
        for (int i = 0; i < hearts.Count; i++) { var h = hearts[i]; hearts[i] = h with { Y = h.Y - h.Speed * dt }; }
        hearts.RemoveAll(h => h.Y < -2);

        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            var color = new Rgb(25, 0, 10);
            foreach (var h in hearts)
            {
                // The heart curve (x² + y² - 1)³ - x²y³ < 0, scaled to the heart's size, point up.
                double hx = (x - h.X) / h.Size, hy = -(y - h.Y) / h.Size * 1.25;
                double f = Math.Pow(hx * hx + hy * hy - 1, 3) - hx * hx * hy * hy * hy;
                double inside = Smooth(0.25, -0.25, f);
                if (inside <= 0) continue;
                var tint = Rgb.FromHsv(h.Hue, 0.85);
                color = EffectMath.Max(color, tint.Scale(inside));
            }
            frame[cell] = color;
        }
    }
}
