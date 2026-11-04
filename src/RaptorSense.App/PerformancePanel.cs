using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>Temperatures, fan speeds, power profile and fan control, through the elevated helper.</summary>
sealed class PerformancePanel : UserControl
{
    static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0, 174, 199));
    static readonly IBrush TileBackground = new SolidColorBrush(Color.FromRgb(28, 30, 36));

    readonly TextBlock helperState = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    readonly TextBlock cpuTemp = Value(), gpuTemp = Value(), cpuFan = Value(), gpuFan = Value();
    readonly TextBlock systemTemp = new() { Opacity = 0.6 };
    readonly WrapPanel profiles = new();
    readonly RadioButton auto = new() { Content = "Auto", GroupName = "fans" };
    readonly RadioButton max = new() { Content = "Max", GroupName = "fans" };
    readonly RadioButton custom = new() { Content = "Custom", GroupName = "fans" };
    readonly Slider cpuSpeed = SpeedSlider(), gpuSpeed = SpeedSlider();
    readonly TextBlock cpuSpeedText = new(), gpuSpeedText = new();
    readonly Button apply = new() { Content = "Apply custom speeds" };
    readonly TextBlock safety = new() { Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    readonly StackPanel controls = new() { Spacing = 10 };
    readonly DispatcherTimer timer;
    IReadOnlyList<PowerProfile> shownProfiles = [];
    bool busy, slidersLoaded, choosingCustom;

    public PerformancePanel()
    {
        var tiles = new UniformGrid { Columns = 4 };
        tiles.Children.Add(Tile("CPU", cpuTemp));
        tiles.Children.Add(Tile("GPU", gpuTemp));
        tiles.Children.Add(Tile("CPU fan", cpuFan));
        tiles.Children.Add(Tile("GPU fan", gpuFan));

        auto.IsCheckedChanged += (_, _) => { if (auto.IsChecked == true && !updating) { choosingCustom = false; _ = SendFans(FanMode.Auto); } };
        max.IsCheckedChanged += (_, _) => { if (max.IsChecked == true && !updating) { choosingCustom = false; _ = SendFans(FanMode.Max); } };
        custom.IsCheckedChanged += (_, _) => { if (custom.IsChecked == true && !updating) choosingCustom = true; UpdateCustomEnabled(); };
        cpuSpeed.ValueChanged += (_, e) => cpuSpeedText.Text = $"CPU fan {e.NewValue:N0}%";
        gpuSpeed.ValueChanged += (_, e) => gpuSpeedText.Text = $"GPU fan {e.NewValue:N0}%";
        apply.Click += async (_, _) => { await SendFans(FanMode.Custom); choosingCustom = false; };

        controls.Children.Add(Heading("Power profile"));
        controls.Children.Add(profiles);
        controls.Children.Add(Heading("Fans"));
        controls.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { auto, max, custom } });
        controls.Children.Add(cpuSpeedText);
        controls.Children.Add(cpuSpeed);
        controls.Children.Add(gpuSpeedText);
        controls.Children.Add(gpuSpeed);
        controls.Children.Add(apply);
        controls.Children.Add(new TextBlock
        {
            Text = $"Custom speeds are at least {FanController.MinCustomPercent}%. At {FanController.HotC} °C the fans go to Max until " +
                   $"everything is below {FanController.CoolC} °C, and they return to Auto when the helper stops.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.6,
            FontSize = 12,
        });
        controls.Children.Add(safety);

        var page = new StackPanel { Spacing = 14, Margin = new Thickness(24), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };
        page.Children.Add(helperState);
        page.Children.Add(tiles);
        page.Children.Add(systemTemp);
        page.Children.Add(controls);
        Content = new ScrollViewer { Content = page };

        controls.IsEnabled = false;
        UpdateCustomEnabled();
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) => await Refresh());
        timer.Start();
    }

    bool updating;

    async Task Refresh()
    {
        // Only talk to the helper while this tab is on screen.
        if (busy || !IsEffectivelyVisible) return;
        busy = true;
        try { Show(await ThermalClient.StatusAsync()); }
        finally { busy = false; }
    }

    async Task SendFans(FanMode mode)
    {
        busy = true;
        try { Show(await ThermalClient.SetFansAsync(mode, (int)cpuSpeed.Value, (int)gpuSpeed.Value)); }
        finally { busy = false; }
    }

    async Task SendProfile(PowerProfile profile)
    {
        busy = true;
        try { Show(await ThermalClient.SetProfileAsync(profile)); }
        finally { busy = false; }
    }

    void Show(ThermalStatus status)
    {
        bool connected = status.Reading is not null;
        controls.IsEnabled = connected;
        helperState.Text = connected
            ? status.Ok ? "Connected to the RaptorSense helper." : $"The helper refused: {status.Error}"
            : status.Error ?? ThermalClient.HelperMissing;
        if (!connected) return;

        var r = status.Reading!.Value;
        cpuTemp.Text = $"{r.CpuTemp} °C";
        gpuTemp.Text = r.GpuTemp > 0 ? $"{r.GpuTemp} °C" : "asleep";
        cpuFan.Text = $"{r.CpuFanRpm} rpm";
        gpuFan.Text = $"{r.GpuFanRpm} rpm";
        systemTemp.Text = $"System {r.SystemTemp} °C";

        if (!shownProfiles.SequenceEqual(status.SupportedProfiles)) BuildProfileButtons(status.SupportedProfiles);
        foreach (var button in profiles.Children.OfType<Button>())
            button.Background = (PowerProfile)button.Tag! == status.Profile ? Accent : null;

        if (!slidersLoaded)
        {
            cpuSpeed.Value = status.CpuPercent;
            gpuSpeed.Value = status.GpuPercent;
            slidersLoaded = true;
        }
        if (!choosingCustom)
        {
            updating = true;
            (status.FanMode switch { FanMode.Max => max, FanMode.Custom => custom, _ => auto }).IsChecked = true;
            updating = false;
        }
        UpdateCustomEnabled();

        safety.IsVisible = status.SafetyOverride;
        safety.Text = $"Safety: it reached {FanController.HotC} °C, so the fans are on Max until it is below {FanController.CoolC} °C.";
    }

    void BuildProfileButtons(IReadOnlyList<PowerProfile> supported)
    {
        profiles.Children.Clear();
        foreach (var profile in supported)
        {
            var button = new Button { Content = profile.ToString(), Tag = profile, Margin = new Thickness(0, 0, 8, 8), MinWidth = 110 };
            button.Click += async (_, _) => await SendProfile(profile);
            profiles.Children.Add(button);
        }
        shownProfiles = supported;
    }

    void UpdateCustomEnabled() => cpuSpeed.IsEnabled = gpuSpeed.IsEnabled = apply.IsEnabled = custom.IsChecked == true;

    static Slider SpeedSlider() => new() { Minimum = FanController.MinCustomPercent, Maximum = 100, Value = 50, TickFrequency = 5, IsSnapToTickEnabled = true };

    static TextBlock Value() => new() { FontSize = 28, FontWeight = FontWeight.SemiBold, Text = "-" };

    static Control Tile(string title, TextBlock value) => new Border
    {
        Background = TileBackground,
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16, 12),
        Margin = new Thickness(0, 0, 12, 0),
        Child = new StackPanel { Children = { new TextBlock { Text = title, Opacity = 0.7 }, value } },
    };

    static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 8, 0, 0) };
}
