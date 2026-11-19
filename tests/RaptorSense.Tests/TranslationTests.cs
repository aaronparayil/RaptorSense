using System.Text.Json;
using System.Text.RegularExpressions;
using RaptorSense.Hardware;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Localization;
using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

/// <summary>
/// Every language has every piece of the app's text, with the same numbered places. The text is found the way the app
/// uses it: each L("...") in the source, plus the names that come from code (effects, their groups, modes).
/// </summary>
/// <remarks>
/// To get the full English list, for adding a language or new text: set RAPTORSENSE_DUMP_TEXT to a file path and run
/// these tests; the list is written there as JSON.
/// </remarks>
public sealed class TranslationTests
{
    static readonly Lazy<SortedSet<string>> required = new(FindText);

    static SortedSet<string> FindText()
    {
        var text = new SortedSet<string>(StringComparer.Ordinal);
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "RaptorSense.slnx"))) root = root.Parent;
        Assert.NotNull(root);

        var call = new Regex(@"\bL\(""((?:[^""\\]|\\.)*)""", RegexOptions.Compiled);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            // Comments show L("...") as examples; only code counts.
            var code = string.Join('\n', File.ReadLines(file).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));
            foreach (Match m in call.Matches(code)) text.Add(Regex.Unescape(m.Groups[1].Value));
        }

        foreach (var effect in Effects.All.Concat(Effects.Nitro)) text.Add(effect.Name);
        text.Add(new StartupShow(LaptopLayout.Helios).Name);
        foreach (var category in EffectCategory.Order)
        {
            text.Add(category);
            text.Add(EffectCategory.Describe(category));
        }
        foreach (var name in Enum.GetNames<PowerProfile>().Concat(Enum.GetNames<FourZoneMode>()).Concat(Enum.GetNames<LightBarMode>())) text.Add(name);
        text.Add(ThermalClient.HelperMissing);
        text.Add("Indigo"); text.Add("Ocean"); text.Add("Teal"); text.Add("Forest"); text.Add("Wine");   // background colour names
        text.Add("Ember"); text.Add("Plum"); text.Add("Graphite"); text.Add("Black");

        if (Environment.GetEnvironmentVariable("RAPTORSENSE_DUMP_TEXT") is { Length: > 0 } path)
            File.WriteAllText(path, JsonSerializer.Serialize(text, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        return text;
    }

    public static TheoryData<string> Tables()
    {
        var data = new TheoryData<string>();
        foreach (var code in Text.BuiltInTables()) data.Add(code);
        return data;
    }

    [Fact]
    public void EveryLanguageOfferedHasATable()
    {
        var tables = Text.BuiltInTables().ToHashSet();
        foreach (var language in Text.Languages.Where(l => l.Code != "en")) Assert.Contains(language.Code, tables);
    }

    [Fact]
    public void TheTextIsFound()
    {
        Assert.Contains("Search effects", required.Value);
        Assert.Contains("Rainbow wave", required.Value);
        Assert.Contains("Saved profile '{0}'.", required.Value);
        Assert.Contains("Lighting tab effects paused: turn on \"Controlling the keyboard\" to play them again.", required.Value);
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void HasEveryText(string code)
    {
        var table = Text.Load(code);
        var missing = required.Value.Where(t => !table.ContainsKey(t)).ToList();
        Assert.True(missing.Count == 0, $"{code} lacks {missing.Count}: " + string.Join(" | ", missing.Take(15)));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void HasNothingTheAppNoLongerSays(string code)
    {
        var stale = Text.Load(code).Keys.Where(k => !required.Value.Contains(k)).ToList();
        Assert.True(stale.Count == 0, $"{code} has {stale.Count} unused: " + string.Join(" | ", stale.Take(15)));
    }

    [Theory]
    [MemberData(nameof(Tables))]
    public void KeepsTheNumberedPlacesAndSymbols(string code)
    {
        var place = new Regex(@"\{\d+(:[^}]*)?\}");
        foreach (var (english, translated) in Text.Load(code))
        {
            Assert.False(string.IsNullOrWhiteSpace(translated), $"{code}: empty for '{english}'");
            var want = place.Matches(english).Select(m => m.Value).Order().ToList();
            var got = place.Matches(translated).Select(m => m.Value).Order().ToList();
            Assert.True(want.SequenceEqual(got), $"{code}: '{english}' → '{translated}'");
            foreach (var symbol in new[] { "★", "♪", "⌨", "°C", "%" })
                Assert.True(english.Contains(symbol) == translated.Contains(symbol) || symbol == "%", $"{code}: '{symbol}' in '{english}' → '{translated}'");
            // A format string must still format: no stray braces.
            _ = string.Format(translated, new object[10]);
        }
    }

    [Theory]
    [InlineData("de", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("pt_BR.UTF-8", "pt-BR")]
    [InlineData("pt-PT", "pt-BR")]
    [InlineData("hi_IN", "hi")]
    [InlineData("fr_CA.UTF-8", "fr")]
    [InlineData("ES", "es")]
    [InlineData("en_GB.UTF-8", "en")]
    public void FindsTheLanguageFromSystemNames(string name, string code) => Assert.Equal(code, Text.Find(name)?.Code);

    [Theory]
    [InlineData("ja-JP")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownLanguagesAreNotFound(string? name) => Assert.Null(Text.Find(name));
}
