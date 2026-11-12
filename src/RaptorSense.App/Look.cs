using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace RaptorSense.App;

/// <summary>
/// The app's look: deep indigo, a violet-to-sky accent, rounded cards and pill buttons. Everything here is a flat
/// colour or a fixed gradient, so the look costs nothing while the window sits idle.
/// </summary>
static class Look
{
    public static readonly Color GroundColor = Color.FromRgb(11, 11, 30);
    public static readonly Color VioletColor = Color.FromRgb(167, 139, 250);
    public static readonly Color SkyColor = Color.FromRgb(56, 189, 248);
    static readonly Color TextColor = Color.FromRgb(244, 243, 255);
    static readonly Color MutedColor = Color.FromRgb(180, 179, 209);
    static readonly Color PopupColor = Color.FromRgb(23, 22, 58);

    public static readonly IBrush Ground = new ImmutableSolidColorBrush(GroundColor);
    public static readonly IBrush Text = new ImmutableSolidColorBrush(TextColor);
    /// <summary>Supporting text: captions, hints and notes.</summary>
    public static readonly IBrush Muted = new ImmutableSolidColorBrush(MutedColor);
    /// <summary>The single accent colour, for text and thin outlines.</summary>
    public static readonly IBrush Accent = new ImmutableSolidColorBrush(VioletColor);
    /// <summary>The accent as a fill, for whatever is selected or switched on. Text on it uses <see cref="OnAccent"/>.</summary>
    public static readonly IBrush AccentFill = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(VioletColor, 0), new GradientStop(SkyColor, 1) },
    }.ToImmutable();
    public static readonly IBrush OnAccent = Ground;
    public static readonly IBrush Warning = new ImmutableSolidColorBrush(Color.FromRgb(253, 186, 116));

    // These four follow the background colour the user picks, so they are brushes whose colour can change.
    static readonly SolidColorBrush PopupBrush = new(PopupColor);
    static readonly SolidColorBrush WellBrush = new(GroundColor, 0.55);
    static readonly SolidColorBrush ToolTipBrush = new(Color.FromRgb(31, 30, 72));
    static readonly SolidColorBrush FocusedFieldBrush = new(Color.FromRgb(16, 15, 43));

    /// <summary>The solid surface of menus, drop-down lists and messages that float over the window.</summary>
    public static IBrush Popup => PopupBrush;

    public static readonly IBrush CardFill = Wash(0.05);
    public static readonly IBrush CardBorder = Wash(0.09);
    /// <summary>The darker well the keyboard sits in.</summary>
    public static IBrush Well => WellBrush;
    public static readonly IBrush WellBorder = Wash(0.10);
    public static readonly IBrush KeyBorder = Wash(0.14);
    public static readonly IBrush Unselected = Wash(0.16);

    // ---- Background colour ----

    /// <summary>The background the app ships with.</summary>
    public static readonly Color DefaultBackground = Color.FromRgb(42, 31, 99);

    /// <summary>Ready-made backgrounds for the info panel; the first is the default.</summary>
    public static readonly IReadOnlyList<(string Name, Color Color)> BackgroundPresets =
    [
        ("Indigo", DefaultBackground),
        ("Ocean", Color.FromRgb(16, 52, 110)),
        ("Teal", Color.FromRgb(10, 74, 82)),
        ("Forest", Color.FromRgb(18, 74, 44)),
        ("Wine", Color.FromRgb(98, 20, 48)),
        ("Ember", Color.FromRgb(104, 46, 16)),
        ("Plum", Color.FromRgb(78, 24, 96)),
        ("Graphite", Color.FromRgb(54, 56, 64)),
        ("Black", Colors.Black),
    ];

    // The brightest the middle of the background may be: any brighter and the light text gets hard to read.
    const double MaxGlow = 0.42;

    static string BackgroundPath => Path.Combine(RaptorSense.Profiles.ProfileStore.Root, "appearance.json");

    /// <summary>The background colour as the user chose it (before it is toned down).</summary>
    public static Color Background { get; private set; } = DefaultBackground;

    /// <summary>A soft glow of the chosen colour behind the top half of the window, fading darker to the edges.</summary>
    public static IBrush WindowBackground { get; private set; } = BackgroundBrush(DefaultBackground);

    /// <summary>
    /// Changes the window's background colour. The window has to take <see cref="WindowBackground"/> again; menus,
    /// tooltips and the keyboard's well follow by themselves.
    /// </summary>
    public static void SetBackground(Color chosen)
    {
        Background = Color.FromRgb(chosen.R, chosen.G, chosen.B);
        var glow = Glow(Background);
        WindowBackground = BackgroundBrush(glow);
        PopupBrush.Color = Lift(Scale(glow, 0.5), 8);
        ToolTipBrush.Color = Lift(Scale(glow, 0.66), 10);
        FocusedFieldBrush.Color = Scale(glow, 0.4);
        WellBrush.Color = Scale(glow, 0.28);
    }

    /// <summary>Uses the background saved last time, if there is one.</summary>
    public static void LoadBackground()
    {
        try
        {
            if (!File.Exists(BackgroundPath)) return;
            var saved = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(BackgroundPath));
            if (saved is not null && saved.TryGetValue("Background", out var hex) && RaptorSense.Lighting.Rgb.TryParse(hex, out var c))
                SetBackground(Color.FromRgb(c.R, c.G, c.B));
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or IOException or UnauthorizedAccessException) { }
    }

    public static void SaveBackground()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackgroundPath)!);
            var hex = new RaptorSense.Lighting.Rgb(Background.R, Background.G, Background.B).ToString();
            File.WriteAllText(BackgroundPath, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["Background"] = hex }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    static IBrush BackgroundBrush(Color glow) => new RadialGradientBrush
    {
        Center = new RelativePoint(0.5, 0.36, RelativeUnit.Relative),
        GradientOrigin = new RelativePoint(0.5, 0.36, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(0.72, RelativeUnit.Relative),
        RadiusY = new RelativeScalar(0.72, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(glow, 0),
            new GradientStop(Scale(glow, 0.55), 0.45),
            new GradientStop(Scale(glow, 0.28), 1),
        },
    }.ToImmutable();

    static Color Glow(Color chosen)
    {
        double brightest = Math.Max(chosen.R, Math.Max(chosen.G, chosen.B)) / 255.0;
        return brightest > MaxGlow ? Scale(chosen, MaxGlow / brightest) : chosen;
    }

    /// <summary>The same hue, bright enough to recognise on a small swatch.</summary>
    public static Color Brightened(Color c)
    {
        int brightest = Math.Max(c.R, Math.Max(c.G, c.B));
        return brightest == 0 ? c : Scale(c, 190.0 / brightest);
    }

    static Color Scale(Color c, double by) => Color.FromRgb((byte)Math.Round(c.R * by), (byte)Math.Round(c.G * by), (byte)Math.Round(c.B * by));

    static Color Lift(Color c, int by) => Color.FromRgb((byte)Math.Min(255, c.R + by), (byte)Math.Min(255, c.G + by), (byte)Math.Min(255, c.B + by));

    // Ships with Windows 11, so nothing has to be bundled; where it is missing the system's default font is used.
    // One family only: with a list, the arrows and media keys fall back to the colour emoji font.
    public static readonly FontFamily Font = new("Segoe UI Variable Display");

    /// <summary>For arrows and media-key symbols, which otherwise come out as colour emoji.</summary>
    public static readonly FontFamily Symbols = new("Segoe UI Symbol");

    /// <summary>White at the given strength: the translucent surfaces that sit on the background.</summary>
    static IBrush Wash(double opacity) => new ImmutableSolidColorBrush(Colors.White, opacity);

    /// <summary>A rounded translucent panel, optionally with a title above its content.</summary>
    public static Border Card(Control content, string? title = null)
    {
        Control child = content;
        if (title is not null)
        {
            var stack = new DockPanel();
            var heading = Heading(title);
            heading.Margin = new Thickness(0, 0, 0, 12);
            DockPanel.SetDock(heading, Dock.Top);
            stack.Children.Add(heading);
            stack.Children.Add(content);
            child = stack;
        }
        return new Border
        {
            Background = CardFill,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(20, 18),
            Child = child,
        };
    }

    public static TextBlock Heading(string text) => new() { Text = text, FontSize = 15, FontWeight = FontWeight.SemiBold };

    /// <summary>Small supporting text that wraps.</summary>
    public static TextBlock Note(string text) => new() { Text = text, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap };

    /// <summary>Marks a button as the selected one of a set: it takes the accent fill.</summary>
    public static void SetSelected(Button button, bool selected) => button.Classes.Set("selected", selected);

    /// <summary>Adds the Fluent theme, recoloured, and the app's own shapes.</summary>
    public static void Install(Application app)
    {
        var fluent = new FluentTheme();
        fluent.Palettes[ThemeVariant.Dark] = new ColorPaletteResources
        {
            Accent = VioletColor,
            RegionColor = GroundColor,
            ChromeMedium = PopupColor,
            ChromeMediumLow = PopupColor,
        };
        app.Styles.Add(fluent);
        app.RequestedThemeVariant = ThemeVariant.Dark;

        var r = app.Resources;
        IBrush popup = Popup, clear = Brushes.Transparent;
        r["FlyoutPresenterBackground"] = popup;
        r["FlyoutBorderThemeBrush"] = Wash(0.12);
        IBrush disabledText = new ImmutableSolidColorBrush(Color.FromRgb(124, 123, 153));
        IBrush violetWash = new ImmutableSolidColorBrush(VioletColor, 0.28);

        r["ContentControlThemeFontFamily"] = Font;

        foreach (var kind in new[] { "Button", "ToggleButton" })
        {
            r[kind + "Background"] = Wash(0.07);
            r[kind + "BackgroundPointerOver"] = Wash(0.13);
            r[kind + "BackgroundPressed"] = Wash(0.05);
            r[kind + "BackgroundDisabled"] = Wash(0.03);
            r[kind + "BorderBrush"] = Wash(0.12);
            r[kind + "BorderBrushPointerOver"] = Wash(0.22);
            r[kind + "BorderBrushPressed"] = Wash(0.12);
            r[kind + "BorderBrushDisabled"] = Wash(0.06);
            r[kind + "Foreground"] = Text;
            r[kind + "ForegroundPointerOver"] = Text;
            r[kind + "ForegroundPressed"] = Text;
            r[kind + "ForegroundDisabled"] = disabledText;
        }
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            r["ToggleButtonBackgroundChecked" + state] = AccentFill;
            r["ToggleButtonForegroundChecked" + state] = OnAccent;
            r["ToggleButtonBorderBrushChecked" + state] = clear;
            r["SliderTrackValueFill" + state] = Accent;
            r["SliderThumbBackground" + state] = Text;
            r["ToggleSwitchFillOn" + state] = Accent;
            r["CheckBoxCheckBackgroundFillChecked" + state] = Accent;
            r["CheckBoxCheckBackgroundStrokeChecked" + state] = Accent;
            r["CheckBoxCheckGlyphForegroundChecked" + state] = OnAccent;
            r["RadioButtonOuterEllipseCheckedFill" + state] = Accent;
            r["RadioButtonOuterEllipseCheckedStroke" + state] = Accent;
            r["ComboBoxItemBackgroundSelected" + state] = violetWash;
            r["ComboBoxItemForegroundSelected" + state] = Text;
            r["TabItemHeaderBackgroundSelected" + state] = AccentFill;
            r["TabItemHeaderForegroundSelected" + state] = OnAccent;
        }
        r["SliderTrackFill"] = Wash(0.14);
        r["SliderTrackFillPointerOver"] = Wash(0.18);
        r["SliderTrackFillPressed"] = Wash(0.18);

        r["TabItemHeaderFontSize"] = 15d;
        r["TabItemHeaderThemeFontWeight"] = FontWeight.Medium;
        r["TabItemHeaderSelectedPipeFill"] = clear;
        r["TabItemHeaderBackgroundUnselected"] = clear;
        r["TabItemHeaderBackgroundUnselectedPointerOver"] = Wash(0.08);
        r["TabItemHeaderBackgroundUnselectedPressed"] = Wash(0.05);
        r["TabItemHeaderForegroundUnselected"] = Muted;
        r["TabItemHeaderForegroundUnselectedPointerOver"] = Text;
        r["TabItemHeaderForegroundUnselectedPressed"] = Text;

        r["TextControlBackground"] = Wash(0.05);
        r["TextControlBackgroundPointerOver"] = Wash(0.08);
        r["TextControlBackgroundFocused"] = FocusedFieldBrush;
        r["TextControlBorderBrush"] = Wash(0.12);
        r["TextControlBorderBrushPointerOver"] = Wash(0.22);
        r["TextControlBorderBrushFocused"] = Accent;
        r["TextControlForeground"] = Text;
        r["TextControlForegroundPointerOver"] = Text;
        r["TextControlForegroundFocused"] = Text;
        r["TextControlPlaceholderForeground"] = Muted;
        r["TextControlPlaceholderForegroundPointerOver"] = Muted;
        r["TextControlPlaceholderForegroundFocused"] = Muted;

        r["ComboBoxBackground"] = Wash(0.06);
        r["ComboBoxBackgroundPointerOver"] = Wash(0.11);
        r["ComboBoxBackgroundPressed"] = Wash(0.05);
        r["ComboBoxBorderBrush"] = Wash(0.12);
        r["ComboBoxBorderBrushPointerOver"] = Wash(0.22);
        r["ComboBoxBorderBrushPressed"] = Wash(0.12);
        r["ComboBoxDropDownBackground"] = popup;
        r["MenuFlyoutPresenterBackground"] = popup;
        r["ToolTipBackground"] = ToolTipBrush;

        r["ExpanderHeaderBackground"] = Wash(0.05);
        r["ExpanderHeaderBackgroundPointerOver"] = Wash(0.09);
        r["ExpanderHeaderBackgroundPressed"] = Wash(0.04);
        r["ExpanderHeaderBorderBrush"] = Wash(0.09);
        r["ExpanderHeaderBorderBrushPointerOver"] = Wash(0.16);
        r["ExpanderHeaderBorderBrushPressed"] = Wash(0.09);
        r["ExpanderContentBackground"] = clear;
        r["ExpanderContentBorderBrush"] = Wash(0.09);

        // The window draws its own title-bar buttons. Minimise, maximise and close are enough: no full-screen button.
        app.Styles.Add(new Style(x => x.Is<Button>().Name("PART_FullScreenButton")) { Setters = { new Setter(Visual.IsVisibleProperty, false) } });

        LoadBackground();

        var pill = new CornerRadius(17);
        app.Styles.Add(new Style(x => x.OfType<Button>())
        {
            Setters = { new Setter(TemplatedControl.CornerRadiusProperty, pill), new Setter(TemplatedControl.PaddingProperty, new Thickness(14, 7)) },
        });
        app.Styles.Add(new Style(x => x.OfType<ToggleButton>())
        {
            Setters = { new Setter(TemplatedControl.CornerRadiusProperty, pill), new Setter(TemplatedControl.PaddingProperty, new Thickness(14, 7)) },
        });
        app.Styles.Add(new Style(x => x.OfType<ComboBox>()) { Setters = { new Setter(TemplatedControl.CornerRadiusProperty, pill) } });
        app.Styles.Add(new Style(x => x.OfType<TextBox>()) { Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(12)) } });
        app.Styles.Add(new Style(x => x.OfType<Expander>()) { Setters = { new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(14)) } });

        // The selected one of a set of plain buttons (a power mode, say) looks like a checked toggle.
        app.Styles.Add(new Style(x => x.OfType<Button>().Class("selected").Template().OfType<ContentPresenter>())
        {
            Setters =
            {
                new Setter(ContentPresenter.BackgroundProperty, AccentFill),
                new Setter(ContentPresenter.ForegroundProperty, OnAccent),
                new Setter(ContentPresenter.BorderBrushProperty, clear),
            },
        });

        // Tabs are pills in a row along the top of the window, centred.
        app.Styles.Add(new Style(x => x.OfType<TabItem>())
        {
            Setters =
            {
                new Setter(TemplatedControl.CornerRadiusProperty, new CornerRadius(20)),
                new Setter(TemplatedControl.PaddingProperty, new Thickness(20, 0)),
                new Setter(Layoutable.MinHeightProperty, 40d),
                new Setter(Layoutable.HeightProperty, 40d),
                new Setter(Layoutable.MarginProperty, new Thickness(2, 0)),
            },
        });
        app.Styles.Add(new Style(x => x.OfType<TabControl>().Template().OfType<ItemsPresenter>())
        {
            Setters = { new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Center) },
        });
    }
}
