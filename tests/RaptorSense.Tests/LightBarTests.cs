using RaptorSense.Hardware;
using RaptorSense.Lighting;
using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

public sealed class LightBarTests
{
    sealed class RecordingBar : ILightBar
    {
        public List<Rgb>? Zones;
        public int Brightness = -1;
        public (LightBarMode Mode, Rgb Color, int Speed)? Mode;

        public void SetStatic(IReadOnlyList<Rgb> zones, int brightness) { Zones = [.. zones]; Brightness = brightness; }

        public void SetMode(LightBarMode mode, Rgb color, int speed, int brightness) { Mode = (mode, color, speed); Brightness = brightness; }
    }

    // The three values sent in the hardware test on 2026-11-05 (zone 1 red, zone 2 green, zone 3 blue), which worked.
    [Theory]
    [InlineData(0, 255, 0, 0, 0x108_0000_FF00UL)]
    [InlineData(1, 0, 255, 0, 0x208_00FF_0000UL)]
    [InlineData(2, 0, 0, 255, 0x408_FF00_0000UL)]
    public void ZoneColorMatchesWhatTheHardwareAccepted(int zone, byte r, byte g, byte b, ulong expected) =>
        Assert.Equal(expected, LightBarProtocol.ZoneColor(zone, new Rgb(r, g, b)));

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void ZoneOutOfRangeIsRejected(int zone) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LightBarProtocol.ZoneColor(zone, Rgb.White));

    [Fact]
    public void StaticBacklightCarriesOnlyBrightness()
    {
        Assert.Equal(Convert.FromHexString("00006400000000000302000000000000"), LightBarProtocol.StaticBacklight(100));
        Assert.Equal(0, LightBarProtocol.StaticBacklight(-5)[2]);
        Assert.Equal(100, LightBarProtocol.StaticBacklight(500)[2]);
    }

    [Fact]
    public void ModeBacklightPutsModeSpeedBrightnessAndColour() =>
        Assert.Equal(Convert.FromHexString("03054B0001FF00EE0302000000000000"),
            LightBarProtocol.ModeBacklight(LightBarMode.Rainbow, new Rgb(0xFF, 0x00, 0xEE), 5, 75));

    [Fact]
    public void TriggerPayloadIsTheFixedOneAcerSends() =>
        Assert.Equal(Convert.FromHexString("100000000000FFFFFF150000"), LightBarProtocol.Trigger);

    [Fact]
    public void OneColourFillsAllThreeZones()
    {
        var bar = new RecordingBar();
        new LightBarRequest { Colors = ["FF0000"], Brightness = 40 }.ApplyTo(bar);
        Assert.Equal([new Rgb(255, 0, 0), new Rgb(255, 0, 0), new Rgb(255, 0, 0)], bar.Zones);
        Assert.Equal(40, bar.Brightness);
    }

    [Fact]
    public void ThreeColoursKeepTheirOrder()
    {
        var bar = new RecordingBar();
        new LightBarRequest { Colors = ["FF0000", "00FF00", "#0000FF"] }.ApplyTo(bar);
        Assert.Equal([new Rgb(255, 0, 0), new Rgb(0, 255, 0), new Rgb(0, 0, 255)], bar.Zones);
    }

    [Fact]
    public void BrightnessIsClamped()
    {
        var bar = new RecordingBar();
        new LightBarRequest { Colors = ["FFFFFF"], Brightness = 900 }.ApplyTo(bar);
        Assert.Equal(100, bar.Brightness);
    }

    [Fact]
    public void ModeUsesTheFirstColour()
    {
        var bar = new RecordingBar();
        new LightBarRequest { Colors = ["00AEC7", "FF0000", "00FF00"], Mode = LightBarMode.Wave, Speed = 3 }.ApplyTo(bar);
        Assert.Equal((LightBarMode.Wave, new Rgb(0x00, 0xAE, 0xC7), 3), bar.Mode);
        Assert.Null(bar.Zones);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void WrongNumberOfColoursIsRefusedBeforeTouchingTheFirmware(int count)
    {
        var bar = new RecordingBar();
        var request = new LightBarRequest { Colors = Enumerable.Repeat("FFFFFF", count).ToList() };
        Assert.Throws<ArgumentException>(() => request.ApplyTo(bar));
        Assert.Null(bar.Zones);
    }

    [Fact]
    public void BadColourOrUnknownModeIsRefused()
    {
        var bar = new RecordingBar();
        Assert.Throws<ArgumentException>(() => new LightBarRequest { Colors = ["nope"] }.ApplyTo(bar));
        Assert.Throws<ArgumentException>(() => new LightBarRequest { Colors = ["FFFFFF"], Mode = (LightBarMode)99 }.ApplyTo(bar));
        Assert.Null(bar.Zones);
        Assert.Null(bar.Mode);
    }

    [Fact]
    public void RequestSurvivesTheJsonRoundTripTheHelperUses()
    {
        var request = new ThermalRequest
        {
            Command = "lightbar",
            LightBar = new LightBarRequest { Colors = ["FF0000", "00FF00", "0000FF"], Brightness = 60, Mode = LightBarMode.Ripple, Speed = 7 },
        };
        var json = System.Text.Json.JsonSerializer.Serialize(request, ThermalProtocol.Json);
        var back = System.Text.Json.JsonSerializer.Deserialize<ThermalRequest>(json, ThermalProtocol.Json)!;
        Assert.Equal("lightbar", back.Command);
        Assert.Equal(["FF0000", "00FF00", "0000FF"], back.LightBar!.Colors);
        Assert.Equal((60, LightBarMode.Ripple, 7), (back.LightBar.Brightness, back.LightBar.Mode, back.LightBar.Speed));
    }
}
