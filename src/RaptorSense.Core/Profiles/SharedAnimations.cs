using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RaptorSense.Lighting;

namespace RaptorSense.Profiles;

/// <summary>One animation on the website's list (site/animations/index.json).</summary>
public sealed record SharedAnimation(string File, string Name, string Author, string About, int Frames, string? Music, string Added)
{
    /// <summary>The part of the file name used in links: "neon-rain" for neon-rain.json.</summary>
    public string Slug => File[..^".json".Length];
}

/// <summary>
/// Animations people shared on the RaptorSense website, fetched only when someone asks: the Shared list in the
/// Animations tab, or an "Add to RaptorSense" link (raptorsense://add-animation/&lt;name&gt;) clicked on the site.
/// Files come only from the website's own animations folder, by a plain file name, never from an address a link
/// supplies, and are imported like any shared file: read as data and cleaned. Nothing is sent but the request itself.
/// </summary>
public static partial class SharedAnimations
{
    public const string Scheme = "raptorsense";
    public const string AddCommand = "add-animation";

    /// <summary>The website. Its github.io address forwards to wherever the site is served.</summary>
    public static Uri Site { get; } = new("https://aaronparayil.github.io/RaptorSense/");

    public static Uri Page { get; } = new(Site, "animations.html");

    static Uri ListUri => new(Site, "animations/index.json");

    static Uri FileUri(string file) => new(Site, "animations/" + file);

    /// <summary>The largest list read; thousands of entries fit well under it.</summary>
    const int MaxListBytes = 2 * 1024 * 1024;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,79}$")]
    private static partial Regex SlugPattern();

    public static bool IsSlug(string? slug) => slug is not null && SlugPattern().IsMatch(slug);

    /// <summary>The link the website's "Add to RaptorSense" button opens for <paramref name="slug"/>.</summary>
    public static string LinkFor(string slug) => $"{Scheme}://{AddCommand}/{slug}";

    /// <summary>
    /// The animation a raptorsense:// link asks for, as its slug, or null if the text isn't such a link. Browsers pass
    /// the link as clicked, sometimes with a slash on the end or percent-encoding; anything but a plain slug is refused.
    /// </summary>
    public static string? SlugFromLink(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;
        var text = Uri.UnescapeDataString(link.Trim());
        var prefix = $"{Scheme}://{AddCommand}/";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var slug = text[prefix.Length..].TrimEnd('/');
        int cut = slug.IndexOfAny(['?', '#']);
        if (cut >= 0) slug = slug[..cut];
        slug = slug.ToLowerInvariant();
        if (slug.EndsWith(".json", StringComparison.Ordinal)) slug = slug[..^5];
        return IsSlug(slug) ? slug : null;
    }

    /// <summary>A client for the website: short timeout, and a user agent that says only the app and its version.</summary>
    public static HttpClient CreateClient(HttpMessageHandler? handler = null)
    {
        var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.Version));
        return http;
    }

    /// <summary>The website's animations, newest first. Entries that don't make sense are left out.</summary>
    public static async Task<IReadOnlyList<SharedAnimation>> ListAsync(HttpClient http, CancellationToken cancel = default)
    {
        var text = await GetTextAsync(http, ListUri, MaxListBytes, cancel);
        var list = new List<SharedAnimation>();
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The website's list of animations isn't readable.");
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                string file = Str(e, "file");
                if (!file.EndsWith(".json", StringComparison.Ordinal) || !IsSlug(file[..^5])) continue;
                string name = Str(e, "name");
                if (name.Length == 0) continue;
                int frames = e.TryGetProperty("frames", out var f) && f.TryGetInt32(out int n) ? n : 0;
                string music = Str(e, "music");
                list.Add(new SharedAnimation(file, name, Str(e, "author"), Str(e, "about"), frames, music.Length > 0 ? music : null, Str(e, "added")));
            }
        }
        catch (JsonException) { throw new InvalidDataException("The website's list of animations isn't readable."); }
        list.Reverse();   // the file lists oldest first
        return list;
    }

    /// <summary>Fetches the website's animation <paramref name="slug"/> and imports it. Returns it as saved.</summary>
    public static async Task<KeyAnimation> AddAsync(HttpClient http, string slug, string? folder = null, CancellationToken cancel = default)
    {
        if (!IsSlug(slug)) throw new InvalidDataException("That isn't the name of an animation on the website.");
        var text = await GetTextAsync(http, FileUri(slug + ".json"), (int)AnimationStore.MaxImportBytes, cancel);
        return AnimationStore.ImportText(text, slug.Replace('-', ' '), folder);
    }

    static async Task<string> GetTextAsync(HttpClient http, Uri uri, int maxBytes, CancellationToken cancel)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidDataException("That animation isn't on the website any more.");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("That file is too big to be a RaptorSense animation.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancel);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancel)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes) throw new InvalidDataException("That file is too big to be a RaptorSense animation.");
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('﻿');
    }

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()!.Trim() : "";
}
