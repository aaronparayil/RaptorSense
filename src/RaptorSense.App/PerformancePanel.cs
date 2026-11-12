using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RaptorSense.Hardware;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>Temperatures, fan speeds, power profile and fan control through the elevated helper, and battery wear (no helper needed).</summary>
sealed class PerformancePanel : UserControl
{
    readonly TextBlock helperState = new() { TextWrapping = TextWrapping.Wrap, Foreground = Look.Muted, Margin = new Thickness(4, 0, 0, 0) };
    readonly TextBlock cpuTemp = Value(), gpuTemp = Value(), cpuFan = Value(), gpuFan = Value();
    readonly TextBlock systemTemp = new() { Foreground = Look.Muted, Margin = new Thickness(4, 0, 0, 0) };
    readonly WrapPanel profiles = new();
    readonly RadioButton auto = new() { Content = "Auto", GroupName = "fans" };
    readonly RadioButton max = new() { Content = "Max", GroupName = "fans" };
    readonly RadioButton custom = new() { Content = "Custom", GroupName = "fans" };
    readonly Slider cpuSpeed = SpeedSlider(), gpuSpeed = SpeedSlider();
    readonly TextBlock cpuSpeedText = new(), gpuSpeedText = new();
    readonly Button apply = new() { Content = "Apply custom speeds" };
    readonly TextBlock safety = new() { Foreground = Look.Warning, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    readonly StackPanel controls = new() { Spacing = 16 };
    readonly TextBlock batteryCycles = Value(), batteryHealth = Value(), batteryNow = Value(), batteryNew = Value();
    readonly StackPanel batteryContent = new() { Spacing = 12 };
    readonly Border batterySection;
    readonly DispatcherTimer timer;
    IReadOnlyList<PowerProfile> shownProfiles = [];
    bool busy, slidersLoaded, choosingCustom;

    public PerformancePanel()
    {
        var tiles = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, -12, -12) };   // takes back the last tile's gap
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

        var fans = new StackPanel { Spacing = 10 };
        fans.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { auto, max, custom } });
        fans.Children.Add(cpuSpeedText);
        fans.Children.Add(cpuSpeed);
        fans.Children.Add(gpuSpeedText);
        fans.Children.Add(gpuSpeed);
        fans.Children.Add(apply);
        fans.Children.Add(Look.Note(
            $"Custom speeds are at least {FanController.MinCustomPercent}%. At {FanController.HotC} °C the fans go to Max until " +
            $"everything is below {FanController.CoolC} °C, and they return to Auto when the helper stops."));
        fans.Children.Add(safety);
        controls.Children.Add(Look.Card(profiles, "Power profile"));
        controls.Children.Add(Look.Card(fans, "Fans"));

        var batteryTiles = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, -12, -12) };
        batteryTiles.Children.Add(Tile("Charge cycles", batteryCycles));
        batteryTiles.Children.Add(Tile("Health", batteryHealth));
        batteryTiles.Children.Add(Tile("Full charge now", batteryNow));
        batteryTiles.Children.Add(Tile("Full charge when new", batteryNew));
        batteryContent.Children.Add(batteryTiles);
        batteryContent.Children.Add(Look.Note("Health compares what a full charge holds now with what it held when new."));
        batterySection = Look.Card(batteryContent, "Battery");
        batterySection.IsVisible = false;
        batterySection.Margin = new Thickness(16, 0, 0, 0);
        batterySection.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(batterySection, 1);

        // Readings across the top; what you can change on the left, the battery on the right.
        var page = new StackPanel { Spacing = 14, Margin = new Thickness(28, 12, 28, 16), MaxWidth = 1180 };
        page.Children.Add(helperState);
        page.Children.Add(tiles);
        page.Children.Add(systemTemp);
        page.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("1.4*,*"), Children = { controls, batterySection } });
        Content = new ScrollViewer { Content = page };

        // These numbers change over days, so reading them when the tab opens is enough.
        AttachedToVisualTree += (_, _) => ShowBattery();

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
            Look.SetSelected(button, (PowerProfile)button.Tag! == status.Profile);

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

    void ShowBattery()
    {
        var battery = WindowsBattery.TryRead();
        batterySection.IsVisible = battery is not null;
        if (battery is not { } b) return;

        batteryCycles.Text = b.CycleCount?.ToString() ?? "-";
        batteryHealth.Text = b.HealthPercent is { } health ? $"{health}%" : "-";
        batteryNow.Text = b.FullChargeWattHours is { } now ? $"{now:N1} Wh" : "-";
        batteryNew.Text = b.DesignWattHours is { } design ? $"{design:N1} Wh" : "-";
    }

    void UpdateCustomEnabled() => cpuSpeed.IsEnabled = gpuSpeed.IsEnabled = apply.IsEnabled = custom.IsChecked == true;

    static Slider SpeedSlider() => new() { Minimum = FanController.MinCustomPercent, Maximum = 100, Value = 50, TickFrequency = 5, IsSnapToTickEnabled = true };

    static TextBlock Value() => new() { FontSize = 28, FontWeight = FontWeight.SemiBold, Text = "-" };

    static Control Tile(string title, TextBlock value) => new Border
    {
        Background = Look.CardFill,
        BorderBrush = Look.CardBorder,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(16),
        Padding = new Thickness(18, 14),
        Margin = new Thickness(0, 0, 12, 12),
        Child = new StackPanel { Children = { new TextBlock { Text = title, Foreground = Look.Muted, FontSize = 13 }, value } },
    };
}
