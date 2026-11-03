using RaptorSense.Lighting;
using Xunit;

namespace RaptorSense.Tests;

public sealed class EffectTests
{
    public static TheoryData<string> AllEffects()
    {
        var data = new TheoryData<string>();
        foreach (var effect in Effects.All) data.Add(effect.Id);
        return data;
    }

    // Effects that legitimately stay dark without input (music, typing) or are painted by the user.
    static readonly HashSet<string> DarkWithoutInput = ["spectrum", "pulse", "ripple", "custom"];

    [Theory]
    [MemberData(nameof(AllEffects))]
    public void Every_effect_renders_ten_seconds_at_any_speed_and_colour_mode(string id)
    {
        var effect = Effects.Find(id)!;
        foreach (var (speed, auto) in new[] { (0.1, false), (1.0, false), (4.0, true) })
        {
            var settings = new EffectSettings { Speed = speed, AutoColor = auto };
            var frame = new Frame();
            bool lit = false;
            double time = 0;
            for (int i = 0; i < 600; i++)   // 10 s at 60 fps
            {
                double delta = 1.0 / 60;
                time += delta * speed;
                IReadOnlyList<int> presses = i % 30 == 0 ? [KeyGeometry.Cells[i % KeyGeometry.Cells.Count]] : [];
                frame.Fill(Rgb.Black);
                effect.Render(frame, new EffectContext(time, i * delta, delta, settings, null, presses));
                foreach (int cell in KeyGeometry.Cells) lit |= frame[cell] != Rgb.Black;
            }
            if (!DarkWithoutInput.Contains(id)) Assert.True(lit, $"{id} never lit a key (speed {speed}, auto {auto})");
        }
    }

    [Fact]
    public void Overlay_effects_stay_dark_until_a_key_is_pressed_then_light_up()
    {
        var overlays = Effects.All.Where(e => e.WorksAsOverlay).ToList();
        Assert.True(overlays.Count >= 10);
        foreach (var shared in overlays)
        {
            var effect = (IEffect)Activator.CreateInstance(shared.GetType())!;   // fresh state
            var frame = new Frame();
            var settings = new EffectSettings();
            for (int i = 1; i <= 60; i++)
            {
                frame.Fill(Rgb.Black);
                effect.Render(frame, new EffectContext(i / 60.0, i / 60.0, 1 / 60.0, settings, null, []));
                Assert.All(KeyGeometry.Cells, cell => Assert.Equal(Rgb.Black, frame[cell]));
            }

            int pressed = KeyMapCell("G");
            frame.Fill(Rgb.Black);
            effect.Render(frame, new EffectContext(1.05, 1.05, 1 / 60.0, settings, null, [pressed]));
            // A press must show within 0.1 s (6 frames): any later and typing feels laggy.
            for (int i = 0; i < 6 && KeyGeometry.Cells.All(c => frame[c] == Rgb.Black); i++)
            {
                frame.Fill(Rgb.Black);
                effect.Render(frame, new EffectContext(1.07 + i / 60.0, 1.07 + i / 60.0, 1 / 60.0, settings, null, []));
            }
            Assert.Contains(KeyGeometry.Cells, cell => frame[cell] != Rgb.Black);
        }
    }

    [Theory]
    [InlineData("00AEC7")]
    [InlineData("FF0080")]
    [InlineData("FFFFFF")]
    [InlineData("000000")]
    [InlineData("7F3A12")]
    public void Colours_survive_a_round_trip_through_hsv(string hex)
    {
        var color = Rgb.Parse(hex);
        var (h, s, v) = color.ToHsv();
        var back = Rgb.FromHsv(h, s, v);
        Assert.InRange(Math.Abs(back.R - color.R), 0, 1);
        Assert.InRange(Math.Abs(back.G - color.G), 0, 1);
        Assert.InRange(Math.Abs(back.B - color.B), 0, 1);
    }

    static int KeyMapCell(string label) => RaptorSense.Layout.KeyMap.Find(label)!.Cell;

    [Fact]
    public void Effect_ids_are_unique()
    {
        var ids = Effects.All.Select(e => e.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Every_effect_has_a_known_category()
    {
        foreach (var effect in Effects.All) Assert.Contains(effect.Category, EffectCategory.Order);
    }

    [Fact]
    public void Every_keyboard_effect_has_a_preview()
    {
        var frame = new Frame();
        foreach (var effect in Effects.All.OfType<KeyboardEffect>())
        {
            frame.Fill(Rgb.Black);
            for (int i = 0; i < 120; i++)
                effect.Render(frame, new EffectContext(i / 60.0, i / 60.0, 1 / 60.0, new EffectSettings(), null, []));
            Assert.Contains(KeyGeometry.Cells, cell => frame[cell] != Rgb.Black);
        }
    }
}
