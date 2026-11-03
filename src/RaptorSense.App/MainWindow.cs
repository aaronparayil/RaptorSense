using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RaptorSense.Audio;
using RaptorSense.Hardware;
using RaptorSense.Input;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Profiles;
using IEffect = RaptorSense.Lighting.IEffect;

namespace RaptorSense.App;

sealed class MainWindow : Window
{
    static readonly Rgb[] Swatches =
    [
        Rgb.PredatorTeal, new(255, 0, 0), new(255, 100, 0), new(255, 220, 0), new(0, 255, 60),
        new(0, 90, 255), new(140, 0, 255), new(255, 0, 128), Rgb.White,
    ];

    readonly EffectEngine engine = new(Ph16Keyboard.TryOpen);
    readonly Frame preview = new();
    readonly Dictionary<int, (SolidColorBrush Fill, TextBlock Label)> keys = [];
    readonly TextBlock status = new() { Margin = new Thickness(16, 10), Opacity = 0.8 };
    readonly ListBox effectList = new() { ItemsSource = Effects.All.Select(DisplayName).ToList() };
    readonly TextBox color1 = new() { Width = 90, MaxLength = 7 };
    readonly TextBox color2 = new() { Width = 90, MaxLength = 7 };
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

        var root = new DockPanel();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        var controls = BuildControls();
        DockPanel.SetDock(controls, Dock.Right);
        root.Children.Add(controls);
        var hint = new TextBlock
        {
            Text = "Click or drag over keys to paint them with colour 1 · right-click to erase",
            Opacity = 0.6,
            Margin = new Thickness(24, 16, 24, 0),
        };
        DockPanel.SetDock(hint, Dock.Top);
        root.Children.Add(hint);
        root.Children.Add(new Viewbox { Margin = new Thickness(24), Child = BuildKeyboard() });
        Content = root;

        ApplyProfile(ProfileStore.LoadLast() ?? new Profile());
        RefreshProfileList();

        engine.Start();
        uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => RefreshPreview());
        uiTimer.Start();
    }

    // ---- Keyboard preview and painting ----

    // Key caps show the symbols printed on them; anything not listed shows its key-map label.
    static readonly Dictionary<string, string> CapText = new()
    {
        ["`"] = "~\n`", ["-"] = "_\n-", ["="] = "+\n=", ["["] = "{\n[", ["]"] = "}\n]", ["\\"] = "|\n\\",
        [";"] = ":\n;", ["'"] = "\"\n'", [","] = "<\n,", ["."] = ">\n.", ["/"] = "?\n/",
        ["Bksp"] = "Backspace", ["Caps"] = "Caps Lock", ["RShift"] = "Shift", ["RCtrl"] = "Ctrl",
        ["Up"] = "▲", ["Down"] = "▼", ["Left"] = "◀", ["Right"] = "▶",
        ["Prev"] = "⏮", ["Play"] = "⏯", ["Next"] = "⏭", ["Power"] = "⏻",
        ["Num/"] = "/", ["Num*"] = "*", ["Num-"] = "-", ["Num+"] = "+", ["NumEnt"] = "Enter", ["Num."] = ".",
        ["Num0"] = "0", ["Num1"] = "1", ["Num2"] = "2", ["Num3"] = "3", ["Num4"] = "4",
        ["Num5"] = "5", ["Num6"] = "6", ["Num7"] = "7", ["Num8"] = "8", ["Num9"] = "9",
    };

    Canvas BuildKeyboard()
    {
        var canvas = new Canvas { Width = PhysicalLayout.Width, Height = PhysicalLayout.Height, Background = Brushes.Transparent };
        foreach (var key in KeyMap.Keys)
        {
            if (PhysicalLayout.For(key.Label) is not { } rect) continue;
            var fill = new SolidColorBrush(Colors.Black);
            var text = CapText.GetValueOrDefault(key.Label, key.Label);
            // Shrink long labels to fit the key's width (about 0.62 em per character) instead of wrapping.
            int longest = text.Split('\n').Max(line => line.Length);
            double fontSize = Math.Clamp((rect.Width - 10) / (longest * 0.62), 8, rect.Height < 40 ? 10 : 13);
            var label = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                LineHeight = fontSize * 1.3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var view = new Border
            {
                Width = rect.Width,
                Height = rect.Height,
                CornerRadius = new CornerRadius(7),
                Background = fill,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 62, 70)),
                BorderThickness = new Thickness(1),
                Child = label,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(view, $"{key.Label} (cell {key.Cell})");
            Canvas.SetLeft(view, rect.X);
            Canvas.SetTop(view, rect.Y);
            canvas.Children.Add(view);
            keys[key.Cell] = (fill, label);
        }

        // Handled on the canvas, not the keys: the pressed element captures the pointer, so a drag would
        // never reach the other keys. Hit-testing by position paints every key the pointer crosses.
        canvas.PointerPressed += (_, e) => PaintAt(canvas, e);
        canvas.PointerMoved += (_, e) => PaintAt(canvas, e);
        return canvas;
    }

    void PaintAt(Canvas canvas, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(canvas);
        bool paint = point.Properties.IsLeftButtonPressed, erase = point.Properties.IsRightButtonPressed;
        if (!paint && !erase) return;

        var hit = KeyMap.Keys.FirstOrDefault(k => PhysicalLayout.For(k.Label)?.Contains(point.Position.X, point.Position.Y) == true);
        if (hit is null) return;   // between keys
        int cell = hit.Cell;

        if (engine.Effect != Effects.Painted) StartPainting();
        Effects.Painted[cell] = erase ? Rgb.Black : engine.Settings.Color;
        e.Handled = true;
    }

    /// <summary>Switches to the painted layout, starting from what the keyboard shows now so nothing jumps.</summary>
    void StartPainting()
    {
        engine.CopyLatestFrame(preview);
        for (int cell = 0; cell < Frame.CellCount; cell++) Effects.Painted[cell] = preview[cell];
        effectList.SelectedIndex = IndexOf(Effects.Painted);
    }

    // ---- Side panel ----

    Control BuildControls()
    {
        var panel = new StackPanel { Spacing = 8, Width = 330, Margin = new Thickness(0, 16, 20, 16) };

        panel.Children.Add(Heading("Effect"));
        effectList.SelectionChanged += (_, _) =>
        {
            if (effectList.SelectedIndex >= 0) SelectEffect(Effects.All[effectList.SelectedIndex]);
        };
        panel.Children.Add(effectList);

        panel.Children.Add(Heading("Colour 1 (also the paint brush)"));
        panel.Children.Add(ColorRow(color1, c => engine.Settings.Color = c));
        panel.Children.Add(Heading("Colour 2"));
        panel.Children.Add(ColorRow(color2, c => engine.Settings.Color2 = c));

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

    Control ColorRow(TextBox hex, Action<Rgb> set)
    {
        hex.TextChanged += (_, _) => { if (Rgb.TryParse(hex.Text, out var c)) set(c); };

        var swatches = new WrapPanel();
        foreach (var swatch in Swatches)
        {
            var button = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 4, 4),
                Background = new SolidColorBrush(ToColor(swatch)),
                CornerRadius = new CornerRadius(13),
            };
            ToolTip.SetTip(button, "#" + swatch);
            button.Click += (_, _) => hex.Text = swatch.ToString();
            swatches.Children.Add(button);
        }

        var row = new StackPanel { Spacing = 4 };
        row.Children.Add(swatches);
        row.Children.Add(hex);
        return row;
    }

    void SelectEffect(IEffect effect)
    {
        if (effect.UsesAudio && audio is null && OperatingSystem.IsWindows())
        {
            try { audio = new LoopbackSpectrum(); engine.Audio = audio; }
            catch (Exception e) { Notify($"Could not capture audio: {e.Message}"); }
        }

        // The key hook only exists while a reactive effect is selected.
        if (effect.UsesKeys && keyHook is null && OperatingSystem.IsWindows())
        {
            try { keyHook = new WindowsKeyHook(); keyHook.CellPressed += engine.NotifyKeyPress; }
            catch (Exception e) { Notify($"Could not watch key presses: {e.Message}"); }
        }
        else if (!effect.UsesKeys && keyHook is not null)
        {
            keyHook.Dispose();
            keyHook = null;
        }

        engine.Effect = effect;
    }

    // ---- Profiles ----

    void ApplyProfile(Profile profile)
    {
        profile.ApplyTo(engine);
        color1.Text = engine.Settings.Color.ToString();
        color2.Text = engine.Settings.Color2.ToString();
        speed.Value = engine.Settings.Speed;
        if (engine.Brightness >= 0)
        {
            brightness.Value = engine.Brightness;
            brightness.IsEnabled = brightnessSynced = true;
        }
        int index = IndexOf(engine.Effect);
        if (effectList.SelectedIndex == index) SelectEffect(engine.Effect);   // re-run hook/audio setup
        else effectList.SelectedIndex = index;
        profileName.Text = profile.Name;
    }

    void SaveProfile()
    {
        var name = profileName.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { Notify("Type a name for the profile first."); return; }
        ProfileStore.Save(Profile.Capture(name, engine));
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
        foreach (var (cell, view) in keys)
        {
            var c = preview[cell];
            view.Fill.Color = ToColor(c);
            view.Label.Foreground = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 140 ? Brushes.Black : Brushes.White;
        }

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
        uiTimer.Stop();
        keyHook?.Dispose();
        engine.Dispose();
        audio?.Dispose();
    }

    void SaveLast()
    {
        try { ProfileStore.SaveLast(Profile.Capture("", engine)); }
        catch (IOException) { }
    }

    static int IndexOf(IEffect effect) => Effects.All.ToList().IndexOf(effect);

    // ♪ = musical note, ⌨ = keyboard. Escaped so no editor or shell encoding can mangle them.
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

    static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
}
