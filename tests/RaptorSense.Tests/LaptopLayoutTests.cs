using RaptorSense.Hardware;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

public sealed class LaptopLayoutTests
{
    [Theory]
    [InlineData("Nitro AN515-57", LaptopLayout.Nitro)]
    [InlineData("Predator PH16-71", LaptopLayout.Helios)]
    [InlineData("Nitro AN515-58", LaptopLayout.Helios)]   // not checked: drawn as the Helios, as before
    [InlineData(null, LaptopLayout.Helios)]
    public void TheLayoutFollowsTheModel(string? model, LaptopLayout expected) => Assert.Equal(expected, LaptopLayouts.For(model));

    [Fact]
    public void TheShortNamesAreWhatTheAppShows()
    {
        Assert.Equal("Nitro", LaptopLayouts.ShortName(LaptopLayout.Nitro));
        Assert.Equal("Helios", LaptopLayouts.ShortName(LaptopLayout.Helios));
    }

    [Fact]
    public void TheDescriptionSaysWhenTheLaptopIsNotTheOneDrawn()
    {
        Assert.Contains("4 lighting zones", LaptopLayouts.Describe(LaptopLayout.Nitro, "Nitro AN515-57"));
        Assert.Contains("every key", LaptopLayouts.Describe(LaptopLayout.Helios, "Predator PH16-71"));
        Assert.Contains("this laptop: Predator PH18-71", LaptopLayouts.Describe(LaptopLayout.Helios, "Predator PH18-71"));
    }

    [Theory]
    [InlineData("Esc", 0)]
    [InlineData("`", 0)]
    [InlineData("4", 0)]
    [InlineData("Q", 0)]
    [InlineData("Ctrl", 0)]
    [InlineData("5", 1)]
    [InlineData("G", 1)]
    [InlineData("Space", 1)]
    [InlineData("Bksp", 2)]
    [InlineData("Enter", 2)]
    [InlineData("RShift", 2)]
    [InlineData("Del", 2)]
    [InlineData("Up", 3)]
    [InlineData("Left", 3)]
    [InlineData("Num5", 3)]
    [InlineData("NumEnt", 3)]
    [InlineData("Predator", 3)]
    public void KeysFallInTheirZone(string label, int zone) => Assert.Equal(zone, FourZoneLayout.ZoneOf(KeyMap.Find(label)!));

    [Fact]
    public void EveryKeyIsInAZoneAndEveryZoneHasKeys()
    {
        var zones = KeyMap.Keys.Where(k => PhysicalLayout.For(k.Label) is not null).Select(FourZoneLayout.ZoneOf).ToList();
        Assert.All(zones, zone => Assert.InRange(zone, 0, FourZoneProtocol.Zones - 1));
        for (int zone = 0; zone < FourZoneProtocol.Zones; zone++) Assert.True(zones.Count(z => z == zone) >= 10, $"zone {zone} has too few keys");
    }

    [Fact]
    public void CellsWithoutAKeyAreInNoZone() => Assert.Equal(-1, FourZoneLayout.ZoneOf(7));   // cell 7 has no key

    [Fact]
    public void AZoneShowsTheAverageOfItsKeys()
    {
        var frame = new Frame();
        foreach (var key in KeyMap.Keys.Where(k => FourZoneLayout.ZoneOf(k) == 0)) frame[key.Cell] = new Rgb(200, 0, 0);
        foreach (var key in KeyMap.Keys.Where(k => FourZoneLayout.ZoneOf(k) == 2)) frame[key.Cell] = key.Label == "Bksp" ? Rgb.White : Rgb.Black;
        frame[7] = Rgb.White;   // no key here: ignored

        var zones = FourZoneLayout.Average(frame.Cells);
        Assert.Equal(new Rgb(200, 0, 0), zones[0]);
        Assert.Equal(Rgb.Black, zones[1]);
        Assert.True(zones[2].R is > 0 and < 64, "one white key among many dark ones is a dim grey");
        Assert.Equal(Rgb.Black, zones[3]);
    }

    [Fact]
    public void SpreadPaintsEachKeyWithItsZoneAndAveragesBack()
    {
        Rgb[] zones = [new(255, 0, 0), new(0, 255, 0), new(0, 0, 255), Rgb.White];
        var frame = new Frame();
        var cells = new Rgb[Frame.CellCount];
        FourZoneLayout.Spread(zones, cells);
        Assert.Equal(new Rgb(0, 0, 255), cells[KeyMap.Find("Enter")!.Cell]);
        Assert.Equal(Rgb.Black, cells[7]);
        Assert.Equal(zones, FourZoneLayout.Average(cells));
    }

    [Fact]
    public void TheNitroKeyReplacesThePredatorKey() => Assert.Equal("Nitro", LaptopLayouts.NitroCaps["Predator"]);

    [Fact]
    public void AStreamedFrameSkipsTheReadBack()
    {
        Assert.True(new FourZoneRequest().Confirm);
        var sent = new FourZoneRequest { Colors = ["FF0000"], Confirm = false };
        var received = System.Text.Json.JsonSerializer.Deserialize<FourZoneRequest>(
            System.Text.Json.JsonSerializer.Serialize(sent, ThermalProtocol.Json), ThermalProtocol.Json)!;
        Assert.False(received.Confirm);
    }
}
