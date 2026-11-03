using RaptorSense.Layout;

namespace RaptorSense.Lighting;

// Software versions of the classic keyboard effects, plus extras. They run in RaptorSense's 60 fps engine, use real
// key positions (KeyGeometry, in key pitches), honour colour 1 / colour 2 / Auto colour, and are scaled by Speed.
// Time-based randomness uses per-effect Random instances; effects are only ever rendered from the engine thread.

/// <summary>Colour 1 to colour 2 across the keyboard (Auto: a rainbow).</summary>
public sealed class GradientEffect : IEffect
{
    public string Id => "gradient";
    public string Name => "Gradient";

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int cell in KeyGeometry.Cells)
        {
            double u = KeyGeometry.U(cell);
            frame[cell] = ctx.Settings.AutoColor ? Rgb.FromHsv(u * 300) : Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, u);
        }
    }
}

/// <summary>Soft bands of colour 1 and colour 2 rolling across the keyboard.</summary>
public sealed class ColorWaveEffect : IEffect
{
    public string Id => "wave";
    public string Name => "Colour wave";
    public string Category => EffectCategory.Animated;

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int cell in KeyGeometry.Cells)
        {
            double u = KeyGeometry.U(cell);
            double t = 0.5 + 0.5 * Math.Sin((u * 2 - ctx.Time * 0.6) * 2 * Math.PI);
            frame[cell] = ctx.Settings.AutoColor
                ? Rgb.FromHsv(u * 360 - ctx.Time * 90)
                : Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, t);
        }
    }
}

/// <summary>The whole keyboard glides through the colour wheel.</summary>
public sealed class NeonEffect : IEffect
{
    public string Id => "neon";
    public string Name => "Neon";
    public string Category => EffectCategory.Animated;

    public void Render(Frame frame, in EffectContext ctx) => frame.Fill(Rgb.FromHsv(ctx.Time * 45));
}

/// <summary>A snake slides along the rows, back and forth, with a fading tail.</summary>
public sealed class SnakeEffect : IEffect
{
    const double KeysPerSecond = 14, TailKeys = 12;

    // Serpentine order: left to right on the top row, right to left on the next, and so on.
    static readonly int[] Path = KeyGeometry.Cells
        .GroupBy(c => (int)Math.Round(KeyGeometry.Y(c)))
        .OrderBy(g => g.Key)
        .SelectMany((row, i) => i % 2 == 0 ? row.OrderBy(KeyGeometry.X) : row.OrderByDescending(KeyGeometry.X))
        .ToArray();

    public string Id => "snake";
    public string Name => "Snake";
    public string Category => EffectCategory.Animated;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double head = ctx.Time * KeysPerSecond % Path.Length;
        for (int i = 0; i < Path.Length; i++)
        {
            double behind = (head - i + Path.Length) % Path.Length;   // 0 at the head, growing along the tail
            if (behind > TailKeys) continue;
            double level = 1 - behind / TailKeys;
            frame[Path[i]] = ctx.Primary(1 - behind / Path.Length).Scale(level * level);
        }
    }
}

/// <summary>A beam sweeps round the keyboard like a radar screen.</summary>
public sealed class RadarEffect : IEffect
{
    public string Id => "radar";
    public string Name => "Radar sweep";
    public string Category => EffectCategory.Animated;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double cx = KeyGeometry.Width / 2, cy = KeyGeometry.Height / 2;
        double beam = ctx.Time * Math.PI % (2 * Math.PI);
        foreach (int cell in KeyGeometry.Cells)
        {
            double angle = Math.Atan2(KeyGeometry.Y(cell) - cy, KeyGeometry.X(cell) - cx);
            double behind = ((beam - angle) % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);   // how far the beam has passed
            double level = Math.Exp(-behind * 2.2);
            frame[cell] = EffectMath.Max(ctx.Primary(angle / (2 * Math.PI) + 0.5).Scale(level), ctx.Settings.Color2.Scale(0.04));
        }
    }
}

/// <summary>A soft spotlight wanders over the keyboard.</summary>
public sealed class SpotlightEffect : IEffect
{
    const double Radius = 3.2;

    public string Id => "spotlight";
    public string Name => "Spotlight";
    public string Category => EffectCategory.Animated;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double x = KeyGeometry.Width * (0.5 + 0.42 * Math.Sin(ctx.Time * 0.9));
        double y = KeyGeometry.Height * (0.5 + 0.38 * Math.Sin(ctx.Time * 1.7 + 1));
        foreach (int cell in KeyGeometry.Cells)
        {
            double d = KeyGeometry.Distance(cell, x, y) / Radius;
            double level = Math.Max(0, 1 - d * d);
            frame[cell] = Rgb.Lerp(ctx.Settings.Color2.Scale(0.06), ctx.Primary(KeyGeometry.U(cell)), level);
        }
    }
}

/// <summary>Lub-dub: two quick pulses, then a pause, rippling out from the middle.</summary>
public sealed class HeartbeatEffect : IEffect
{
    const double Period = 1.3;

    public string Id => "heartbeat";
    public string Name => "Heartbeat";
    public string Category => EffectCategory.Animated;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double cx = KeyGeometry.Width / 2, cy = KeyGeometry.Height / 2;
        foreach (int cell in KeyGeometry.Cells)
        {
            double t = (ctx.Time - KeyGeometry.Distance(cell, cx, cy) * 0.025) % Period;
            if (t < 0) t += Period;
            double beat = Math.Exp(-Math.Pow((t - 0.12) / 0.06, 2)) + 0.65 * Math.Exp(-Math.Pow((t - 0.38) / 0.07, 2));
            frame[cell] = ctx.Primary(KeyGeometry.U(cell)).Scale(0.06 + 0.94 * Math.Min(1, beat));
        }
    }
}

/// <summary>Slow, flowing curtains of colour 1 and colour 2.</summary>
public sealed class AuroraEffect : IEffect
{
    public string Id => "aurora";
    public string Name => "Aurora";
    public string Category => EffectCategory.Animated;

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            double mix = EffectMath.Noise(x * 0.22 + ctx.Time * 0.35, y * 0.3 - ctx.Time * 0.15);
            double glow = 0.25 + 0.75 * EffectMath.Noise(x * 0.4 - ctx.Time * 0.5, y * 0.5 + 17);
            var color = ctx.Settings.AutoColor
                ? Rgb.FromHsv(120 + mix * 200)
                : Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, mix);
            frame[cell] = color.Scale(glow);
        }
    }
}

/// <summary>Rings spread from random keys, like drops on water.</summary>
public sealed class RipplesEffect : IEffect
{
    const double KeysPerSecond = 9, Lifetime = 1.6, RingWidth = 1.0, SpawnEvery = 0.55;

    readonly Random random = new();
    readonly List<(double X, double Y, double Start, double Hue)> ripples = [];
    double nextSpawn, lastTime;

    public string Id => "ripples";
    public string Name => "Ripples";
    public string Category => EffectCategory.Nature;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double now = ctx.Time;
        if (now < lastTime)   // a new clock (e.g. another engine run): start fresh rather than wait for the old schedule
        {
            ripples.Clear();
            nextSpawn = now;
        }
        lastTime = now;
        if (now >= nextSpawn)
        {
            int origin = KeyGeometry.Cells[random.Next(KeyGeometry.Cells.Count)];
            ripples.Add((KeyGeometry.X(origin), KeyGeometry.Y(origin), now, random.NextDouble() * 360));
            nextSpawn = now + SpawnEvery * (0.6 + random.NextDouble() * 0.8);
        }
        ripples.RemoveAll(r => now - r.Start > Lifetime);

        foreach (int cell in KeyGeometry.Cells)
        {
            var color = Rgb.Black;
            foreach (var r in ripples)
            {
                double age = now - r.Start;
                double ring = 1 - Math.Abs(KeyGeometry.Distance(cell, r.X, r.Y) - age * KeysPerSecond) / RingWidth;
                if (ring <= 0) continue;
                var tint = ctx.Settings.AutoColor ? Rgb.FromHsv(r.Hue) : ctx.Settings.Color;
                color = EffectMath.Max(color, tint.Scale(ring * (1 - age / Lifetime)));
            }
            frame[cell] = color;
        }
    }
}

/// <summary>Drops fall down the keyboard with short trails. Also the base of <see cref="MatrixEffect"/>.</summary>
public class RainEffect : IEffect
{
    readonly Random random = new();
    readonly List<(double X, double Y, double Speed, double Hue)> drops = [];
    double spawnDebt;

    protected virtual double DropsPerSecond => 7;
    protected virtual double Trail => 1.6;
    protected virtual double FallSpeed => 5;
    protected virtual bool SnapToColumns => false;

    public virtual string Id => "raindrop";
    public virtual string Name => "Rain drop";
    public string Category => EffectCategory.Nature;

    protected virtual Rgb DropColor(in EffectContext ctx, double hue, double along) =>
        ctx.Settings.AutoColor ? Rgb.FromHsv(hue) : ctx.Settings.Color;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double dt = Math.Clamp(ctx.DeltaTime * ctx.Settings.Speed, 0, 0.1);
        for (spawnDebt += dt * DropsPerSecond; spawnDebt >= 1; spawnDebt--)
        {
            double x = SnapToColumns
                ? KeyGeometry.X(KeyGeometry.Cells[random.Next(KeyGeometry.Cells.Count)])
                : random.NextDouble() * KeyGeometry.Width;
            drops.Add((x, -Trail * random.NextDouble(), FallSpeed * (0.7 + random.NextDouble() * 0.6), random.NextDouble() * 360));
        }
        for (int i = 0; i < drops.Count; i++) { var d = drops[i]; drops[i] = (d.X, d.Y + d.Speed * dt, d.Speed, d.Hue); }
        drops.RemoveAll(d => d.Y - Trail > KeyGeometry.Height);

        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            var color = Rgb.Black;
            foreach (var d in drops)
            {
                double across = 1 - Math.Abs(x - d.X) / 0.7;
                double behind = d.Y - y;   // positive once the drop has passed this key
                if (across <= 0 || behind < -0.5 || behind > Trail) continue;
                double level = across * (behind < 0 ? 1 + behind * 2 : 1 - behind / Trail);
                color = EffectMath.Max(color, DropColor(ctx, d.Hue, behind / Trail).Scale(level));
            }
            frame[cell] = color;
        }
    }
}

/// <summary>Green code rain: bright heads, long trails, falling down the key columns.</summary>
public sealed class MatrixEffect : RainEffect
{
    protected override double DropsPerSecond => 9;
    protected override double Trail => 3.5;
    protected override double FallSpeed => 4;
    protected override bool SnapToColumns => true;

    public override string Id => "matrix";
    public override string Name => "Matrix rain";

    protected override Rgb DropColor(in EffectContext ctx, double hue, double along)
    {
        var body = ctx.Settings.AutoColor ? Rgb.FromHsv(hue) : new Rgb(0, 255, 70);
        return along < 0.15 ? Rgb.Lerp(Rgb.White, body, along / 0.15) : body;
    }
}

/// <summary>Snowflakes drifting slowly down over a cold glow.</summary>
public sealed class SnowEffect : IEffect
{
    const double FlakesPerSecond = 5;

    readonly Random random = new();
    readonly List<(double X, double Y, double Speed, double Phase)> flakes = [];
    double spawnDebt;

    public string Id => "snow";
    public string Name => "Snow";
    public string Category => EffectCategory.Nature;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double dt = Math.Clamp(ctx.DeltaTime * ctx.Settings.Speed, 0, 0.1);
        for (spawnDebt += dt * FlakesPerSecond; spawnDebt >= 1; spawnDebt--)
            flakes.Add((random.NextDouble() * KeyGeometry.Width, -0.5, 0.9 + random.NextDouble() * 0.8, random.NextDouble() * 6));
        for (int i = 0; i < flakes.Count; i++) { var f = flakes[i]; flakes[i] = (f.X, f.Y + f.Speed * dt, f.Speed, f.Phase); }
        flakes.RemoveAll(f => f.Y > KeyGeometry.Height + 0.5);

        var flake = ctx.Settings.AutoColor ? Rgb.White : Rgb.Lerp(Rgb.White, ctx.Settings.Color, 0.25);
        var sky = new Rgb(0, 20, 45);
        foreach (int cell in KeyGeometry.Cells)
        {
            var color = sky;
            foreach (var f in flakes)
            {
                double x = f.X + 0.4 * Math.Sin(ctx.Time * 1.5 + f.Phase);   // gentle sideways drift
                double level = 1 - KeyGeometry.Distance(cell, x, f.Y) / 0.75;
                if (level > 0) color = EffectMath.Max(color, flake.Scale(level));
            }
            frame[cell] = color;
        }
    }
}

/// <summary>Random keys sparkle and fade, like stars.</summary>
public sealed class StarsEffect : IEffect
{
    const double StarsPerSecond = 18, Life = 1.0;

    readonly Random random = new();
    readonly List<(int Cell, double Start, double Hue)> stars = [];
    double spawnDebt;

    public string Id => "stars";
    public string Name => "Twinkling stars";
    public string Category => EffectCategory.Nature;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double now = ctx.Time;
        for (spawnDebt += Math.Clamp(ctx.DeltaTime * ctx.Settings.Speed, 0, 0.1) * StarsPerSecond; spawnDebt >= 1; spawnDebt--)
            stars.Add((KeyGeometry.Cells[random.Next(KeyGeometry.Cells.Count)], now, random.NextDouble() * 360));
        stars.RemoveAll(s => now - s.Start > Life || now < s.Start);

        var night = ctx.Settings.Color2.Scale(0.05);
        foreach (int cell in KeyGeometry.Cells) frame[cell] = night;
        foreach (var s in stars)
        {
            double age = (now - s.Start) / Life;
            double level = age < 0.2 ? age / 0.2 : 1 - (age - 0.2) / 0.8;   // quick rise, slow fade
            var color = ctx.Settings.AutoColor ? Rgb.FromHsv(s.Hue) : ctx.Settings.Color;
            frame[s.Cell] = EffectMath.Max(frame[s.Cell], color.Scale(level * level));
        }
    }
}

/// <summary>Jagged bolts strike from the F row to the space bar, with a flash and an afterglow.</summary>
public sealed class LightningEffect : IEffect
{
    const double Fade = 0.45;

    readonly Random random = new();
    readonly double[] bolt = new double[Frame.CellCount];
    double nextStrike, lastStrike = -10;

    public string Id => "lightning";
    public string Name => "Lightning";
    public string Category => EffectCategory.Nature;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double now = ctx.Time;
        if (now >= nextStrike || now < lastStrike)
        {
            Strike();
            lastStrike = now;
            nextStrike = now + 0.7 + random.NextDouble() * 2.0;
        }

        double age = now - lastStrike;
        // Two flickers, then a fading afterglow.
        double flash = age < 0.06 ? 1 : age < 0.1 ? 0.2 : age < 0.16 ? 0.9 : Math.Max(0, 1 - (age - 0.16) / Fade);
        var boltColor = ctx.Settings.AutoColor ? Rgb.FromHsv(lastStrike * 97) : ctx.Settings.Color;
        var sky = ctx.Settings.Color2.Scale(0.04);
        foreach (int cell in KeyGeometry.Cells)
        {
            double level = bolt[cell] * flash;
            var color = Rgb.Lerp(sky, Rgb.Lerp(boltColor, Rgb.White, 0.5 * bolt[cell]), level);
            if (age < 0.06) color = EffectMath.Max(color, boltColor.Scale(0.25));   // the whole sky lights up briefly
            frame[cell] = color;
        }
    }

    void Strike()
    {
        Array.Clear(bolt);
        double x = KeyGeometry.Width * (0.15 + random.NextDouble() * 0.7);
        for (double y = 0; y <= KeyGeometry.Height; y += 0.25)
        {
            x += (random.NextDouble() - 0.5) * 0.9;
            foreach (int cell in KeyGeometry.Cells)
            {
                double level = 1 - KeyGeometry.Distance(cell, x, y) / 0.8;
                if (level > bolt[cell]) bolt[cell] = level;
            }
        }
    }
}

/// <summary>Flames rising from the space bar row.</summary>
public sealed class FireEffect : IEffect
{
    public string Id => "fire";
    public string Name => "Fire";
    public string Category => EffectCategory.Nature;

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int cell in KeyGeometry.Cells)
        {
            double v = KeyGeometry.V(cell);   // 1 at the bottom
            double flicker = EffectMath.Noise(KeyGeometry.X(cell) * 0.7, KeyGeometry.Y(cell) * 0.9 + ctx.Time * 3.2);
            double heat = Math.Pow(v, 1.6) * (0.55 + 0.9 * flicker);
            frame[cell] = ctx.Settings.AutoColor ? Rgb.FromHsv(260 + heat * 100).Scale(Math.Clamp(heat, 0, 1)) : EffectMath.Fire(heat);
        }
    }
}

/// <summary>A blazing ball crosses the keyboard, leaving a burning trail.</summary>
public sealed class FireballEffect : IEffect
{
    const double Radius = 1.6, Cooling = 1.8;

    readonly double[] heat = new double[Frame.CellCount];

    public string Id => "fireball";
    public string Name => "Fireball";
    public string Category => EffectCategory.Nature;

    public void Render(Frame frame, in EffectContext ctx)
    {
        double span = KeyGeometry.Width + 6;
        double x = ctx.Time * 9 % span - 3;   // enters and leaves beyond the edges
        double y = KeyGeometry.Height * (0.5 + 0.28 * Math.Sin(ctx.Time * 2.3));
        double cooling = Math.Clamp(ctx.DeltaTime * ctx.Settings.Speed, 0, 0.1) * Cooling;

        foreach (int cell in KeyGeometry.Cells)
        {
            double core = 1 - KeyGeometry.Distance(cell, x, y) / Radius;
            heat[cell] = Math.Max(heat[cell] - cooling, core > 0 ? Math.Min(1, core * 1.4) : 0);
            double flicker = 0.85 + 0.3 * EffectMath.Noise(cell * 0.37, ctx.Time * 9);
            double h = heat[cell] * flicker;
            frame[cell] = ctx.Settings.AutoColor ? Rgb.FromHsv(ctx.Time * 40 + h * 60).Scale(Math.Min(1, h)) : EffectMath.Fire(h);
        }
    }
}
