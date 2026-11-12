using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Profiles;
using Xunit;

namespace RaptorSense.Tests;

/// <summary>Starter animations, and animation files going out to and coming in from other people.</summary>
public sealed class AnimationSharingTests : IDisposable
{
    static readonly Rgb Red = new(255, 0, 0);

    readonly string folder = Path.Combine(Path.GetTempPath(), "raptorsense-tests", Guid.NewGuid().ToString("N"));
    readonly string store, outside;

    public AnimationSharingTests()
    {
        store = Path.Combine(folder, "store");
        outside = Path.Combine(folder, "outside");
        Directory.CreateDirectory(outside);
    }

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    string Shared(string fileName, string json)
    {
        var path = Path.Combine(outside, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    // ---- Starters ----

    public static TheoryData<string> Starters()
    {
        var data = new TheoryData<string>();
        foreach (var name in AnimationStarters.Names) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Starters))]
    public void Every_starter_is_a_playable_animation_that_lights_keys(string name)
    {
        var animation = AnimationStarters.Create(name, Red)!;

        Assert.Equal(name, animation.Name);
        Assert.InRange(animation.Frames.Count, 2, KeyAnimation.MaxFrames);
        Assert.All(animation.Frames, f =>
        {
            Assert.InRange(f.Hold, 0, AnimationFrame.MaxHold);
            Assert.InRange(f.Fade, 0, AnimationFrame.MaxFade);
        });
        Assert.Contains(animation.Frames, f => f.Keys.Count > 0);

        var compiled = animation.Compile();
        Assert.InRange(compiled.Length, 0.5, 20);
        var frame = new Frame();
        bool lit = false;
        for (double t = 0; t < compiled.Length; t += 1 / 60.0)
        {
            compiled.Render(frame, t);
            foreach (int cell in KeyGeometry.Cells) lit |= frame[cell] != Rgb.Black;
        }
        Assert.True(lit);
    }

    [Fact]
    public void Starters_use_the_colour_given_except_the_rainbow()
    {
        var sweep = AnimationStarters.Create("Sweep", Red)!;
        Assert.All(sweep.Frames.SelectMany(f => f.Keys.Values), hex => Assert.EndsWith("0000", hex));   // only red in it

        var rainbow = AnimationStarters.Create("Rainbow drift", Red)!;
        Assert.True(rainbow.Frames[0].Keys.Values.Distinct().Count() > 10);
    }

    [Fact]
    public void An_unknown_starter_is_null()
    {
        Assert.Null(AnimationStarters.Create("No such starter", Red));
    }

    // ---- Export and import ----

    [Fact]
    public void An_exported_animation_imports_back_the_same()
    {
        var original = AnimationStarters.Create("Ripple", Red)! with { Name = "My ripple" };
        var file = Path.Combine(outside, "ripple.json");
        AnimationStore.Export(original, file);

        var imported = AnimationStore.Import(file, store);

        Assert.Equal("My ripple", imported.Name);
        Assert.Equal(["My ripple"], AnimationStore.List(store));
        var loaded = AnimationStore.Load("My ripple", store)!;
        Assert.Equal(original.Frames.Count, loaded.Frames.Count);
        for (int i = 0; i < original.Frames.Count; i++)
        {
            Assert.Equal(original.Frames[i].Hold, loaded.Frames[i].Hold);
            Assert.Equal(original.Frames[i].Fade, loaded.Frames[i].Fade);
            Assert.Equal(original.Frames[i].ToCells(), loaded.Frames[i].ToCells());
        }
    }

    [Fact]
    public void Importing_never_replaces_an_animation_that_is_already_there()
    {
        AnimationStore.Save(new KeyAnimation { Name = "Wave", Frames = [new AnimationFrame { Keys = new() { ["W"] = "FF0000" } }] }, store);
        var file = Shared("anything.json", """{ "Name": "Wave", "Frames": [ { "Hold": 1, "Keys": { "A": "0000FF" } } ] }""");

        var first = AnimationStore.Import(file, store);
        var second = AnimationStore.Import(file, store);

        Assert.Equal("Wave 2", first.Name);
        Assert.Equal("Wave 3", second.Name);
        Assert.Equal(["Wave", "Wave 2", "Wave 3"], AnimationStore.List(store));
        Assert.Equal("FF0000", AnimationStore.Load("Wave", store)!.Frames[0].Keys["W"]);   // the original is untouched
    }

    [Fact]
    public void An_import_keeps_only_known_keys_valid_colours_and_times_in_range()
    {
        var file = Shared("odd.json", """
            {
              "Name": "  Odd one  ",
              "Script": "calc.exe",
              "Frames": [
                { "Hold": -3, "Fade": 1e12, "Keys": { "W": "ff0000", "NoSuchKey": "00FF00", "A": "red" }, "Run": "calc.exe" },
                null,
                { "Hold": 0.2 }
              ]
            }
            """);

        var imported = AnimationStore.Import(file, store);

        Assert.Equal("Odd one", imported.Name);
        Assert.Equal(2, imported.Frames.Count);
        Assert.Equal(0, imported.Frames[0].Hold);
        Assert.Equal(AnimationFrame.MaxFade, imported.Frames[0].Fade);
        Assert.Equal(new Dictionary<string, string> { ["W"] = "FF0000" }, imported.Frames[0].Keys);
        var saved = File.ReadAllText(Directory.GetFiles(store).Single());
        Assert.DoesNotContain("calc", saved);
        Assert.DoesNotContain("NoSuchKey", saved);
    }

    [Fact]
    public void A_file_without_a_name_takes_its_file_name_and_long_names_are_cut()
    {
        var unnamed = AnimationStore.Import(Shared("Cool sweep.json", """{ "Frames": [ { "Keys": { "W": "FF0000" } } ] }"""), store);
        Assert.Equal("Cool sweep", unnamed.Name);

        var longName = new string('x', 300);
        var cut = AnimationStore.Import(Shared("long.json", $$"""{ "Name": "{{longName}}", "Frames": [ { } ] }"""), store);
        Assert.Equal(60, cut.Name.Length);
    }

    [Fact]
    public void An_import_is_cut_to_the_frame_limit()
    {
        var frames = string.Join(",", Enumerable.Repeat("""{ "Hold": 0.1, "Keys": { "W": "FF0000" } }""", KeyAnimation.MaxFrames + 40));
        var imported = AnimationStore.Import(Shared("many.json", $$"""{ "Name": "Many", "Frames": [ {{frames}} ] }"""), store);
        Assert.Equal(KeyAnimation.MaxFrames, imported.Frames.Count);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("""{ "Name": "A profile", "Effect": "fire", "Keys": { "W": "FF0000" } }""")]
    [InlineData("""{ "Name": "Empty", "Frames": [] }""")]
    [InlineData("""{ "Name": "Nulls", "Frames": [ null, null ] }""")]
    [InlineData("""{ "Name": "Bad", "Frames": [ { "Hold": "soon" } ] }""")]
    public void Files_that_are_not_animations_are_refused_and_nothing_is_saved(string json)
    {
        var e = Assert.Throws<InvalidDataException>(() => AnimationStore.Import(Shared("bad.json", json), store));
        Assert.Contains("isn't a RaptorSense animation", e.Message);
        Assert.Empty(AnimationStore.List(store));
    }

    [Fact]
    public void A_huge_or_missing_file_is_refused()
    {
        var huge = Path.Combine(outside, "huge.json");
        using (var stream = File.Create(huge)) stream.SetLength(AnimationStore.MaxImportBytes + 1);
        Assert.Contains("too big", Assert.Throws<InvalidDataException>(() => AnimationStore.Import(huge, store)).Message);

        Assert.Throws<InvalidDataException>(() => AnimationStore.Import(Path.Combine(outside, "gone.json"), store));
        Assert.Empty(AnimationStore.List(store));
    }

    [Fact]
    public void Unique_names_count_up_from_two()
    {
        Assert.Equal("Animation", AnimationStore.UniqueName("Animation", store));
        AnimationStore.Save(new KeyAnimation { Name = "Animation", Frames = [new AnimationFrame()] }, store);
        Assert.Equal("Animation 2", AnimationStore.UniqueName(" Animation ", store));
        AnimationStore.Save(new KeyAnimation { Name = "Animation 2", Frames = [new AnimationFrame()] }, store);
        Assert.Equal("Animation 3", AnimationStore.UniqueName("Animation", store));
    }
}
