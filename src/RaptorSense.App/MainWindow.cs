using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RaptorSense.Audio;
using RaptorSense.Hardware;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using IEffect = RaptorSense.Lighting.IEffect;

namespace RaptorSense.App;

sealed class MainWindow : Window
{
    const double KeySize = 46, KeyGap = 5;

    static readonly Rgb[] Swatches =
    [
        Rgb.PredatorTeal, new(255, 0, 0), new(255, 100, 0), new(255, 220, 0), new(0, 255, 60),
        new(0, 90, 255), new(140, 0, 255), new(255, 0, 128), Rgb.White,
    ];

    readonly EffectEngine engine = new(Ph16Keyboard.TryOpen);
    readonly Frame preview = new();
    readonly Dictionary<int, (SolidColorBrush Fill, TextBlock Label)> keys = [];
    readonly TextBlock status = new() { Margin = new Thickness(16, 10), Opacity = 0.8 };
    readonly Slider brightness = new() { Minimum = 0, Maximum = Ph16Keyboard.MaxBrightness, IsEnabled = false };
    readonly DispatcherTimer uiTimer;
    IAudioSpectrum? audio;
    bool brightnessSynced;

    public MainWindow()
    {
        Title = $"{AppInfo.Name} {AppInfo.Version}";
        Width = 1500;
        Height = 560;
        MinWidth = 900;
        MinHeight = 420;
        Background = new SolidColorBrush(Color.FromRgb(16, 17, 20));

        var root = new DockPanel();
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        var controls = BuildControls();
        DockPanel.SetDock(controls, Dock.Right);
        root.Children.Add(controls);
        root.Children.Add(new Viewbox { Margin = new Thickness(24), Child = BuildKeyboard() });
        Content = root;

        engine.Start();
        uiTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Render, (_, _) => RefreshPreview());
        uiTimer.Start();
    }

    Canvas BuildKeyboard()
    {
        const double pitch = KeySize + KeyGap;
        var canvas = new Canvas { Width = KeyMap.Columns * pitch, Height = KeyMap.Rows * pitch };
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
            };
            ToolTip.SetTip(view, key.Confirmed ? $"{key.Label} (cell {key.Cell})" : $"{key.Label} (cell {key.Cell}, position not confirmed)");
            Canvas.SetLeft(view, key.Column * pitch);
            Canvas.SetTop(view, (KeyMap.Rows - 1 - key.Row) * pitch);
            canvas.Children.Add(view);
            keys[key.Cell] = (fill, label);
        }
        return canvas;
    }

    Control BuildControls()
    {
        var panel = new StackPanel { Spacing = 10, Width = 320, Margin = new Thickness(0, 20, 20, 20) };

        panel.Children.Add(Heading("Effect"));
        var effects = new ListBox { ItemsSource = Effects.All.Select(e => e.UsesAudio ? $"{e.Name}  ♪" : e.Name).ToList(), SelectedIndex = 0 };
        effects.SelectionChanged += (_, _) =>
        {
            if (effects.SelectedIndex >= 0) SelectEffect(Effects.All[effects.SelectedIndex]);
        };
        panel.Children.Add(effects);

        panel.Children.Add(Heading("Colour 1"));
        panel.Children.Add(ColorRow(() => engine.Settings.Color, c => engine.Settings.Color = c));
        panel.Children.Add(Heading("Colour 2"));
        panel.Children.Add(ColorRow(() => engine.Settings.Color2, c => engine.Settings.Color2 = c));

        panel.Children.Add(Heading("Speed"));
        var speed = new Slider { Minimum = 0.1, Maximum = 4, Value = engine.Settings.Speed };
        speed.ValueChanged += (_, e) => engine.Settings.Speed = e.NewValue;
        panel.Children.Add(speed);

        panel.Children.Add(Heading("Brightness"));
        brightness.ValueChanged += (_, e) => { if (brightnessSynced) engine.Brightness = (int)Math.Round(e.NewValue); };
        panel.Children.Add(brightness);

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
            Margin = new Thickness(0, 16, 0, 0),
        });

        return new ScrollViewer { Content = panel };
    }

    Control ColorRow(Func<Rgb> get, Action<Rgb> set)
    {
        var hex = new TextBox { Text = get().ToString(), Width = 90, MaxLength = 7 };
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
            button.Click += (_, _) => { set(swatch); hex.Text = swatch.ToString(); };
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
            catch (Exception e) { status.Text = $"Could not capture audio: {e.Message}"; }
        }
        engine.Effect = effect;
    }

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
            catch (Exception e) { status.Text = $"Could not set the keyboard effect: {e.Message}"; }
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
            brightness.IsEnabled = true;
            brightnessSynced = true;
        }
        status.Text = $"{engine.Status}   ·   {engine.RenderFps:N0} fps rendered, {engine.SendFps:N0} fps sent";
    }

    protected override void OnClosed(EventArgs e)
    {
        uiTimer.Stop();
        engine.Dispose();
        audio?.Dispose();
        base.OnClosed(e);
    }

    static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
}
