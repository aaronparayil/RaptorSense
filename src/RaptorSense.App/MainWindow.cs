using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RaptorSense.Audio;
using RaptorSense.Automation;
using RaptorSense.Hardware;
using RaptorSense.Input;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Profiles;
using RaptorSense.Thermal;
using IEffect = RaptorSense.Lighting.IEffect;
using Key = RaptorSense.Layout.Key;

namespace RaptorSense.App;

sealed class MainWindow : Window, IAutomationTarget
{
    readonly EffectEngine engine = new(Ph16Keyboard.TryOpen);
    readonly Frame preview = new();
    readonly KeyboardView keyboard = new() { Margin = new Thickness(24) };
    readonly TextBlock status = new() { Margin = new Thickness(16, 10), Opacity = 0.8 };
    readonly Dictionary<IEffect, ToggleButton> effectTiles = [];
    readonly CheckBox autoColor = new() { Content = "Auto (rainbow colours)" };
    readonly ComboBox overlayPicker = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ColorEditor colorEditor = new();
    readonly LightBarPanel lightBar = new();
    readonly AutomationRunner automations;
    readonly Slider speed = new() { Minimum = 0.1, Maximum = 4 };
    readonly Slider brightness = new() { Minimum = 0, Maximum = Ph16Keyboard.MaxBrightness, IsEnabled = false };
    readonly ComboBox profileList = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Saved profiles" };
    readonly TextBox profileName = new() { PlaceholderText = "Profile name" };
    readonly ToggleSwitch output = new() { IsChecked = true, OnContent = "Controlling the keyboard", OffContent = "Preview only" };
    readonly ToggleSwitch autostart = new() { OnContent = "Starts with Windows", OffContent = "Start with Windows" };
    readonly DispatcherTimer uiTimer;
    IAudioSpectrum? audio;
    IKeyPressSource? keyHook;
    bool brightnessSynced;
    string? notice;
    DateTime noticeUntil;
    bool shutDown;

    /// <summary>Raised when something the tray menu shows has changed (profiles, pause, autostart).</summary>
    public event Action? TrayStateChanged;

    public MainWindow()
    {
        Title = $"{AppInfo.Name} {AppInfo.Version}";
        Icon = App.Icon;
        Width = 1500;
        Height = 640;
        MinWidth = 900;
        MinHeight = 480;
        Background = new SolidColorBrush(Color.FromRgb(16, 17, 20));

        var lighting = new DockPanel();
        var controls = BuildControls();
        DockPanel.SetDock(controls, Dock.Right);
        lighting.Children.Add(controls);
        var hint = new TextBlock
        {
            Text = "Click or drag over keys to paint them with colour 1 · right-click to erase",
            Opacity = 0.6,
            Margin = new Thickness(24, 16, 24, 0),
        };
        DockPanel.SetDock(hint, Dock.Top);
        lighting.Children.Add(hint);
        keyboard.KeyPainted += PaintKey;
        lighting.Children.Add(keyboard);

        automations = new AutomationRunner(this);
        automations.EnabledChanged += () => TrayStateChanged?.Invoke();

        var tabs = new TabControl
        {
            Margin = new Thickness(8, 4, 0, 0),
            Items =
            {
                new TabItem { Header = "Lighting", Content = lighting },
                new TabItem { Header = "Animations", Content = new AnimationsPanel(engine, () => ChooseEffect(Effects.Animation)) },
                new TabItem { Header = "Light bar", Content = lightBar },
                new TabItem { Header = "Performance", Content = new PerformancePanel() },
                new TabItem { Header = "Automations", Content = new AutomationsPanel(automations) },
            },
        };
        var root = new DockPanel();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        root.Children.Add(tabs);
        Content = root;

        ApplyProfile(ProfileStore.LoadLast() ?? new Profile());
        RefreshProfileList();

        engine.Start();
        uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => RefreshPreview());
        uiTimer.Start();
        automations.Start();
    }

    // ---- Automations ----

    Profile IAutomationTarget.CaptureLighting() => Profile.Capture("", engine, lightBar.Current);

    Profile? IAutomationTarget.LoadProfile(string name) => ProfileStore.Load(name);

    void IAutomationTarget.ApplyLighting(Profile profile)
    {
        ApplyProfile(profile);
        Notify(profile.Name.Length > 0 ? $"Automation: lighting profile '{profile.Name}'." : "Automation ended: your lighting is back.");
    }

    Task<ThermalStatus> IAutomationTarget.GetStatusAsync() => ThermalClient.StatusAsync();

    Task<ThermalStatus> IAutomationTarget.SetPowerAsync(PowerProfile profile) => ThermalClient.SetProfileAsync(profile);

    Task<ThermalStatus> IAutomationTarget.SetFansAsync(FanMode mode, int cpuPercent, int gpuPercent) => ThermalClient.SetFansAsync(mode, cpuPercent, gpuPercent);

    public bool AutomationsEnabled
    {
        get => automations.Settings.Enabled;
        set => automations.Update(automations.Settings with { Enabled = value });
    }

    // ---- Keyboard preview and painting ----

    void PaintKey(Key hit, bool erase)
    {
        int cell = hit.Cell;
        if (colorEditor.Picking && !erase)
        {
            engine.CopyLatestFrame(preview);
            colorEditor.Pick(preview[cell]);   // takes what the key shows right now, from any effect
            Notify($"Picked #{preview[cell]} from {hit.Label}.");
            return;
        }

        if (engine.Effect != Effects.Painted) StartPainting();
        Effects.Painted[cell] = erase ? Rgb.Black : engine.Settings.Color;
    }
    /// <summary>Switches to the painted layout, starting from what the keyboard shows now so nothing jumps.</summary>
    void StartPainting()
    {
        engine.CopyLatestFrame(preview);
        for (int cell = 0; cell < Frame.CellCount; cell++) Effects.Painted[cell] = preview[cell];
        ChooseEffect(Effects.Painted);
    }

    // ---- Effect picker ----

    Control BuildEffectPicker()
    {
        var picker = new StackPanel { Spacing = 4 };
        foreach (var category in EffectCategory.Order)
        {
            var grid = new UniformGrid { Columns = 2 };
            foreach (var effect in Effects.All.Where(e => e.Category == category))
            {
                var tile = new ToggleButton
                {
                    Content = DisplayName(effect),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 6, 6),
                    Padding = new Thickness(10, 7),
                };
                tile.Click += (_, _) =>
                {
                    if (effect == Effects.Animation && !EnsureAnimation())
                    {
                        ChooseEffect(engine.Effect);   // undoes the tile's own toggle
                        Notify("Build an animation in the Animations tab first.");
                        return;
                    }
                    ChooseEffect(effect);
                };
                effectTiles[effect] = tile;
                grid.Children.Add(tile);
            }

            if (category == EffectCategory.Keyboard)
            {
                var section = new StackPanel { Spacing = 6 };
                section.Children.Add(new TextBlock
                {
                    Text = "Run by the keyboard itself: they keep going when RaptorSense is closed and use no CPU. " +
                           "The preview here is an approximation.",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Opacity = 0.65,
                });
                section.Children.Add(grid);
                picker.Children.Add(new Expander { Header = category, Content = section, HorizontalAlignment = HorizontalAlignment.Stretch });
            }
            else
            {
                picker.Children.Add(new TextBlock { Text = category, Opacity = 0.65, FontSize = 12, Margin = new Thickness(0, 6, 0, 2) });
                picker.Children.Add(grid);
            }
        }
        return picker;
    }

    /// <summary>Makes sure "Your animation" has something to play: the one last used, or else the first saved.</summary>
    static bool EnsureAnimation()
    {
        if (Effects.Animation.Animation is not null) return true;
        foreach (var name in AnimationStore.List())
        {
            if (AnimationStore.Load(name) is not { } animation) continue;
            Effects.Animation.Show(animation.Compile(), fromStart: true);
            return true;
        }
        return false;
    }

    /// <summary>Highlights the effect's tile and switches the engine to it.</summary>
    void ChooseEffect(IEffect effect)
    {
        foreach (var (e, tile) in effectTiles) tile.IsChecked = e == effect;
        SelectEffect(effect);
    }

    // ---- Side panel ----

    Control BuildControls()
    {
        var panel = new StackPanel { Spacing = 8, Width = 360, Margin = new Thickness(0, 16, 20, 16) };

        panel.Children.Add(Heading("Effect"));
        panel.Children.Add(BuildEffectPicker());
        panel.Children.Add(Heading("Typing overlay"));
        panel.Children.Add(BuildOverlayPicker());

        panel.Children.Add(Heading("Colours"));
        panel.Children.Add(new TextBlock
        {
            Text = "Colour 1 is also the paint brush; typing effects use colour 2.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.65,
        });
        colorEditor.ColorChanged += (index, color) =>
        {
            if (index == 0) engine.Settings.Color = color;
            else engine.Settings.Color2 = color;
        };
        panel.Children.Add(colorEditor);
        autoColor.IsCheckedChanged += (_, _) => engine.Settings.AutoColor = autoColor.IsChecked == true;
        panel.Children.Add(autoColor);

        var fill = new Button { Content = "Fill all keys" };
        fill.Click += (_, _) => { StartPainting(); Effects.Painted.Fill(engine.Settings.Color); };
        var clear = new Button { Content = "Clear" };
        clear.Click += (_, _) => { StartPainting(); Effects.Painted.Fill(Rgb.Black); };
        panel.Children.Add(Row(fill, clear));

        panel.Children.Add(Heading("Speed"));
        speed.ValueChanged += (_, e) => engine.Settings.Speed = e.NewValue;
        panel.Children.Add(speed);

        panel.Children.Add(Heading("Brightness"));
        brightness.ValueChanged += (_, e) => { if (brightnessSynced) engine.Brightness = (int)Math.Round(e.NewValue); };
        panel.Children.Add(brightness);

        panel.Children.Add(Heading("Profiles"));
        var load = new Button { Content = "Load" };
        load.Click += (_, _) => LoadSelectedProfile();
        var delete = new Button { Content = "Delete" };
        delete.Click += (_, _) => DeleteSelectedProfile();
        panel.Children.Add(profileList);
        panel.Children.Add(Row(load, delete));
        var save = new Button { Content = "Save" };
        save.Click += (_, _) => SaveProfile();
        panel.Children.Add(new DockPanel { Children = { Docked(save, Dock.Right), profileName } });

        output.IsCheckedChanged += (_, _) =>
        {
            engine.OutputEnabled = output.IsChecked == true;
            TrayStateChanged?.Invoke();
        };
        panel.Children.Add(output);

        if (Autostart.IsSupported)
        {
            autostart.IsChecked = Autostart.IsEnabled;
            autostart.IsCheckedChanged += (_, _) =>
            {
                try { Autostart.IsEnabled = autostart.IsChecked == true; }
                catch (Exception e) { Notify($"Could not change Start with Windows: {e.Message}"); }
                TrayStateChanged?.Invoke();
            };
            panel.Children.Add(autostart);
        }

        var handBack = new Button { Content = "Hand back: keyboard's own rainbow wave" };
        handBack.Click += (_, _) => HandBack();
        panel.Children.Add(handBack);

        panel.Children.Add(new TextBlock
        {
            Text = $"{AppInfo.Name} {AppInfo.Version}\nCreated by {AppInfo.Author}\nFree and open source ({AppInfo.Licence})",
            FontSize = 11,
            Opacity = 0.55,
            Margin = new Thickness(0, 12, 0, 0),
        });

        return new ScrollViewer { Content = panel };
    }

    void SelectEffect(IEffect effect)
    {
        if (effect.UsesAudio && audio is null && OperatingSystem.IsWindows())
        {
            try { audio = new LoopbackSpectrum(); engine.Audio = audio; }
            catch (Exception e) { Notify($"Could not capture audio: {e.Message}"); }
        }

        engine.Effect = effect;
        UpdateKeyHook();
    }

    // The key hook only exists while the effect or the typing overlay reacts to key presses.
    void UpdateKeyHook()
    {
        if (engine.NeedsKeys && keyHook is null && OperatingSystem.IsWindows())
        {
            try { keyHook = new WindowsKeyHook(); keyHook.CellPressed += engine.NotifyKeyPress; }
            catch (Exception e) { Notify($"Could not watch key presses: {e.Message}"); }
        }
        else if (!engine.NeedsKeys && keyHook is not null)
        {
            keyHook.Dispose();
            keyHook = null;
        }
    }

    static readonly IReadOnlyList<IEffect> OverlayEffects = Effects.All.Where(e => e.WorksAsOverlay).ToList();

    Control BuildOverlayPicker()
    {
        overlayPicker.ItemsSource = new[] { "None" }.Concat(OverlayEffects.Select(e => e.Name)).ToList();
        overlayPicker.SelectedIndex = 0;
        overlayPicker.SelectionChanged += (_, _) =>
        {
            engine.Overlay = overlayPicker.SelectedIndex > 0 ? OverlayEffects[overlayPicker.SelectedIndex - 1] : null;
            UpdateKeyHook();
        };
        var section = new StackPanel { Spacing = 4 };
        section.Children.Add(overlayPicker);
        section.Children.Add(new TextBlock
        {
            Text = "A typing effect drawn on top of any effect, e.g. lightning on every key press over Aurora.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.65,
        });
        return section;
    }

    // ---- Profiles ----

    void ApplyProfile(Profile profile)
    {
        profile.ApplyTo(engine);
        colorEditor.SetColors(engine.Settings.Color, engine.Settings.Color2);
        speed.Value = engine.Settings.Speed;
        if (engine.Brightness >= 0)
        {
            brightness.Value = engine.Brightness;
            brightness.IsEnabled = brightnessSynced = true;
        }
        autoColor.IsChecked = engine.Settings.AutoColor;
        var overlay = engine.Overlay;   // read before the picker's change handler can overwrite it
        overlayPicker.SelectedIndex = overlay is null ? 0 : OverlayEffects.ToList().IndexOf(overlay) + 1;
        engine.Overlay = overlay;
        ChooseEffect(engine.Effect);   // also sets up the key hook or audio capture the effect needs
        lightBar.Load(profile.LightBar, send: true);   // null leaves the bar as it is
        profileName.Text = profile.Name;
    }

    void SaveProfile()
    {
        var name = profileName.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { Notify("Type a name for the profile first."); return; }
        ProfileStore.Save(Profile.Capture(name, engine, lightBar.Current));
        RefreshProfileList();
        profileList.SelectedItem = name;
        TrayStateChanged?.Invoke();
        Notify($"Saved profile '{name}'.");
    }

    void LoadSelectedProfile()
    {
        if (profileList.SelectedItem is string name) LoadProfile(name);
    }

    public void LoadProfile(string name)
    {
        var profile = ProfileStore.Load(name);
        if (profile is null) { Notify($"Could not read profile '{name}'."); return; }
        ApplyProfile(profile);
        Notify($"Loaded profile '{name}'.");
    }

    void DeleteSelectedProfile()
    {
        if (profileList.SelectedItem is not string name) return;
        ProfileStore.Delete(name);
        RefreshProfileList();
        TrayStateChanged?.Invoke();
        Notify($"Deleted profile '{name}'.");
    }

    void RefreshProfileList() => profileList.ItemsSource = ProfileStore.List();

    // ---- Misc ----

    void HandBack()
    {
        output.IsChecked = false;   // the engine lets go of the keyboard on its next frame
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                using var keyboard = Ph16Keyboard.TryOpen();
                keyboard?.SetBuiltInEffect(BuiltInEffect.Wave, Rgb.Black, 6, Math.Max(engine.Brightness, 1));
            }
            catch (Exception e) { Notify($"Could not set the keyboard effect: {e.Message}"); }
        }, DispatcherPriority.Background);
    }

    void RefreshPreview()
    {
        engine.CopyLatestFrame(preview);
        keyboard.Show(preview.Cells);

        if (!brightnessSynced && engine.Brightness >= 0)
        {
            brightness.Value = engine.Brightness;
            brightness.IsEnabled = brightnessSynced = true;
        }
        status.Text = DateTime.UtcNow < noticeUntil
            ? notice
            : $"{engine.Status}   ·   {engine.RenderFps:N0} fps rendered, {engine.SendFps:N0} fps sent";
    }

    /// <summary>Shows a message in the status bar for a few seconds instead of the live engine status.</summary>
    void Notify(string message)
    {
        notice = message;
        noticeUntil = DateTime.UtcNow.AddSeconds(3);
        status.Text = message;
    }

    // ---- Tray integration ----

    public IReadOnlyList<string> Profiles => ProfileStore.List();

    public bool OutputEnabled
    {
        get => output.IsChecked == true;
        set => output.IsChecked = value;
    }

    public bool StartWithWindows
    {
        get => autostart.IsChecked == true;
        set => autostart.IsChecked = value;
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    // Closing the window only hides it: the lighting keeps running from the tray. Quit is in the tray menu.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!shutDown && e.CloseReason is WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            SaveLast();
            Hide();
        }
        base.OnClosing(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // No preview to draw while hidden.
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible) uiTimer.Start(); else uiTimer.Stop();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Shutdown();
        base.OnClosed(e);
    }

    /// <summary>Saves the current setup and releases the keyboard, audio and key hook. Safe to call twice.</summary>
    public void Shutdown()
    {
        if (shutDown) return;
        shutDown = true;
        SaveLast();
        automations.Dispose();
        uiTimer.Stop();
        keyHook?.Dispose();
        engine.Dispose();
        audio?.Dispose();
    }

    void SaveLast()
    {
        // While a rule is showing its own lighting, the user's setup is what it will go back to.
        try { ProfileStore.SaveLast(automations.LightingBefore ?? Profile.Capture("", engine, lightBar.Current)); }
        catch (IOException) { }
    }

    // Musical note and keyboard symbols. This file is UTF-8: edit it with UTF-8-aware tools (Windows PowerShell 5.1's
    // Get-Content/Set-Content without -Encoding utf8 garbles these).
    static string DisplayName(IEffect e) => e.UsesAudio ? $"{e.Name}  ♪" : e.UsesKeys ? $"{e.Name}  ⌨" : e.Name;

    static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        control.Margin = new Thickness(8, 0, 0, 0);
        return control;
    }
}
