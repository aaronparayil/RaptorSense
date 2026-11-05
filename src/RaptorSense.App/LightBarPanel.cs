using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RaptorSense.Hardware;
using RaptorSense.Lighting;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>
/// The rear light bar: a colour for each of its three zones, or one of its built-in animations.
/// Changes go through the elevated helper, and only when Apply is pressed: one change takes a third of a second or so,
/// too slow to follow a colour wheel being dragged.
/// </summary>
sealed class LightBarPanel : UserControl
{
    static readonly string[] ZoneNames = ["Left", "Centre", "Right"];

    readonly Rgb[] zones = [Rgb.PredatorTeal, Rgb.PredatorTeal, Rgb.PredatorTeal];
    readonly ColorEditor editor;
    readonly ComboBox mode = new() { ItemsSource = new[] { "Static" }.Concat(Enum.GetNames<LightBarMode>()).ToList(), SelectedIndex = 0, MinWidth = 160 };
    readonly Slider brightness = new() { Minimum = 0, Maximum = LightBarProtocol.MaxBrightness, Value = LightBarProtocol.MaxBrightness };
    readonly Slider speed = new() { Minimum = LightBarProtocol.MinSpeed, Maximum = LightBarProtocol.MaxSpeed, Value = LightBarProtocol.DefaultSpeed, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly TextBlock brightnessText = new(), speedText = new();
    readonly TextBlock animationNote = new()
    {
        Text = "Animations use the Left colour for the whole bar.", Opacity = 0.6, FontSize = 12, TextWrapping = TextWrapping.Wrap,
    };
    readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    readonly Button apply = new() { Content = "Apply" };
    readonly Button off = new() { Content = "Off" };
    readonly Button copyToAll = new() { Content = "Use this colour on all zones" };
    bool busy;

    public LightBarPanel()
    {
        editor = new ColorEditor(ZoneNames, zones, withEyedropper: false);
        editor.ColorChanged += (i, color) => zones[i] = color;

        copyToAll.Click += (_, _) =>
        {
            Array.Fill(zones, editor.CurrentColor);
            editor.SetColors(zones);
        };
        mode.SelectionChanged += (_, _) => UpdateEnabled();
        brightness.ValueChanged += (_, e) => brightnessText.Text = $"Brightness {e.NewValue:N0}%";
        speed.ValueChanged += (_, e) => speedText.Text = $"Animation speed {e.NewValue:N0}";
        brightnessText.Text = $"Brightness {brightness.Value:N0}%";
        speedText.Text = $"Animation speed {speed.Value:N0}";

        apply.Click += async (_, _) => await Apply();
        off.Click += async (_, _) => await Send(new LightBarRequest { Colors = [Rgb.Black.ToString()], Brightness = (int)brightness.Value });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, off } };
        var page = new StackPanel { Spacing = 12, Margin = new Thickness(24), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(new TextBlock
        {
            Text = "The rear light bar goes through the RaptorSense helper (see README: Fans and power modes). Pick a colour for each zone, then Apply.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        page.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { new TextBlock { Text = "Mode", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 }, mode },
        });
        page.Children.Add(editor);
        page.Children.Add(copyToAll);
        page.Children.Add(animationNote);
        page.Children.Add(brightnessText);
        page.Children.Add(brightness);
        page.Children.Add(speedText);
        page.Children.Add(speed);
        page.Children.Add(buttons);
        page.Children.Add(result);
        Content = new ScrollViewer { Content = page };

        UpdateEnabled();
    }

    void UpdateEnabled()
    {
        bool animated = mode.SelectedIndex > 0;
        speed.IsEnabled = animated;
        animationNote.IsVisible = animated;
        copyToAll.IsEnabled = !animated;
    }

    Task Apply() => Send(mode.SelectedIndex > 0
        ? new LightBarRequest
        {
            Colors = [zones[0].ToString()],
            Mode = Enum.Parse<LightBarMode>((string)mode.SelectedItem!),
            Speed = (int)speed.Value,
            Brightness = (int)brightness.Value,
        }
        : new LightBarRequest { Colors = zones.Select(z => z.ToString()).ToList(), Brightness = (int)brightness.Value });

    async Task Send(LightBarRequest request)
    {
        if (busy) return;
        busy = true;
        apply.IsEnabled = off.IsEnabled = false;
        result.Text = "Sending…";
        try
        {
            var status = await ThermalClient.SetLightBarAsync(request);
            result.Text = status.Ok ? "Light bar updated." : status.Error ?? ThermalClient.HelperMissing;
        }
        finally
        {
            busy = false;
            apply.IsEnabled = off.IsEnabled = true;
        }
    }
}
