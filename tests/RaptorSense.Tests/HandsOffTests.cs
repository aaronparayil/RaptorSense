using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

public sealed class HandsOffTests
{
    [Theory]
    [InlineData(HandsOffChoice.Automatic, true, true)]
    [InlineData(HandsOffChoice.Automatic, false, false)]
    [InlineData(HandsOffChoice.Never, true, false)]
    [InlineData(HandsOffChoice.Never, false, false)]
    [InlineData(HandsOffChoice.Always, true, true)]
    [InlineData(HandsOffChoice.Always, false, true)]
    public void TheChoiceDecides(HandsOffChoice choice, bool otherRuns, bool expected) =>
        Assert.Equal(expected, new HandsOff(() => otherRuns, () => 0) { Choice = choice }.Active);

    [Fact]
    public void WithoutAChoiceItFollowsTheOtherApp() => Assert.Equal(HandsOffChoice.Automatic, new HandsOff(() => false, () => 0).Choice);

    [Fact]
    public void TheRunningProgramsAreLookedAtAtMostOnceInAFewSeconds()
    {
        long now = 1000;
        int looked = 0;
        bool runs = false;
        var handsOff = new HandsOff(() => { looked++; return runs; }, () => now);
        Assert.False(handsOff.Active);
        runs = true;
        now += 2999;
        Assert.False(handsOff.Active);
        Assert.Equal(1, looked);
        now += 1;
        Assert.True(handsOff.Active);
        Assert.Equal(2, looked);
        runs = false;
        now += 3000;
        Assert.False(handsOff.Active);
    }

    [Theory]
    [InlineData(HandsOffChoice.Never)]
    [InlineData(HandsOffChoice.Always)]
    public void AFixedChoiceNeverLooksAtTheRunningPrograms(HandsOffChoice choice) =>
        _ = new HandsOff(() => throw new InvalidOperationException("looked"), () => 0) { Choice = choice }.Active;

    [Theory]
    [InlineData(HandsOffChoice.Automatic)]
    [InlineData(HandsOffChoice.Never)]
    [InlineData(HandsOffChoice.Always)]
    public void TheChoiceIsSavedAndReadBack(HandsOffChoice choice) => Assert.Equal(choice, HandsOff.Parse(HandsOff.Save(choice)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("something else")]
    public void AnythingElseSavedIsAutomatic(string? saved) => Assert.Equal(HandsOffChoice.Automatic, HandsOff.Parse(saved));
}
