using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// Sci-fi: screens, scanners and energy.

/// <summary>Pulses of light racing along the traces of a circuit board.</summary>
public sealed class CircuitBoardEffect : KeyShader
{
    const int W = KeyGrid.Columns, H = KeyGrid.Rows, Tail = 4;
    static readonly List<(int C, int R)>[] Paths = BuildPaths();
    // For each pixel of the grid: which path passes through it, and where along it.
    static readonly List<(int Path, int Index)>[,] OnPath = Index();
    readonly double[,] lit = new double[W, H];

    public override string Id => "sci-circuit";
    public override string Name => "Circuit board";
    public override string Category => EffectCategory.SciFi;

    static List<(int C, int R)>[] BuildPaths()
    {
        var random = new Random(7);   // the same board every time
        var paths = new List<(int C, int R)>[7];
        for (int p = 0; p < paths.Length; p++)
        {
            var path = new List<(int C, int R)>();
            int c = random.Next(W), r = random.Next(H), dc = 1, dr = 0;
            int length = 14 + random.Next(10);
            while (path.Count < length)
            {
                path.Add((c, r));
                if (random.NextDouble() < 0.3) (dc, dr) = dr == 0 ? (0, random.Next(2) * 2 - 1) : (random.Next(2) * 2 - 1, 0);
                if (c + dc is < 0 or >= W) dc = -dc;
                if (r + dr is < 0 or >= H) dr = -dr;
                c += dc;
                r += dr;
            }
            paths[p] = path;
        }
        return paths;
    }

    static List<(int, int)>[,] Index()
    {
        var index = new List<(int, int)>[W, H];
        for (int c = 0; c < W; c++) for (int r = 0; r < H; r++) index[c, r] = [];
        for (int p = 0; p < Paths.Length; p++)
            for (int i = 0; i < Paths[p].Count; i++) index[Paths[p][i].C, Paths[p][i].R].Add((p, i));
        return index;
    }

    protected override void Prepare(in EffectContext ctx)
    {
        for (int c = 0; c < W; c++)
            for (int r = 0; r < H; r++)
            {
                double level = 0;
                foreach (var (p, i) in OnPath[c, r])
                {
                    double span = Paths[p].Count + Tail * 2;
                    double head = (ctx.Time * (6 + p) + Hash(p) * span) % span - Tail;
                    double behind = head - i;
                    level = Math.Max(level, behind is >= 0 and <= Tail ? 1 - behind / Tail : 0.12);
                }
                lit[c, r] = level;
            }
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double level = lit[KeyGrid.Column(cell), KeyGrid.Row(cell)];
        if (level <= 0) return new Rgb(0, 12, 6);
        return level <= 0.12 ? ctx.Secondary(KeyGeometry.U(cell)).Scale(0.2) : Rgb.Lerp(ctx.Primary(KeyGeometry.U(cell)), Rgb.White, level * 0.3).Scale(level);
    }
}

/// <summary>A beam sweeping back and forth across the keyboard, with a long glowing tail.</summary>
public sealed class ScanBeamEffect : KeyShader
{
    const double Period = 2.8;
    double position, direction;

    public override string Id => "sci-scan-beam";
    public override string Name => "Scan beam";
    public override string Category => EffectCategory.SciFi;

    protected override void Prepare(in EffectContext ctx)
    {
        double t = Fract(ctx.Time / Period) * 2;
        double eased = 0.5 - 0.5 * Math.Cos(Math.PI * (t < 1 ? t : 2 - t));
        position = eased * KeyGeometry.Width;
        direction = t < 1 ? 1 : -1;
    }

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double behind = (position - x) * direction;
        double level = behind < 0 ? Glow(behind, 0.5) : Math.Exp(-behind / 2.2);
        return EffectMath.Max(ctx.Secondary(KeyGeometry.U(cell)).Scale(0.05), ctx.Primary(KeyGeometry.U(cell)).Scale(level));
    }
}

/// <summary>A flickery hologram: a light band climbing up through faint scan lines.</summary>
public sealed class HologramEffect : KeyShader
{
    public override string Id => "sci-hologram";
    public override string Name => "Hologram";
    public override string Category => EffectCategory.SciFi;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double band = Glow(y - (KeyGeometry.Height + 1 - Fract(ctx.Time * 0.35) * (KeyGeometry.Height + 2.5)), 0.7);
        double shimmer = 0.08 * EffectMath.Noise(cell * 0.9, ctx.Time * 5);
        double lines = KeyGrid.Row(cell) % 2 == 0 ? 1 : 0.75;
        var tint = ctx.Settings.AutoColor ? Rgb.FromHsv(180 + ctx.Time * 15) : ctx.Settings.Color;
        return Rgb.Lerp(tint, Rgb.White, band * 0.4).Scale((0.3 + 0.6 * band + shimmer) * lines);
    }
}

/// <summary>Rows of bits flowing past, each row at its own speed and direction.</summary>
public sealed class DataStreamEffect : KeyShader
{
    public override string Id => "sci-data-stream";
    public override string Name => "Data stream";
    public override string Category => EffectCategory.SciFi;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        int row = KeyGrid.Row(cell);
        double speed = (2 + 4 * Hash(row + 11)) * (row % 2 == 0 ? 1 : -1);
        double position = x * 1.3 + ctx.Time * speed;
        int bit = (int)Math.Floor(position);
        double edge = Fract(position);
        double on = Hash(bit * 31 + row * 1013) > 0.5 ? Smooth(0, 0.15, edge) * Smooth(1, 0.85, edge) : 0;
        double level = on * (0.4 + 0.6 * Hash(bit * 7 + row));
        return EffectMath.Max(ctx.Secondary(KeyGeometry.U(cell)).Scale(0.05), ctx.Primary(KeyGeometry.U(cell)).Scale(level));
    }
}

/// <summary>Rings of a heads-up display turning round a centre point, each its own way.</summary>
public sealed class HudRingsEffect : KeyShader
{
    static readonly (double Radius, int Segments, double Speed)[] Rings = [(1.6, 3, 1.2), (3.7, 5, -0.6), (6.3, 8, 0.35)];

    public override string Id => "sci-hud";
    public override string Name => "HUD rings";
    public override string Category => EffectCategory.SciFi;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double dx = x - KeyGeometry.Width / 2, dy = (y - KeyGeometry.Height / 2) * 2.3;
        double r = Math.Sqrt(dx * dx + dy * dy), turn = (Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1;
        var color = ctx.Secondary(0.5).Scale(Glow(r, 0.6));
        for (int i = 0; i < Rings.Length; i++)
        {
            var (radius, segments, speed) = Rings[i];
            double f = Fract(turn * segments + ctx.Time * speed);
            double on = Smooth(0.08, 0.16, f) * Smooth(0.82, 0.74, f);
            double level = Glow(r - radius, 0.45) * on;
            var tint = i == 1 ? ctx.Secondary(turn) : ctx.Primary(turn);
            color = EffectMath.Max(color, tint.Scale(level));
        }
        return color;
    }
}

/// <summary>A clean gradient that every so often glitches: a row slips sideways and changes colour for a moment.</summary>
public sealed class GlitchEffect : SteppedEffect
{
    int row = -1;
    double offset, until, next, time;

    public override string Id => "sci-glitch";
    public override string Name => "Glitch";
    public override string Category => EffectCategory.SciFi;

    protected override void Reset() { row = -1; next = 0; time = 0; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        time += dt;
        if (time >= next)
        {
            row = Random.Next(KeyGrid.Rows);
            offset = (Random.Next(2) * 2 - 1) * (1 + Random.NextDouble() * 4);
            until = time + 0.12 + Random.NextDouble() * 0.15;
            next = time + 0.4 + Random.NextDouble() * 1.4;
        }
        bool glitching = time < until;
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell);
            bool slipped = glitching && KeyGrid.Row(cell) == row;
            if (slipped) x += offset;
            double u = Fract(x / KeyGeometry.Width + time * 0.05);
            var color = ctx.Blend(Wave(u * 2 * Math.PI), u);
            frame[cell] = slipped ? Turn(color, 140) : color.Scale(0.9 + 0.1 * EffectMath.Noise(cell, time * 3));
        }
    }
}

/// <summary>A glowing reactor core, sending rings of energy out across the keyboard.</summary>
public sealed class ReactorCoreEffect : KeyShader
{
    const double RingEvery = 1.4;

    public override string Id => "sci-reactor";
    public override string Name => "Reactor core";
    public override string Category => EffectCategory.SciFi;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double dx = x - KeyGeometry.Width / 2, dy = (y - KeyGeometry.Height / 2) * 1.8;
        double r = Math.Sqrt(dx * dx + dy * dy);
        double core = Glow(r, 1.6) * (0.8 + 0.2 * Math.Sin(ctx.Time * 3));
        double ring = 0;
        for (int k = 0; k < 3; k++)
        {
            double age = Fract(ctx.Time / RingEvery + k / 3.0) * RingEvery * 2.2;
            ring = Math.Max(ring, Glow(r - age * 4, 0.6) * Math.Max(0, 1 - age / (RingEvery * 2.2)));
        }
        var energy = ctx.Primary(0.5);
        return EffectMath.Max(Rgb.Lerp(energy, Rgb.White, 0.6).Scale(core), energy.Scale(ring * 0.9 + 0.05));
    }
}

/// <summary>Two strands winding round each other along the keyboard, joined by rungs, like DNA.</summary>
public sealed class DnaHelixEffect : KeyShader
{
    public override string Id => "sci-dna";
    public override string Name => "DNA helix";
    public override string Category => EffectCategory.SciFi;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double phase = x * 0.65 - ctx.Time * 1.6;
        double cy = KeyGeometry.Height / 2, amplitude = 1.9;
        double a = cy + amplitude * Math.Sin(phase), b = cy - amplitude * Math.Sin(phase);
        double front = Math.Cos(phase);   // which strand is in front, for depth
        double u = KeyGeometry.U(cell);
        var color = ctx.Primary(u).Scale(Glow(y - a, 0.5) * (0.6 + 0.4 * front));
        color = EffectMath.Max(color, ctx.Secondary(u).Scale(Glow(y - b, 0.5) * (0.6 - 0.4 * front)));
        double rung = Smooth(0.8, 0.97, Wave(phase * 3)) * (y > Math.Min(a, b) && y < Math.Max(a, b) ? 1 : 0);
        return EffectMath.Max(color, Rgb.White.Scale(rung * 0.25));
    }
}

/// <summary>Keys light up one by one in reading order, as if a terminal were typing, then the screen clears.</summary>
public sealed class TerminalEffect : SteppedEffect
{
    static readonly int[] Order = KeyGeometry.Cells.Where(c => KeyGrid.Row(c) > 0).ToArray();
    int typed;
    double clock, pause, clearing, time;

    public override string Id => "sci-terminal";
    public override string Name => "Terminal";
    public override string Category => EffectCategory.SciFi;

    protected override void Reset() { typed = 0; clock = pause = clearing = time = 0; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        time += dt;
        if (clearing > 0)
        {
            clearing -= dt;
            if (clearing <= 0) typed = 0;
        }
        else if (pause > 0) pause -= dt;
        else
            for (clock += dt; clock >= 1 / 15.0 && pause <= 0; clock -= 1 / 15.0)
            {
                typed++;
                if (typed >= Order.Length) { clearing = 0.8; clock = 0; break; }
                if (Random.NextDouble() < 0.06) pause = 0.3 + Random.NextDouble() * 0.6;
            }

        var green = ctx.Settings.AutoColor ? Rgb.FromHsv(120 + time * 15) : new Rgb(40, 255, 90);
        double fade = clearing > 0 ? clearing / 0.8 : 1;
        frame.Fill(Rgb.Black);
        for (int i = 0; i < Order.Length && i < typed; i++)
            frame[Order[i]] = green.Scale((0.25 + 0.15 * Hash(Order[i] + typed / 40)) * fade);
        if (clearing <= 0 && typed < Order.Length)
        {
            bool blink = pause <= 0 || Fract(time) < 0.6;   // a steady cursor while typing, a slow blink while waiting
            frame[Order[typed]] = blink ? Rgb.Lerp(green, Rgb.White, 0.4) : green.Scale(0.15);
        }
    }
}

/// <summary>Energy charges up from left to right, glows full, then drains away.</summary>
public sealed class ChargingEffect : KeyShader
{
    const double Fill = 4.5, Full = 1.5, Drain = 2, Cycle = Fill + Full + Drain;

    public override string Id => "sci-charging";
    public override string Name => "Charging";
    public override string Category => EffectCategory.SciFi;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        double t = ctx.Time % Cycle;
        double level = t < Fill ? t / Fill : t < Fill + Full ? 1 : 1 - (t - Fill - Full) / Drain;
        double front = level * (KeyGeometry.Width + 1);
        double inside = Smooth(front + 0.4, front - 0.4, x);
        double edge = Glow(x - front, 0.5) * (t < Fill ? 1 : 0);
        double glow = t >= Fill && t < Fill + Full ? 0.25 * Math.Sin(Math.PI * (t - Fill) / Full) : 0;
        var color = ctx.Settings.AutoColor ? Rgb.FromHsv(level * 120) : ctx.Blend(level, KeyGeometry.U(cell));
        return Rgb.Lerp(color.Scale(0.85 * inside), Rgb.White, Math.Min(1, edge * 0.7 + glow * inside));
    }
}
