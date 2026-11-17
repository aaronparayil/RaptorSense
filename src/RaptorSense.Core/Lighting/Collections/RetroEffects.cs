using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// Retro: little games and pixel-era looks that play themselves on the key grid (KeyGrid, 19 by 6).

/// <summary>Conway's Game of Life on the keys: cells are born, live and die by their neighbours.</summary>
public sealed class GameOfLifeEffect : SteppedEffect
{
    const double StepSeconds = 0.32;
    const int W = KeyGrid.Columns, H = KeyGrid.Rows;
    bool[,] alive = new bool[W, H], next = new bool[W, H];
    readonly double[,] shown = new double[W, H];
    readonly Queue<int> history = new();
    double clock;
    int generation;
    bool started;

    public override string Id => "ret-life";
    public override string Name => "Game of Life";
    public override string Category => EffectCategory.Retro;

    protected override void Reset() { clock = 0; started = false; }

    void Seed()
    {
        for (int c = 0; c < W; c++) for (int r = 0; r < H; r++) alive[c, r] = Random.NextDouble() < 0.38;
        generation = 0;
        history.Clear();
    }

    void Advance()
    {
        int population = 0, signature = 17;
        for (int c = 0; c < W; c++)
            for (int r = 0; r < H; r++)
            {
                int n = 0;
                for (int dc = -1; dc <= 1; dc++)
                    for (int dr = -1; dr <= 1; dr++)
                        if ((dc != 0 || dr != 0) && alive[(c + dc + W) % W, (r + dr + H) % H]) n++;
                bool live = alive[c, r] ? n is 2 or 3 : n == 3;
                next[c, r] = live;
                if (live) { population++; signature = unchecked(signature * 31 + c * 7 + r); }
            }
        (alive, next) = (next, alive);
        generation++;
        // Start again when it dies out, settles into a repeat, or has run a long while.
        bool repeating = history.Contains(signature);
        history.Enqueue(signature);
        if (history.Count > 6) history.Dequeue();
        if (population < 4 || repeating || generation > 90) Seed();
    }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        if (!started) { Seed(); started = true; }
        for (clock += dt; clock >= StepSeconds; clock -= StepSeconds) Advance();
        double fade = dt / 0.2;
        for (int c = 0; c < W; c++)
            for (int r = 0; r < H; r++)
                shown[c, r] = alive[c, r] ? Math.Min(1, shown[c, r] + fade) : Math.Max(0, shown[c, r] - fade * 0.6);

        foreach (int cell in KeyGeometry.Cells)
        {
            double level = shown[KeyGrid.Column(cell), KeyGrid.Row(cell)];
            var color = ctx.Settings.AutoColor ? Rgb.FromHsv(KeyGrid.Column(cell) * 18 + ctx.Time * 20) : ctx.Settings.Color;
            frame[cell] = EffectMath.Max(ctx.Secondary(0.5).Scale(0.05), color.Scale(level));
        }
    }
}

/// <summary>Blocks drop down the columns and stack up; a full row clears away.</summary>
public sealed class FallingBlocksEffect : SteppedEffect
{
    const int W = KeyGrid.Columns, H = KeyGrid.Rows;
    const double RowsPerSecond = 14;
    readonly double[,] board = new double[W, H];   // 0 = empty, otherwise the block's colour mix (0..1) + 1
    readonly double[] clearing = new double[H];
    int column = -1;
    double row, tone;
    double wipe;   // > 0 while the whole board fades away

    public override string Id => "ret-blocks";
    public override string Name => "Falling blocks";
    public override string Category => EffectCategory.Retro;

    protected override void Reset() { Array.Clear(board); Array.Clear(clearing); column = -1; wipe = 0; }

    int Height(int c)
    {
        int h = 0;
        while (h < H && board[c, H - 1 - h] > 0) h++;
        return h;
    }

    void Spawn()
    {
        int lowest = Enumerable.Range(0, W).Min(Height);
        var choices = Enumerable.Range(0, W).Where(c => Height(c) == lowest).ToList();
        column = choices[Random.Next(choices.Count)];
        row = -1;
        tone = Random.NextDouble();
    }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        if (wipe > 0)
        {
            wipe -= dt;
            if (wipe <= 0) Reset();
        }
        else
        {
            for (int r = 0; r < H; r++)
            {
                if (clearing[r] <= 0) continue;
                clearing[r] -= dt;
                if (clearing[r] > 0) continue;
                for (int rr = r; rr > 0; rr--) for (int c = 0; c < W; c++) board[c, rr] = board[c, rr - 1];
                for (int c = 0; c < W; c++) board[c, 0] = 0;
            }
            if (column < 0) Spawn();
            row += dt * RowsPerSecond;
            int below = (int)Math.Floor(row) + 1;
            if (below >= H || (below >= 0 && board[column, below] > 0))
            {
                int landed = Math.Max(0, below - 1);
                board[column, landed] = 1 + tone;
                column = -1;
                if (landed == 0) wipe = 1.0;
                bool full = true;
                for (int c = 0; c < W && full; c++) full = board[c, landed] > 0;
                if (full) clearing[landed] = 0.45;
            }
        }

        double wipeLevel = wipe > 0 ? wipe : 1;
        foreach (int cell in KeyGeometry.Cells)
        {
            int c = KeyGrid.Column(cell), r = KeyGrid.Row(cell);
            double value = board[c, r];
            var color = ctx.Secondary(0.5).Scale(0.04);
            if (value > 0)
            {
                var block = ctx.Settings.AutoColor ? Rgb.FromHsv((value - 1) * 360) : ctx.Blend(value - 1, c / (double)W);
                if (clearing[r] > 0) block = Rgb.Lerp(Rgb.White, block, clearing[r] / 0.45).Scale(clearing[r] / 0.45);
                color = block.Scale(wipeLevel);
            }
            if (c == column && r == (int)Math.Floor(row))
                color = ctx.Settings.AutoColor ? Rgb.FromHsv(tone * 360) : Rgb.Lerp(ctx.Blend(tone, 0.5), Rgb.White, 0.3);
            frame[cell] = color;
        }
    }
}

/// <summary>A ball bouncing off the edges of the keyboard, changing colour at every wall.</summary>
public sealed class BouncingBallEffect : SteppedEffect
{
    readonly double[] trail = new double[Frame.CellCount];
    double x = 3, y = 1.5, vx = 5.5, vy = 3.4, hue;
    bool second;

    public override string Id => "ret-bounce";
    public override string Name => "Bouncing ball";
    public override string Category => EffectCategory.Retro;

    protected override void Reset() { Array.Clear(trail); x = 3; y = 1.5; vx = 5.5; vy = 3.4; }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        x += vx * dt;
        y += vy * dt;
        if (x < 0.4 || x > KeyGeometry.Width - 0.4) { vx = -vx; x = Math.Clamp(x, 0.4, KeyGeometry.Width - 0.4); Bounce(); }
        if (y < 0.3 || y > KeyGeometry.Height - 0.3) { vy = -vy; y = Math.Clamp(y, 0.3, KeyGeometry.Height - 0.3); Bounce(); }

        var ball = ctx.Settings.AutoColor ? Rgb.FromHsv(hue) : second ? ctx.Settings.Color2 : ctx.Settings.Color;
        foreach (int cell in KeyGeometry.Cells)
        {
            double near = Glow(KeyGeometry.Distance(cell, x, y), 0.75);
            trail[cell] = Math.Max(trail[cell] - dt * 1.8, near);
            frame[cell] = ball.Scale(trail[cell] * trail[cell]);
        }
    }

    void Bounce()
    {
        hue += 57;
        second = !second;
    }
}

/// <summary>Two paddles knock a ball back and forth across the keyboard.</summary>
public sealed class PaddleRallyEffect : SteppedEffect
{
    double ballX, ballY, vx, vy, leftY, rightY;

    public override string Id => "ret-rally";
    public override string Name => "Paddle rally";
    public override string Category => EffectCategory.Retro;

    double Left => 0.45;
    double Right => KeyGeometry.Width - 0.45;

    protected override void Reset() => Serve(1);

    void Serve(int direction)
    {
        ballX = KeyGeometry.Width / 2;
        ballY = KeyGeometry.Height / 2;
        vx = 7.5 * direction;
        vy = (Random.NextDouble() - 0.5) * 6;
        leftY = rightY = KeyGeometry.Height / 2;
    }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        if (vx == 0) Serve(1);
        ballX += vx * dt;
        ballY += vy * dt;
        if (ballY < 0.3 || ballY > KeyGeometry.Height - 0.3) { vy = -vy; ballY = Math.Clamp(ballY, 0.3, KeyGeometry.Height - 0.3); }
        // Each paddle chases the ball, but only so fast: now and then one misses, and the ball is served again.
        leftY += Math.Clamp(ballY - leftY, -4.2 * dt, 4.2 * dt);
        rightY += Math.Clamp(ballY - rightY, -4.2 * dt, 4.2 * dt);
        if (vx < 0 && ballX <= Left + 0.6)
        {
            if (Math.Abs(ballY - leftY) < 1.3) { vx = -vx; vy += (ballY - leftY) * 3; }
            else if (ballX < -1) Serve(1);
        }
        if (vx > 0 && ballX >= Right - 0.6)
        {
            if (Math.Abs(ballY - rightY) < 1.3) { vx = -vx; vy += (ballY - rightY) * 3; }
            else if (ballX > KeyGeometry.Width + 1) Serve(-1);
        }
        vy = Math.Clamp(vy, -6, 6);

        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            var color = Rgb.Black;
            if (Math.Abs(x - KeyGeometry.Width / 2) < 0.5 && KeyGrid.Row(cell) % 2 == 0) color = ctx.Secondary(0.5).Scale(0.25);
            if (x < Left + 0.7 && Math.Abs(y - leftY) < 1.2) color = ctx.Secondary(0);
            if (x > Right - 0.7 && Math.Abs(y - rightY) < 1.2) color = ctx.Secondary(1);
            double ball = Glow(KeyGeometry.Distance(cell, ballX, ballY), 0.6);
            frame[cell] = EffectMath.Max(color, Rgb.Lerp(ctx.Primary(0.5), Rgb.White, 0.3).Scale(ball));
        }
    }
}

/// <summary>A snake that hunts down food on the keys, growing longer with every bite.</summary>
public sealed class SnakeHuntEffect : SteppedEffect
{
    const int W = KeyGrid.Columns, H = KeyGrid.Rows, MaxLength = 32;
    const double StepSeconds = 0.13;
    readonly LinkedList<(int C, int R)> body = new();
    readonly double[,] shade = new double[W, H];
    (int C, int R) food;
    double clock, ate = 1;

    public override string Id => "ret-snake-hunt";
    public override string Name => "Snake hunt";
    public override string Category => EffectCategory.Retro;

    protected override void Reset() { body.Clear(); clock = 0; }

    void Start()
    {
        body.Clear();
        body.AddFirst((Random.Next(W), Random.Next(H)));
        PlaceFood();
    }

    void PlaceFood()
    {
        do food = (Random.Next(W), Random.Next(H)); while (body.Contains(food));
    }

    bool Free((int C, int R) p) => p.C is >= 0 and < W && p.R is >= 0 and < H && !body.Take(body.Count - 1).Contains(p);

    int Room((int C, int R) p) => new[] { (p.C + 1, p.R), (p.C - 1, p.R), (p.C, p.R + 1), (p.C, p.R - 1) }.Count(Free);

    void Move()
    {
        var head = body.First!.Value;
        var options = new[] { (head.C + 1, head.R), (head.C - 1, head.R), (head.C, head.R + 1), (head.C, head.R - 1) }
            .Where(Free).ToList();
        if (options.Count == 0) { Start(); return; }
        // Closest to the food first, but never into a dead end when there is a way with room.
        var best = options.OrderBy(p => Room(p) == 0 ? 1 : 0)
            .ThenBy(p => Math.Abs(p.Item1 - food.C) + Math.Abs(p.Item2 - food.R))
            .ThenBy(_ => Random.Next()).First();
        body.AddFirst(best);
        if (best == food)
        {
            ate = 0;
            if (body.Count >= MaxLength) { Start(); return; }
            PlaceFood();
        }
        else body.RemoveLast();
    }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        if (body.Count == 0) Start();
        for (clock += dt; clock >= StepSeconds; clock -= StepSeconds) Move();
        ate += dt;

        Array.Clear(shade);
        int i = 0;
        foreach (var (c, r) in body) shade[c, r] = Math.Max(shade[c, r], 1 - 0.75 * i++ / Math.Max(1, body.Count));
        foreach (int cell in KeyGeometry.Cells)
        {
            int c = KeyGrid.Column(cell), r = KeyGrid.Row(cell);
            var color = ctx.Primary(c / (double)W).Scale(shade[c, r]);
            if ((c, r) == food) color = EffectMath.Max(color, ctx.Secondary(0.5).Scale(0.55 + 0.45 * Wave(ctx.Time * 6)));
            if (ate < 0.3 && (c, r) == body.First!.Value) color = Rgb.Lerp(Rgb.White, color, ate / 0.3);
            frame[cell] = color;
        }
    }
}

/// <summary>Bars of different heights, sorted into order one swap at a time, then shuffled again.</summary>
public sealed class SortingEffect : SteppedEffect
{
    const int W = KeyGrid.Columns, H = KeyGrid.Rows;
    const double CompareSeconds = 0.06;
    readonly int[] heights = new int[W];
    int pass, at, swapsThisPass;
    double clock, hold;

    public override string Id => "ret-sort";
    public override string Name => "Sorting";
    public override string Category => EffectCategory.Retro;

    protected override void Reset() { Shuffle(); clock = 0; }

    void Shuffle()
    {
        for (int i = 0; i < W; i++) heights[i] = 1 + Random.Next(H);
        pass = at = swapsThisPass = 0;
        hold = 0;
    }

    void Compare()
    {
        if (hold > 0) return;
        if (heights[at] > heights[at + 1]) { (heights[at], heights[at + 1]) = (heights[at + 1], heights[at]); swapsThisPass++; }
        at++;
        if (at < W - 1 - pass) return;
        if (swapsThisPass == 0 || pass >= W - 2) { hold = 2.2; return; }
        pass++;
        at = 0;
        swapsThisPass = 0;
    }

    protected override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        if (heights[0] == 0) Shuffle();
        if (hold > 0)
        {
            hold -= dt;
            if (hold <= 0) Shuffle();
        }
        for (clock += dt; clock >= CompareSeconds; clock -= CompareSeconds) Compare();

        foreach (int cell in KeyGeometry.Cells)
        {
            int c = KeyGrid.Column(cell), fromBottom = H - 1 - KeyGrid.Row(cell);
            if (fromBottom >= heights[c]) { frame[cell] = Rgb.Black; continue; }
            double t = (heights[c] - 1) / (double)(H - 1);
            var color = ctx.Settings.AutoColor ? Rgb.FromHsv(t * 280) : ctx.Blend(t, t);
            bool comparing = hold <= 0 && (c == at || c == at + 1);
            frame[cell] = comparing ? Rgb.Lerp(color, Rgb.White, 0.6) : hold > 0 ? color : color.Scale(0.75);
        }
    }
}

/// <summary>A chunky progress bar filling up across the keyboard, again and again.</summary>
public sealed class LoadingBarEffect : KeyShader
{
    const double Fill = 6, Hold = 0.8, Fade = 0.8, Cycle = Fill + Hold + Fade;

    public override string Id => "ret-loading";
    public override string Name => "Loading bar";
    public override string Category => EffectCategory.Retro;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        int row = KeyGrid.Row(cell), column = KeyGrid.Column(cell);
        double t = ctx.Time % Cycle;
        if (row is 1 or 5) return ctx.Secondary(0.5).Scale(0.12);   // the bar's frame
        if (row is 0) return Rgb.Black;
        int filled = (int)Math.Floor(Math.Min(t, Fill) / Fill * (KeyGrid.Columns + 0.99));
        double level = column < filled ? 1 : 0;
        if (t > Fill + Hold) level *= 1 - (t - Fill - Hold) / Fade;
        double shine = Glow(column - (ctx.Time * 9 % (KeyGrid.Columns + 6) - 3), 1.2) * 0.45;
        var color = ctx.Blend(column / (double)KeyGrid.Columns, column / (double)KeyGrid.Columns);
        return Rgb.Lerp(color, Rgb.White, shine * level).Scale(level);
    }
}

/// <summary>A rainbow in seven flat colours, stepping along the keys like an old game's title screen.</summary>
public sealed class EightBitRainbowEffect : KeyShader
{
    static readonly Rgb[] Colours =
        [new(255, 0, 0), new(255, 128, 0), new(255, 230, 0), new(0, 220, 0), new(0, 150, 255), new(80, 0, 255), new(200, 0, 255)];

    public override string Id => "ret-8bit-rainbow";
    public override string Name => "8-bit rainbow";
    public override string Category => EffectCategory.Retro;

    protected override Rgb Shade(int cell, double x, double y, in EffectContext ctx)
    {
        int step = (int)Math.Floor(ctx.Time * 5);
        int index = ((KeyGrid.Column(cell) + KeyGrid.Row(cell) + step) / 2 % Colours.Length + Colours.Length) % Colours.Length;
        return Colours[index];
    }
}
