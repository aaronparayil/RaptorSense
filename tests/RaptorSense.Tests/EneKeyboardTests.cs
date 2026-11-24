using System.ComponentModel;
using RaptorSense.Hardware;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Thermal;
using Xunit;

namespace RaptorSense.Tests;

// The layouts and device answers other projects published (docs/protocol.md): RaptorSense has no such laptop to ask.
public sealed class EneKeyboardTests
{
    // The target list in predator-sense's tests, and the AN16S-61's own description of its keyboard (issue 31 there).
    static readonly byte[] Targets = [0xA1, 3, 0x65, 0x21, 0x83, 0, 0, 0, 0, 0, 0];
    static readonly byte[] KeyboardCaps = [0xA3, 0x21, 0x01, 0x09, 0x04, 0xFB, 0x07, 0x00, 0x00];

    sealed class FakeController : IEneReports
    {
        public byte[] TargetList = Targets, Caps = KeyboardCaps;
        public readonly List<byte[]> Sent = [];
        public bool Broken, Disposed;

        /// <summary>The reports that change the lights.</summary>
        public List<byte[]> Lighting => Sent.Where(r => r[0] == EneProtocol.LightingReport).ToList();

        public void Set(ReadOnlySpan<byte> report)
        {
            if (Broken) throw new Win32Exception(31);
            Sent.Add(report.ToArray());
        }

        public byte[] Get(byte reportId, int length) => reportId switch
        {
            EneProtocol.TargetListReport => TargetList,
            EneProtocol.CapabilitiesReport => Caps,
            _ => throw new Win32Exception(87),
        };

        public void Dispose() => Disposed = true;
    }

    static EneKeyboard Open(FakeController controller, string? model = "Predator PHN16S-71") =>
        EneKeyboard.TryOpen(() => controller, model) ?? throw new InvalidOperationException("not recognised");

    [Fact]
    public void ReadsTheTargetList() => Assert.Equal([0x65, 0x21, 0x83], EneProtocol.ParseTargets(Targets));

    [Theory]
    [InlineData(new byte[] { 0xA1, 10, 0x21, 0x83 })]   // says more than it holds
    [InlineData(new byte[] { 0xA3, 1, 0x21 })]          // another report
    [InlineData(new byte[] { 0xA1 })]
    [InlineData(new byte[0])]
    public void ABadTargetListHasNoTargets(byte[] report) => Assert.Empty(EneProtocol.ParseTargets(report));

    [Fact]
    public void ReadsTheCapabilities()
    {
        var caps = EneProtocol.ParseCapabilities(EneProtocol.Keyboard, KeyboardCaps);
        Assert.Equal(new EneCapabilities(0x21, 4, 0x7FB), caps);
        Assert.True(caps!.Value.Supports(0x02));
        Assert.False(caps.Value.Supports(0x03));
        Assert.False(caps.Value.Supports(0x00));
        Assert.False(caps.Value.Supports(0x21));
    }

    [Theory]
    [InlineData(new byte[] { 0xA3, 0x83, 1, 5, 1, 0x3B, 0, 0, 0 })]   // the cover logo's
    [InlineData(new byte[] { 0xA3, 0x21, 1, 9, 0, 0xFB, 7, 0, 0 })]   // no zones
    [InlineData(new byte[] { 0xA3, 0x21, 1, 9, 4, 0xFB })]            // too short
    public void CapabilitiesOfSomethingElseAreRefused(byte[] report) =>
        Assert.Null(EneProtocol.ParseCapabilities(EneProtocol.Keyboard, report));

    // kbrgb's layout: A4 21 02 brightness speed direction R G B mask 00.
    [Fact]
    public void StaticColourHasTheDocumentedBytes() =>
        Assert.Equal([0xA4, 0x21, 0x02, 100, 0, 0, 0x12, 0x34, 0x56, 0x04, 0], EneProtocol.Static(0x04, new Rgb(0x12, 0x34, 0x56), 100));

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(40, 40)]
    [InlineData(255, 100)]   // above 100 has never been tried on the real thing
    public void BrightnessStaysWithinAHundred(int asked, byte sent) => Assert.Equal(sent, EneProtocol.Static(1, Rgb.White, asked)[3]);

    [Theory]
    [InlineData(FourZoneMode.Breathing, 0x04)]
    [InlineData(FourZoneMode.Neon, 0x05)]
    [InlineData(FourZoneMode.Wave, 0x07)]
    [InlineData(FourZoneMode.Shifting, 0x0A)]
    [InlineData(FourZoneMode.Zoom, 0x09)]
    public void EffectsUseTheKnownCodes(FourZoneMode mode, byte code)
    {
        var report = EneProtocol.Effect(mode, Rgb.White, 5, 100, FourZoneDirection.RightToLeft, 0);
        Assert.Equal(EneProtocol.LightingLength, report.Length);
        Assert.Equal(code, report[2]);
        Assert.Equal(EneProtocol.AllZones, report[9]);
    }

    // 1 and 3 turn the lights off, 6 and 8 glitch, 12 freezes them.
    [Fact]
    public void NoModeThatMisbehavesIsEverSent() =>
        Assert.Empty(Enum.GetValues<FourZoneMode>().Select(EneProtocol.ModeCode).Intersect(new byte[] { 1, 3, 6, 8, 12 }));

    [Theory]
    [InlineData(FourZoneDirection.LeftToRight, 1)]
    [InlineData(FourZoneDirection.RightToLeft, 2)]
    public void TheWaveGoesTheWayAsked(FourZoneDirection direction, byte flag) =>
        Assert.Equal(flag, EneProtocol.Effect(FourZoneMode.Wave, Rgb.White, 5, 100, direction, 0)[5]);

    [Theory]
    [InlineData("Predator PHN16S-71", 0)]
    [InlineData("Predator PHN16S-71/Macan_ARX", 0)]
    [InlineData("Nitro AN16S-61", 2)]
    [InlineData("Predator PHN16S-71X", 2)]
    [InlineData(null, 2)]
    public void BreathingUsesTheByteConfirmedOnThatModel(string? model, byte flag) => Assert.Equal(flag, EneProtocol.PlainFlag(model));

    [Fact]
    public void FindingTheKeyboardChangesNoLights()
    {
        var controller = new FakeController();
        using var keyboard = Open(controller);
        Assert.Empty(controller.Lighting);
        Assert.Equal([0xA2, 0x21], Assert.Single(controller.Sent));
        Assert.True(keyboard.Supports(FourZoneMode.Wave));
    }

    [Fact]
    public void NoKeyboardAmongTheTargetsMeansNothingIsSent()
    {
        var controller = new FakeController { TargetList = [0xA1, 1, 0x83, 0, 0, 0, 0, 0, 0, 0, 0] };
        Assert.Null(EneKeyboard.TryOpen(() => controller, null));
        Assert.Empty(controller.Sent);
        Assert.True(controller.Disposed);
    }

    [Theory]
    [InlineData(1, 0xFB)]     // one zone: not a layout RaptorSense knows
    [InlineData(4, 0xF9)]     // four zones but no static colours
    public void AnUnexpectedKeyboardIsLeftAlone(byte zones, byte modes)
    {
        var controller = new FakeController { Caps = [0xA3, 0x21, 1, 9, zones, modes, 7, 0, 0] };
        Assert.Null(EneKeyboard.TryOpen(() => controller, null));
        Assert.Empty(controller.Lighting);
        Assert.True(controller.Disposed);
    }

    [Fact]
    public void ADeviceThatDoesNotAnswerIsLeftAlone()   // as the PHN16-72's 1025174B:6243 refuses A1
    {
        var controller = new FakeController { Broken = true };
        Assert.Null(EneKeyboard.TryOpen(() => controller, null));
        Assert.True(controller.Disposed);
    }

    [Fact]
    public void NoControllerIsNoKeyboard() => Assert.Null(EneKeyboard.TryOpen(() => null, null));

    [Fact]
    public void OneColourGoesInOneReport()
    {
        var controller = new FakeController();
        using var keyboard = Open(controller);
        new FourZoneRequest { Colors = ["FF0000"], Brightness = 60 }.ApplyTo(keyboard);
        Assert.Equal(EneProtocol.Static(0x0F, new Rgb(255, 0, 0), 60), Assert.Single(controller.Lighting));
    }

    [Fact]
    public void FourColoursGoToTheirZonesLeftToRight()
    {
        var controller = new FakeController();
        using var keyboard = Open(controller);
        new FourZoneRequest { Colors = ["FF0000", "00FF00", "0000FF", "FFFFFF"] }.ApplyTo(keyboard);
        Assert.Equal([1, 2, 4, 8], controller.Lighting.Select(r => (int)r[9]));
        Assert.Equal(["FF0000", "00FF00", "0000FF", "FFFFFF"], controller.Lighting.Select(r => new Rgb(r[6], r[7], r[8]).ToString()));
    }

    [Fact]
    public void AnEffectTheKeyboardDoesNotListIsNotSent()
    {
        var controller = new FakeController { Caps = [0xA3, 0x21, 1, 9, 4, 0x1A, 0, 0, 0] };   // static, breathing and neon only
        using var keyboard = Open(controller);
        Assert.Throws<NotSupportedException>(() => keyboard.SetEffect(FourZoneMode.Wave, Rgb.White, 5, 100, FourZoneDirection.LeftToRight));
        Assert.Empty(controller.Lighting);
    }

    [Fact]
    public void AfterAFailureTheControllerIsOpenedAgain()   // the laptop slept, say
    {
        var first = new FakeController();
        var second = new FakeController();
        var controllers = new Queue<FakeController>([first, second]);
        using var keyboard = EneKeyboard.TryOpen(() => controllers.Dequeue(), null)!;
        first.Broken = true;
        Assert.Throws<Win32Exception>(() => keyboard.SetZones([Rgb.White, Rgb.White, Rgb.White, Rgb.White], 100));
        Assert.True(first.Disposed);
        keyboard.SetZones([Rgb.White, Rgb.White, Rgb.White, Rgb.White], 100);
        Assert.Single(second.Lighting);
    }

    [Fact]
    public void ADisposedKeyboardSendsNothing()
    {
        var controller = new FakeController();
        var keyboard = Open(controller);
        keyboard.Dispose();
        Assert.Throws<ObjectDisposedException>(() => keyboard.SetZones([Rgb.White, Rgb.White, Rgb.White, Rgb.White], 100));
        Assert.Empty(controller.Lighting);
    }

    [Theory]
    [InlineData("Predator PHN16S-71", true, LaptopLayout.FourZones)]
    [InlineData(null, true, LaptopLayout.FourZones)]
    [InlineData("Predator PHN16S-71", false, LaptopLayout.Helios)]
    [InlineData("Predator PH16-71", true, LaptopLayout.Helios)]   // a model known to light another way keeps its own
    [InlineData("Nitro AN515-57", true, LaptopLayout.Nitro)]
    public void TheLayoutFollowsTheKeyboardFound(string? model, bool ene, LaptopLayout expected) => Assert.Equal(expected, LaptopLayouts.For(model, ene));

    [Fact]
    public void TheFourZoneLayoutIsNamedAfterTheLaptop()
    {
        Assert.Equal("Predator", LaptopLayouts.ShortName(LaptopLayout.FourZones, "Predator PHN16S-71"));
        Assert.Equal("Nitro", LaptopLayouts.ShortName(LaptopLayout.FourZones, "Nitro ANV16S-41"));
        Assert.Null(LaptopLayouts.CapsFor(LaptopLayout.FourZones, "Predator PHN16S-71"));
        Assert.Same(LaptopLayouts.NitroCaps, LaptopLayouts.CapsFor(LaptopLayout.FourZones, "Nitro ANV16S-41"));
        Assert.Contains("4 lighting zones", LaptopLayouts.Describe(LaptopLayout.FourZones, "Predator PHN16S-71"));
        Assert.Same(Effects.Nitro, Effects.For(LaptopLayout.FourZones));
        Assert.False(LaptopModels.HasFanControl("Predator PHN16S-71"));   // lighting only: the fans stay read-only
    }
}
