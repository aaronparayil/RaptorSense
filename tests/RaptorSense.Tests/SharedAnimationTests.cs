using System.Net;
using System.Text;
using RaptorSense.Profiles;
using Xunit;

namespace RaptorSense.Tests;

/// <summary>
/// Animations from the website: which links the app accepts, reading the website's list, and adding a file, all
/// against a pretend website so no test goes online.
/// </summary>
public sealed class SharedAnimationTests : IDisposable
{
    readonly string folder = Directory.CreateTempSubdirectory("raptorsense-shared-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Theory]
    [InlineData("raptorsense://add-animation/neon-rain", "neon-rain")]
    [InlineData("raptorsense://add-animation/neon-rain/", "neon-rain")]
    [InlineData("RaptorSense://Add-Animation/Neon-Rain", "neon-rain")]
    [InlineData("raptorsense://add-animation/neon-rain.json", "neon-rain")]
    [InlineData("raptorsense://add-animation/neon-rain?from=site", "neon-rain")]
    [InlineData("raptorsense%3A%2F%2Fadd-animation%2Fsweep", "sweep")]
    [InlineData("  raptorsense://add-animation/a1  ", "a1")]
    public void Accepts_the_websites_links(string link, string slug) => Assert.Equal(slug, SharedAnimations.SlugFromLink(link));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://aaronparayil.github.io/RaptorSense/animations/sweep.json")]
    [InlineData("raptorsense://add-animation/")]
    [InlineData("raptorsense://add-animation/../profiles/last")]
    [InlineData("raptorsense://add-animation/..%2F..%2Fsecret")]
    [InlineData("raptorsense://add-animation/https://evil.example/x")]
    [InlineData("raptorsense://add-animation/a b")]
    [InlineData("raptorsense://add-animation/-starts-with-dash")]
    [InlineData("raptorsense://delete-everything/sweep")]
    [InlineData("--minimized")]
    public void Refuses_anything_else(string? link) => Assert.Null(SharedAnimations.SlugFromLink(link));

    [Fact]
    public void A_link_round_trips() => Assert.Equal("rainbow-drift", SharedAnimations.SlugFromLink(SharedAnimations.LinkFor("rainbow-drift")));

    [Fact]
    public async Task Reads_the_list_newest_first_and_skips_nonsense()
    {
        var site = new PretendSite
        {
            ["animations/index.json"] = """
                [
                  { "file": "sweep.json", "name": "Sweep", "author": "RaptorSense", "about": "A sweep.", "frames": 21, "music": null, "added": "2026-11-20" },
                  { "file": "../evil.json", "name": "Evil", "author": "x", "frames": 1 },
                  { "file": "no-name.json", "name": "", "author": "x", "frames": 1 },
                  "not an object",
                  { "file": "beat-rain.json", "name": "Beat rain", "author": "Someone", "frames": 8, "music": "beat", "added": "2026-11-21" }
                ]
                """,
        };
        using var http = SharedAnimations.CreateClient(site);
        var list = await SharedAnimations.ListAsync(http, TestContext.Current.CancellationToken);

        Assert.Equal(["beat-rain", "sweep"], list.Select(a => a.Slug));
        Assert.Equal("beat", list[0].Music);
        Assert.Null(list[1].Music);
        Assert.Equal("A sweep.", list[1].About);
        Assert.All(site.Asked, uri => Assert.StartsWith(SharedAnimations.Site.ToString(), uri.ToString()));
    }

    [Fact]
    public async Task Adds_a_file_from_the_website_cleaned_and_without_replacing_one_already_there()
    {
        var site = new PretendSite
        {
            ["animations/neon-rain.json"] = "﻿" + """
                { "Name": "Neon rain", "Frames": [ { "Hold": 999, "Fade": 0.2, "Keys": { "q": "#00FF00", "NoSuchKey": "FF0000" } } ], "Music": "BEAT" }
                """,
        };
        using var http = SharedAnimations.CreateClient(site);
        AnimationStore.Save(new() { Name = "Neon rain", Frames = [new()] }, folder);

        var added = await SharedAnimations.AddAsync(http, "neon-rain", folder, TestContext.Current.CancellationToken);

        Assert.Equal("Neon rain 2", added.Name);
        Assert.Equal("beat", added.Music);
        var frame = Assert.Single(added.Frames);
        Assert.Equal(60, frame.Hold);                                   // clamped like any import
        Assert.Equal(new Dictionary<string, string> { ["Q"] = "00FF00" }, frame.Keys);
        Assert.NotNull(AnimationStore.Load("Neon rain 2", folder));
        Assert.Equal(new Uri(SharedAnimations.Site, "animations/neon-rain.json"), Assert.Single(site.Asked));
    }

    [Fact]
    public async Task Says_plainly_when_it_is_gone_too_big_or_not_an_animation()
    {
        var site = new PretendSite
        {
            ["animations/huge.json"] = new string(' ', (int)AnimationStore.MaxImportBytes + 10),
            ["animations/junk.json"] = "<html>not json</html>",
        };
        using var http = SharedAnimations.CreateClient(site);
        var cancel = TestContext.Current.CancellationToken;

        var gone = await Assert.ThrowsAsync<InvalidDataException>(() => SharedAnimations.AddAsync(http, "missing", folder, cancel));
        Assert.Contains("isn't on the website", gone.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => SharedAnimations.AddAsync(http, "huge", folder, cancel));
        await Assert.ThrowsAsync<InvalidDataException>(() => SharedAnimations.AddAsync(http, "junk", folder, cancel));
        await Assert.ThrowsAsync<InvalidDataException>(() => SharedAnimations.AddAsync(http, "../escape", folder, cancel));
        Assert.Empty(AnimationStore.List(folder));
    }

    /// <summary>Answers from a dictionary of paths under the website, 404 for anything else, and remembers what was asked.</summary>
    sealed class PretendSite : HttpMessageHandler
    {
        readonly Dictionary<string, string> files = [];
        public List<Uri> Asked { get; } = [];

        public string this[string path] { set => files[new Uri(SharedAnimations.Site, path).ToString()] = value; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);
            Assert.Contains(request.Headers.UserAgent, p => p.Product?.Name == "RaptorSense");
            return Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
