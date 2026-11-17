using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// More of Nature. These keep their own natural colours, whatever the colour setting.

/// <summary>Waves rolling in towards you, with white foam on each crest.</summary>
public sealed class OceanWavesEffect : KeyShader
{
    static readonly Rgb Deep = new(0, 30, 90), Water = new(0, 130, 190), Foam = new(220, 245, 255);

    public override string Id => "nat-ocean";
    public override string Name => "Ocean waves";
    public override string Category => EffectCategory.Nature;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double phase = y * 1.05 - ctx.Time * 1.3 + 0.5 * Math.Sin(x * 0.28 + ctx.Time * 0.3);
        double swell = Wave(phase);
        double crest = Math.Pow(swell, 8) * (0.6 + 0.4 * EffectMath.Noise(x * 0.9, ctx.Time));
        var color = Rgb.Lerp(Rgb.Lerp(Deep, Water, swell), Foam, crest);
        return color;
    }
}

/// <summary>The sun rises from the space bar, crosses a brightening sky and sets again.</summary>
public sealed class SunriseEffect : KeyShader
{
    const double Day = 24;
    static readonly Rgb Night = new(3, 4, 22), Dawn = new(255, 80, 40), Noon = new(70, 160, 255), Sun = new(255, 220, 120);
    double sunX, sunY, height;

    public override string Id => "nat-sunrise";
    public override string Name => "Sunrise";
    public override string Category => EffectCategory.Nature;

    protected override void Prepare(in EffectContext ctx)
    {
        double t = Fract(ctx.Time / Day);
        height = Math.Sin(Math.PI * t);   // 0 at the horizon, 1 at noon
        sunX = KeyGeometry.Width * (0.1 + 0.8 * t);
        sunY = KeyGeometry.Height + 1.2 - (KeyGeometry.Height + 0.6) * height;
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        var sky = height < 0.4 ? Rgb.Lerp(Night, Dawn, height / 0.4) : Rgb.Lerp(Dawn, Noon, (height - 0.4) / 0.6);
        double towardsHorizon = KeyGeometry.V(cell);
        sky = Rgb.Lerp(sky, Dawn, towardsHorizon * (1 - height) * 0.5).Scale(0.35 + 0.65 * Math.Max(0.15, height));
        double sun = Glow(Math.Sqrt((x - sunX) * (x - sunX) + (y - sunY) * (y - sunY)), 1.6);
        return Rgb.Lerp(sky, Sun, sun);
    }
}

/// <summary>Green leaves overhead, with patches of sunlight drifting through them.</summary>
public sealed class ForestCanopyEffect : KeyShader
{
    static readonly Rgb Shade1 = new(0, 35, 5), Leaf = new(30, 140, 20), Sunlight = new(210, 255, 120);

    public override string Id => "nat-forest";
    public override string Name => "Forest canopy";
    public override string Category => EffectCategory.Nature;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        var leaves = Rgb.Lerp(Shade1, Leaf, Cloud(x * 0.4, y * 0.45 + ctx.Time * 0.08));
        double light = Smooth(0.62, 0.85, EffectMath.Noise(x * 0.55 + ctx.Time * 0.3, y * 0.6 - ctx.Time * 0.1));
        return Rgb.Lerp(leaves, Sunlight, light * 0.8);
    }
}

/// <summary>Streams of water pouring down, foaming where they land on the bottom row.</summary>
public sealed class WaterfallEffect : KeyShader
{
    static readonly Rgb Back = new(0, 25, 60), Stream = new(60, 170, 255), Foam = new(230, 250, 255);

    public override string Id => "nat-waterfall";
    public override string Name => "Waterfall";
    public override string Category => EffectCategory.Nature;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        int lane = (int)Math.Floor(x * 1.4);
        double speed = 2.2 + 1.6 * Hash(lane);
        double streak = Math.Pow(Wave(2 * Math.PI * (y * 0.45 - ctx.Time * speed * 0.5 + Hash(lane + 50) * 10)), 3);
        var color = Rgb.Lerp(Back, Stream, 0.25 + 0.75 * streak);
        double foam = Smooth(4.3, 5.3, y) * (0.5 + 0.5 * EffectMath.Noise(x * 1.5, ctx.Time * 4));
        return Rgb.Lerp(color, Foam, foam);
    }
}

/// <summary>A dark crust of cooling rock, with glowing cracks of lava creeping across it.</summary>
public sealed class LavaFlowEffect : KeyShader
{
    public override string Id => "nat-lava-flow";
    public override string Name => "Lava flow";
    public override string Category => EffectCategory.Nature;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double n = Cloud(x * 0.32 - ctx.Time * 0.18, y * 0.5 + ctx.Time * 0.05);
        double crack = Math.Max(0, 1 - Math.Abs(n - 0.5) * 7);
        double heat = 0.12 + 0.85 * crack + 0.1 * EffectMath.Noise(x, ctx.Time * 2);
        return EffectMath.Fire(heat * 0.92);
    }
}

/// <summary>Heavy rain driven at a slant, splashing into puddles along the bottom row.</summary>
public sealed class RainstormEffect : SteppedEffect
{
    static readonly Rgb Sky = new(5, 10, 25), Drop = new(150, 190, 255);
    readonly List<(double X, double Y, double Speed)> drops = [];
    readonly List<(double X, double Start)> splashes = [];
    double spawn, time;

    public override string Id => "nat-rainstorm";
    public override string Name => "Rainstorm";
    public override string Category => EffectCategory.Nature;

    protected override void Reset() { drops.Clear(); splashes.Clear(); time = 0; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        time += dt;
        for (spawn += dt * 22; spawn >= 1; spawn--)
            drops.Add((Random.NextDouble() * (KeyGeometry.Width + 3) - 1, -0.5, 9 + Random.NextDouble() * 4));
        for (int i = drops.Count - 1; i >= 0; i--)
        {
            var d = drops[i];
            d = (d.X + d.Speed * 0.3 * dt, d.Y + d.Speed * dt, d.Speed);
            if (d.Y > KeyGeometry.Height - 0.3) { splashes.Add((d.X, time)); drops.RemoveAt(i); continue; }
            drops[i] = d;
        }
        splashes.RemoveAll(s => time - s.Start > 0.5);
        if (splashes.Count > 40) splashes.RemoveRange(0, splashes.Count - 40);

        var drop = Drop;
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            var color = Rgb.Lerp(Sky, new Rgb(10, 25, 60), KeyGeometry.V(cell));
            foreach (var d in drops)
            {
                double level = 1 - ToSegment(x, y, d.X, d.Y, d.X - 0.25, d.Y - 0.85) / 0.45;
                if (level > 0) color = EffectMath.Max(color, drop.Scale(level));
            }
            if (y > KeyGeometry.Height - 1.4)
                foreach (var s in splashes)
                {
                    double age = time - s.Start, ring = 1 - Math.Abs(Math.Abs(x - s.X) - age * 3) / 0.6;
                    if (ring > 0) color = EffectMath.Max(color, drop.Scale(ring * (1 - age / 0.5) * 0.8));
                }
            frame[cell] = color;
        }
    }
}

/// <summary>Autumn leaves tumbling down, swaying from side to side.</summary>
public sealed class FallingLeavesEffect : SteppedEffect
{
    static readonly Rgb[] Colours = [new(230, 60, 10), new(255, 140, 0), new(220, 180, 20), new(160, 50, 10), new(200, 30, 30)];
    readonly List<(double X, double Y, double Speed, double Phase, Rgb Color)> leaves = [];
    double spawn, time;

    public override string Id => "nat-leaves";
    public override string Name => "Falling leaves";
    public override string Category => EffectCategory.Nature;

    protected override void Reset() { leaves.Clear(); time = 0; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        time += dt;
        for (spawn += dt * 2.2; spawn >= 1; spawn--)
            leaves.Add((Random.NextDouble() * KeyGeometry.Width, -0.6, 0.7 + Random.NextDouble() * 0.6, Random.NextDouble() * 6,
                        Colours[Random.Next(Colours.Length)]));
        for (int i = 0; i < leaves.Count; i++) { var l = leaves[i]; leaves[i] = l with { Y = l.Y + l.Speed * dt }; }
        leaves.RemoveAll(l => l.Y > KeyGeometry.Height + 0.6);

        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            var color = new Rgb(18, 7, 0);
            foreach (var l in leaves)
            {
                double lx = l.X + 1.1 * Math.Sin(time * 1.3 + l.Phase);
                double level = 1 - Math.Sqrt((x - lx) * (x - lx) + (y - l.Y) * (y - l.Y) * 1.5) / 0.85;
                if (level > 0) color = EffectMath.Max(color, l.Color.Scale(Math.Min(1, level * 1.4)));
            }
            frame[cell] = color;
        }
    }
}

/// <summary>Bubbles rising through water, wobbling as they go.</summary>
public sealed class BubblesEffect : SteppedEffect
{
    readonly List<(double X, double Y, double Speed, double Size, double Phase)> bubbles = [];
    double spawn, time;

    public override string Id => "nat-bubbles";
    public override string Name => "Bubbles";
    public override string Category => EffectCategory.Nature;

    protected override void Reset() { bubbles.Clear(); time = 0; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        time += dt;
        for (spawn += dt * 3.2; spawn >= 1; spawn--)
            bubbles.Add((Random.NextDouble() * KeyGeometry.Width, KeyGeometry.Height + 0.6, 1 + Random.NextDouble() * 1.2,
                         0.45 + Random.NextDouble() * 0.5, Random.NextDouble() * 6));
        for (int i = 0; i < bubbles.Count; i++) { var b = bubbles[i]; bubbles[i] = b with { Y = b.Y - b.Speed * dt }; }
        bubbles.RemoveAll(b => b.Y < -0.8);

        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell), u = KeyGeometry.U(cell);
            var color = ctx.Secondary(u).Scale(0.1);
            foreach (var b in bubbles)
            {
                double bx = b.X + 0.35 * Math.Sin(time * 3 + b.Phase);
                double d = Math.Sqrt((x - bx) * (x - bx) + (y - b.Y) * (y - b.Y));
                double rim = Glow(d - b.Size, 0.35);   // brighter at the edge than in the middle
                if (rim > 0.05) color = EffectMath.Max(color, Rgb.Lerp(ctx.Primary(u), Rgb.White, 0.3).Scale(rim));
            }
            frame[cell] = color;
        }
    }
}

/// <summary>Coral along the bottom, swaying in the current, with fish swimming past above it.</summary>
public sealed class CoralReefEffect : KeyShader
{
    static readonly Rgb[] Coral = [new(255, 60, 120), new(255, 120, 40), new(170, 60, 255), new(255, 200, 60)];
    static readonly Rgb Water = new(0, 70, 120), Fish = new(255, 150, 0);
    readonly (double X, double Y)[] fish = new (double, double)[3];

    public override string Id => "nat-reef";
    public override string Name => "Coral reef";
    public override string Category => EffectCategory.Nature;

    protected override void Prepare(in EffectContext ctx)
    {
        for (int i = 0; i < fish.Length; i++)
        {
            double span = KeyGeometry.Width + 4;
            double x = (ctx.Time * (1.6 + 0.7 * i) + i * 7) % span - 2;
            fish[i] = (i % 2 == 0 ? x : KeyGeometry.Width - x, 0.8 + i * 1.1 + 0.3 * Math.Sin(ctx.Time * 2 + i));
        }
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double sway = 0.5 * Math.Sin(ctx.Time * 0.9 + x * 0.6);
        double coralTop = 3.4 + 0.9 * Hash((int)Math.Floor(x * 0.9)) + sway * 0.4;
        Rgb color;
        if (y > coralTop) color = Coral[(int)(Hash((int)Math.Floor(x * 0.9) + 20) * Coral.Length) % Coral.Length].Scale(0.55 + 0.45 * Wave(ctx.Time + x));
        else color = Rgb.Lerp(new Rgb(0, 120, 170), Water, KeyGeometry.V(cell)).Scale(0.5 + 0.2 * EffectMath.Noise(x * 0.5 + ctx.Time * 0.3, y));
        for (int i = 0; i < fish.Length; i++)
        {
            double level = Glow(Math.Sqrt((x - fish[i].X) * (x - fish[i].X) + (y - fish[i].Y) * (y - fish[i].Y) * 2), 0.7);
            if (level > 0.05) color = EffectMath.Max(color, (i == 1 ? new Rgb(255, 230, 40) : Fish).Scale(level));
        }
        return color;
    }
}

/// <summary>A glowing volcano in the middle of the bottom row, throwing up sparks that fall back down.</summary>
public sealed class VolcanoEffect : SteppedEffect
{
    readonly List<(double X, double Y, double Vx, double Vy, double Life)> sparks = [];
    double spawn, time;

    public override string Id => "nat-volcano";
    public override string Name => "Volcano";
    public override string Category => EffectCategory.Nature;

    protected override void Reset() { sparks.Clear(); time = 0; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        time += dt;
        double cx = KeyGeometry.Width * 0.42;
        for (spawn += dt * 14; spawn >= 1; spawn--)
            sparks.Add((cx + (Random.NextDouble() - 0.5) * 1.2, KeyGeometry.Height - 1.2, (Random.NextDouble() - 0.5) * 6,
                        -(5 + Random.NextDouble() * 4), 1));
        for (int i = sparks.Count - 1; i >= 0; i--)
        {
            var s = sparks[i];
            s = (s.X + s.Vx * dt, s.Y + s.Vy * dt, s.Vx, s.Vy + 9 * dt, s.Life - dt * 0.6);
            if (s.Life <= 0 || s.Y > KeyGeometry.Height + 0.5) sparks.RemoveAt(i);
            else sparks[i] = s;
        }

        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            double cone = (y - (KeyGeometry.Height - 1.4)) - Math.Abs(x - cx) * 0.55;
            double heat = cone > 0 ? Math.Min(1, 0.4 + cone * 0.5) * (0.75 + 0.25 * EffectMath.Noise(x, time * 3)) : 0;
            double glow = 0.08 * Glow(Math.Sqrt((x - cx) * (x - cx) + (y - KeyGeometry.Height) * (y - KeyGeometry.Height)), 6);
            var color = EffectMath.Fire(Math.Max(heat * 0.9, glow));
            foreach (var s in sparks)
            {
                double level = 1 - Math.Sqrt((x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y)) / 0.6;
                if (level > 0) color = EffectMath.Max(color, EffectMath.Fire(0.5 + 0.45 * s.Life).Scale(level * s.Life));
            }
            frame[cell] = color;
        }
    }
}

/// <summary>Frost creeps in from the edges until the keyboard is iced over, then melts away.</summary>
public sealed class FrostEffect : KeyShader
{
    const double Cycle = 16;
    static readonly Rgb Ice = new(200, 235, 255), Cold = new(0, 25, 60);

    public override string Id => "nat-frost";
    public override string Name => "Frost";
    public override string Category => EffectCategory.Nature;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double edge = Math.Min(Math.Min(x, KeyGeometry.Width - x) / 2.2, Math.Min(y, KeyGeometry.Height - y));
        double reach = 3.2 * (0.5 - 0.5 * Math.Cos(2 * Math.PI * ctx.Time / Cycle));   // how far the frost has come in
        double crystal = EffectMath.Noise(x * 1.3, y * 1.3) * 0.8;
        double frozen = Smooth(reach + 0.4, reach - 0.4, edge + crystal);
        double sparkle = frozen * 0.25 * Math.Pow(EffectMath.Noise(x * 2.5 + 9, ctx.Time * 0.8), 4);
        var color = Rgb.Lerp(Cold, Ice, frozen * 0.85 + sparkle);
        return color;
    }
}

/// <summary>Grass swaying in the wind, flowers dotted through it, under a blue sky.</summary>
public sealed class MeadowEffect : KeyShader
{
    static readonly Rgb Sky = new(40, 120, 230), GrassDark = new(10, 70, 5), GrassLight = new(80, 200, 30);
    static readonly Rgb[] Flowers = [new(255, 60, 160), new(255, 220, 40), new(190, 90, 255), new(255, 255, 255)];

    public override string Id => "nat-meadow";
    public override string Name => "Meadow";
    public override string Category => EffectCategory.Nature;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double ground = 1.7 + 0.35 * Math.Sin(x * 0.4);
        Rgb color;
        if (y < ground) color = Rgb.Lerp(Sky, new Rgb(150, 200, 255), y / ground).Scale(0.7);
        else
        {
            double gust = Wave(x * 0.55 - ctx.Time * 1.6 + y * 0.4);
            color = Rgb.Lerp(GrassDark, GrassLight, gust * (y - ground + 0.5) / 3);
            if (Hash(cell + 900) > 0.82) color = Flowers[(int)(Hash(cell + 77) * Flowers.Length) % Flowers.Length].Scale(0.6 + 0.4 * gust);
        }
        return color;
    }
}
