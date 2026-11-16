using RaptorSense.Hardware;
using RaptorSense.Lighting;
using Xunit;

namespace RaptorSense.Tests;

// These pin the layouts to the Linux sources in docs/protocol.md. They say nothing about whether the AN515-57 accepts them.
public sealed class FourZoneKeyboardTests
{
    [Fact]
    public void EnableSetsTheUpdateBitAndAllFourZones() =>
        Assert.Equal(0x0F00_0000_0008UL, FourZoneProtocol.EnableAllZones);

    [Theory]
    [InlineData(0, 255, 0, 0, 0x0000_FF01UL)]
    [InlineData(1, 0, 255, 0, 0x00FF_0002UL)]
    [InlineData(2, 0, 0, 255, 0xFF00_0004UL)]
    [InlineData(3, 0x12, 0x34, 0x56, 0x5634_1208UL)]
    public void ZoneColorIsMaskThenRgb(int zone, byte r, byte g, byte b, ulong expected) =>
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
