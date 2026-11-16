using RaptorSense.Layout;
using RaptorSense.Lighting;
using Xunit;

namespace RaptorSense.Tests;

public sealed class StartupShowTests
{
    static EffectContext At(double realTime) => new(realTime, realTime, 0.1, new EffectSettings(), null, []);

    static int Cell(string label) => KeyMap.Find(label)!.Cell;

    [Theory]
    [InlineData(0.8)]    // rainbow
    [InlineData(1.6)]    // strobe
    [InlineData(3.0)]    // scanner
    [InlineData(4.5)]    // pops
    [InlineData(5.45)]   // white flash
    public void EveryPartOfTheShowLightsTheKeyboard(double t) =>
        Assert.Contains(Enumerable.Range(0, 4), zone => StartupShow.ColorAt((zone + 0.5) / 4, zone, t) != Rgb.Black);

    [Fact]
    public void TheShowIsMulticoloured()
    {
        var colours = new HashSet<Rgb>();
        for (double t = 0; t < StartupShow.Duration; t += 0.1)
            for (int zone = 0; zone < 4; zone++) colours.Add(StartupShow.ColorAt((zone + 0.5) / 4, zone, t));
        Assert.True(colours.Count > 40, $"only {colours.Count} colours");
    }

    [Fact]
    public void TheFinaleFlashesWhite() => Assert.Equal(Rgb.White, StartupShow.ColorAt(0.5, 1, 5.45));

    [Fact]
    public void ItFadesOutAtTheEnd()
    {
        var nearEnd = StartupShow.ColorAt(0.5, 1, StartupShow.Duration - 0.05);
        Assert.True(nearEnd.R + nearEnd.G + nearEnd.B < 60);
        Assert.Equal(Rgb.Black, StartupShow.ColorAt(0.5, 1, StartupShow.Duration));
        Assert.Equal(Rgb.Black, StartupShow.ColorAt(0.5, 1, -1));
    }

    [Fact]
    public void OnTheNitroEachZoneIsOneColour()
    {
        var show = new StartupShow(LaptopLayout.Nitro);
        for (double t = 0; t < StartupShow.Duration; t += 0.1)
        {
            var frame = new Frame();
            show.Render(frame, At(100 + t));
            Assert.Equal(frame[Cell("Q")], frame[Cell("Esc")]);
            Assert.Equal(frame[Cell("Num5")], frame[Cell("Up")]);
        }
    }

    [Fact]
    public void OnTheHeliosItMovesAcrossTheKeys()
    {
        var show = new StartupShow(LaptopLayout.Helios);
        var frame = new Frame();
        show.Render(frame, At(10));
        show.Render(frame, At(10.8));
        Assert.NotEqual(frame[Cell("Esc")], frame[Cell("Num5")]);
    }

    [Fact]
    public void ItCountsFromItsFirstFrameAndFinishes()
    {
        var show = new StartupShow(LaptopLayout.Nitro);
        var frame = new Frame();
        show.Render(frame, At(50));
        Assert.Equal(0, show.Elapsed);
        Assert.False(show.Finished);
        show.Render(frame, At(50 + StartupShow.Duration));
        Assert.True(show.Finished);

        show.Restart();
        show.Render(frame, At(200));
        Assert.False(show.Finished);
    }

    [Fact]
    public void TheShowIsNotInEitherEffectList()
    {
        Assert.DoesNotContain(Effects.All, e => e.Id == "startup-show");
        Assert.DoesNotContain(Effects.Nitro, e => e.Id == "startup-show");
    }
}
