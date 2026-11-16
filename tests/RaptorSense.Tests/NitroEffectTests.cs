using RaptorSense.Audio;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using Xunit;

namespace RaptorSense.Tests;

public sealed class NitroEffectTests
{
    sealed class FakeAudio(params float[] levels) : IAudioSpectrum
    {
        public float[] Levels = levels;
        public float[] GetBands(int count) => [.. Enumerable.Range(0, count).Select(i => i < Levels.Length ? Levels[i] : 0f)];
        public float Bass => Levels.Length > 0 ? Levels[0] : 0;
        public float Level => Levels.Length > 0 ? Levels.Average() : 0;
        public void Dispose() { }
    }

    static readonly IEffect[] Shared = [Effects.Static, Effects.Painted, Effects.Animation];

    public static TheoryData<string> NitroOnly()
    {
        var data = new TheoryData<string>();
        foreach (var effect in Effects.Nitro.Except(Shared)) data.Add(effect.Id);
        return data;
    }

    static IEffect Fresh(string id) => (IEffect)Activator.CreateInstance(Effects.Find(id)!.GetType())!;

    static int Cell(string label) => KeyMap.Find(label)!.Cell;

    static Rgb[] Zones(Frame frame) => FourZoneLayout.Average(frame.Cells);

    static EffectContext At(double t, IAudioSpectrum? audio = null, IReadOnlyList<int>? presses = null, EffectSettings? settings = null) =>
        new(t, t, 0.1, settings ?? new EffectSettings(), audio, presses ?? []);

    [Fact]
    public void TheNitroHasAtLeastTenEffectsOfItsOwn() => Assert.True(Effects.Nitro.Except(Shared).Count() >= 10);

    [Fact]
    public void NoneOfTheHeliosPerKeyEffectsAreInTheNitroList() =>
        Assert.Empty(Effects.Nitro.Except(Shared).Intersect(Effects.All));

    [Fact]
    public void TheNitroHasNoKeyboardBuiltInEffects() =>
        Assert.DoesNotContain(Effects.Nitro, e => e.Category == EffectCategory.Keyboard);

    [Fact]
    public void EveryCategoryButBuiltInHasNitroEffects()
    {
        foreach (var category in EffectCategory.Order.Where(c => c != EffectCategory.Keyboard))
            Assert.Contains(Effects.Nitro, e => e.Category == category);
    }

    [Fact]
    public void IdsAreUniqueAcrossBothLaptops()
    {
        var ids = Effects.All.Concat(Effects.Nitro).Distinct().Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ProfilesFindNitroEffectsAndEachLaptopHasADefault()
    {
        Assert.IsType<ColourDriftEffect>(Effects.Find("nitro-drift"));
        Assert.Contains(Effects.DefaultFor(LaptopLayout.Nitro), Effects.Nitro);
        Assert.Contains(Effects.DefaultFor(LaptopLayout.Helios), Effects.All);
        Assert.Same(Effects.Nitro, Effects.For(LaptopLayout.Nitro));
        Assert.Same(Effects.All, Effects.For(LaptopLayout.Helios));
    }

    [Theory]
    [MemberData(nameof(NitroOnly))]
    public void EveryNitroEffectGivesEachZoneOneColourAndRunsForAMinute(string id)
    {
        var effect = Fresh(id);
        var audio = new FakeAudio(0.9f, 0.5f, 0.3f, 0.1f);
        foreach (var settings in new[] { new EffectSettings(), new EffectSettings { AutoColor = true, Speed = 4 } })
        {
            bool lit = false;
            for (int i = 0; i < 600; i++)   // a minute at the Nitro's ten updates a second
            {
                audio.Levels[0] = i % 10 < 2 ? 0.95f : 0.05f;   // a beat every second
                IReadOnlyList<int> presses = i % 3 == 0 ? [Cell("G")] : [];
                var frame = new Frame();
                effect.Render(frame, new EffectContext(i * 0.1 * settings.Speed, i * 0.1, 0.1, settings, audio, presses));
                foreach (var key in KeyMap.Keys)
                {
                    int zone = FourZoneLayout.ZoneOf(key);
                    if (zone < 0) continue;
                    var first = KeyMap.Keys.First(k => FourZoneLayout.ZoneOf(k) == zone);
                    Assert.Equal(frame[first.Cell], frame[key.Cell]);   // a zone is one colour, as on the keyboard
                }
                lit |= Zones(frame).Any(z => z != Rgb.Black);
            }
            Assert.True(lit, $"{id} never lit a zone");
        }
    }

    [Theory]
    [MemberData(nameof(NitroOnly))]
    public void OverlaysStayDarkUntilAKeyIsPressed(string id)
    {
        var effect = Fresh(id);
        if (!effect.WorksAsOverlay) return;
        for (int i = 1; i <= 20; i++)
        {
            var frame = new Frame();
            effect.Render(frame, At(i * 0.1));
            Assert.All(Zones(frame), z => Assert.Equal(Rgb.Black, z));
        }
        var pressed = new Frame();
        effect.Render(pressed, At(2.1, presses: [Cell("Q")]));
        Assert.NotEqual(Rgb.Black, Zones(pressed)[0]);
    }

    [Fact]
    public void ZoneFlashLightsOnlyTheZoneTyped()
    {
        var effect = new ZoneFlashEffect();
        var frame = new Frame();
        effect.Render(frame, At(1, presses: [Cell("Enter")]));
        var zones = Zones(frame);
        Assert.NotEqual(Rgb.Black, zones[2]);
        Assert.Equal(Rgb.Black, zones[0]);
        Assert.Equal(Rgb.Black, zones[3]);

        for (int i = 1; i <= 10; i++) { frame = new Frame(); effect.Render(frame, At(1 + i * 0.1)); }
        Assert.All(Zones(frame), z => Assert.Equal(Rgb.Black, z));   // gone after a second
    }

    [Fact]
    public void ARippleReachesTheFarZoneAfterTheNearOne()
    {
        var effect = new TypingRippleEffect();
        var frame = new Frame();
        effect.Render(frame, new EffectContext(1, 1, 0.1, new EffectSettings(), null, [Cell("Q")]));
        var start = Zones(frame);
        Assert.True(start[0].R + start[0].G + start[0].B > start[3].R + start[3].G + start[3].B);

        frame = new Frame();
        effect.Render(frame, new EffectContext(1.36, 1.36, 0.1, new EffectSettings(), null, []));
        var later = Zones(frame);
        Assert.True(later[3].R + later[3].G + later[3].B > later[0].R + later[0].G + later[0].B);
    }

    [Fact]
    public void TypingFasterMakesTheKeyboardHotter()
    {
        var effect = new TypingSpeedEffect();
        var frame = new Frame();
        for (int i = 0; i < 30; i++) effect.Render(frame, At(i * 0.1));
        Assert.True(effect.Intensity < 0.05);

        for (int i = 30; i < 60; i++) effect.Render(frame, At(i * 0.1, presses: [Cell("A")]));   // 10 keys a second
        Assert.True(effect.Intensity > 0.7, $"intensity {effect.Intensity}");

        for (int i = 60; i < 120; i++) effect.Render(frame, At(i * 0.1));
        Assert.True(effect.Intensity < 0.1, "cools down when typing stops");
    }

    [Fact]
    public void ZoneHeatBuildsWhereYouType()
    {
        var effect = new ZoneHeatEffect();
        var frame = new Frame();
        for (int i = 0; i < 10; i++) { frame = new Frame(); effect.Render(frame, At(i * 0.1, presses: [Cell("Num5")])); }
        var zones = Zones(frame);
        Assert.NotEqual(Rgb.Black, zones[3]);
        Assert.Equal(Rgb.Black, zones[0]);
    }

    [Fact]
    public void MusicZonesPutBassOnTheLeft()
    {
        var effect = new MusicZonesEffect();
        var frame = new Frame();
        effect.Render(frame, At(1, new FakeAudio(1f, 0f, 0f, 0f), settings: new EffectSettings { Color = Rgb.White, Color2 = Rgb.White }));
        var zones = Zones(frame);
        Assert.True(zones[0].R > 200);
        Assert.True(zones[3].R < 40);
    }

    [Fact]
    public void MusicEffectsStayDarkWithoutSound()
    {
        foreach (var effect in Effects.Nitro.Where(e => e.UsesAudio))
        {
            var frame = new Frame();
            effect.Render(frame, At(1));
            Assert.All(Zones(frame), z => Assert.Equal(Rgb.Black, z));
        }
    }

    [Fact]
    public void BeatColourChangesColourOnABeat()
    {
        var effect = new BeatColourEffect();
        var audio = new FakeAudio(0f);
        var settings = new EffectSettings { AutoColor = true };
        var frame = new Frame();
        for (int i = 0; i < 20; i++) effect.Render(frame, At(i * 0.1, audio, settings: settings));
        var before = Zones(frame)[0];
        audio.Levels[0] = 1f;
        frame = new Frame();
        effect.Render(frame, At(2.0, audio, settings: settings));
        var after = Zones(frame)[0];
        Assert.NotEqual(before, after);
        Assert.True(after.R + after.G + after.B > before.R + before.G + before.B, "the beat flashes brighter");
    }

    [Fact]
    public void TheScannerVisitsBothEnds()
    {
        var effect = new ScannerEffect();
        int[] brightest = new int[4];
        for (int i = 0; i < 48; i++)
        {
            var frame = new Frame();
            effect.Render(frame, At(i * 0.05));
            var zones = Zones(frame);
            int top = Enumerable.Range(0, 4).OrderByDescending(z => zones[z].R + zones[z].G + zones[z].B).First();
            brightest[top]++;
        }
        Assert.True(brightest[0] > 0 && brightest[3] > 0);
    }

    [Fact]
    public void TheSirenIsRedOnTheLeftAndBlueOnTheRight()
    {
        var effect = new SirenEffect();
        bool red = false, blue = false;
        for (int i = 0; i < 20; i++)
        {
            var frame = new Frame();
            effect.Render(frame, At(i * 0.05));
            var zones = Zones(frame);
            red |= zones[0].R > 200 && zones[0].B < 50;
            blue |= zones[3].B > 200 && zones[3].R < 50;
        }
        Assert.True(red && blue);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.3)]
    [InlineData(0.55)]
    [InlineData(0.999)]
    [InlineData(1.0)]
    public void TheSkyHasAColourAtEveryTimeOfDay(double t)
    {
        var sky = DayAndNightEffect.SkyAt(t);
        Assert.True(sky.R + sky.G + sky.B > 0);
    }
}
