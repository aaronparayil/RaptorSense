using System.Text.Json;
using RaptorSense.Hardware;
using RaptorSense.Lighting;
using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

public sealed class FourZoneTrialTests
{
    sealed class RecordingKeyboard : IFourZoneKeyboard
    {
        public IReadOnlyList<Rgb>? Zones;
        public FourZoneOrder? Order;
        public bool? Confirmed;
        public FourZoneMode? Mode;

        public void SetZones(IReadOnlyList<Rgb> zones, int brightness, bool confirm = true, FourZoneOrder order = FourZoneOrder.ZonesFirst) =>
            (Zones, Order, Confirmed) = (zones, order, confirm);

        public void SetEffect(FourZoneMode mode, Rgb color, int speed, int brightness, FourZoneDirection direction) => Mode = mode;
    }

    [Theory]
    [InlineData("Predator PHN16-71", true)]
    [InlineData("predator phn16-71", true)]
    [InlineData("Predator PHN16-72/EQE_RTX", true)]
    [InlineData("Nitro AN515-58", true)]
    [InlineData("Predator PH315-53", true)]
    [InlineData("Predator PH16-71", false)]    // known: per key
    [InlineData("Nitro AN515-57", false)]      // known: already lit
    [InlineData("Predator PHN16S-71", false)]  // its firmware takes colours and shows none (an ENE keyboard)
    [InlineData("Predator PH16-73", false)]
    [InlineData("Predator PHN16-71X", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheListedModelsMayBeTried(string? model, bool expected) => Assert.Equal(expected, LaptopModels.MayTryFourZoneKeyboard(model));

    [Fact]
    public void AModelToTryIsNotVerifiedForAnythingElse()
    {
        foreach (var code in LaptopModels.KeyboardCandidates)
        {
            Assert.False(LaptopModels.IsVerified(code));
            Assert.False(LaptopModels.HasFanControl(code));
            Assert.False(LaptopModels.HasFourZoneKeyboard(code));
        }
    }

    [Theory]
    [InlineData("Predator PH16-71/PHN16-71")]   // a board name never counts as the model
    [InlineData("Predator X/PH16-71")]
    public void ABoardNameIsNeverTheModel(string model)
    {
        Assert.False(LaptopModels.MayTryFourZoneKeyboard(model));
        Assert.False(LaptopModels.IsVerified(model));
    }

    // What the helper asks before it passes a keyboard change on.
    [Theory]
    [InlineData("Predator PHN16-71", true, true)]
    [InlineData("Predator PHN16-71", false, false)]   // not chosen by its owner
    [InlineData("Predator PH18-71", true, false)]     // not a listed model, whatever the app says
    [InlineData("Predator PH16-71", true, false)]
    [InlineData(null, true, false)]
    [InlineData("Nitro AN515-57", false, true)]
    [InlineData("Nitro AN515-57", true, true)]
    public void TheHelperTakesATestOnlyWhereItMayBeTried(string? model, bool trial, bool expected) =>
        Assert.Equal(expected, LaptopModels.AllowsKeyboard(model, new FourZoneRequest { Colors = ["FF0000"], Trial = trial }));

    [Fact]
    public void AMissingRequestIsNotAllowed() => Assert.False(LaptopModels.AllowsKeyboard("Nitro AN515-57", null));

    [Theory]
    [InlineData(FourZoneOrder.ZonesFirst)]
    [InlineData(FourZoneOrder.StaticFirst)]
    public void TheTestSendsFourDifferentColoursInTheOrderAsked(FourZoneOrder order)
    {
        var keyboard = new RecordingKeyboard();
        FourZoneTrial.Test(order).ApplyTo(keyboard);
        Assert.Equal(FourZoneTrial.TestColors, keyboard.Zones);
        Assert.Equal(4, FourZoneTrial.TestColors.Distinct().Count());
        Assert.Equal(order, keyboard.Order);
        Assert.False(keyboard.Confirmed);   // the owner's eyes decide, not a read-back
    }

    [Fact]
    public void ACheckedKeyboardKeepsItsOwnOrder()
    {
        var keyboard = new RecordingKeyboard();
        new FourZoneRequest { Colors = ["FF0000"], Order = FourZoneOrder.StaticFirst }.ApplyTo(keyboard);
        Assert.Equal(FourZoneOrder.ZonesFirst, keyboard.Order);
    }

    [Fact]
    public void AnUnknownOrderIsRefused() =>
        Assert.Throws<ArgumentException>(() => new FourZoneRequest { Colors = ["FF0000"], Trial = true, Order = (FourZoneOrder)9 }.ApplyTo(new RecordingKeyboard()));

    [Fact]
    public void PuttingBackStartsTheKeyboardsOwnWave()
    {
        var keyboard = new RecordingKeyboard();
        FourZoneTrial.PutBack().ApplyTo(keyboard);
        Assert.Equal(FourZoneMode.Wave, keyboard.Mode);
        Assert.Null(keyboard.Zones);
    }

    [Fact]
    public void TheTwoOrdersHaveTheirOwnStaticBytes()
    {
        Assert.Equal([0, 0, 100, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0], FourZoneProtocol.StaticBacklight(100));
        Assert.Equal([0, 0, 100, 0, 0, 0, 0, 0, 3, 1, 0, 0, 0, 0, 0, 0], FourZoneProtocol.StaticFirstBacklight(100));
    }

    [Theory]
    [InlineData(FourZoneOrder.ZonesFirst)]
    [InlineData(FourZoneOrder.StaticFirst)]
    public void AnAcceptedTestIsRemembered(FourZoneOrder order) =>
        Assert.Equal(order, FourZoneTrial.Load(FourZoneTrial.Save(" Predator PHN16-71 ", order), "Predator PHN16-71"));

    [Theory]
    [InlineData("Predator PHN16-71|ZonesFirst", "Predator PHN16-72")]   // settings copied to another laptop
    [InlineData("Predator PH18-71|ZonesFirst", "Predator PH18-71")]     // not a model to try
    [InlineData("Predator PHN16-71|0", "Predator PHN16-71")]
    [InlineData("Predator PHN16-71|Sideways", "Predator PHN16-71")]
    [InlineData("Predator PHN16-71|", "Predator PHN16-71")]
    [InlineData("ZonesFirst", "Predator PHN16-71")]
    [InlineData("", "Predator PHN16-71")]
    [InlineData(null, "Predator PHN16-71")]
    [InlineData("Predator PHN16-71|ZonesFirst", null)]
    public void AnAnswerForSomethingElseDoesNotCount(string? saved, string? model) => Assert.Null(FourZoneTrial.Load(saved, model));

    [Fact]
    public void TheReportSaysWhatHappened()
    {
        var report = FourZoneTrial.Report(" Predator PHN16-71 ", "1.9.0",
        [
            (FourZoneOrder.ZonesFirst, FourZoneTrial.Describe(FourZoneSeen.Nothing), "zones read 1 2 3 4"),
            (FourZoneOrder.StaticFirst, FourZoneTrial.Describe(FourZoneSeen.AllRight), null),
        ]);
        Assert.Contains("RaptorSense 1.9.0", report);
        Assert.Contains("Laptop: Predator PHN16-71", report);
        Assert.Contains("ZonesFirst: nothing changed, or the keys went dark", report);
        Assert.Contains("zones read 1 2 3 4", report);
        Assert.Contains("StaticFirst: red, green, blue, white from left to right", report);
    }

    [Fact]
    public void OnlyLaptopsThatCanBeTriedAreAskedToUpdateTheirHelper()
    {
        Assert.Equal(ThermalProtocol.HelperRevision, ThermalProtocol.RevisionNeeded("Predator PHN16-71"));
        Assert.Equal(4, ThermalProtocol.RevisionNeeded("Predator PH16-71"));
        Assert.Equal(4, ThermalProtocol.RevisionNeeded("Nitro AN515-57"));
        Assert.True(ThermalProtocol.TrialRevision <= ThermalProtocol.HelperRevision);
    }

    [Fact]
    public void TheRequestSurvivesThePipeAndAnOldOneIsNoTest()
    {
        var sent = new ThermalRequest { Command = "keyboard", Keyboard = FourZoneTrial.Test(FourZoneOrder.StaticFirst) };
        var received = JsonSerializer.Deserialize<ThermalRequest>(JsonSerializer.Serialize(sent, ThermalProtocol.Json), ThermalProtocol.Json)!;
        Assert.True(received.Keyboard!.Trial);
        Assert.Equal(FourZoneOrder.StaticFirst, received.Keyboard.Order);

        var old = JsonSerializer.Deserialize<ThermalRequest>("""{"Command":"keyboard","Keyboard":{"Colors":["FF0000"]}}""", ThermalProtocol.Json)!;
        Assert.False(old.Keyboard!.Trial);
        Assert.Equal(FourZoneOrder.ZonesFirst, old.Keyboard.Order);
    }
}
