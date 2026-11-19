using System.Diagnostics;
using RaptorSense.Audio;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using Xunit;

namespace RaptorSense.Tests;

/// <summary>The hundred effects of the collections (Ambient, Patterns, Space and the rest), and every effect's safety.</summary>
public sealed class CollectionTests
{
    static readonly string[] NewCategories =
    [
        EffectCategory.Ambient, EffectCategory.Patterns, EffectCategory.Space, EffectCategory.Retro,
        EffectCategory.SciFi, EffectCategory.Celebrations, EffectCategory.Handy,
    ];

    public static TheoryData<string> Software()
    {
        var data = new TheoryData<string>();
        foreach (var e in Effects.All.Where(e => e is not (KeyboardEffect or PaintedEffect or AnimationEffect))) data.Add(e.Id);
        return data;
    }

    [Fact]
    public void There_are_a_hundred_of_them_all_on_the_helios_list()
    {
        Assert.Equal(100, Effects.Collections.Count);
        Assert.All(Effects.Collections, e => Assert.Contains(e, Effects.All));
        Assert.Empty(Effects.Collections.Intersect(Effects.Nitro));
    }

    [Fact]
    public void Software_effect_names_are_unique_on_the_helios_list()
    {
        // The keyboard's own effects share names such as "Breathing" with the software ones; they have their own section.
        var names = Effects.All.Where(e => e is not KeyboardEffect).Select(e => e.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_new_category_has_at_least_five_effects_and_a_description()
    {
        foreach (var category in NewCategories)
        {
            Assert.True(Effects.All.Count(e => e.Category == category) >= 5, category);
            Assert.NotEmpty(EffectCategory.Describe(category));
        }
        Assert.All(EffectCategory.Order, c => Assert.NotEmpty(EffectCategory.Describe(c)));
    }

    [Theory]
    [MemberData(nameof(Software))]
    public void Each_lights_keys_with_the_demo_music_and_some_typing(string id)
    {
        var effect = Effects.CreateCopy(Effects.Find(id)!)!;
        var audio = new DemoAudio();
        var frame = new Frame();
        bool lit = false;
        Run(effect, audio, 1.0, 600, frame, _ => lit |= KeyGeometry.Cells.Any(c => frame[c] != Rgb.Black));
        Assert.True(lit, $"{id} never lit a key");
    }

    /// <summary>
    /// The general flash rule used for screens (WCAG 2.3.1): no more than three flashes in any second, a flash being
    /// a rise and a fall of the keyboard's average brightness by a tenth or more. Checked at normal speed and at
    /// twice it, with the demo music and steady typing, over twenty seconds.
    /// </summary>
    [Theory]
    [MemberData(nameof(Software))]
    public void None_flashes_more_than_three_times_a_second(string id)
    {
        foreach (double speed in new[] { 1.0, 2.0 })
        {
            var effect = Effects.CreateCopy(Effects.Find(id)!)!;
            var levels = new List<double>();
            var frame = new Frame();
            Run(effect, new DemoAudio(), speed, 1200, frame, _ => levels.Add(KeyGeometry.Cells.Average(c => Luminance(frame[c]))));

            // Turning points that differ from the one before by at least 0.1.
            var turns = new List<int>();
            double extreme = levels[0];
            int direction = 0;
            for (int i = 1; i < levels.Count; i++)
            {
                double change = levels[i] - extreme;
                if (direction >= 0 && change <= -0.1 || direction <= 0 && change >= 0.1)
                {
                    turns.Add(i);
                    direction = Math.Sign(change);
                    extreme = levels[i];
                }
                else if (direction > 0 && levels[i] > extreme || direction < 0 && levels[i] < extreme) extreme = levels[i];
            }
            int worst = 0;
            for (int i = 0; i < turns.Count; i++) worst = Math.Max(worst, turns.Count(t => t >= turns[i] && t < turns[i] + 60));
            Assert.True(worst <= 6, $"{id} at speed {speed}: {worst} brightness swings within one second (at most 6 allowed, 3 flashes)");
        }
    }

    [Theory]
    [MemberData(nameof(Software))]
    public void Each_takes_under_two_milliseconds_a_frame(string id)
    {
        var effect = Effects.CreateCopy(Effects.Find(id)!)!;
        var frame = new Frame();
        Run(effect, new DemoAudio(), 1, 60, frame, _ => { });   // warm up
        var clock = Stopwatch.StartNew();
        Run(effect, new DemoAudio(), 1, 300, frame, _ => { });
        double perFrame = clock.Elapsed.TotalMilliseconds / 300;
        Assert.True(perFrame < 2, $"{id} took {perFrame:N2} ms a frame");
    }

    [Fact]
    public void Copies_are_new_objects_of_the_same_effect()
    {
        foreach (var effect in Effects.All.Concat(Effects.Nitro))
        {
            var copy = Effects.CreateCopy(effect);
            if (effect is PaintedEffect or AnimationEffect) { Assert.Null(copy); continue; }
            Assert.NotNull(copy);
            Assert.NotSame(effect, copy);
            if (effect is not KeyboardEffect) Assert.Equal(effect.Id, copy!.Id);
        }
    }

    [Fact]
    public void The_clock_shows_twenty_five_past_three_in_the_afternoon()
    {
        var clock = new ClockEffect { Now = () => new DateTime(2026, 11, 17, 15, 25, 0) };
        var frame = new Frame();
        clock.Render(frame, Context(1, 1));
        bool Bright(string label) => Luminance(frame[KeyMap.Find(label)!.Cell]) > 0.05;
        Assert.True(Bright("F1") && Bright("F2") && Bright("F3"));
        Assert.False(Bright("F4"));
        Assert.True(Bright("5"));
        Assert.False(Bright("6"));
        Assert.True(Bright("Esc"));   // afternoon
    }

    [Fact]
    public void The_focus_timer_fills_over_twenty_five_minutes_then_turns_green_for_the_break()
    {
        var timer = new FocusTimerEffect();
        var frame = new Frame();
        int first = KeyMap.Find("`")!.Cell, middle = KeyMap.Find("6")!.Cell, last = KeyMap.Find("=")!.Cell;
        timer.Render(frame, Context(0, 100));
        Assert.True(Luminance(frame[first]) < 0.05);
        timer.Render(frame, Context(0, 100 + 12.5 * 60));
        Assert.True(Luminance(frame[first]) > Luminance(frame[last]));
        Assert.True(Luminance(frame[middle]) > 0.05);
        timer.Render(frame, Context(0, 100 + 26 * 60));
        var rest = frame[first];
        Assert.True(rest.G > rest.R && rest.G > rest.B, $"break colour {rest}");
    }

    [Fact]
    public void Demo_music_beats_twice_a_second()
    {
        var audio = new DemoAudio();
        var beats = new BeatDetector();
        int count = 0;
        for (int i = 0; i < 600; i++)
        {
            audio.Time = i / 60.0;
            audio.GetBands(16);
            if (beats.Update(audio.Bass, 1 / 60.0)) count++;
        }
        Assert.InRange(count, 17, 21);   // 10 s at 120 a minute
    }

    static EffectContext Context(double time, double realTime) =>
        new(time, realTime, 1 / 60.0, new EffectSettings(), null, []);

    /// <summary>Plays an effect at 60 fps with the demo music and a key press six times a second.</summary>
    static void Run(IEffect effect, DemoAudio audio, double speed, int frames, Frame frame, Action<int> after)
    {
        var settings = new EffectSettings { Speed = speed };
        double time = 0;
        for (int i = 0; i < frames; i++)
        {
            double delta = 1 / 60.0;
            time += delta * speed;
            audio.Time = i * delta;
            IReadOnlyList<int> presses = i % 10 == 0 ? [KeyGeometry.Cells[(i * 7) % KeyGeometry.Cells.Count]] : [];
            frame.Fill(Rgb.Black);
            effect.Render(frame, new EffectContext(time, i * delta, delta, settings, audio, presses));
            after(i);
        }
    }

    /// <summary>Relative luminance 0..1 of a key's colour, as the screen flash rules define it.</summary>
    static double Luminance(Rgb c)
    {
        static double Linear(byte v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }
}
