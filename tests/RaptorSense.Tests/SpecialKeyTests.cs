using RaptorSense.Input;
using Xunit;

namespace RaptorSense.Tests;

public sealed class SpecialKeyTests
{
    [Theory]
    [InlineData(new byte[] { 0x04, 0x81, 0xFF }, SpecialKey.Predator)]
    [InlineData(new byte[] { 0x04, 0x7D, 0xFF }, SpecialKey.BrightnessDown)]   // Fn+F11
    [InlineData(new byte[] { 0x04, 0x7C, 0xFF }, SpecialKey.BrightnessUp)]     // Fn+F12
    public void Special_keys_are_recognised_when_pressed(byte[] report, SpecialKey key)
    {
        Assert.Equal(key, SpecialKeyReports.Parse(report));
    }

    [Theory]
    [InlineData(new byte[] { 0x04, 0x00, 0x00 })]   // let go: must not count as a second press
    [InlineData(new byte[] { 0x04, 0x80, 0xFF })]   // another Fn key
    [InlineData(new byte[] { 0x04, 0x81, 0x00 })]
    [InlineData(new byte[] { 0x04, 0x7C, 0x00 })]
    [InlineData(new byte[] { 0x03, 0x81, 0xFF })]   // another report
    [InlineData(new byte[] { 0x04, 0x81 })]         // cut short
    [InlineData(new byte[0])]
    public void Anything_else_is_ignored(byte[] report)
    {
        Assert.Equal(SpecialKey.None, SpecialKeyReports.Parse(report));
    }

    [Theory]
    [InlineData(0, 12)]
    [InlineData(12, 25)]
    [InlineData(25, 37)]
    [InlineData(37, 50)]
    [InlineData(50, 50)]    // already brightest
    [InlineData(30, 37)]    // set by the slider, between two levels
    [InlineData(49, 50)]
    public void Brighter_moves_to_the_next_level_up(int from, int to)
    {
        Assert.Equal(to, BrightnessSteps.Next(from, brighter: true));
    }

    [Theory]
    [InlineData(50, 37)]
    [InlineData(37, 25)]
    [InlineData(25, 12)]
    [InlineData(12, 0)]
    [InlineData(0, 0)]      // already off
    [InlineData(30, 25)]
    [InlineData(1, 0)]
    public void Dimmer_moves_to_the_next_level_down(int from, int to)
    {
        Assert.Equal(to, BrightnessSteps.Next(from, brighter: false));
    }

    [Fact]
    public void The_levels_are_the_keyboards_own()
    {
        Assert.Equal([0, 12, 25, 37, 50], BrightnessSteps.Levels);
        Assert.Equal(RaptorSense.Hardware.Ph16Keyboard.MaxBrightness, BrightnessSteps.Levels[^1]);
    }
}
