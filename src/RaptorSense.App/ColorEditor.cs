using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RaptorSense.Lighting;
using RaptorSense.Profiles;

namespace RaptorSense.App;

/// <summary>
/// Edits colour 1 and colour 2: pick which with the chips, then use the wheel, brightness, R/G/B, hex or a swatch.
/// The eyedropper takes the colour of the next key clicked in the preview.
/// </summary>
sealed class ColorEditor : UserControl
{
    const int MaxCustomSwatches = 16;

    static readonly Rgb[] Presets =
    [
        Rgb.PredatorTeal, new(255, 0, 0), new(255, 100, 0), new(255, 220, 0), new(0, 255, 60),
        new(0, 90, 255), new(140, 0, 255), new(255, 0, 128), Rgb.White,
    ];

    static readonly IBrush Selected = new SolidColorBrush(Color.FromRgb(0, 174, 199));
    static readonly IBrush Unselected = new SolidColorBrush(Color.FromRgb(60, 62, 70));

    readonly string[] names;
    readonly Rgb[] colors;
    readonly (Border Chip, Border Fill)[] chips;
    readonly ColorWheel wheel = new();
    readonly Slider brightness = new() { Minimum = 0, Maximum = 1, Value = 1 };
    readonly NumericUpDown red = Channel(), green = Channel(), blue = Channel();
    readonly TextBox hex = new() { Width = 90, MaxLength = 7 };
    readonly WrapPanel swatches = new();
    readonly ToggleButton eyedropper = new() { Content = "Eyedropper" };
    readonly List<Rgb> custom = ProfileStore.LoadSwatches().Select(s => Rgb.TryParse(s, out var c) ? c : (Rgb?)null).OfType<Rgb>().ToList();
    int target;
    bool updating;

    /// <summary>Raised with the index of the colour being edited (0 = first chip) and its new colour.</summary>
    public event Action<int, Rgb>? ColorChanged;

    /// <summary>True while the eyedropper waits for a key to be clicked.</summary>
    public bool Picking => eyedropper.IsChecked == true;

    /// <summary>The colour of the chip being edited.</summary>
    public Rgb CurrentColor => colors[target];

    /// <param name="names">One chip per name. Defaults to the keyboard's "Colour 1" and "Colour 2".</param>
    /// <param name="initial">Starting colours, one per name (otherwise teal, then pink).</param>
    /// <param name="withEyedropper">The eyedropper takes a colour from the keyboard preview, so only the lighting tab has it.</param>
    public ColorEditor(string[]? names = null, Rgb[]? initial = null, bool withEyedropper = true)
    {
        this.names = names ?? ["Colour 1", "Colour 2"];
        colors = initial is not null && initial.Length == this.names.Length
            ? [.. initial]
            : [.. this.names.Select((_, i) => i == 0 ? Rgb.PredatorTeal : new Rgb(255, 0, 128))];
        chips = new (Border, Border)[this.names.Length];

        var chipRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        for (int i = 0; i < this.names.Length; i++) chipRow.Children.Add(BuildChip(i));

        wheel.Changed += () => Set(Rgb.FromHsv(wheel.Hue, wheel.Saturation, brightness.Value), wheel);
        brightness.ValueChanged += (_, e) =>
        {
            if (updating) return;
            wheel.SetHsv(wheel.Hue, wheel.Saturation, e.NewValue);
            Set(Rgb.FromHsv(wheel.Hue, wheel.Saturation, e.NewValue), brightness);
        };
        foreach (var box in new[] { red, green, blue })
            box.ValueChanged += (_, _) => { if (!updating) Set(new Rgb((byte)(red.Value ?? 0), (byte)(green.Value ?? 0), (byte)(blue.Value ?? 0)), box); };
        hex.TextChanged += (_, _) => { if (!updating && Rgb.TryParse(hex.Text, out var c)) Set(c, hex); };
        ToolTip.SetTip(eyedropper, "Then click a key in the preview to take its colour");

        var numbers = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        numbers.Children.Add(Labelled("R", red));
        numbers.Children.Add(Labelled("G", green));
        numbers.Children.Add(Labelled("B", blue));
        numbers.Children.Add(Labelled("#", hex));

        var picker = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        picker.Children.Add(wheel);
        picker.Children.Add(numbers);

        var layout = new StackPanel { Spacing = 8 };
        layout.Children.Add(chipRow);
        layout.Children.Add(picker);
        layout.Children.Add(new DockPanel
        {
            Children = { Docked(new TextBlock { Text = "Brightness", Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center }, Dock.Left), brightness },
        });
        layout.Children.Add(swatches);
        if (withEyedropper) layout.Children.Add(eyedropper);
        Content = layout;

        RebuildSwatches();
        ShowTarget();
    }

    /// <summary>Shows colours loaded from a profile, without raising <see cref="ColorChanged"/>.</summary>
    public void SetColors(Rgb color1, Rgb color2)
    {
        colors[0] = color1;
        colors[1] = color2;
        RefreshChips();
        ShowTarget();
    }

    /// <summary>Shows all the colours at once (one per chip), without raising <see cref="ColorChanged"/>.</summary>
    public void SetColors(IReadOnlyList<Rgb> all)
    {
        for (int i = 0; i < colors.Length && i < all.Count; i++) colors[i] = all[i];
        RefreshChips();
        ShowTarget();
    }

    /// <summary>The eyedropper's result: applies it to the colour being edited.</summary>
    public void Pick(Rgb color)
    {
        eyedropper.IsChecked = false;
        Set(color, null);
    }

    void Set(Rgb color, object? source)
    {
        colors[target] = color;
        ColorChanged?.Invoke(target, color);
        RefreshChips();
        Show(color, source);
    }

    void ShowTarget() => Show(colors[target], null);

    // Updates every input except the one being typed or dragged in, so nothing jumps under the pointer.
    void Show(Rgb color, object? source)
    {
        updating = true;
        var (h, s, v) = color.ToHsv();
        if (source is null || source == red || source == green || source == blue || source == hex)
        {
            // A black or grey colour has no hue: keep the wheel's, so it doesn't snap to red.
            wheel.SetHsv(s > 0 ? h : wheel.Hue, v > 0 ? s : wheel.Saturation, v);
            brightness.Value = v;
        }
        if (source != red) red.Value = color.R;
        if (source != green) green.Value = color.G;
        if (source != blue) blue.Value = color.B;
        if (source != hex) hex.Text = color.ToString();
        updating = false;
    }

    Control BuildChip(int index)
    {
        var fill = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11) };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            Padding = new Thickness(8, 5),
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { fill, new TextBlock { Text = names[index], VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        chip.PointerPressed += (_, _) =>
        {
            target = index;
            RefreshChips();
            ShowTarget();
        };
        chips[index] = (chip, fill);
        RefreshChips();
        return chip;
    }

    void RefreshChips()
    {
        for (int i = 0; i < chips.Length; i++)
        {
            if (chips[i].Chip is null) continue;
            chips[i].Fill.Background = new SolidColorBrush(ToColor(colors[i]));
            chips[i].Chip.BorderBrush = i == target ? Selected : Unselected;
        }
    }

    void RebuildSwatches()
    {
        swatches.Children.Clear();
        foreach (var preset in Presets) swatches.Children.Add(Swatch(preset, removable: false));
        foreach (var saved in custom) swatches.Children.Add(Swatch(saved, removable: true));

        var add = new Button { Content = "+", Width = 26, Height = 26, Padding = new Thickness(0), CornerRadius = new CornerRadius(13) };
        ToolTip.SetTip(add, "Save the current colour as a swatch");
        add.Click += (_, _) =>
        {
            var color = colors[target];
            if (Presets.Contains(color) || custom.Contains(color)) return;
            custom.Add(color);
            if (custom.Count > MaxCustomSwatches) custom.RemoveAt(0);
            SaveSwatches();
        };
        swatches.Children.Add(add);
    }

    Control Swatch(Rgb color, bool removable)
    {
        var button = new Button
        {
            Width = 26,
            Height = 26,
            Margin = new Thickness(0, 0, 4, 4),
            Background = new SolidColorBrush(ToColor(color)),
            CornerRadius = new CornerRadius(13),
        };
        ToolTip.SetTip(button, removable ? $"#{color}  (right-click to remove)" : $"#{color}");
        button.Click += (_, _) => Set(color, null);
        if (removable)
        {
            button.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Right) return;
                custom.Remove(color);
                SaveSwatches();
            };
        }
        return button;
    }

    void SaveSwatches()
    {
        try { ProfileStore.SaveSwatches(custom.Select(c => c.ToString())); }
        catch (IOException) { }
        RebuildSwatches();
    }

    static NumericUpDown Channel() => new()
    {
        Minimum = 0, Maximum = 255, Increment = 1, FormatString = "0", Width = 110, Value = 0,
    };

    static Control Labelled(string label, Control input) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        Children = { new TextBlock { Text = label, Width = 12, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 }, input },
    };

    static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        control.Margin = new Thickness(0, 0, 10, 0);
        return control;
    }

    static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);
}
