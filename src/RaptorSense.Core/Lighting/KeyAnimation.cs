using System.Text.Json.Serialization;
using RaptorSense.Audio;
using RaptorSense.Layout;

namespace RaptorSense.Lighting;

/// <summary>How an animation follows the music the computer is playing.</summary>
public enum MusicReaction
{
    /// <summary>It doesn't: frames play at their own times.</summary>
    None,

    /// <summary>Each beat moves to the next frame.</summary>
    Beat,

    /// <summary>It plays faster the louder the music is.</summary>
    Speed,

    /// <summary>It plays at its own times and its brightness follows the bass.</summary>
    Pulse,
}

public static class MusicReactions
{
    /// <summary>Reads the word stored in an animation file. Anything unknown means no reaction.</summary>
    public static MusicReaction Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "beat" => MusicReaction.Beat,
        "speed" => MusicReaction.Speed,
        "pulse" => MusicReaction.Pulse,
        _ => MusicReaction.None,
    };

    /// <summary>The word to store, or null for no reaction (so files without one stay as they were).</summary>
    public static string? ToText(MusicReaction reaction) => reaction == MusicReaction.None ? null : reaction.ToString().ToLowerInvariant();
}

/// <summary>One picture of an animation: painted keys, how long it stays, and how long it takes to fade into the next.</summary>
public sealed record AnimationFrame
{
    public const double MaxHold = 60, MaxFade = 30;

    /// <summary>Seconds the frame stays as painted.</summary>
    public double Hold { get; init; } = 0.5;

    /// <summary>Seconds it then takes to blend into the next frame (0 = jump).</summary>
    public double Fade { get; init; }

    /// <summary>Painted keys by label (see <see cref="KeyMap"/>), as RRGGBB. Keys not listed are off.</summary>
    public Dictionary<string, string> Keys { get; init; } = [];

    /// <summary>The frame's colour for every lighting cell. Unknown keys and bad colours are skipped.</summary>
    public Rgb[] ToCells()
    {
        var cells = new Rgb[Frame.CellCount];
        foreach (var (label, hex) in Keys ?? [])
        {
            if (KeyMap.Find(label) is { } key && Rgb.TryParse(hex, out var color)) cells[key.Cell] = color;
        }
        return cells;
    }

    public static AnimationFrame FromCells(ReadOnlySpan<Rgb> cells, double hold, double fade)
    {
        var keys = new Dictionary<string, string>();
        foreach (var key in KeyMap.Keys)
        {
            if (key.Cell < cells.Length && cells[key.Cell] != Rgb.Black) keys[key.Label] = cells[key.Cell].ToString();
        }
        return new AnimationFrame { Hold = hold, Fade = fade, Keys = keys };
    }

    /// <summary>A time from a file, made safe to use: a real number from 0 to <paramref name="max"/> seconds.</summary>
    public static double CleanSeconds(double seconds, double max) => double.IsFinite(seconds) ? Math.Clamp(seconds, 0, max) : 0;
}

/// <summary>
/// A key animation the user built: frames played in order, looping. Stored as readable JSON so people can share and
/// hand-edit them. It is data only (colours and times), never code, so a shared file can't do anything else.
/// </summary>
public sealed record KeyAnimation
{
    public const int MaxFrames = 500;

    public string Name { get; init; } = "";

    public IReadOnlyList<AnimationFrame> Frames { get; init; } = [];

    /// <summary>How it follows the music: "beat", "speed" or "pulse" (see <see cref="MusicReaction"/>). Left out when it doesn't.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Music { get; init; }

    /// <summary>Turns the frames into colour arrays once, so playing costs one blend of 128 colours per frame drawn.</summary>
    public CompiledAnimation Compile() =>
        new(Name ?? "", (Frames ?? []).OfType<AnimationFrame>().Take(MaxFrames).Select(f => (f.ToCells(), f.Hold, f.Fade)).ToList(), MusicReactions.Parse(Music));

    /// <summary>
    /// The same animation with only what RaptorSense understands: known keys, valid colours, times in range and at most
    /// <see cref="MaxFrames"/> frames. Used for files that come from somewhere else.
    /// </summary>
    public KeyAnimation Cleaned() => this with
    {
        Name = Name ?? "",
        Music = MusicReactions.ToText(MusicReactions.Parse(Music)),
        Frames = (Frames ?? []).OfType<AnimationFrame>().Take(MaxFrames)
            .Select(f => AnimationFrame.FromCells(f.ToCells(), AnimationFrame.CleanSeconds(f.Hold, AnimationFrame.MaxHold), AnimationFrame.CleanSeconds(f.Fade, AnimationFrame.MaxFade)))
            .ToList(),
    };
}

/// <summary>An animation ready to play: immutable, so the editor can swap in a new one while the engine draws.</summary>
public sealed class CompiledAnimation
{
    /// <summary>Shortest time a frame lasts, so an animation of zero-length frames still moves instead of spinning.</summary>
    public const double MinFrameSeconds = 1.0 / 60;

    readonly Rgb[][] frames;
    readonly double[] starts, holds, fades;

    public CompiledAnimation(string name, IReadOnlyList<(Rgb[] Cells, double Hold, double Fade)> frames, MusicReaction music = MusicReaction.None)
    {
        Name = name;
        Music = music;
        int count = frames.Count;
        this.frames = new Rgb[count][];
        starts = new double[count];
        holds = new double[count];
        fades = new double[count];
        double at = 0;
        for (int i = 0; i < count; i++)
        {
            var cells = new Rgb[Frame.CellCount];
            frames[i].Cells.AsSpan(0, Math.Min(frames[i].Cells.Length, Frame.CellCount)).CopyTo(cells);
            this.frames[i] = cells;
            fades[i] = AnimationFrame.CleanSeconds(frames[i].Fade, AnimationFrame.MaxFade);
            holds[i] = Math.Max(AnimationFrame.CleanSeconds(frames[i].Hold, AnimationFrame.MaxHold), MinFrameSeconds - fades[i]);
            starts[i] = at;
            at += holds[i] + fades[i];
        }
        Length = at;
    }

    public string Name { get; }

    public MusicReaction Music { get; }

    public int Count => frames.Length;

    /// <summary>Seconds for one pass through every frame.</summary>
    public double Length { get; }

    /// <summary>Draws the animation as it looks <paramref name="time"/> seconds after it started (it loops).</summary>
    public void Render(Frame target, double time)
    {
        if (Count == 0) return;
        double t = time % Length;
        if (t < 0) t += Length;
        int i = Array.BinarySearch(starts, t);
        if (i < 0) i = ~i - 1;
        i = Math.Clamp(i, 0, Count - 1);

        double into = t - starts[i];
        var from = frames[i];
        if (into <= holds[i] || fades[i] <= 0)
        {
            for (int cell = 0; cell < Frame.CellCount; cell++) target[cell] = from[cell];
            return;
        }
        var to = frames[(i + 1) % Count];
        double blend = (into - holds[i]) / fades[i];
        for (int cell = 0; cell < Frame.CellCount; cell++)
            target[cell] = from[cell] == to[cell] ? from[cell] : Rgb.Lerp(from[cell], to[cell], blend);
    }

    /// <summary>Draws one frame as painted, without its fade.</summary>
    public void RenderFrame(Frame target, int index)
    {
        if (index < 0 || index >= Count) return;
        for (int cell = 0; cell < Frame.CellCount; cell++) target[cell] = frames[index][cell];
    }

    /// <summary>Seconds frame <paramref name="index"/> takes to fade into the next.</summary>
    public double FadeOf(int index) => index >= 0 && index < Count ? fades[index] : 0;

    /// <summary>Draws the colours in <paramref name="from"/> turning into frame <paramref name="index"/>: 0 is all <paramref name="from"/>, 1 is the frame.</summary>
    public void RenderBlend(Frame target, ReadOnlySpan<Rgb> from, int index, double blend)
    {
        if (index < 0 || index >= Count) return;
        var to = frames[index];
        if (blend >= 1 || from.Length < Frame.CellCount)
        {
            for (int cell = 0; cell < Frame.CellCount; cell++) target[cell] = to[cell];
            return;
        }
        blend = Math.Max(blend, 0);
        for (int cell = 0; cell < Frame.CellCount; cell++)
            target[cell] = from[cell] == to[cell] ? to[cell] : Rgb.Lerp(from[cell], to[cell], blend);
    }
}

/// <summary>
/// Plays the animation built in the Animations tab. An animation can follow the music: step on each beat, play
/// faster when it is louder, or pulse with the bass (see <see cref="MusicReaction"/>).
/// </summary>
public sealed class AnimationEffect : IEffect
{
    const int Bands = 16;

    /// <summary>With <see cref="MusicReaction.Speed"/>: how fast it plays in silence and at full level, as a multiple of normal.</summary>
    public const double SlowestRate = 0.25, FastestRate = 2.5;

    /// <summary>With <see cref="MusicReaction.Pulse"/>: the brightness left when there is no bass.</summary>
    public const double DimmestPulse = 0.2;

    // How quickly the speed follows the level, so it surges with the music instead of twitching.
    const double LevelSeconds = 0.15;

    // Written by the UI thread, read by the engine thread.
    volatile CompiledAnimation? animation;
    volatile int pinned = -1;
    volatile bool restart = true;

    // Engine thread only.
    readonly BeatDetector beats = new();
    readonly Rgb[] shown = new Rgb[Frame.CellCount], from = new Rgb[Frame.CellCount];
    double position, lastTime, level, sinceStep, stepFade;
    int step;

    public string Id => "animation";
    public string Name => "Your animation";

    /// <summary>True while the animation it shows follows the music, so audio capture is needed.</summary>
    public bool UsesAudio => animation is { Music: not MusicReaction.None };

    /// <summary>What plays, or null for nothing (all keys off).</summary>
    public CompiledAnimation? Animation => animation;

    /// <summary>The frame shown still while it is being edited, or -1 to play.</summary>
    public int PinnedFrame => pinned;

    /// <summary>
    /// Shows <paramref name="next"/>. With <paramref name="pinnedFrame"/> it shows that frame still; otherwise it
    /// plays, from the first frame when <paramref name="fromStart"/> (a different animation, or Play pressed).
    /// </summary>
    public void Show(CompiledAnimation? next, int pinnedFrame = -1, bool fromStart = false)
    {
        animation = next;
        pinned = pinnedFrame;
        if (fromStart) restart = true;
    }

    public void Render(Frame frame, in EffectContext ctx)
    {
        var current = animation;
        if (current is null) return;
        if (restart)
        {
            restart = false;
            position = 0;
            step = 0;
            stepFade = 0;
            lastTime = ctx.Time;
            beats.Reset();
        }
        double elapsed = Math.Max(ctx.Time - lastTime, 0);   // already scaled by the speed setting
        lastTime = ctx.Time;

        int still = pinned;
        if (still >= 0) { current.RenderFrame(frame, still); return; }
        if (current.Count == 0) return;

        // Without audio capture (another system, or it could not start) it simply plays at its own times.
        var audio = ctx.Audio;
        switch (audio is null ? MusicReaction.None : current.Music)
        {
            case MusicReaction.Beat:
                audio!.GetBands(Bands);
                step %= current.Count;   // the editor may have swapped in a shorter animation
                if (beats.Update(audio.Bass, ctx.DeltaTime))
                {
                    // Fade from whatever is on the keys now, so a beat landing mid-fade never jumps.
                    stepFade = current.FadeOf(step);
                    shown.CopyTo(from, 0);
                    step = (step + 1) % current.Count;
                    sinceStep = 0;
                }
                else sinceStep += elapsed;
                current.RenderBlend(frame, from, step, stepFade <= 0 ? 1 : sinceStep / stepFade);
                for (int cell = 0; cell < Frame.CellCount; cell++) shown[cell] = frame[cell];
                break;

            case MusicReaction.Speed:
                audio!.GetBands(Bands);
                level += (audio.Level - level) * (1 - Math.Exp(-ctx.DeltaTime / LevelSeconds));
                position += elapsed * (SlowestRate + (FastestRate - SlowestRate) * Math.Clamp(level, 0, 1));
                current.Render(frame, position);
                break;

            case MusicReaction.Pulse:
                audio!.GetBands(Bands);
                position += elapsed;
                current.Render(frame, position);
                double brightness = DimmestPulse + (1 - DimmestPulse) * Math.Clamp(audio.Bass, 0, 1);
                for (int cell = 0; cell < Frame.CellCount; cell++)
                    if (frame[cell] != Rgb.Black) frame[cell] = frame[cell].Scale(brightness);
                break;

            default:
                position += elapsed;
                current.Render(frame, position);
                break;
        }
    }
}

/// <summary>
/// Moves a painted frame one key in a direction, using where keys really sit, so a column of keys can be slid across
/// the keyboard frame by frame. Keys that move off the edge are dropped; where two keys land on one, the brighter wins.
/// </summary>
public static class KeyShift
{
    public enum Direction { Left, Right, Up, Down }

    static readonly int[][] Targets = Enum.GetValues<Direction>().Select(BuildTargets).ToArray();

    public static Rgb[] Shift(ReadOnlySpan<Rgb> cells, Direction direction)
    {
        var targets = Targets[(int)direction];
        var moved = new Rgb[Frame.CellCount];
        for (int cell = 0; cell < Frame.CellCount && cell < cells.Length; cell++)
        {
            int to = targets[cell];
            if (to >= 0 && cells[cell] != Rgb.Black) moved[to] = EffectMath.Max(moved[to], cells[cell]);
        }
        return moved;
    }

    /// <summary>The key each cell moves to, or -1 if it falls off the keyboard.</summary>
    public static int Target(int cell, Direction direction) => cell is >= 0 and < Frame.CellCount ? Targets[(int)direction][cell] : -1;

    static int[] BuildTargets(Direction direction)
    {
        var (dx, dy) = direction switch
        {
            Direction.Left => (-1.0, 0.0),
            Direction.Right => (1.0, 0.0),
            Direction.Up => (0.0, -1.0),
            _ => (0.0, 1.0),
        };
        var targets = new int[Frame.CellCount];
        Array.Fill(targets, -1);
        foreach (var key in KeyMap.Keys)
        {
            if (PhysicalLayout.For(key.Label) is not { } from) continue;
            // One key pitch from the centre, but always past the key's own edge, so wide keys (Space) hand on to the next.
            double reach = Math.Max(PhysicalLayout.Pitch, (dx != 0 ? from.Width : from.Height) / 2 + PhysicalLayout.Pitch / 2);
            double x = from.CenterX + dx * reach, y = from.CenterY + dy * reach;

            int best = -1;
            double bestGap = PhysicalLayout.Pitch / 2, bestCentre = double.MaxValue;
            foreach (var other in KeyMap.Keys)
            {
                if (other.Cell == key.Cell || PhysicalLayout.For(other.Label) is not { } r) continue;
                double gapX = Math.Max(0, Math.Max(r.X - x, x - (r.X + r.Width)));
                double gapY = Math.Max(0, Math.Max(r.Y - y, y - (r.Y + r.Height)));
                double gap = Math.Sqrt(gapX * gapX + gapY * gapY);
                double centre = Math.Abs(r.CenterX - x) + Math.Abs(r.CenterY - y);
                if (gap < bestGap || (gap == bestGap && centre < bestCentre)) (best, bestGap, bestCentre) = (other.Cell, gap, centre);
            }
            targets[key.Cell] = best;
        }
        return targets;
    }
}
