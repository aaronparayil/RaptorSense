using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Profiles;
using Xunit;

namespace RaptorSense.Tests;

public sealed class AnimationTests
{
    static readonly Rgb Red = new(255, 0, 0), Blue = new(0, 0, 255);

    static int Cell(string label) => KeyMap.Find(label)!.Cell;

    static AnimationFrame Paint(string label, Rgb color, double hold = 1, double fade = 0) =>
        new() { Hold = hold, Fade = fade, Keys = new() { [label] = color.ToString() } };

    static Rgb At(CompiledAnimation animation, double time, string label)
    {
        var frame = new Frame();
        animation.Render(frame, time);
        return frame[Cell(label)];
    }

    [Fact]
    public void Frames_hold_then_jump_and_loop()
    {
        var animation = new KeyAnimation { Name = "a", Frames = [Paint("W", Red), Paint("W", Blue)] }.Compile();

        Assert.Equal(2, animation.Length, 6);
        Assert.Equal(Red, At(animation, 0.5, "W"));
        Assert.Equal(Blue, At(animation, 1.5, "W"));
        Assert.Equal(Red, At(animation, 2.5, "W"));
        Assert.Equal(Rgb.Black, At(animation, 0.5, "A"));
    }

    [Fact]
    public void A_fade_blends_into_the_next_frame_and_the_last_fades_into_the_first()
    {
        var animation = new KeyAnimation { Name = "a", Frames = [Paint("W", Red, hold: 1, fade: 1), Paint("W", Blue, hold: 1, fade: 1)] }.Compile();

        Assert.Equal(Red, At(animation, 0.9, "W"));
        var halfway = At(animation, 1.5, "W");
        Assert.InRange(halfway.R, 120, 135);
        Assert.InRange(halfway.B, 120, 135);
        Assert.Equal(Blue, At(animation, 2.5, "W"));
        var wrapping = At(animation, 3.5, "W");   // blue fading back to red
        Assert.InRange(wrapping.R, 120, 135);
        Assert.Equal(Red, At(animation, 4.2, "W"));
    }

    [Fact]
    public void Zero_negative_and_broken_times_still_give_a_playable_animation()
    {
        var animation = new KeyAnimation
        {
            Name = "a",
            Frames =
            [
                new AnimationFrame { Hold = 0, Fade = 0 },
                new AnimationFrame { Hold = -5, Fade = double.NaN },
                new AnimationFrame { Hold = double.PositiveInfinity, Fade = 1e9 },
            ],
        }.Compile();

        Assert.True(double.IsFinite(animation.Length));
        Assert.Equal(2 / 60.0 + AnimationFrame.MaxFade, animation.Length, 6);   // two shortest frames, then a 0 s hold and the longest fade
        for (double t = -3; t < 40; t += 0.37) animation.Render(new Frame(), t);
    }

    [Fact]
    public void An_empty_or_null_animation_draws_nothing()
    {
        var empty = new KeyAnimation { Name = "a", Frames = null! }.Compile();
        var frame = new Frame();
        frame.Fill(Red);
        empty.Render(frame, 1);
        Assert.Equal(0, empty.Count);
        Assert.Equal(Red, frame[0]);   // left as it was: the engine clears frames before drawing

        var effect = new AnimationEffect();
        var target = new Frame();
        effect.Render(target, new EffectContext(1, 1, 1 / 60.0, new EffectSettings(), null, []));
        Assert.All(KeyGeometry.Cells, c => Assert.Equal(Rgb.Black, target[c]));
    }

    [Fact]
    public void Unknown_keys_and_bad_colours_are_skipped()
    {
        var frame = new AnimationFrame { Keys = new() { ["W"] = "FF0000", ["NoSuchKey"] = "00FF00", ["A"] = "not a colour" } };
        var cells = frame.ToCells();
        Assert.Equal(Red, cells[Cell("W")]);
        Assert.Equal(Rgb.Black, cells[Cell("A")]);
        Assert.Equal(Frame.CellCount, cells.Length);
    }

    [Fact]
    public void Frames_store_only_lit_keys_and_round_trip()
    {
        var cells = new Rgb[Frame.CellCount];
        cells[Cell("Space")] = Blue;
        cells[Cell("Esc")] = Red;
        var frame = AnimationFrame.FromCells(cells, hold: 0.25, fade: 0.1);

        Assert.Equal(2, frame.Keys.Count);
        Assert.Equal("0000FF", frame.Keys["Space"]);
        Assert.Equal(cells, frame.ToCells());
    }

    [Fact]
    public void The_store_saves_lists_loads_and_deletes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "raptorsense-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var animation = new KeyAnimation { Name = "Wave: left/right", Frames = [Paint("W", Red, 0.3, 0.2), Paint("A", Blue)] };
            AnimationStore.Save(animation, folder);

            Assert.Equal(["Wave: left/right"], AnimationStore.List(folder));
            var loaded = AnimationStore.Load("Wave: left/right", folder)!;
            Assert.Equal(2, loaded.Frames.Count);
            Assert.Equal(0.3, loaded.Frames[0].Hold);
            Assert.Equal(0.2, loaded.Frames[0].Fade);
            Assert.Equal("0000FF", loaded.Frames[1].Keys["A"]);
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));

            AnimationStore.Delete("Wave: left/right", folder);
            Assert.Empty(AnimationStore.List(folder));
            Assert.Null(AnimationStore.Load("Wave: left/right", folder));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_broken_file_loads_as_nothing()
    {
        var folder = Path.Combine(Path.GetTempPath(), "raptorsense-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "bad.json"), "{ \"Name\": \"bad\", \"Frames\": [ { \"Hold\": \"soon\" } ] }");
            Assert.Null(AnimationStore.Load("bad", folder));
            Assert.Empty(AnimationStore.List(folder));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("Q", KeyShift.Direction.Right, "W")]
    [InlineData("W", KeyShift.Direction.Left, "Q")]
    [InlineData("Alt", KeyShift.Direction.Right, "Space")]
    [InlineData("Space", KeyShift.Direction.Right, "AltGr")]
    [InlineData("Space", KeyShift.Direction.Left, "Alt")]
    [InlineData("1", KeyShift.Direction.Up, "F1")]
    [InlineData("Q", KeyShift.Direction.Down, "A")]
    [InlineData("RShift", KeyShift.Direction.Right, "Up")]
    public void Shifting_moves_a_key_to_its_real_neighbour(string from, KeyShift.Direction direction, string to)
    {
        Assert.Equal(Cell(to), KeyShift.Target(Cell(from), direction));
    }

    [Theory]
    [InlineData("Esc", KeyShift.Direction.Left)]
    [InlineData("F5", KeyShift.Direction.Up)]
    [InlineData("Space", KeyShift.Direction.Down)]
    [InlineData("Num+", KeyShift.Direction.Right)]
    public void Keys_at_the_edge_fall_off(string from, KeyShift.Direction direction)
    {
        Assert.Equal(-1, KeyShift.Target(Cell(from), direction));
        var cells = new Rgb[Frame.CellCount];
        cells[Cell(from)] = Red;
        Assert.All(KeyShift.Shift(cells, direction), c => Assert.Equal(Rgb.Black, c));
    }

    [Fact]
    public void Every_key_moves_somewhere_or_off_and_never_onto_itself()
    {
        foreach (var direction in Enum.GetValues<KeyShift.Direction>())
            foreach (var key in KeyMap.Keys)
            {
                int target = KeyShift.Target(key.Cell, direction);
                Assert.NotEqual(key.Cell, target);
                if (target >= 0) Assert.Contains(target, KeyMap.Keys.Select(k => k.Cell));
            }
    }

    [Fact]
    public void Where_two_keys_land_on_one_the_brighter_wins()
    {
        // Rows don't line up (wide keys, the half-height F row), so some keys share a neighbour: find such a pair.
        var (direction, pair) = Enum.GetValues<KeyShift.Direction>()
            .SelectMany(d => KeyMap.Keys.GroupBy(k => KeyShift.Target(k.Cell, d)).Where(g => g.Key >= 0 && g.Count() > 1).Select(g => (d, g.ToList())))
            .First();
        int target = KeyShift.Target(pair[0].Cell, direction);
        var cells = new Rgb[Frame.CellCount];
        cells[pair[0].Cell] = new Rgb(40, 0, 0);
        cells[pair[1].Cell] = Red;
        Assert.Equal(Red, KeyShift.Shift(cells, direction)[target]);
    }

    [Fact]
    public void The_effect_shows_a_pinned_frame_and_plays_from_the_start_when_asked()
    {
        var animation = new KeyAnimation { Name = "a", Frames = [Paint("W", Red), Paint("W", Blue)] }.Compile();
        var effect = new AnimationEffect();
        var settings = new EffectSettings();
        Rgb Draw(double time)
        {
            var frame = new Frame();
            effect.Render(frame, new EffectContext(time, time, 1 / 60.0, settings, null, []));
            return frame[Cell("W")];
        }

        effect.Show(animation, pinnedFrame: 1);
        Assert.Equal(Blue, Draw(100.2));
        Assert.Equal(Blue, Draw(101.2));

        effect.Show(animation, fromStart: true);
        Assert.Equal(Red, Draw(50.0));   // starts at frame 1 whatever the engine's clock says
        Assert.Equal(Red, Draw(50.9));
        Assert.Equal(Blue, Draw(51.5));
    }

    [Fact]
    public void A_profile_whose_animation_is_missing_shows_the_painted_keys()
    {
        using var engine = new EffectEngine(() => null);
        new Profile { Effect = "animation", Animation = "missing-" + Guid.NewGuid().ToString("N") }.ApplyTo(engine);
        Assert.Same(Effects.Painted, engine.Effect);
    }
}
