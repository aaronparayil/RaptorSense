using RaptorSense.Hardware;
using RaptorSense.Lighting;
using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

// The layouts, as in docs/protocol.md. Static zone colours and their read-back were checked on an AN515-57 on 2026-11-16.
public sealed class FourZoneKeyboardTests
{
    sealed class RecordingKeyboard : IFourZoneKeyboard
    {
        public List<Rgb>? Zones;
        public int Brightness = -1;
        public bool? Confirmed;
        public (FourZoneMode Mode, Rgb Color, int Speed, FourZoneDirection Direction)? Effect;

        public void SetZones(IReadOnlyList<Rgb> zones, int brightness, bool confirm = true)
        {
            Zones = [.. zones];
            Brightness = brightness;
            Confirmed = confirm;
        }

        public void SetEffect(FourZoneMode mode, Rgb color, int speed, int brightness, FourZoneDirection direction)
        {
            Effect = (mode, color, speed, direction);
            Brightness = brightness;
        }
    }

    // What the AN515-57 sent back on 2026-11-16: its factory purple AA00FF, and zone 1 after being set to red.
    [Theory]
    [InlineData(0xFF00AA00UL, 0xAA, 0x00, 0xFF)]
    [InlineData(0x0000FF00UL, 0xFF, 0x00, 0x00)]
    public void ReadZoneColorDecodesWhatTheLaptopSent(ulong output, byte r, byte g, byte b) =>
        Assert.Equal(new Rgb(r, g, b), FourZoneProtocol.ReadZoneColor(output));

    [Fact]
    public void ReadZoneColorIsNullForAFailedRead() =>   // the AN515-57's answer to mask 0xF
        Assert.Null(FourZoneProtocol.ReadZoneColor(0xFF00AA02UL));

    [Fact]
    public void WhatIsWrittenReadsBackAsTheSameColour()
    {
        var color = new Rgb(0x12, 0x34, 0x56);
        uint written = FourZoneProtocol.ZoneColor(2, color);
        Assert.Equal(color, FourZoneProtocol.ReadZoneColor(written & ~0xFFu));   // a reply has status 0 where the mask was
    }

    [Fact]
    public void OneColourFillsAllFourZones()
    {
        var keyboard = new RecordingKeyboard();
        new FourZoneRequest { Colors = ["FF0000"], Brightness = 40 }.ApplyTo(keyboard);
        Assert.Equal(Enumerable.Repeat(new Rgb(255, 0, 0), 4), keyboard.Zones);
        Assert.Equal(40, keyboard.Brightness);
        Assert.Null(keyboard.Effect);
    }

    [Fact]
    public void FourColoursGoLeftToRight()
    {
        var keyboard = new RecordingKeyboard();
        new FourZoneRequest { Colors = ["FF0000", "00FF00", "0000FF", "FFFFFF"] }.ApplyTo(keyboard);
        Assert.Equal([new Rgb(255, 0, 0), new Rgb(0, 255, 0), new Rgb(0, 0, 255), Rgb.White], keyboard.Zones);
        Assert.Equal(FourZoneProtocol.MaxBrightness, keyboard.Brightness);
    }

    [Fact]
    public void AnEffectUsesTheFirstColour()
    {
        var keyboard = new RecordingKeyboard();
        new FourZoneRequest { Colors = ["00FF00"], Mode = FourZoneMode.Wave, Speed = 7, Direction = FourZoneDirection.LeftToRight }.ApplyTo(keyboard);
        Assert.Equal((FourZoneMode.Wave, new Rgb(0, 255, 0), 7, FourZoneDirection.LeftToRight), keyboard.Effect);
        Assert.Null(keyboard.Zones);
    }

    [Fact]
    public void StaticModeSetsTheZones()
    {
        var keyboard = new RecordingKeyboard();
        new FourZoneRequest { Colors = ["00FF00"], Mode = FourZoneMode.Static }.ApplyTo(keyboard);
        Assert.NotNull(keyboard.Zones);
        Assert.Null(keyboard.Effect);
    }

    [Fact]
    public void BrightnessIsClampedBeforeItIsSent()
    {
        var keyboard = new RecordingKeyboard();
        new FourZoneRequest { Colors = ["FF0000"], Brightness = 900 }.ApplyTo(keyboard);
        Assert.Equal(FourZoneProtocol.MaxBrightness, keyboard.Brightness);
    }

    [Theory]
    [InlineData("")]
    [InlineData("FF0000,00FF00")]
    [InlineData("FF0000,00FF00,0000FF")]
    [InlineData("nope")]
    public void MalformedColoursAreRejectedWithoutSending(string colors)
    {
        var keyboard = new RecordingKeyboard();
        var request = new FourZoneRequest { Colors = colors.Split(',', StringSplitOptions.RemoveEmptyEntries) };
        Assert.Throws<ArgumentException>(() => request.ApplyTo(keyboard));
        Assert.False(request.TryGetZones(out _));
        Assert.Null(keyboard.Zones);
    }

    [Fact]
    public void UnknownModeOrDirectionInARequestIsRejectedWithoutSending()
    {
        var keyboard = new RecordingKeyboard();
        Assert.Throws<ArgumentException>(() => new FourZoneRequest { Colors = ["FF0000"], Mode = (FourZoneMode)42 }.ApplyTo(keyboard));
        Assert.Throws<ArgumentException>(() =>
            new FourZoneRequest { Colors = ["FF0000"], Mode = FourZoneMode.Wave, Direction = (FourZoneDirection)7 }.ApplyTo(keyboard));
        Assert.Null(keyboard.Zones);
        Assert.Null(keyboard.Effect);
    }

    [Theory]
    [InlineData("Nitro AN515-57", true)]
    [InlineData("nitro an515-57", true)]
    [InlineData("Nitro AN515-58", false)]
    [InlineData("Predator PH16-71", false)]
    [InlineData(null, false)]
    public void OnlyTheCheckedNitroHasAFourZoneKeyboard(string? model, bool expected) =>
        Assert.Equal(expected, LaptopModels.HasFourZoneKeyboard(model));

    [Fact]
    public void TheNitroIsNotFullyVerifiedButItsRefusalSaysWhatWorks()
    {
        Assert.False(LaptopModels.IsVerified("Nitro AN515-57"));
        Assert.Contains("the keyboard's colours work", LaptopModels.Refusal("Nitro AN515-57"));
        Assert.DoesNotContain("keyboard", LaptopModels.Refusal("Nitro AN515-58"));
    }

    [Fact]
    public void EnableSetsTheUpdateBitAndAllFourZones() =>
        Assert.Equal(0x0F00_0000_0008UL, FourZoneProtocol.EnableAllZones);

    [Theory]
    [InlineData(0, 255, 0, 0, 0x0000_FF01u)]
    [InlineData(1, 0, 255, 0, 0x00FF_0002u)]
    [InlineData(2, 0, 0, 255, 0xFF00_0004u)]
    [InlineData(3, 0x12, 0x34, 0x56, 0x5634_1208u)]
    public void ZoneColorIsMaskThenRgb(int zone, byte r, byte g, byte b, uint expected) =>
        Assert.Equal(expected, FourZoneProtocol.ZoneColor(zone, new Rgb(r, g, b)));

    [Fact]
    public void ZoneColorIsNotPackedLikeTheLightBar() =>
        Assert.NotEqual(LightBarProtocol.ZoneColor(0, Rgb.White), FourZoneProtocol.ZoneColor(0, Rgb.White));

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void ZoneOutOfRangeIsRejected(int zone) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => FourZoneProtocol.ZoneColor(zone, Rgb.White));

    [Fact]
    public void StaticBacklightCarriesBrightnessAndByte9()
    {
        Assert.Equal(Convert.FromHexString("00006400000000000001000000000000"), FourZoneProtocol.StaticBacklight(100));
        Assert.Equal(0, FourZoneProtocol.StaticBacklight(-5)[2]);
        Assert.Equal(100, FourZoneProtocol.StaticBacklight(500)[2]);
    }

    [Fact]
    public void WaveSetsByte3AndDirection() =>
        Assert.Equal(Convert.FromHexString("0305320802FF00EE0001000000000000"),
            FourZoneProtocol.EffectBacklight(FourZoneMode.Wave, new Rgb(0xFF, 0x00, 0xEE), 5, 50, FourZoneDirection.LeftToRight));

    [Fact]
    public void OtherEffectsLeaveByte3Clear() =>
        Assert.Equal(Convert.FromHexString("01096400010000FF0001000000000000"),
            FourZoneProtocol.EffectBacklight(FourZoneMode.Breathing, new Rgb(0, 0, 0xFF), 20, 100));

    [Fact]
    public void SpeedIsClampedToOneToNine() =>
        Assert.Equal(1, FourZoneProtocol.EffectBacklight(FourZoneMode.Neon, Rgb.White, 0, 100)[1]);

    [Fact]
    public void StaticModeGivesTheStaticPayload() =>
        Assert.Equal(FourZoneProtocol.StaticBacklight(40), FourZoneProtocol.EffectBacklight(FourZoneMode.Static, Rgb.White, 5, 40));

    [Fact]
    public void UnknownModeOrDirectionIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => FourZoneProtocol.EffectBacklight((FourZoneMode)9, Rgb.White, 5, 50));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FourZoneProtocol.EffectBacklight(FourZoneMode.Wave, Rgb.White, 5, 50, (FourZoneDirection)0));
    }
}
