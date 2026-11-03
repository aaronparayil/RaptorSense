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
    const double KeySize = 46, KeyGap = 5, Pitch = KeySize + KeyGap;

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
    readonly DispatcherTimer uiTimer;
    IAudioSpectrum? audio;
    IKeyPressSource? keyHook;
    bool brightnessSynced;
    string? notice;
    DateTime noticeUntil;

    public MainWindow()
    {
        Title = $"{AppInfo.Name} {AppInfo.Version}";
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
            Text = "Click or drag over keys to paint them with colour 1 Â· right-click to erase",
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

    Canvas BuildKeyboard()
    {
        var canvas = new Canvas { Width = KeyMap.Columns * Pitch, Height = KeyMap.Rows * Pitch, Background = Brushes.Transparent };
        foreach (var key in KeyMap.Keys)
        {
            var fill = new SolidColorBrush(Colors.Black);
            var label = new TextBlock
            {
                Text = key.Label,
                FontSize = key.Label.Length > 4 ? 9 : 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var view = new Border
            {
                Width = KeySize,
                Height = KeySize,
                CornerRadius = new CornerRadius(6),
                Background = fill,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 62, 70)),
                BorderThickness = new Thickness(1),
                Child = label,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            Canvas.SetLeft(view, key.Column * Pitch);
            Canvas.SetTop(view, (KeyMap.Rows - 1 - key.Row) * Pitch);
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

        int column = (int)(point.Position.X / Pitch);
        int row = KeyMap.Rows - 1 - (int)(point.Position.Y / Pitch);
        if (point.Position.X % Pitch > KeySize || point.Position.Y % Pitch > KeySize) return;   // in a gap
        int cell = column * KeyMap.Rows + row;
        if (!keys.ContainsKey(cell)) return;

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

        var output = new ToggleSwitch { IsChecked = true, OnContent = "Controlling the keyboard", OffContent = "Preview only" };
        output.IsCheckedChanged += (_, _) => engine.OutputEnabled = output.IsChecked == true;
        panel.Children.Add(output);

        var handBack = new Button { Content = "Hand back: keyboard's own rainbow wave" };
        handBack.Click += (_, _) => HandBack(output);
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
        Notify($"Saved profile '{name}'.");
    }

    void LoadSelectedProfile()
    {
        if (profileList.SelectedItem is not string name) return;
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
        Notify($"Deleted profile '{name}'.");
    }

    void RefreshProfileList() => profileList.ItemsSource = ProfileStore.List();

    // ---- Misc ----

    void HandBack(ToggleSwitch output)
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
            : $"{engine.Status}   Â·   {engine.RenderFps:N0} fps rendered, {engine.SendFps:N0} fps sent";
    }

    /// <summary>Shows a message in the status bar for a few seconds instead of the live engine status.</summary>
    void Notify(string message)
    {
        notice = message;
        noticeUntil = DateTime.UtcNow.AddSeconds(3);
        status.Text = message;
    }

    protected override void OnClosed(EventArgs e)
    {
        try { ProfileStore.SaveLast(Profile.Capture("", engine)); }
        catch (IOException) { }
        uiTimer.Stop();
        keyHook?.Dispose();
        engine.Dispose();
        audio?.Dispose();
        base.OnClosed(e);
    }

    static int IndexOf(IEffect effect) => Effects.All.ToList().IndexOf(effect);

    static string DisplayName(IEffect e) => e.UsesAudio ? $"{e.Name}  â™ª" : e.UsesKeys ? $"{e.Name}  âŒ¨" : e.Name;

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
