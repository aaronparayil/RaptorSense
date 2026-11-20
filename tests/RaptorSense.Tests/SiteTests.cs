using System.Text.Json;
using System.Text.Json.Nodes;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Profiles;
using Xunit;

namespace RaptorSense.Tests;

/// <summary>
/// The website in site/: its drawing of the keyboard is the app's, and every shared animation on it is one the app
/// takes exactly as it is, lists correctly and doesn't flash.
/// </summary>
/// <remarks>
/// After a change to the key layout, set RAPTORSENSE_WRITE_SITE=1 and run these tests to rewrite site/data/keyboard.json.
/// </remarks>
public sealed class SiteTests
{
    static readonly Lazy<string> site = new(() =>
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RaptorSense.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        return Path.Combine(root.FullName, "site");
    });

    static string Site => site.Value;

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>What site/data/keyboard.json should hold: every lit key's label and where it sits.</summary>
    static string KeyboardJson()
    {
        var keys = new JsonArray();
        foreach (var key in KeyMap.Keys)
        {
            if (PhysicalLayout.For(key.Label) is not { } r) continue;
            keys.Add(new JsonObject
            {
                ["label"] = key.Label,
                ["x"] = Math.Round(r.X, 1), ["y"] = Math.Round(r.Y, 1), ["w"] = Math.Round(r.Width, 1), ["h"] = Math.Round(r.Height, 1),
            });
        }
        var layout = new JsonObject
        {
            ["width"] = Math.Round(PhysicalLayout.Width, 1),
            ["height"] = Math.Round(PhysicalLayout.Height, 1),
            ["keys"] = keys,
        };
        return layout.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";
    }

    [Fact]
    public void The_sites_keyboard_is_the_apps()
    {
        var path = Path.Combine(Site, "data", "keyboard.json");
        var expected = KeyboardJson();
        if (Environment.GetEnvironmentVariable("RAPTORSENSE_WRITE_SITE") == "1") File.WriteAllText(path, expected);
        Assert.Equal(expected, File.ReadAllText(path).ReplaceLineEndings("\n"));
    }

    static List<string> FileNames() =>
        Directory.EnumerateFiles(Path.Combine(Site, "animations"), "*.json")
            .Select(f => Path.GetFileName(f)!)
            .Where(name => name != "index.json")
            .Order(StringComparer.Ordinal)
            .ToList();

    public static TheoryData<string> AnimationFiles() => new(FileNames());

    static KeyAnimation Read(string file) =>
        JsonSerializer.Deserialize<KeyAnimation>(File.ReadAllText(Path.Combine(Site, "animations", file)))!;

    [Theory]
    [MemberData(nameof(AnimationFiles))]
    public void Each_animation_is_one_the_app_takes_as_it_is(string file)
    {
        var path = Path.Combine(Site, "animations", file);
        Assert.True(new FileInfo(path).Length <= AnimationStore.MaxImportBytes, $"{file} is bigger than the app imports");
        var animation = Read(file);
        Assert.False(string.IsNullOrWhiteSpace(animation.Name), $"{file} has no name");
        Assert.InRange(animation.Frames.Count, 1, KeyAnimation.MaxFrames);

        // Cleaning drops unknown keys, black keys and bad colours and clamps times; a site file has none of those.
        var cleaned = animation.Cleaned();
        Assert.Equal(JsonSerializer.Serialize(cleaned), JsonSerializer.Serialize(animation with { Name = cleaned.Name }));

        // And the app can import it.
        var folder = Directory.CreateTempSubdirectory("raptorsense-site-").FullName;
        try { Assert.Equal(animation.Frames.Count, AnimationStore.Import(path, folder).Frames.Count); }
        finally { Directory.Delete(folder, recursive: true); }
    }

    /// <summary>
    /// The same rule the built-in effects follow (WCAG 2.3.1, see <see cref="CollectionTests"/>): no more than three
    /// flashes in any second, a flash being a rise and a fall of the keyboard's average brightness by a tenth or more.
    /// scripts/site/add_animation.py checks the same before anything is added.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnimationFiles))]
    public void No_animation_flashes_more_than_three_times_a_second(string file)
    {
        var compiled = Read(file).Compile();
        var frame = new Frame();
        var levels = new List<double>();
        double seconds = Math.Min(Math.Max(compiled.Length * 2, 4), 60);
        for (int i = 0; i < seconds * 60; i++)
        {
            compiled.Render(frame, i / 60.0);
            levels.Add(KeyGeometry.Cells.Average(c => Luminance(frame[c])));
        }

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
        Assert.True(worst <= 6, $"{file}: {worst} brightness swings within one second (at most 6 allowed, 3 flashes)");
    }

    [Fact]
    public void The_list_matches_the_files()
    {
        var index = JsonNode.Parse(File.ReadAllText(Path.Combine(Site, "animations", "index.json")))!.AsArray();
        var listed = index.Select(e => (string)e!["file"]!).ToList();
        Assert.Equal(listed.Count, listed.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(FileNames(), listed.Order(StringComparer.Ordinal));

        foreach (var entry in index)
        {
            var file = (string)entry!["file"]!;
            Assert.Matches("^[a-z0-9][a-z0-9-]*\\.json$", file);
            var animation = Read(file);
            Assert.Equal(animation.Name, (string?)entry["name"]);
            Assert.Equal(animation.Frames.Count, (int)entry["frames"]!);
            Assert.Equal(animation.Music, (string?)entry["music"]);
            Assert.False(string.IsNullOrWhiteSpace((string?)entry["author"]), $"{file} has no author");
            Assert.Matches("^\\d{4}-\\d{2}-\\d{2}$", (string?)entry["added"] ?? "");
        }
    }

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
