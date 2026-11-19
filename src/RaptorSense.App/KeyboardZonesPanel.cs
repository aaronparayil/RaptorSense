using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RaptorSense.Hardware;
using RaptorSense.Lighting;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>
/// The 4-zone keyboard of a Nitro 5 AN515-57: a colour for each zone, or one of the keyboard's own effects.
/// Changes go through the elevated helper, and only when Apply is pressed, like the light bar's.
/// </summary>
sealed class KeyboardZonesPanel : UserControl
{
    static readonly string[] ZoneNames = [L("Zone {0}", 1), L("Zone {0}", 2), L("Zone {0}", 3), L("Zone {0}", 4)];   // left to right
    static readonly FourZoneMode[] Modes = Enum.GetValues<FourZoneMode>();

    readonly Rgb[] zones = [Rgb.PredatorTeal, Rgb.PredatorTeal, Rgb.PredatorTeal, Rgb.PredatorTeal];
    readonly ColorEditor editor;
    readonly ComboBox mode = new() { ItemsSource = Modes.Select(m => L(m.ToString())).ToList(), SelectedIndex = 0, MinWidth = 160 };
    readonly ComboBox direction = new() { ItemsSource = new[] { L("Right to left"), L("Left to right") }, SelectedIndex = 0, MinWidth = 160 };
    readonly Slider brightness = new() { Minimum = 0, Maximum = FourZoneProtocol.MaxBrightness, Value = FourZoneProtocol.MaxBrightness };
    readonly Slider speed = new() { Minimum = FourZoneProtocol.MinSpeed, Maximum = FourZoneProtocol.MaxSpeed, Value = FourZoneProtocol.DefaultSpeed, TickFrequency = 1, IsSnapToTickEnabled = true };
    readonly TextBlock brightnessText = new(), speedText = new();
    readonly StackPanel directionRow;
    readonly TextBlock effectNote = new()
    {
        Text = L("Effects use Zone 1's colour; some, like Neon and Wave, pick their own colours."),
        Foreground = Look.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap,
    };
    readonly TextBlock result = new() { TextWrapping = TextWrapping.Wrap, Foreground = Look.Muted };
    readonly Button apply = new() { Content = L("Apply") };
    readonly Button off = new() { Content = L("Off") };
    readonly Button copyToAll = new() { Content = L("Use this colour on all zones") };
    bool busy;

    /// <summary>Raised after the keyboard took a change from this tab, so the Lighting tab's effects stop overwriting it.</summary>
    public event Action? Applied;

    public KeyboardZonesPanel()
    {
        editor = new ColorEditor(ZoneNames, zones, withEyedropper: false);
        editor.ColorChanged += (i, color) => zones[i] = color;

        copyToAll.Click += (_, _) =>
        {
            Array.Fill(zones, editor.CurrentColor);
            editor.SetColors(zones);
        };
        mode.SelectionChanged += (_, _) => UpdateEnabled();
        brightness.ValueChanged += (_, e) => brightnessText.Text = L("Brightness {0:N0}%", e.NewValue);
        speed.ValueChanged += (_, e) => speedText.Text = L("Effect speed {0:N0}", e.NewValue);
        brightnessText.Text = L("Brightness {0:N0}%", brightness.Value);
        speedText.Text = L("Effect speed {0:N0}", speed.Value);

        apply.Click += async (_, _) => await Send(Request());
        off.Click += async (_, _) => await Send(new FourZoneRequest { Colors = [Rgb.Black.ToString()], Brightness = (int)brightness.Value });

        directionRow = Row(L("Direction"), direction);

        var colours = new StackPanel { Spacing = 12 };
        colours.Children.Add(editor);
        colours.Children.Add(copyToAll);

        var settings = new StackPanel { Spacing = 10 };
        settings.Children.Add(Row(L("Mode"), mode));
        settings.Children.Add(effectNote);
        settings.Children.Add(directionRow);
        settings.Children.Add(brightnessText);
        settings.Children.Add(brightness);
        settings.Children.Add(speedText);
        settings.Children.Add(speed);
        settings.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { apply, off } });
        settings.Children.Add(result);
        var settingsCard = Look.Card(settings, L("Mode and brightness"));
        settingsCard.Margin = new Thickness(16, 0, 0, 0);
        settingsCard.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(settingsCard, 1);

        var page = new StackPanel { Spacing = 14, Margin = new Thickness(28, 12, 28, 16), MaxWidth = 900 };
        page.Children.Add(new TextBlock
        {
            Text = L("This laptop's keyboard lights in four zones, left to right. It goes through the RaptorSense helper (see README: Fans and power modes). Pick a colour for each zone, then Apply."),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Look.Muted,
            Margin = new Thickness(4, 0, 0, 0),
        });
        page.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { Look.Card(colours, L("Zone colours")), settingsCard } });
        Content = new ScrollViewer { Content = page };

        UpdateEnabled();
    }

    static StackPanel Row(string label, Control control) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Foreground = Look.Muted, MinWidth = 64 }, control },
    };

    FourZoneMode SelectedMode => Modes[Math.Max(mode.SelectedIndex, 0)];

    void UpdateEnabled()
    {
        bool effect = SelectedMode != FourZoneMode.Static;
        speed.IsEnabled = effect;
        effectNote.IsVisible = effect;
        directionRow.IsVisible = SelectedMode == FourZoneMode.Wave;
        copyToAll.IsEnabled = !effect;
    }

    FourZoneRequest Request() => SelectedMode == FourZoneMode.Static
        ? new FourZoneRequest { Colors = zones.Select(z => z.ToString()).ToList(), Brightness = (int)brightness.Value }
        : new FourZoneRequest
        {
            Colors = [zones[0].ToString()],
            Mode = SelectedMode,
            Speed = (int)speed.Value,
            Brightness = (int)brightness.Value,
            Direction = direction.SelectedIndex == 1 ? FourZoneDirection.LeftToRight : FourZoneDirection.RightToLeft,
        };

    async Task Send(FourZoneRequest request)
    {
        if (busy) return;
        busy = true;
        apply.IsEnabled = off.IsEnabled = false;
        result.Text = L("Sending…");
        try
        {
            var status = await ThermalClient.SetKeyboardAsync(request);
            if (status.Ok) Applied?.Invoke();
            result.Text = status.Ok ? L("Keyboard updated.")
                : status.Reading is not null && status.HelperRevision < ThermalProtocol.FourZoneRevision
                    ? L("The helper installed is from an older RaptorSense that can't light this keyboard. Update it on the Performance tab.")
                    : status.Error ?? L(ThermalClient.HelperMissing);
        }
        finally
        {
            busy = false;
            apply.IsEnabled = off.IsEnabled = true;
        }
    }
}
