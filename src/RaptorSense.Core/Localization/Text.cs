using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace RaptorSense.Localization;

/// <summary>A language the app can be shown in.</summary>
/// <param name="Code">The file name of its table, such as "de" or "pt-BR"; "en" for English, which needs no table.</param>
/// <param name="Name">Its name in that language, as the language list shows it.</param>
public sealed record Language(string Code, string Name);

/// <summary>
/// The app's words in the chosen language. The English text itself is the key: <c>L("Save")</c> looks "Save" up in the
/// language's table and gives back the English when the table has no entry, so a missing translation shows English
/// rather than a gap. Tables are JSON files of "English": "translation" pairs, built into this library from
/// Localization/*.json.
/// </summary>
/// <remarks>
/// Text with values in it uses numbered places, as string.Format does: <c>L("{0} of {1}", index, count)</c>, so each
/// language can put the values where its grammar wants them.
/// </remarks>
public static class Text
{
    public static IReadOnlyList<Language> Languages { get; } =
    [
        new("en", "English"),
        new("de", "Deutsch"),
        new("es", "Español"),
        new("fr", "Français"),
        new("pt-BR", "Português (Brasil)"),
        new("hi", "हिन्दी"),
    ];

    static Dictionary<string, string> table = [];

    /// <summary>The language in use.</summary>
    public static Language Current { get; private set; } = Languages[0];

    /// <summary>Switches to a language by code; anything unknown, or null, means English.</summary>
    public static void Use(string? code)
    {
        Current = Find(code) ?? Languages[0];
        table = Current.Code == "en" ? [] : Load(Current.Code);
    }

    /// <summary>The language for <paramref name="code"/>, matching "pt-BR", "pt_BR.UTF-8", "pt" or "PT-br" alike.</summary>
    public static Language? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string tag = code.Split('.', '@')[0].Replace('_', '-');
        return Languages.FirstOrDefault(l => l.Code.Equals(tag, StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault(l => l.Code.Split('-')[0].Equals(tag.Split('-')[0], StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The system's display language, if it is one of ours. Windows says it through the culture; the Linux build runs
    /// without culture data, so there it comes from the usual environment variables.
    /// </summary>
    public static Language? FromSystem()
    {
        foreach (var name in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value) && value is not ("C" or "POSIX") && !value.StartsWith("C.", StringComparison.Ordinal))
                return Find(value);
        }
        string culture = CultureInfo.CurrentUICulture.Name;
        return culture.Length == 0 ? null : Find(culture);
    }

    /// <summary>The text in the current language.</summary>
    public static string L(string english) => table.TryGetValue(english, out var translated) ? translated : english;

    /// <summary>The text in the current language, with the values put in its numbered places.</summary>
    public static string L(string english, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, L(english), values);

    /// <summary>The table for a language code, empty when there is none. Public for the tests.</summary>
    public static Dictionary<string, string> Load(string code)
    {
        using var stream = typeof(Text).Assembly.GetManifestResourceStream($"RaptorSense.Localization.{code}.json");
        if (stream is null) return [];
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }

    /// <summary>Every language that has a table built in, for the tests.</summary>
    public static IEnumerable<string> BuiltInTables() =>
        typeof(Text).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("RaptorSense.Localization.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => n["RaptorSense.Localization.".Length..^".json".Length]);
}
