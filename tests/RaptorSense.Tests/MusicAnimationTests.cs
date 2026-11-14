using RaptorSense.Audio;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Profiles;
using Xunit;

namespace RaptorSense.Tests;

public sealed class MusicAnimationTests
{
    const int SampleRate = 48000, Fps = 60;

    static readonly Rgb Red = new(255, 0, 0), Green = new(0, 255, 0), Blue = new(0, 0, 255);

    static int W => KeyMap.Find("W")!.Cell;

    /// <summary>Audio whose levels the test sets by hand.</summary>
    sealed class FakeAudio : IAudioSpectrum
    {
        public float Bass { get; set; }
        public float Level { get; set; }
        public int Analyses { get; private set; }

        public float[] GetBands(int count)
        {
            Analyses++;
            return new float[count];
        }

        public void Dispose() { }
    }

    static CompiledAnimation Three(MusicReaction music, double hold = 1, double fade = 0)
    {
        Rgb[] Cells(Rgb color)
        {
            var cells = new Rgb[Frame.CellCount];
            cells[W] = color;
            return cells;
        }
        return new CompiledAnimation("a", [(Cells(Red), hold, fade), (Cells(Green), hold, fade), (Cells(Blue), hold, fade)], music);
    }

    /// <summary>Draws frames at 60 a second and returns the W key's colour after each.</summary>
    sealed class Player(AnimationEffect effect, IAudioSpectrum? audio)
    {
        readonly EffectSettings settings = new();
        double time = 10;

        public Rgb Step(int frames = 1)
        {
            var frame = new Frame();
            for (int i = 0; i < frames; i++)
            {
                time += 1.0 / Fps;
                frame.Fill(Rgb.Black);
                effect.Render(frame, new EffectContext(time, time, 1.0 / Fps, settings, audio, []));
            }
            return frame[W];
        }
    }

    // ---- Beat detection ----

    /// <summary>Made-up music: a bass drum on every beat, a steady note above it and, if asked, a bass note that never stops.</summary>
    static float[] Music(double seconds, double beatsPerMinute, bool drone = false)
    {
        var samples = new float[(int)(seconds * SampleRate)];
        double beat = 60 / beatsPerMinute;
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)SampleRate;
            double sinceKick = t % beat;
            double kick = Math.Exp(-sinceKick / 0.06) * Math.Sin(2 * Math.PI * (55 + 60 * Math.Exp(-sinceKick / 0.03)) * sinceKick);
            double pad = 0.25 * Math.Sin(2 * Math.PI * 440 * t) + 0.15 * Math.Sin(2 * Math.PI * 1320 * t);
            double bass = drone ? 0.35 * Math.Sin(2 * Math.PI * 82 * t) : 0;
            samples[i] = (float)(0.8 * kick + pad + bass);
        }
        return samples;
    }

    /// <summary>Runs samples through the analyser and the detector the way the engine does, 60 times a second.</summary>
    static int CountBeats(float[] samples, double skipSeconds = 0)
    {
        var analyzer = new SpectrumAnalyzer(SampleRate);
        var detector = new BeatDetector();
        int perFrame = SampleRate / Fps, beats = 0;
        for (int at = 0; at + perFrame <= samples.Length; at += perFrame)
        {
            analyzer.Add(samples.AsSpan(at, perFrame));
            analyzer.GetBands(16);
            if (detector.Update(analyzer.Bass, 1.0 / Fps) && at >= skipSeconds * SampleRate) beats++;
        }
        return beats;
    }

    [Theory]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(128)]
    [InlineData(174)]
    public void A_bass_drum_gives_one_beat_per_hit(double beatsPerMinute)
    {
        const double seconds = 12, skip = 2;   // the first moments settle the automatic gain
        int expected = (int)Math.Round((seconds - skip) * beatsPerMinute / 60);
        Assert.InRange(CountBeats(Music(seconds, beatsPerMinute), skip), expected - 1, expected + 1);
    }

    [Fact]
    public void The_drum_is_still_found_over_a_bass_note_that_never_stops()
    {
        Assert.InRange(CountBeats(Music(12, 120, drone: true), skipSeconds: 2), 19, 21);
    }

    /// <summary>
    /// Made-up music closer to a real song: a bass drum on some beats of the bar, a snare on beats two and four,
    /// hi-hats in between and a held bass note that changes every bar.
    /// </summary>
    static float[] Band(double seconds, double beatsPerMinute, bool drumOnEveryBeat)
    {
        var samples = new float[(int)(seconds * SampleRate)];
        var random = new Random(7);
        double beat = 60 / beatsPerMinute;
        double[] bassNotes = [55, 65.4, 49, 73.4];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)SampleRate;
            int beatIndex = (int)(t / beat);
            double sinceBeat = t - beatIndex * beat, sinceHalf = t % (beat / 2);
            bool kickHere = drumOnEveryBeat || beatIndex % 2 == 0;
            double kick = kickHere ? Math.Exp(-sinceBeat / 0.06) * Math.Sin(2 * Math.PI * (55 + 60 * Math.Exp(-sinceBeat / 0.03)) * sinceBeat) : 0;
            double noise = random.NextDouble() * 2 - 1;
            double snare = beatIndex % 2 == 1 ? 0.5 * Math.Exp(-sinceBeat / 0.05) * (0.7 * noise + 0.3 * Math.Sin(2 * Math.PI * 190 * sinceBeat)) : 0;
            double hat = 0.15 * Math.Exp(-sinceHalf / 0.015) * noise;
            double bass = 0.3 * Math.Sin(2 * Math.PI * bassNotes[beatIndex / 4 % bassNotes.Length] * t);
            double chords = 0.2 * Math.Sin(2 * Math.PI * 220 * t) + 0.15 * Math.Sin(2 * Math.PI * 277 * t) + 0.15 * Math.Sin(2 * Math.PI * 330 * t);
            samples[i] = (float)(0.8 * kick + snare + hat + bass + chords);
        }
        return samples;
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(126, true)]
    [InlineData(100, false)]
    [InlineData(140, false)]
    public void In_a_fuller_mix_the_beats_follow_the_bass_drum_not_the_snare_or_hi_hats(double beatsPerMinute, bool drumOnEveryBeat)
    {
        const double seconds = 14, skip = 2;
        double drums = (seconds - skip) * beatsPerMinute / 60 / (drumOnEveryBeat ? 1 : 2);
        Assert.InRange(CountBeats(Band(seconds, beatsPerMinute, drumOnEveryBeat), skip), (int)(drums * 0.85), (int)Math.Ceiling(drums * 1.15));
    }

    [Fact]
    public void Silence_and_a_steady_note_give_no_beats()
    {
        Assert.Equal(0, CountBeats(new float[SampleRate * 5]));

        var steady = new float[SampleRate * 8];
        for (int i = 0; i < steady.Length; i++) steady[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 80 * i / SampleRate));
        Assert.Equal(0, CountBeats(steady, skipSeconds: 1));   // the note starting may count as one
    }

    [Fact]
    public void A_long_loud_stretch_is_one_beat_and_beats_cannot_come_faster_than_the_limit()
    {
        var detector = new BeatDetector();
        int beats = 0;
        for (int i = 0; i < Fps * 3; i++) if (detector.Update(0.9, 1.0 / Fps)) beats++;
        Assert.Equal(1, beats);

        detector.Reset();
        beats = 0;
        for (int i = 0; i < Fps * 2; i++) if (detector.Update(i % 2 == 0 ? 1 : 0, 1.0 / Fps)) beats++;   // 30 jumps a second
        Assert.InRange(beats, 1, (int)(2 / BeatDetector.MinGapSeconds) + 1);

        Assert.False(detector.Update(double.NaN, 1.0 / Fps));
        Assert.False(detector.Update(1, -1));
    }

    // ---- Playing ----

    [Fact]
    public void On_the_beat_each_beat_moves_one_frame_and_nothing_moves_in_between()
    {
        var effect = new AnimationEffect();
        var audio = new FakeAudio();
        var player = new Player(effect, audio);
        effect.Show(Three(MusicReaction.Beat, hold: 0.1), fromStart: true);

        Assert.Equal(Red, player.Step());
        Assert.Equal(Red, player.Step(Fps * 3));   // three seconds of quiet: its own times are not used

        Rgb Hit()
        {
            audio.Bass = 1;
            var color = player.Step();
            audio.Bass = 0;
            player.Step(Fps / 2);
            return color;
        }
        Assert.Equal(Green, Hit());
        Assert.Equal(Blue, Hit());
        Assert.Equal(Red, Hit());   // loops
        Assert.True(audio.Analyses > Fps);
    }

    [Fact]
    public void On_the_beat_a_fade_blends_from_what_the_keys_show()
    {
        var effect = new AnimationEffect();
        var audio = new FakeAudio();
        var player = new Player(effect, audio);
        effect.Show(Three(MusicReaction.Beat, fade: 0.5), fromStart: true);
        Assert.Equal(Red, player.Step(10));

        audio.Bass = 1;
        player.Step();
        audio.Bass = 0;
        var halfway = player.Step(Fps / 4);   // a quarter second into a half-second fade
        Assert.InRange(halfway.R, 100, 155);
        Assert.InRange(halfway.G, 100, 155);
        Assert.Equal(Green, player.Step(Fps / 2));
    }

    [Fact]
    public void Faster_when_louder_covers_more_of_the_loop_with_loud_music()
    {
        // Count frame changes over the same four seconds, quiet and loud.
        int Changes(float level)
        {
            var effect = new AnimationEffect();
            var player = new Player(effect, new FakeAudio { Level = level });
            effect.Show(Three(MusicReaction.Speed, hold: 0.5), fromStart: true);
            int changes = 0;
            var last = player.Step();
            for (int i = 0; i < Fps * 4; i++)
            {
                var now = player.Step();
                if (now != last) changes++;
                last = now;
            }
            return changes;
        }

        int quiet = Changes(0), loud = Changes(1);
        Assert.InRange(quiet, 1, 3);     // 4 s at a quarter speed = 1 s of animation = 2 changes
        Assert.InRange(loud, 17, 20);    // 4 s at 2.5 times = 10 s = about 19 changes
    }

    [Fact]
    public void Pulse_keeps_its_own_times_and_dims_without_bass()
    {
        var effect = new AnimationEffect();
        var audio = new FakeAudio { Bass = 1 };
        var player = new Player(effect, audio);
        effect.Show(Three(MusicReaction.Pulse), fromStart: true);

        Assert.Equal(Red, player.Step(Fps / 2));
        audio.Bass = 0;
        var dim = player.Step();
        Assert.InRange(dim.R, 45, 56);   // a fifth of 255
        Assert.Equal(0, dim.G);
        audio.Bass = 1;
        Assert.Equal(Green, player.Step(Fps));   // one second on: the second frame, at its own time
    }

    [Fact]
    public void Without_audio_a_music_animation_plays_at_its_own_times()
    {
        foreach (var music in Enum.GetValues<MusicReaction>())
        {
            var effect = new AnimationEffect();
            var player = new Player(effect, audio: null);
            effect.Show(Three(music), fromStart: true);
            Assert.Equal(Red, player.Step(Fps / 2));
            Assert.Equal(Green, player.Step(Fps));
            Assert.Equal(Blue, player.Step(Fps));
        }
    }

    [Fact]
    public void A_frame_being_edited_stays_still_and_asks_for_no_audio()
    {
        var effect = new AnimationEffect();
        var audio = new FakeAudio { Bass = 1, Level = 1 };
        var player = new Player(effect, audio);
        effect.Show(Three(MusicReaction.Beat), pinnedFrame: 2);
        Assert.Equal(Blue, player.Step(Fps));
        Assert.Equal(0, audio.Analyses);
    }

    [Fact]
    public void A_shorter_animation_swapped_in_mid_play_keeps_playing()
    {
        var effect = new AnimationEffect();
        var audio = new FakeAudio();
        var player = new Player(effect, audio);
        effect.Show(Three(MusicReaction.Beat), fromStart: true);
        for (int i = 0; i < 2; i++) { audio.Bass = 1; player.Step(); audio.Bass = 0; player.Step(Fps / 2); }   // now on the third frame

        var cells = new Rgb[Frame.CellCount];
        cells[W] = Red;
        effect.Show(new CompiledAnimation("a", [(cells, 1, 0)], MusicReaction.Beat));
        Assert.Equal(Red, player.Step(5));
    }

    [Fact]
    public void The_effect_needs_audio_only_while_its_animation_follows_the_music()
    {
        var effect = new AnimationEffect();
        Assert.False(effect.UsesAudio);
        effect.Show(Three(MusicReaction.None));
        Assert.False(effect.UsesAudio);
        effect.Show(Three(MusicReaction.Pulse));
        Assert.True(effect.UsesAudio);
        effect.Show(null);
        Assert.False(effect.UsesAudio);
    }

    // ---- Files ----

    [Theory]
    [InlineData("beat", MusicReaction.Beat)]
    [InlineData(" Speed ", MusicReaction.Speed)]
    [InlineData("PULSE", MusicReaction.Pulse)]
    [InlineData("disco", MusicReaction.None)]
    [InlineData("", MusicReaction.None)]
    [InlineData(null, MusicReaction.None)]
    public void The_word_in_a_file_is_read_leniently(string? text, MusicReaction expected)
    {
        Assert.Equal(expected, MusicReactions.Parse(text));
        Assert.Equal(expected, new KeyAnimation { Name = "a", Frames = [new AnimationFrame()], Music = text }.Compile().Music);
    }

    [Fact]
    public void The_reaction_is_saved_with_the_animation_and_left_out_when_there_is_none()
    {
        var folder = Path.Combine(Path.GetTempPath(), "raptorsense-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var frames = new[] { new AnimationFrame { Keys = new() { ["W"] = "FF0000" } } };
            AnimationStore.Save(new KeyAnimation { Name = "Thump", Frames = frames, Music = MusicReactions.ToText(MusicReaction.Beat) }, folder);
            AnimationStore.Save(new KeyAnimation { Name = "Plain", Frames = frames }, folder);

            Assert.Equal("beat", AnimationStore.Load("Thump", folder)!.Music);
            Assert.Equal(MusicReaction.Beat, AnimationStore.Load("Thump", folder)!.Compile().Music);
            Assert.Null(AnimationStore.Load("Plain", folder)!.Music);
            Assert.DoesNotContain("Music", File.ReadAllText(Path.Combine(folder, "Plain.json")));

            // A shared file keeps a known word and drops anything else.
            var shared = Path.Combine(folder, "shared");
            Directory.CreateDirectory(shared);
            var odd = Path.Combine(shared, "odd.json");
            File.WriteAllText(odd, """{ "Name": "Odd", "Music": "run a program", "Frames": [ { "Keys": { "W": "FF0000" } } ] }""");
            Assert.Null(AnimationStore.Import(odd, folder).Music);
            var good = Path.Combine(shared, "good.json");
            AnimationStore.Export(new KeyAnimation { Name = "Good", Frames = frames, Music = "Pulse" }, good);
            Assert.Equal("pulse", AnimationStore.Import(good, folder).Music);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
