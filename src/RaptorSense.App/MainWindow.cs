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

sealed class MainWindow : Window, IAutomationTarget, IGalleryHost
{
    // One app for every laptop: the model picks the keyboard drawn, how effects reach it and which tabs show.
    // On the Nitro the per-key USB keyboard isn't there, so the engine doesn't look for it.
    static readonly bool HasFourZoneKeyboard = ThisLaptop.HasFourZoneKeyboard;

    readonly EffectEngine engine = new(HasFourZoneKeyboard ? NoPerKeyKeyboard : Ph16Keyboard.TryOpen);
    readonly KeyboardZonesPanel zonesPanel = new();
    readonly StartupShow startupShow = new(ThisLaptop.Layout);
    readonly ToggleSwitch showOnStart = new() { OnContent = "Light show when RaptorSense starts", OffContent = "No light show at start" };
    IEffect? afterShow;
    DispatcherTimer? showTimer;
    ZoneStreamer? zoneStreamer;
    readonly Rgb[] zonePreview = new Rgb[Frame.CellCount];

    static Ph16Keyboard? NoPerKeyKeyboard() => null;
    readonly Frame preview = new();
    // The strip along the top holds the window's title, the tabs and its own buttons, and drags the window.
    const double TopBarHeight = 64;

    // Room for the window's own minimise, maximise and close buttons at the right of the strip.
    const double CaptionButtonsWidth = 146;

    readonly KeyboardView keyboard = HasFourZoneKeyboard ? new(LaptopLayouts.NitroCaps, FourZoneLayout.ZoneOf) : new();
    // Connection and frame rates: only shown, and only kept up to date, while the info panel is open.
    readonly TextBlock status = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock frameRates = new() { FontSize = 13, Foreground = Look.Muted };
    readonly TextBlock toastText = new() { FontSize = 13 };
    readonly Border toast;
    bool infoOpen;
    readonly TextBlock effectTitle = new() { FontSize = 30, FontWeight = FontWeight.SemiBold };
    // An effect can have two tiles: in its category, and under Favourites.
    readonly Dictionary<IEffect, List<ToggleButton>> effectTiles = [];
    readonly TextBox effectSearch = new() { PlaceholderText = "Search effects" };
    readonly List<(Expander Box, string Category, List<(IEffect Effect, ToggleButton Tile)> Tiles)> pickerGroups = [];
    readonly StackPanel favouritesGroup = new() { Spacing = 4 };
    readonly TabItem galleryTab = new() { Header = "Gallery", Content = new Panel() };
    TabControl? tabs;
    EffectGallery? gallery;
    bool demoMusic;
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
    readonly ToggleSwitch autostart = new() { OnContent = Autostart.OnLabel, OffContent = Autostart.OffLabel };
    readonly DispatcherTimer uiTimer;
    IAudioSpectrum? audio;
    IKeyPressSource? keyHook;
    SpecialKeyWatcher? specialKeys;
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

        tabs = new TabControl
        {
            Margin = new Thickness(0, (TopBarHeight - 40) / 2, 0, 0),
            Padding = new Thickness(0),
            Background = null,   // nothing to hit between the tabs, so the strip behind them can move the window
            Items =
            {
                new TabItem { Header = "Lighting", Content = lighting },
                galleryTab,
                new TabItem { Header = "Animations", Content = new AnimationsPanel(engine, () => ChooseEffect(Effects.Animation)) },
                new TabItem { Header = "Light bar", Content = lightBar, IsVisible = ThisLaptop.HasLightBar },
                // Only on laptops whose 4-zone keyboard has been checked. Reading the model needs no administrator rights.
                new TabItem { Header = "Keyboard zones", Content = zonesPanel, IsVisible = HasFourZoneKeyboard },
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
        // The gallery is built the first time it is opened, and its previews move only while it is in sight.
        tabs.SelectionChanged += (_, _) => UpdateGallery();

        ApplyProfile(ProfileStore.LoadLast() ?? new Profile());
        RefreshProfileList();

        // The Helios's keyboard tells the engine its brightness when it connects; the Nitro's zones start at full.
        if (HasFourZoneKeyboard && engine.Brightness < 0) engine.Brightness = Ph16Keyboard.MaxBrightness;
        engine.Start();
        if (HasFourZoneKeyboard)
        {
            zoneStreamer = new ZoneStreamer(engine, () => engine.OutputEnabled);
            // A colour set on the Keyboard zones tab stays: the Lighting tab's effects stop sending until turned back on.
            zonesPanel.Applied += () =>
            {
                if (output.IsChecked != true) return;
                output.IsChecked = false;
                Notify("Lighting tab effects paused: turn on \"Controlling the keyboard\" to play them again.");
            };
        }
        uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => RefreshPreview());
        uiTimer.Start();
        if (StartupShowEnabled) PlayStartupShow();
        automations.Start();

        if (OperatingSystem.IsWindows())
        {
            specialKeys = new SpecialKeyWatcher();
            specialKeys.Pressed += key => Dispatcher.UIThread.Post(() =>
            {
                if (key == SpecialKey.Predator) OnPredatorKey();
                else if (key is SpecialKey.BrightnessUp or SpecialKey.BrightnessDown) OnBrightnessKey(key == SpecialKey.BrightnessUp);
            });
        }
    }

    // The PREDATOR key opens the window, as it opens Acer's app. Pressed with the window already in front, it puts it
    // back in the tray.
    void OnPredatorKey()
    {
        if (shutDown) return;
        if (IsVisible && IsActive && WindowState != WindowState.Minimized) Close();
        else ShowFromTray();
    }

    // Fn+F11 and Fn+F12. The keyboard changes its own level when they are pressed, but the next frame would put the
    // old one back, so the brightness here takes the same step. Moving the slider is what tells the engine.
    void OnBrightnessKey(bool brighter)
    {
        if (shutDown || !brightnessSynced) return;
        brightness.Value = BrightnessSteps.Next(engine.Brightness, brighter);
    }

    // ---- Automations ----

    Profile IAutomationTarget.CaptureLighting()
    {
        EndStartupShow();   // what to go back to is the user's effect, not the show
        return Profile.Capture("", engine, lightBar.Current);
    }

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
        var color = erase ? Rgb.Black : engine.Settings.Color;
        if (!HasFourZoneKeyboard)
        {
            Effects.Painted[cell] = color;
            return;
        }
        // The Nitro lights a zone, not a key: painting a key paints its whole zone.
        int zone = FourZoneLayout.ZoneOf(hit);
        foreach (var key in KeyMap.Keys)
            if (FourZoneLayout.ZoneOf(key) == zone) Effects.Painted[key.Cell] = color;
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
        var picker = new StackPanel { Spacing = 8 };
        var browse = new Button
        {
            Content = $"Browse all {LaptopEffects.Count} in the gallery",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        browse.Click += (_, _) => OpenGallery();
        picker.Children.Add(effectSearch);
        picker.Children.Add(browse);
        picker.Children.Add(favouritesGroup);
        RefreshFavourites();
        Favourites.Changed += RefreshFavourites;

        foreach (var category in EffectCategory.Order)
        {
            // Each laptop has its own list: the Nitro's effects are made for its four zones.
            var inCategory = LaptopEffects.Where(e => e.Category == category).ToList();
            if (inCategory.Count == 0) continue;
            var grid = new UniformGrid { Columns = 2 };
            var tiles = new List<(IEffect, ToggleButton)>();
            foreach (var effect in inCategory)
            {
                var tile = MakeTile(effect);
                tiles.Add((effect, tile));
                grid.Children.Add(tile);
            }

            Control content = grid;
            if (category == EffectCategory.Keyboard)
                content = new StackPanel
                {
                    Spacing = 6,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Run by the keyboard itself: they keep going when RaptorSense is closed and use no CPU. " +
                                   "The preview here is an approximation.",
                            TextWrapping = TextWrapping.Wrap,
                            FontSize = 12,
                            Opacity = 0.65,
                        },
                        grid,
                    },
                };
            var box = new Expander
            {
                Header = $"{category}  ·  {inCategory.Count}",
                Content = content,
                IsExpanded = category == EffectCategory.Basic,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            pickerGroups.Add((box, category, tiles));
            picker.Children.Add(box);
        }
        effectSearch.TextChanged += (_, _) => FilterPicker();
        return picker;
    }

    ToggleButton MakeTile(IEffect effect)
    {
        var tile = new ToggleButton
        {
            // Two to a row inside a group: long names end in "..." and show in full in the tooltip.
            Content = new TextBlock { Text = DisplayName(effect), TextTrimming = TextTrimming.CharacterEllipsis },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(12, 8),
            IsChecked = effect == engine.Effect,
        };
        ToolTip.SetTip(tile, DisplayName(effect));
        AutomationProperties.SetName(tile, effect.Name);
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
        if (!effectTiles.TryGetValue(effect, out var list)) effectTiles[effect] = list = [];
        list.Add(tile);
        return tile;
    }

    /// <summary>The starred effects, at the top of the list. Rebuilt when a star changes in the gallery.</summary>
    void RefreshFavourites()
    {
        foreach (var old in favouritesGroup.Children.OfType<UniformGrid>().SelectMany(w => w.Children.OfType<ToggleButton>()))
            foreach (var list in effectTiles.Values) list.Remove(old);
        favouritesGroup.Children.Clear();
        var starred = Favourites.Ids.Select(id => LaptopEffects.FirstOrDefault(e => e.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            .OfType<IEffect>().ToList();
        if (starred.Count == 0) return;
        favouritesGroup.Children.Add(new TextBlock { Text = "★ Favourites", FontFamily = Look.Symbols, Opacity = 0.65, FontSize = 12, Margin = new Thickness(0, 4, 0, 2) });
        var grid = new UniformGrid { Columns = 2 };
        foreach (var effect in starred) grid.Children.Add(MakeTile(effect));
        favouritesGroup.Children.Add(grid);
    }

    /// <summary>Shows only the tiles whose name matches the search, opening the groups that have any.</summary>
    void FilterPicker()
    {
        string text = effectSearch.Text?.Trim() ?? "";
        favouritesGroup.IsVisible = text.Length == 0;
        foreach (var (box, category, tiles) in pickerGroups)
        {
            int shown = 0;
            foreach (var (effect, tile) in tiles)
            {
                bool match = text.Length == 0 || effect.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                             || category.Contains(text, StringComparison.OrdinalIgnoreCase);
                tile.IsVisible = match;
                if (match) shown++;
            }
            box.IsVisible = shown > 0;
            box.IsExpanded = text.Length > 0 ? shown > 0 : category == EffectCategory.Basic || category == engine.Effect.Category;
        }
    }

    void OpenGallery()
    {
        if (tabs is not null) tabs.SelectedItem = galleryTab;
    }

    void UpdateGallery()
    {
        bool showing = tabs?.SelectedItem == galleryTab;
        // Hidden in the tray, the gallery isn't kept: the memory goes back, and it is built again when next opened.
        // A showcase still playing keeps it until it ends.
        if (!IsVisible && gallery is { Showcasing: false })
        {
            gallery.Close();
            gallery = null;
            galleryTab.Content = new Panel();
            return;
        }
        if (showing && IsVisible && gallery is null) galleryTab.Content = gallery = new EffectGallery(this, LaptopEffects, HasFourZoneKeyboard);
        gallery?.SetActive(showing && IsVisible);
    }

    EffectEngine IGalleryHost.Engine => engine;

    void IGalleryHost.Choose(IEffect effect)
    {
        if (effect == Effects.Animation && !EnsureAnimation())
        {
            Notify("Build an animation in the Animations tab first.");
            return;
        }
        ChooseEffect(effect);
    }

    void IGalleryHost.SetDemoMusic(bool on)
    {
        demoMusic = on;
        UpdateAudio();
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
        foreach (var (e, tiles) in effectTiles)
            foreach (var tile in tiles) tile.IsChecked = e == effect;
        // Open its group in the list, so an effect picked in the gallery can be found here too.
        if (effectSearch.Text is not { Length: > 0 })
            foreach (var (box, category, _) in pickerGroups)
                if (category == effect.Category) box.IsExpanded = true;
        effectTitle.Text = EffectName(effect);
        SelectEffect(effect);
    }

    // ---- Lighting page ----

    /// <summary>
    /// The keyboard is the centrepiece, with what plays on it named above. Colours, speed and profiles sit in cards
    /// below it; the effects and the rarely changed keyboard settings scroll in a column on the right.
    /// </summary>
    /// <summary>"Currently on Nitro" or "Currently on Helios": which keyboard this window draws and drives.</summary>
    static Control LaptopBadge() => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Margin = new Thickness(0, 0, 0, 10),
        Children =
        {
            new Border
            {
                Background = Look.AccentFill,
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(12, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"Currently on {LaptopLayouts.ShortName(ThisLaptop.Layout)}",
                    Foreground = Look.OnAccent,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 13,
                },
            },
            new TextBlock
            {
                Text = LaptopLayouts.Describe(ThisLaptop.Layout, ThisLaptop.Model),
                Foreground = Look.Muted,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            },
        },
    };

    Control BuildLighting()
    {
        keyboard.KeyPainted += PaintKey;

        var hint = new TextBlock
        {
            Text = HasFourZoneKeyboard
                ? "Click or drag over keys to paint them · each of the 4 zones shows the average of its keys"
                : "Click or drag over keys to paint them with colour 1 · right-click to erase",
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
                    Children = { LaptopBadge(), new TextBlock { Text = "Now playing on your keyboard", Foreground = Look.Muted, FontSize = 13 }, effectTitle },
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
        content.Children.Add(Look.Note(ThisLaptop.HasLightBar
            ? "A profile keeps the effect, colours, speed, brightness and light bar."
            : "A profile keeps the effect, colours, speed and brightness."));
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
                catch (Exception e) { Notify($"Could not change \"{Autostart.OffLabel}\": {e.Message}"); }
                TrayStateChanged?.Invoke();
            };
            content.Children.Add(autostart);
        }

        showOnStart.IsChecked = StartupShowEnabled;
        showOnStart.IsCheckedChanged += (_, _) => StartupShowEnabled = showOnStart.IsChecked == true;
        content.Children.Add(showOnStart);
        var playShow = new Button { Content = "Play the start-up show" };
        playShow.Click += (_, _) => PlayStartupShow();
        content.Children.Add(playShow);

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

        var drawingHeading = Look.Heading("Drawing");
        drawingHeading.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(drawingHeading);
        var graphicsCard = new CheckBox { Content = "Draw this window with the graphics card", IsChecked = AppearanceSettings.UseGraphicsCard };
        graphicsCard.IsCheckedChanged += (_, _) => AppearanceSettings.UseGraphicsCard = graphicsCard.IsChecked == true;
        panel.Children.Add(graphicsCard);
        panel.Children.Add(Look.Note("Off keeps memory low. On can be smoother on a very large screen, but uses about 100 MB more. " +
                                     "It changes the next time RaptorSense starts."));

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
        if (zoneStreamer is not null)
        {
            status.Text = zoneStreamer.Status;
            frameRates.Text = $"{engine.RenderFps:N0} fps rendered, {zoneStreamer.SendFps:N1} zone updates a second";
            return;
        }
        status.Text = engine.Status;
        frameRates.Text = $"{engine.RenderFps:N0} fps rendered, {engine.SendFps:N0} fps sent";
    }

    void SelectEffect(IEffect effect)
    {
        engine.Effect = effect;
        UpdateAudio();
        UpdateKeyHook();
    }

    // Audio capture only exists while the effect follows the music ("Your animation" does only if the animation asks).
    // During the gallery's showcase the demo beat stands in while nothing is playing.
    void UpdateAudio()
    {
        bool needed = engine.Effect.UsesAudio;
        if (needed && audio is null && OperatingSystem.IsWindows())
        {
            try { audio = new LoopbackSpectrum(); }
            catch (Exception e) { Notify($"Could not capture audio: {e.Message}"); }
        }
        engine.Audio = !needed ? null : demoMusic ? new FallbackAudio(audio) : audio;
        if (!needed && audio is not null)
        {
            audio.Dispose();
            audio = null;
        }
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

    static readonly IReadOnlyList<IEffect> LaptopEffects = Effects.For(ThisLaptop.Layout);
    static readonly IReadOnlyList<IEffect> OverlayEffects = LaptopEffects.Where(e => e.WorksAsOverlay).ToList();

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
            Text = HasFourZoneKeyboard
                ? "A typing effect drawn on top of any effect, e.g. Zone flash over Lava lamp."
                : "A typing effect drawn on top of any effect, e.g. lightning on every key press over Aurora.",
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
        // A profile made on the other laptop may name effects this keyboard doesn't have.
        if (!LaptopEffects.Contains(engine.Effect)) engine.Effect = Effects.DefaultFor(ThisLaptop.Layout);
        if (engine.Overlay is { } saved && !OverlayEffects.Contains(saved)) engine.Overlay = null;
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
        if (ThisLaptop.HasLightBar) lightBar.Load(profile.LightBar, send: true);   // null leaves the bar as it is
        profileName.Text = profile.Name;
    }

    void SaveProfile()
    {
        var name = profileName.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { Notify("Type a name for the profile first."); return; }
        EndStartupShow();
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
        if (HasFourZoneKeyboard)
        {
            _ = HandBackZones();
            return;
        }
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

    // ---- Start-up show ----

    const string StartupShowKey = "StartupShow";

    /// <summary>
    /// The party lights when RaptorSense starts, which is when the laptop starts with "Start with Windows" on.
    /// Off unless turned on: the show strobes and flashes white, which nobody should get from an update without
    /// having asked for it.
    /// </summary>
    static bool StartupShowEnabled
    {
        get => AppearanceSettings.Get(StartupShowKey) == "on";
        set => AppearanceSettings.Set(StartupShowKey, value ? "on" : null);
    }

    /// <summary>Plays the show, then goes back to the effect that was playing (unless another one is picked meanwhile).</summary>
    void PlayStartupShow()
    {
        if (engine.Effect != startupShow) afterShow = engine.Effect;
        startupShow.Restart();
        SelectEffect(startupShow);
        effectTitle.Text = startupShow.Name;
        foreach (var tile in effectTiles.Values.SelectMany(t => t)) tile.IsChecked = false;

        showTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) =>
        {
            if (engine.Effect != startupShow) { showTimer!.Stop(); afterShow = null; }   // something else was picked
            else if (startupShow.Finished) EndStartupShow();
        });
        showTimer.Start();
    }

    /// <summary>Ends the show now, if it is playing, so nothing saves it in place of the user's effect.</summary>
    void EndStartupShow()
    {
        showTimer?.Stop();
        if (engine.Effect == startupShow) ChooseEffect(afterShow ?? Effects.DefaultFor(ThisLaptop.Layout));
        afterShow = null;
    }

    // The Nitro's own rainbow wave, through the helper.
    async Task HandBackZones()
    {
        var status = await ThermalClient.SetKeyboardAsync(new FourZoneRequest
        {
            Colors = [Rgb.White.ToString()], Mode = FourZoneMode.Wave, Speed = FourZoneProtocol.DefaultSpeed,
        });
        if (!status.Ok) Notify($"Could not set the keyboard effect: {status.Error ?? ThermalClient.HelperMissing}");
    }

    void RefreshPreview()
    {
        if (WindowState == WindowState.Minimized) return;   // nothing of it is on screen
        engine.CopyLatestFrame(preview);
        if (HasFourZoneKeyboard)
        {
            // Shown as the keyboard will light: every key in a zone takes the zone's colour.
            FourZoneLayout.Spread(FourZoneLayout.Average(preview.Cells), zonePreview);
            keyboard.Show(zonePreview);
        }
        else keyboard.Show(preview.Cells);

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
        if (OperatingSystem.IsWindows() && TryGetPlatformHandle() is { } handle) WindowActivation.BringToFront(handle.Handle);
    }

    // Closing the window only hides it: the lighting keeps running from the tray. Quit is in the tray menu.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!shutDown && e.CloseReason is WindowCloseReason.WindowClosing)
        {
            e.Cancel = true;
            SaveLast();
            Hide();
            ReleaseMemorySoon();
        }
        base.OnClosing(e);
    }

    /// <summary>
    /// With the window out of sight, hands back the memory that showing it needed: one full, compacting collection
    /// a moment after it closes. It runs only then, never while the app just sits in the tray.
    /// </summary>
    void ReleaseMemorySoon() => DispatcherTimer.RunOnce(() =>
    {
        if (!IsVisible) GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }, TimeSpan.FromSeconds(2));

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // No preview to draw while hidden.
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible) uiTimer.Start(); else uiTimer.Stop();
            UpdateGallery();
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
        if (OperatingSystem.IsWindows()) specialKeys?.Dispose();
        zoneStreamer?.Dispose();
        engine.Dispose();
        audio?.Dispose();
    }

    void SaveLast()
    {
        // While a rule is showing its own lighting, the user's setup is what it will go back to.
        EndStartupShow();
        try { ProfileStore.SaveLast(automations.LightingBefore ?? Profile.Capture("", engine, lightBar.Current)); }
        catch (IOException) { }
    }

    // Musical note and keyboard symbols. This file is UTF-8: edit it with UTF-8-aware tools (Windows PowerShell 5.1's
    // Get-Content/Set-Content without -Encoding utf8 garbles these).
    static string DisplayName(IEffect e) => e.UsesAudio ? $"{EffectName(e)}  ♪" : e.UsesKeys ? $"{EffectName(e)}  ⌨" : EffectName(e);

    // On the Nitro, painting colours whole zones.
    static string EffectName(IEffect e) => HasFourZoneKeyboard && e == Effects.Painted ? "Paint zones" : e.Name;

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
