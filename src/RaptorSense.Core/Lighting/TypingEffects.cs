namespace RaptorSense.Lighting;

// Effects started by key presses. They draw with colour 2 (or Auto rainbow hues) on black, so each one also works as
// a typing overlay on top of any other effect. Ages use the speed-scaled ctx.Time; everything is capped so fast
// typing can't build up unbounded work.

/// <summary>Shared bookkeeping: one entry per press, dropped after <see cref="Lifetime"/>, reset if the clock restarts.</summary>
public abstract class PressEffect<T> : IEffect where T : struct
{
    protected readonly Random Random = new();
    protected readonly List<(T Data, double Start, double Hue)> Active = [];
    double lastTime;

    public abstract string Id { get; }
    public abstract string Name { get; }
    public string Category => EffectCategory.Typing;
    public bool UsesKeys => true;
    public bool WorksAsOverlay => true;

    protected abstract double Lifetime { get; }
    protected virtual int MaxActive => 24;

    protected abstract T Spawn(int cell);

    /// <summary>Draws one active press; <paramref name="age"/> is in seconds since the press.</summary>
    protected abstract void Draw(Frame frame, in EffectContext ctx, T data, double age, Rgb color);

    public void Render(Frame frame, in EffectContext ctx)
    {
        double now = ctx.Time;
        if (now < lastTime) Active.Clear();
        lastTime = now;
        foreach (int cell in ctx.Presses)
            if (KeyGeometry.Cells.Contains(cell)) Active.Add((Spawn(cell), now, Random.NextDouble() * 360));
        Active.RemoveAll(a => now - a.Start > Lifetime);
        if (Active.Count > MaxActive) Active.RemoveRange(0, Active.Count - MaxActive);
        foreach (var a in Active) Draw(frame, ctx, a.Data, now - a.Start, ctx.Accent(a.Hue));
    }

    protected static void Light(Frame frame, int cell, Rgb color, double level)
    {
        if (level > 0) frame[cell] = EffectMath.Max(frame[cell], color.Scale(Math.Min(1, level)));
    }
}

/// <summary>A jagged bolt strikes through the pressed key, from the F row to the space bar.</summary>
public sealed class TypingLightningEffect : PressEffect<TypingLightningEffect.Bolt>
{
    public readonly record struct Bolt(double[] Levels);

    public override string Id => "typing-lightning";
    public override string Name => "Typing lightning";
    protected override double Lifetime => 0.6;
    protected override int MaxActive => 8;

    protected override Bolt Spawn(int cell)
    {
        var levels = new double[Frame.CellCount];
        double x0 = KeyGeometry.X(cell), y0 = KeyGeometry.Y(cell);
        foreach (int direction in new[] { -1, 1 })   // up from the key, then down from it
        {
            double x = x0;
            for (double y = y0; y >= -0.5 && y <= KeyGeometry.Height + 0.5; y += 0.25 * direction)
            {
                x += (Random.NextDouble() - 0.5) * 0.8;
                foreach (int c in KeyGeometry.Cells) levels[c] = Math.Max(levels[c], 1 - KeyGeometry.Distance(c, x, y) / 0.75);
            }
        }
        return new Bolt(levels);
    }

    protected override void Draw(Frame frame, in EffectContext ctx, Bolt bolt, double age, Rgb color)
    {
        double flash = age < 0.05 ? 1 : age < 0.09 ? 0.25 : age < 0.14 ? 0.9 : Math.Max(0, 1 - (age - 0.14) / 0.45);
        foreach (int cell in KeyGeometry.Cells)
            Light(frame, cell, Rgb.Lerp(color, Rgb.White, 0.45 * bolt.Levels[cell]), bolt.Levels[cell] * flash);
    }
}

/// <summary>A fireball shoots along the row, away from the nearer edge, leaving a burning trail.</summary>
public sealed class TypingFireballEffect : PressEffect<TypingFireballEffect.Ball>
{
    const double KeysPerSecond = 14, Radius = 1.1, Trail = 4;

    public readonly record struct Ball(double X, double Y, int Direction);

    public override string Id => "typing-fireball";
    public override string Name => "Typing fireball";
    protected override double Lifetime => (KeyGeometry.Width + Trail + 2) / KeysPerSecond;
    protected override int MaxActive => 10;

    protected override Ball Spawn(int cell) =>
        new(KeyGeometry.X(cell), KeyGeometry.Y(cell), KeyGeometry.U(cell) < 0.5 ? 1 : -1);

    protected override void Draw(Frame frame, in EffectContext ctx, Ball ball, double age, Rgb color)
    {
        double head = ball.X + ball.Direction * age * KeysPerSecond;
        foreach (int cell in KeyGeometry.Cells)
        {
            double dy = Math.Abs(KeyGeometry.Y(cell) - ball.Y);
            if (dy > Radius) continue;
            double behind = (head - KeyGeometry.X(cell)) * ball.Direction;   // > 0 behind the head
            double heat = behind < 0 ? 1 + behind / Radius : 1 - behind / Trail;
            heat *= 1 - dy / Radius;
            if (heat <= 0) continue;
            heat *= 0.85 + 0.3 * EffectMath.Noise(cell * 0.41, ctx.Time * 10);   // flicker
            var fire = ctx.Settings.AutoColor ? color.Scale(Math.Min(1, heat)) : EffectMath.Fire(heat);
            frame[cell] = EffectMath.Max(frame[cell], fire);
        }
    }
}

/// <summary>Two comets streak out along the row, left and right, with fading tails.</summary>
public sealed class CometEffect : PressEffect<CometEffect.Origin>
{
    const double KeysPerSecond = 18, Tail = 4;

    public readonly record struct Origin(double X, double Y);

    public override string Id => "typing-comet";
    public override string Name => "Comet";
    protected override double Lifetime => (KeyGeometry.Width + Tail) / KeysPerSecond;

    protected override Origin Spawn(int cell) => new(KeyGeometry.X(cell), KeyGeometry.Y(cell));

    protected override void Draw(Frame frame, in EffectContext ctx, Origin o, double age, Rgb color)
    {
        double travelled = age * KeysPerSecond;
        foreach (int cell in KeyGeometry.Cells)
        {
            if (Math.Abs(KeyGeometry.Y(cell) - o.Y) > 0.5) continue;   // same row only
            double behind = travelled - Math.Abs(KeyGeometry.X(cell) - o.X);
            if (behind < -0.5 || behind > Tail) continue;
            double level = behind < 0 ? 1 + behind * 2 : 1 - behind / Tail;
            Light(frame, cell, behind < 0.5 ? Rgb.Lerp(Rgb.White, color, 0.4) : color, level * level);
        }
    }
}

/// <summary>A glowing blast swells around the pressed key and fades.</summary>
public sealed class BurstEffect : PressEffect<BurstEffect.Origin>
{
    const double MaxRadius = 3.5;

    public readonly record struct Origin(double X, double Y);

    public override string Id => "typing-burst";
    public override string Name => "Burst";
    protected override double Lifetime => 0.7;

    protected override Origin Spawn(int cell) => new(KeyGeometry.X(cell), KeyGeometry.Y(cell));

    protected override void Draw(Frame frame, in EffectContext ctx, Origin o, double age, Rgb color)
    {
        double t = age / Lifetime;
        double radius = MaxRadius * Math.Sqrt(t);   // fast at first, then slowing
        foreach (int cell in KeyGeometry.Cells)
        {
            double d = KeyGeometry.Distance(cell, o.X, o.Y);
            if (d > radius) continue;
            double level = (1 - d / MaxRadius) * (1 - t) * (0.8 + 0.4 * EffectMath.Noise(cell * 0.7, age * 12));
            Light(frame, cell, d < 0.6 ? Rgb.Lerp(Rgb.White, color, t) : color, level);
        }
    }
}

/// <summary>The key flashes and drops fall from it down the keyboard.</summary>
public sealed class SplashEffect : PressEffect<SplashEffect.Drops>
{
    const double Trail = 1.4;

    public readonly record struct Drops(double X, double Y, double[] Offsets, double[] Speeds);

    public override string Id => "typing-splash";
    public override string Name => "Splash";
    protected override double Lifetime => 1.4;

    protected override Drops Spawn(int cell) => new(KeyGeometry.X(cell), KeyGeometry.Y(cell),
        [-0.6 + Random.NextDouble() * 0.2, Random.NextDouble() * 0.2 - 0.1, 0.4 + Random.NextDouble() * 0.2],
        [5 + Random.NextDouble() * 2, 6 + Random.NextDouble() * 2, 5 + Random.NextDouble() * 2]);

    protected override void Draw(Frame frame, in EffectContext ctx, Drops drops, double age, Rgb color)
    {
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            if (age < 0.25 && KeyGeometry.Distance(cell, drops.X, drops.Y) < 0.6) Light(frame, cell, Rgb.White, 1 - age / 0.25);
            for (int i = 0; i < drops.Offsets.Length; i++)
            {
                double across = 1 - Math.Abs(x - drops.X - drops.Offsets[i]) / 0.7;
                double behind = drops.Y + age * drops.Speeds[i] - y;
                if (across <= 0 || behind < -0.4 || behind > Trail || y < drops.Y) continue;
                Light(frame, cell, color, across * (behind < 0 ? 1 + behind * 2.5 : 1 - behind / Trail));
            }
        }
    }
}

/// <summary>Lines shoot out along the key's row and column.</summary>
public sealed class CrosshairEffect : PressEffect<CrosshairEffect.Origin>
{
    const double KeysPerSecond = 28;

    public readonly record struct Origin(double X, double Y);

    public override string Id => "typing-cross";
    public override string Name => "Crosshair";
    protected override double Lifetime => 0.65;

    protected override Origin Spawn(int cell) => new(KeyGeometry.X(cell), KeyGeometry.Y(cell));

    protected override void Draw(Frame frame, in EffectContext ctx, Origin o, double age, Rgb color)
    {
        double reach = age * KeysPerSecond, fade = 1 - age / Lifetime;
        foreach (int cell in KeyGeometry.Cells)
        {
            double dx = Math.Abs(KeyGeometry.X(cell) - o.X), dy = Math.Abs(KeyGeometry.Y(cell) - o.Y);
            double row = dy < 0.5 && dx <= reach ? 1 - dx / (KeyGeometry.Width * 0.8) : 0;
            double column = dx < 0.6 && dy <= reach ? 1 - dy / KeyGeometry.Height : 0;
            Light(frame, cell, color, Math.Max(row, column) * fade);
        }
    }
}

/// <summary>The more a key is used, the hotter it glows; heat fades over about twenty seconds.</summary>
public sealed class HeatmapEffect : IEffect
{
    const double PerPress = 0.22, Spread = 0.05, CoolingPerSecond = 0.05;

    readonly double[] heat = new double[Frame.CellCount];

    public string Id => "typing-heatmap";
    public string Name => "Heatmap";
    public string Category => EffectCategory.Typing;
    public bool UsesKeys => true;
    public bool WorksAsOverlay => true;

    public void Render(Frame frame, in EffectContext ctx)
    {
        foreach (int pressed in ctx.Presses)
        {
            if (!KeyGeometry.Cells.Contains(pressed)) continue;
            foreach (int cell in KeyGeometry.Cells)
            {
                double d = KeyGeometry.Distance(cell, KeyGeometry.X(pressed), KeyGeometry.Y(pressed));
                if (d < 0.1) heat[cell] = Math.Min(1.2, heat[cell] + PerPress);
                else if (d < 1.3) heat[cell] = Math.Min(1.2, heat[cell] + Spread);   // a little warmth for the neighbours
            }
        }
        double cooling = Math.Clamp(ctx.DeltaTime * ctx.Settings.Speed, 0, 0.1) * CoolingPerSecond;
        foreach (int cell in KeyGeometry.Cells)
        {
            heat[cell] = Math.Max(0, heat[cell] - cooling);
            double h = Math.Min(1, heat[cell]);
            if (h <= 0) continue;
            // Auto colour: cold blue through to hot red, instead of the fire palette.
            frame[cell] = ctx.Settings.AutoColor ? Rgb.FromHsv(240 - 240 * h).Scale(Math.Min(1, h * 2)) : EffectMath.Fire(h);
        }
    }
}

/// <summary>The key and a few neighbours pop into random bright colours.</summary>
public sealed class ConfettiEffect : PressEffect<ConfettiEffect.Pieces>
{
    public readonly record struct Pieces((int Cell, double Hue, double Delay)[] Items);

    public override string Id => "typing-confetti";
    public override string Name => "Confetti";
    protected override double Lifetime => 1.8;

    protected override Pieces Spawn(int cell)
    {
        var near = KeyGeometry.Cells.Where(c => c != cell && KeyGeometry.Distance(c, KeyGeometry.X(cell), KeyGeometry.Y(cell)) < 2.2)
            .OrderBy(_ => Random.Next()).Take(5);
        var items = new List<(int, double, double)> { (cell, Random.NextDouble() * 360, 0) };
        foreach (int c in near) items.Add((c, Random.NextDouble() * 360, Random.NextDouble() * 0.25));
        return new Pieces(items.ToArray());
    }

    protected override void Draw(Frame frame, in EffectContext ctx, Pieces pieces, double age, Rgb color)
    {
        foreach (var (cell, hue, delay) in pieces.Items)
        {
            double t = age - delay;
            if (t < 0) continue;
            double level = t < 0.8 ? 1 : 1 - (t - 0.8) / (Lifetime - 0.8 - delay);
            Light(frame, cell, Rgb.FromHsv(hue), level);   // always multicoloured: that is the point of confetti
        }
    }
}

/// <summary>Little sparks twinkle on keys around the one pressed.</summary>
public sealed class SparkleEffect : PressEffect<SparkleEffect.Sparks>
{
    public readonly record struct Sparks((int Cell, double Delay)[] Items);

    public override string Id => "typing-sparkle";
    public override string Name => "Sparkle";
    protected override double Lifetime => 0.9;

    protected override Sparks Spawn(int cell)
    {
        var near = KeyGeometry.Cells.Where(c => KeyGeometry.Distance(c, KeyGeometry.X(cell), KeyGeometry.Y(cell)) < 2.8)
            .OrderBy(_ => Random.Next()).Take(8);
        return new Sparks(near.Select(c => (c, Random.NextDouble() * 0.35)).ToArray());
    }

    protected override void Draw(Frame frame, in EffectContext ctx, Sparks sparks, double age, Rgb color)
    {
        foreach (var (cell, delay) in sparks.Items)
        {
            double t = (age - delay) / 0.5;
            if (t is < 0 or > 1) continue;
            double level = t < 0.25 ? t / 0.25 : 1 - (t - 0.25) / 0.75;
            Light(frame, cell, Rgb.Lerp(Rgb.White, color, 0.5), level * level);
        }
    }
}
