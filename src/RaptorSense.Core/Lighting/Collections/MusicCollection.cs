using RaptorSense.Audio;
using static RaptorSense.Lighting.Shaping;

namespace RaptorSense.Lighting;

// More Music effects. They stay dark until there is audio to follow. Anything that lights the whole keyboard moves
// with a smoothed level, so a fast beat can't make it flash.

/// <summary>Bookkeeping shared by the music effects: the bands, smoothed levels and beats.</summary>
public abstract class MusicEffect : SteppedEffect
{
    readonly BeatDetector beats = new();
    double smoothLevel, smoothBass;

    public override string Category => EffectCategory.Music;
    public override bool UsesAudio => true;

    /// <summary>How many bands <see cref="Draw"/> gets.</summary>
    protected virtual int BandCount => 16;

    /// <summary>Overall level, following the music over about a third of a second.</summary>
    protected double Level => smoothLevel;

    /// <summary>Bass level, following quickly but not instantly.</summary>
    protected double Bass => smoothBass;

    protected sealed override void Step(Frame frame, in EffectContext ctx, double dt)
    {
        if (ctx.Audio is not { } audio) return;
        var bands = audio.GetBands(BandCount);
        double real = Math.Clamp(ctx.DeltaTime, 0, 0.1);
        smoothLevel += (audio.Level - smoothLevel) * (1 - Math.Exp(-real / 0.35));
        smoothBass += (audio.Bass - smoothBass) * (1 - Math.Exp(-real / 0.08));
        bool beat = beats.Update(audio.Bass, real);
        Draw(frame, ctx, bands, beat, dt, real);
    }

    protected override void Reset() { beats.Reset(); smoothLevel = smoothBass = 0; }

    /// <param name="real">Seconds since the last frame, not scaled by the speed: music doesn't speed up.</param>
    protected abstract void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real);

    /// <summary>The band under a position 0..1, blending between neighbours.</summary>
    protected static double BandAt(float[] bands, double position)
    {
        double f = Math.Clamp(position, 0, 1) * (bands.Length - 1);
        int i = Math.Min((int)f, bands.Length - 2);
        return bands[i] + (bands[i + 1] - bands[i]) * (f - i);
    }
}

/// <summary>A level meter growing out from the middle, green to red, with a peak that falls back slowly.</summary>
public sealed class VuMeterEffect : MusicEffect
{
    double peak;

    public override string Id => "mus-vu";
    public override string Name => "VU meter";

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        double level = Math.Clamp(Bass * 0.6 + Level * 0.6, 0, 1);
        peak = Math.Max(level, peak - real * 0.4);
        double half = KeyGeometry.Width / 2;
        foreach (int cell in KeyGeometry.Cells)
        {
            double from = Math.Abs(KeyGeometry.X(cell) - half) / half;   // 0 in the middle, 1 at the edges
            var color = ctx.Settings.AutoColor ? Rgb.FromHsv(from * 240 + ctx.Time * 30)
                : Palette(from, new Rgb(0, 220, 60), new Rgb(255, 220, 0), new Rgb(255, 30, 0));
            double lit = Smooth(level + 0.03, level - 0.03, from);
            double mark = Glow(from - peak, 0.04);
            frame[cell] = EffectMath.Max(color.Scale(lit), Rgb.White.Scale(mark * 0.8));
        }
    }
}

/// <summary>A spectrum whose bars grow up and down from the middle row.</summary>
public sealed class MirroredSpectrumEffect : MusicEffect
{
    public override string Id => "mus-mirror";
    public override string Name => "Mirrored spectrum";

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        double middle = KeyGeometry.Height / 2;
        foreach (int cell in KeyGeometry.Cells)
        {
            double u = KeyGeometry.U(cell);
            double height = BandAt(bands, u) * middle * 1.15;
            double from = Math.Abs(KeyGeometry.Y(cell) - middle);
            double lit = Math.Clamp(height - from + 0.5, 0, 1);
            frame[cell] = ctx.Blend(from / middle, u).Scale(lit);
        }
    }
}

/// <summary>A spectrogram: the spectrum along the top row, older moments scrolling down below it.</summary>
public sealed class SpectrogramEffect : MusicEffect
{
    const double RowSeconds = 0.06;
    readonly double[,] history = new double[KeyGrid.Columns, KeyGrid.Rows];
    double clock;

    public override string Id => "mus-spectrogram";
    public override string Name => "Spectrogram";
    protected override int BandCount => KeyGrid.Columns;

    protected override void Reset() { base.Reset(); Array.Clear(history); }

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        for (clock += real; clock >= RowSeconds; clock -= RowSeconds)
            for (int c = 0; c < KeyGrid.Columns; c++)
            {
                for (int r = KeyGrid.Rows - 1; r > 0; r--) history[c, r] = history[c, r - 1];
                history[c, 0] = bands[c];
            }
        foreach (int cell in KeyGeometry.Cells)
        {
            double v = history[KeyGrid.Column(cell), KeyGrid.Row(cell)];
            frame[cell] = ctx.Settings.AutoColor ? Rgb.FromHsv(240 - v * 240).Scale(Math.Min(1, v * 1.5)) : EffectMath.Fire(v * 1.1);
        }
    }
}

/// <summary>Every beat sends a ring out from the middle, each in the next colour.</summary>
public sealed class BeatRingsEffect : MusicEffect
{
    readonly List<(double Start, double Hue)> rings = [];
    double time, hue;

    public override string Id => "mus-beat-rings";
    public override string Name => "Beat rings";

    protected override void Reset() { base.Reset(); rings.Clear(); time = 0; }

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        time += real;
        if (beat) { rings.Add((time, hue)); hue += 47; }
        rings.RemoveAll(r => time - r.Start > 1.2);
        double cx = KeyGeometry.Width / 2, cy = KeyGeometry.Height / 2;
        foreach (int cell in KeyGeometry.Cells)
        {
            double d = Math.Sqrt(Math.Pow(KeyGeometry.X(cell) - cx, 2) + Math.Pow((KeyGeometry.Y(cell) - cy) * 1.6, 2));
            var color = ctx.Primary(0.5).Scale(0.05 + 0.1 * Level);
            foreach (var (start, h) in rings)
            {
                double age = time - start;
                double ring = Glow(d - age * 9, 0.9) * (1 - age / 1.2);
                var tint = ctx.Settings.AutoColor ? Rgb.FromHsv(h) : (int)(h / 47) % 2 == 0 ? ctx.Settings.Color : ctx.Settings.Color2;
                color = EffectMath.Max(color, tint.Scale(ring));
            }
            frame[cell] = color;
        }
    }
}

/// <summary>Flames that leap higher the louder the music plays.</summary>
public sealed class MusicFireEffect : MusicEffect
{
    double time;

    public override string Id => "mus-fire";
    public override string Name => "Music fire";

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        time += dt;
        foreach (int cell in KeyGeometry.Cells)
        {
            double u = KeyGeometry.U(cell), v = KeyGeometry.V(cell);
            double reach = 0.25 + 0.75 * Math.Clamp(BandAt(bands, u) * 0.6 + Level * 0.5, 0, 1);
            double flicker = EffectMath.Noise(KeyGeometry.X(cell) * 0.7, KeyGeometry.Y(cell) * 0.9 + time * 3.2);
            double heat = Math.Clamp((v - (1 - reach)) / reach, 0, 1) * (0.6 + 0.7 * flicker);
            frame[cell] = ctx.Settings.AutoColor ? Rgb.FromHsv(260 + heat * 100).Scale(Math.Min(1, heat)) : EffectMath.Fire(heat);
        }
    }
}

/// <summary>A wave running along the keyboard that swings wider with the bass.</summary>
public sealed class BassWaveEffect : MusicEffect
{
    double time;

    public override string Id => "mus-bass-wave";
    public override string Name => "Bass wave";

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        time += dt;
        double amplitude = 0.3 + 2.4 * Bass;
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), u = KeyGeometry.U(cell);
            double curve = KeyGeometry.Height / 2 + amplitude * Math.Sin(x * 0.55 - time * 3);
            double level = Glow(KeyGeometry.Y(cell) - curve, 0.55 + 0.3 * Bass);
            frame[cell] = EffectMath.Max(ctx.Secondary(u).Scale(0.04), ctx.Blend(Bass, u).Scale(level));
        }
    }
}

/// <summary>Sparkles that come thick and fast with the high notes: cymbals and hi-hats.</summary>
public sealed class TrebleSparkleEffect : MusicEffect
{
    readonly List<(int Cell, double Start, double Hue)> sparks = [];
    double time, debt;

    public override string Id => "mus-treble-sparkle";
    public override string Name => "Treble sparkle";

    protected override void Reset() { base.Reset(); sparks.Clear(); time = 0; }

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        time += real;
        double treble = 0;
        for (int i = bands.Length * 2 / 3; i < bands.Length; i++) treble += bands[i];
        treble /= bands.Length - bands.Length * 2 / 3;
        for (debt += real * treble * 90; debt >= 1; debt--) sparks.Add((RandomCell(), time, Random.NextDouble() * 360));
        sparks.RemoveAll(s => time - s.Start > 0.5);
        if (sparks.Count > 80) sparks.RemoveRange(0, sparks.Count - 80);

        foreach (int cell in KeyGeometry.Cells) frame[cell] = ctx.Primary(KeyGeometry.U(cell)).Scale(0.07);
        foreach (var (cell, start, hue) in sparks)
        {
            double age = (time - start) / 0.5;
            double level = age < 0.15 ? age / 0.15 : 1 - (age - 0.15) / 0.85;
            var tint = ctx.Settings.AutoColor ? Rgb.FromHsv(hue) : Rgb.Lerp(ctx.Settings.Color2, Rgb.White, 0.4);
            frame[cell] = EffectMath.Max(frame[cell], tint.Scale(level));
        }
    }
}

/// <summary>A disco floor: tiles of colour that change on the beat, blending rather than flashing.</summary>
public sealed class DiscoFloorEffect : MusicEffect
{
    const int TileColumns = 7, TileRows = 3;
    readonly Rgb[] from = new Rgb[TileColumns * TileRows], to = new Rgb[TileColumns * TileRows];
    double blend = 1;

    public override string Id => "mus-disco";
    public override string Name => "Disco floor";

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        if (beat || to[0] == Rgb.Black)
        {
            for (int i = 0; i < to.Length; i++)
            {
                from[i] = Rgb.Lerp(from[i], to[i], blend);
                to[i] = ctx.Settings.AutoColor || Random.NextDouble() < 0.5 ? Rgb.FromHsv(Random.NextDouble() * 360, 0.9) : Random.Next(2) == 0 ? ctx.Settings.Color : ctx.Settings.Color2;
            }
            blend = 0;
        }
        blend = Math.Min(1, blend + real / 0.25);
        foreach (int cell in KeyGeometry.Cells)
        {
            int tx = Math.Min(TileColumns - 1, (int)(KeyGeometry.U(cell) * TileColumns));
            int ty = Math.Min(TileRows - 1, (int)(KeyGeometry.V(cell) * TileRows));
            frame[cell] = Rgb.Lerp(from[ty * TileColumns + tx], to[ty * TileColumns + tx], blend).Scale(0.55 + 0.35 * Level);
        }
    }
}

/// <summary>Aurora curtains that flow faster and glow brighter as the music gets louder.</summary>
public sealed class MusicAuroraEffect : MusicEffect
{
    double phase;

    public override string Id => "mus-aurora";
    public override string Name => "Music aurora";

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        phase += dt * (0.4 + 2.2 * Level);
        foreach (int cell in KeyGeometry.Cells)
        {
            double x = KeyGeometry.X(cell), y = KeyGeometry.Y(cell);
            double mix = EffectMath.Noise(x * 0.22 + phase * 0.35, y * 0.3 - phase * 0.15);
            double glow = (0.2 + 0.8 * EffectMath.Noise(x * 0.4 - phase * 0.5, y * 0.5 + 17)) * (0.3 + 0.7 * Level);
            var color = ctx.Settings.AutoColor ? Rgb.FromHsv(120 + mix * 200) : Rgb.Lerp(ctx.Settings.Color, ctx.Settings.Color2, mix);
            frame[cell] = color.Scale(Math.Min(1, glow * 1.3));
        }
    }
}

/// <summary>The whole keyboard takes the mood of the sound: warm for bass-heavy music, cool for bright, airy music.</summary>
public sealed class MoodColourEffect : MusicEffect
{
    double hue = 200, time;

    public override string Id => "mus-mood";
    public override string Name => "Mood colour";

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        time += real;
        double sum = 0, weighted = 0;
        for (int i = 0; i < bands.Length; i++) { sum += bands[i]; weighted += bands[i] * i; }
        double centre = sum > 0.01 ? weighted / sum / (bands.Length - 1) : 0.5;   // 0 = all bass, 1 = all treble
        double target = 10 + centre * 250;
        hue += (target - hue) * (1 - Math.Exp(-real / 0.8));
        foreach (int cell in KeyGeometry.Cells)
        {
            double u = KeyGeometry.U(cell);
            frame[cell] = Rgb.FromHsv(hue + (u - 0.5) * 40 + Math.Sin(time * 0.5) * 10).Scale(0.3 + 0.7 * Level);
        }
    }
}

/// <summary>An equaliser across the rows: the bottom row is the bass, the F row the treble, each lit from the left.</summary>
public sealed class RowEqualiserEffect : MusicEffect
{
    public override string Id => "mus-row-eq";
    public override string Name => "Row equaliser";
    protected override int BandCount => KeyGrid.Rows;

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        foreach (int cell in KeyGeometry.Cells)
        {
            double level = bands[KeyGrid.Rows - 1 - KeyGrid.Row(cell)];
            double u = KeyGeometry.U(cell);
            double lit = Smooth(level + 0.04, level - 0.04, u);
            frame[cell] = ctx.Blend(u, u).Scale(lit);
        }
    }
}

/// <summary>The spectrum drawn round a circle: low notes to the left and right, high notes up top and below.</summary>
public sealed class CircularSpectrumEffect : MusicEffect
{
    public override string Id => "mus-circle";
    public override string Name => "Circular spectrum";

    protected override void Draw(Frame frame, in EffectContext ctx, float[] bands, bool beat, double dt, double real)
    {
        double cx = KeyGeometry.Width / 2, cy = KeyGeometry.Height / 2;
        foreach (int cell in KeyGeometry.Cells)
        {
            double dx = KeyGeometry.X(cell) - cx, dy = (KeyGeometry.Y(cell) - cy) * 3;
            double r = Math.Sqrt(dx * dx + dy * dy);
            double band = Math.Abs(Math.Atan2(dy, Math.Abs(dx))) / (Math.PI / 2);   // 0 sideways, 1 straight up or down
            double reach = 1 + BandAt(bands, band) * 8;
            double lit = Math.Clamp(reach - r, 0, 1);
            var color = ctx.Settings.AutoColor ? Rgb.FromHsv(band * 240 + ctx.Time * 20) : ctx.Blend(r / 9, band);
            frame[cell] = color.Scale(lit);
        }
    }
}
