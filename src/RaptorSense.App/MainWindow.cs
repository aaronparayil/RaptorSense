using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Documents;
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
    // The strip along the top holds the window's title, the tabs and its own buttons, and drags the window.
    const double TopBarHeight = 64;

    // Room for the window's own minimise, maximise and close buttons at the right of the strip.
    const double CaptionButtonsWidth = 146;

    readonly KeyboardView keyboard = new();
    // Connection and frame rates: only shown, and only kept up to date, while the info panel is open.
    readonly TextBlock status = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock frameRates = new() { FontSize = 13, Foreground = Look.Muted };
    readonly TextBlock toastText = new() { FontSize = 13 };
    readonly Border toast;
    bool infoOpen;
    readonly TextBlock effectTitle = new() { FontSize = 30, FontWeight = FontWeight.SemiBold };
    readonly Dictionary<IEffect, ToggleButton> effectTiles = [];
    readonly CheckBox autoColor = new() { Content = "Auto (rainbow colours)" };
    readonly ComboBox overlayPicker = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ColorEditor colorEditor = new(wide: true);
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
    DateTime noticeUntil;
    bool shutDown;

    /// <summary>Raised when something the tray menu shows has changed (profiles, pause, autostart).</summary>
    public event Action? TrayStateChanged;

    public MainWindow()
    {
        Title = AppInfo.Name;   // the version is in the info panel
        Icon = App.Icon;
        Width = 1500;
        Height = 900;
        MinWidth = 1280;
        MinHeight = 720;
        // Open in the middle of the screen, and no bigger than the screen has room for.
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (Screens.Primary is { } screen)
        {
            Width = Math.Max(MinWidth, Math.Min(Width, screen.WorkingArea.Width / screen.Scaling - 40));
            Height = Math.Max(MinHeight, Math.Min(Height, screen.WorkingArea.Height / screen.Scaling - 40));
        }
        Background = Look.WindowBackground;
        Foreground = Look.Text;
        FontFamily = Look.Font;
        // The tabs sit where the title bar would be; the window keeps its own minimise, maximise and close buttons.
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = TopBarHeight;

        var lighting = BuildLighting();

        automations = new AutomationRunner(this);
        automations.EnabledChanged += () => TrayStateChanged?.Invoke();

        var tabs = new TabControl
        {
            Margin = new Thickness(0, (TopBarHeight - 40) / 2, 0, 0),
            Padding = new Thickness(0),
            Background = null,   // nothing to hit between the tabs, so the strip behind them can move the window
            Items =
            {
                new TabItem { Header = "Lighting", Content = lighting },
                new TabItem { Header = "Animations", Content = new AnimationsPanel(engine, () => ChooseEffect(Effects.Animation)) },
                new TabItem { Header = "Light bar", Content = lightBar },
                new TabItem { Header = "Performance", Content = new PerformancePanel() },
                new TabItem { Header = "Automations", Content = new AutomationsPanel(automations) },
            },
        };

        // Behind the tabs: the empty parts of the top strip are the window's title bar. Windows then moves the window,
        // maximises it on a double-click and offers its snap layouts, exactly as it does for any other title bar.
        var dragStrip = new Border { Height = TopBarHeight, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.Transparent };
        WindowDecorationProperties.SetElementRole(dragStrip, WindowDecorationsElementRole.TitleBar);

        var info = new Button
        {
            Content = "i",
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(15),
            FontWeight = FontWeight.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, (TopBarHeight - 30) / 2, CaptionButtonsWidth + 12, 0),
            Flyout = BuildInfoFlyout(),
        };
        ToolTip.SetTip(info, "Connection and app details");
        AutomationProperties.SetName(info, "Info");

        toast = new Border
        {
            Background = Look.Popup,
            BorderBrush = Look.KeyBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(18, 10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 22),
            IsVisible = false,
            IsHitTestVisible = false,
            Child = toastText,
        };

        Content = new Grid { Children = { dragStrip, tabs, info, toast } };

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
                    Padding = new Thickness(14, 8),
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
        effectTitle.Text = effect.Name;
        SelectEffect(effect);
    }

    // ---- Lighting page ----

    /// <summary>
    /// The keyboard is the centrepiece, with what plays on it named above. Colours, speed and profiles sit in cards
    /// below it; the effects and the rarely changed keyboard settings scroll in a column on the right.
    /// </summary>
    Control BuildLighting()
    {
        keyboard.KeyPainted += PaintKey;

        var hint = new TextBlock
        {
            Text = "Click or drag over keys to paint them with colour 1 · right-click to erase",
            Foreground = Look.Muted,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 6),
        };
        DockPanel.SetDock(hint, Dock.Right);
        var header = new DockPanel
        {
            Margin = new Thickness(4, 0, 0, 14),
            Children =
            {
                hint,
                new StackPanel
                {
                    Children = { new TextBlock { Text = "Now playing on your keyboard", Foreground = Look.Muted, FontSize = 13 }, effectTitle },
                },
            },
        };

        var keyboardWell = new Border
        {
            Background = Look.Well,
            BorderBrush = Look.WellBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(22),
            Child = keyboard,
        };

        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("1.9*,*,*"), Margin = new Thickness(0, 16, 0, 0) };
        cards.Children.Add(BuildColourCard());
        cards.Children.Add(InColumn(BuildAdjustCard(), 1));
        cards.Children.Add(InColumn(BuildProfilesCard(), 2));

        var main = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        main.Children.Add(header);
        main.Children.Add(InRow(keyboardWell, 1));
        main.Children.Add(InRow(cards, 2));

        var side = new StackPanel { Spacing = 16, Width = 372 };
        side.Children.Add(Look.Card(BuildEffectPicker(), "Effects"));
        side.Children.Add(BuildKeyboardCard());
        var sideScroll = new ScrollViewer { Content = side, Margin = new Thickness(16, 0, 0, 0) };

        var page = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(28, 12, 28, 24) };
        page.Children.Add(main);
        page.Children.Add(InColumn(sideScroll, 1));
        return page;
    }

    Control BuildColourCard()
    {
        colorEditor.ColorChanged += (index, color) =>
        {
            if (index == 0) engine.Settings.Color = color;
            else engine.Settings.Color2 = color;
        };
        autoColor.IsCheckedChanged += (_, _) => engine.Settings.AutoColor = autoColor.IsChecked == true;

        var fill = new Button { Content = "Fill all keys" };
        fill.Click += (_, _) => { StartPainting(); Effects.Painted.Fill(engine.Settings.Color); };
        var clear = new Button { Content = "Clear" };
        clear.Click += (_, _) => { StartPainting(); Effects.Painted.Fill(Rgb.Black); };

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(colorEditor);
        content.Children.Add(Row(autoColor, fill, clear));
        content.Children.Add(Look.Note("Colour 1 is also the paint brush; typing effects use colour 2."));
        return Look.Card(content, "Colours");
    }

    Control BuildAdjustCard()
    {
        speed.ValueChanged += (_, e) => engine.Settings.Speed = e.NewValue;
        brightness.ValueChanged += (_, e) => { if (brightnessSynced) engine.Brightness = (int)Math.Round(e.NewValue); };

        var content = new StackPanel { Spacing = 4 };
        content.Children.Add(Caption("Speed"));
        content.Children.Add(speed);
        content.Children.Add(Caption("Brightness"));
        content.Children.Add(brightness);
        content.Children.Add(Caption("Typing overlay"));
        content.Children.Add(BuildOverlayPicker());
        var card = Look.Card(content, "Adjust");
        card.Margin = new Thickness(16, 0, 0, 0);
        return card;
    }

    Control BuildProfilesCard()
    {
        var load = new Button { Content = "Load" };
        load.Click += (_, _) => LoadSelectedProfile();
        var delete = new Button { Content = "Delete" };
        delete.Click += (_, _) => DeleteSelectedProfile();
        var save = new Button { Content = "Save" };
        save.Click += (_, _) => SaveProfile();

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(profileList);
        content.Children.Add(Row(load, delete));
        content.Children.Add(new DockPanel { Children = { Docked(save, Dock.Right), profileName } });
        content.Children.Add(Look.Note("A profile keeps the effect, colours, speed, brightness and light bar."));
        var card = Look.Card(content, "Profiles");
        card.Margin = new Thickness(16, 0, 0, 0);
        return card;
    }

    Control BuildKeyboardCard()
    {
        var content = new StackPanel { Spacing = 8 };

        output.IsCheckedChanged += (_, _) =>
        {
            engine.OutputEnabled = output.IsChecked == true;
            TrayStateChanged?.Invoke();
        };
        content.Children.Add(output);

        if (Autostart.IsSupported)
        {
            autostart.IsChecked = Autostart.IsEnabled;
            autostart.IsCheckedChanged += (_, _) =>
            {
                try { Autostart.IsEnabled = autostart.IsChecked == true; }
                catch (Exception e) { Notify($"Could not change Start with Windows: {e.Message}"); }
                TrayStateChanged?.Invoke();
            };
            content.Children.Add(autostart);
        }

        var handBack = new Button { Content = "Hand back: keyboard's own rainbow wave" };
        handBack.Click += (_, _) => HandBack();
        content.Children.Add(handBack);

        return Look.Card(content, "Keyboard");
    }

    /// <summary>
    /// What the "i" button opens: the keyboard connection and frame rates, the window's background colour, and who
    /// made the app.
    /// </summary>
    Flyout BuildInfoFlyout()
    {
        var panel = new StackPanel { Spacing = 6, Width = 346 };
        // A flyout is its own little window, so it doesn't inherit the main window's font and text colour.
        TextElement.SetFontFamily(panel, Look.Font);
        TextElement.SetForeground(panel, Look.Text);
        panel.Children.Add(Look.Heading("Keyboard"));
        panel.Children.Add(status);
        panel.Children.Add(frameRates);

        var backgroundHeading = Look.Heading("Background");
        backgroundHeading.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(backgroundHeading);
        bool backgroundChanged = false;
        var presets = new WrapPanel();
        foreach (var (name, color) in Look.BackgroundPresets)
        {
            var preset = new Button
            {
                Width = 32,
                Height = 32,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(16),
                Content = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(Look.Brightened(color)),   // the real colours are too dark to tell apart
                    BorderBrush = Look.KeyBorder,
                    BorderThickness = new Thickness(1),
                },
            };
            ToolTip.SetTip(preset, name);
            AutomationProperties.SetName(preset, name + " background");
            preset.Click += (_, _) =>
            {
                Look.SetBackground(color);
                Background = Look.WindowBackground;
                backgroundChanged = true;
            };
            presets.Children.Add(preset);
        }
        panel.Children.Add(presets);

        var about = Look.Heading("About");
        about.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(about);
        panel.Children.Add(Look.Note($"{AppInfo.Name} {AppInfo.Version}\nCreated by {AppInfo.Author}\nFree and open source ({AppInfo.Licence})"));

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Opened += (_, _) => { infoOpen = true; ShowEngineStatus(); };
        flyout.Closed += (_, _) =>
        {
            infoOpen = false;
            if (!backgroundChanged) return;
            backgroundChanged = false;
            Look.SaveBackground();
        };
        return flyout;
    }

    void ShowEngineStatus()
    {
        status.Text = engine.Status;
        frameRates.Text = $"{engine.RenderFps:N0} fps rendered, {engine.SendFps:N0} fps sent";
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
        if (infoOpen) ShowEngineStatus();
        if (toast.IsVisible && DateTime.UtcNow >= noticeUntil) toast.IsVisible = false;
    }

    /// <summary>Shows a message at the bottom of the window for a few seconds.</summary>
    void Notify(string message)
    {
        toastText.Text = message;
        toast.IsVisible = true;
        noticeUntil = DateTime.UtcNow.AddSeconds(3);
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

    static TextBlock Caption(string text) => new() { Text = text, Foreground = Look.Muted, FontSize = 13, Margin = new Thickness(0, 4, 0, 0) };

    static Control InColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    static Control InRow(Control control, int row)
    {
        Grid.SetRow(control, row);
        return control;
    }

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
