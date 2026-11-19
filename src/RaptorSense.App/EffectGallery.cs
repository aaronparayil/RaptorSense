using System.Diagnostics;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using RaptorSense.Audio;
using RaptorSense.Lighting;
using IEffect = RaptorSense.Lighting.IEffect;

namespace RaptorSense.App;

/// <summary>What the gallery needs from the window.</summary>
interface IGalleryHost
{
    EffectEngine Engine { get; }

    /// <summary>Plays the effect on the keyboard, as picking it on the Lighting tab does.</summary>
    void Choose(IEffect effect);

    /// <summary>On: music effects fall back on the demo beat while the computer is quiet.</summary>
    void SetDemoMusic(bool on);
}

/// <summary>
/// Every effect at once, each playing on its own little keyboard: grouped by collection, with a search, favourites,
/// and a showcase that plays them all on the real keyboard one after another. Only the previews on screen move, and
/// none of them do while the tab is out of sight.
/// </summary>
sealed class EffectGallery : UserControl
{
    const string AllFilter = "All", FavouritesFilter = "Favourites";
    const double CardWidth = 236;

    sealed record Card(IEffect Effect, Button Button, EffectThumbnail Thumbnail, Border Outline, Border Badge, ToggleButton Star, Section Section);

    sealed class Section(string category, StackPanel panel)
    {
        public string Category { get; } = category;
        public StackPanel Panel { get; } = panel;
        public List<Card> Cards { get; } = [];
    }

    readonly IGalleryHost host;
    readonly IReadOnlyList<IEffect> effects;
    readonly bool zoned;
    readonly List<Card> cards = [];
    readonly List<Section> sections = [];
    readonly Dictionary<string, ToggleButton> chips = [];
    readonly ScrollViewer scroll;
    readonly TextBox search = new() { PlaceholderText = L("Search effects"), Width = 260 };
    readonly TextBlock emptyNote = new() { Text = L("No effect matches."), Foreground = Look.Muted, IsVisible = false, Margin = new Thickness(4, 20) };

    // Previews
    readonly DispatcherTimer previewTimer;
    readonly Stopwatch watch = new();
    readonly DemoAudio demo = new();
    readonly Random random = new();
    readonly List<int> previewPresses = [];
    readonly Frame playingFrame = new();
    double lastTick, nextPreviewPress;
    int ticks;
    string filter = AllFilter;
    IEffect? highlighted;
    bool active;

    // Showcase
    readonly Border banner;
    readonly TextBlock bannerTitle = new() { FontSize = 18, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock bannerNote = new() { FontSize = 13, Foreground = Look.Muted, VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox interval = new() { ItemsSource = new[] { 5, 8, 15, 30 }.Select(s => L("{0} s each", s)).ToArray(), SelectedIndex = 1 };
    readonly Button playAll = new() { Content = L("Play them all") };
    readonly DispatcherTimer showcaseTimer;
    List<IEffect> playlist = [];
    int position;
    double showUntil;
    IEffect? beforeShowcase;

    static readonly int[] TypingKeys = KeyGeometry.Cells.Where(c => KeyGrid.Row(c) is >= 1 and <= 4 && KeyGeometry.X(c) < 14).ToArray();

    public EffectGallery(IGalleryHost host, IReadOnlyList<IEffect> effects, bool zoned)
    {
        this.host = host;
        this.effects = effects;
        this.zoned = zoned;

        var title = new TextBlock { Text = L("Effect gallery"), FontSize = 30, FontWeight = FontWeight.SemiBold };
        var subtitle = new TextBlock
        {
            Text = L("{0} effects, each playing as it would on your keyboard. Click one to play it for real. Previews of music and typing effects use a pretend beat and pretend typing.", effects.Count),
            Foreground = Look.Muted,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        };
        playAll.Click += (_, _) => StartShowcase();
        ToolTip.SetTip(playAll, L("Plays every effect in view on the keyboard, one after another"));
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Bottom, Children = { search, playAll } };
        DockPanel.SetDock(tools, Dock.Right);
        var header = new DockPanel
        {
            Margin = new Thickness(4, 0, 0, 12),
            Children = { tools, new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 16, 0), Children = { title, subtitle } } },
        };

        var chipRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        AddChip(chipRow, AllFilter, $"{L("All")}  {effects.Count}");
        AddChip(chipRow, FavouritesFilter, L("★ Favourites"));
        foreach (var category in EffectCategory.Order)
        {
            int count = effects.Count(e => e.Category == category);
            if (count > 0) AddChip(chipRow, category, $"{ShortCategory(category)}  {count}");
        }
        chips[AllFilter].IsChecked = true;

        banner = BuildBanner();

        var list = new StackPanel { Spacing = 22, Margin = new Thickness(0, 0, 12, 24) };
        foreach (var category in EffectCategory.Order)
        {
            var inCategory = effects.Where(e => e.Category == category).ToList();
            if (inCategory.Count == 0) continue;
            var wrap = new WrapPanel();
            var panel = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = L(category), FontSize = 20, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = L(EffectCategory.Describe(category)), Foreground = Look.Muted, FontSize = 13, Margin = new Thickness(0, 0, 0, 6) },
                    wrap,
                },
            };
            var section = new Section(category, panel);
            foreach (var effect in inCategory)
            {
                var card = BuildCard(effect, section);
                section.Cards.Add(card);
                cards.Add(card);
                wrap.Children.Add(card.Button);
            }
            sections.Add(section);
            list.Children.Add(panel);
        }
        list.Children.Add(emptyNote);
        scroll = new ScrollViewer { Content = list };

        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), Margin = new Thickness(28, 12, 16, 0) };
        page.Children.Add(header);
        page.Children.Add(At(chipRow, 1));
        page.Children.Add(At(banner, 2));
        page.Children.Add(At(scroll, 3));
        Content = page;

        search.TextChanged += (_, _) => ApplyFilter();
        Favourites.Changed += OnFavouritesChanged;

        // Fifteen frames a second is plenty for a preview, and half the drawing of thirty.
        previewTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(66), DispatcherPriority.Render, (_, _) => Tick());
        showcaseTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => ShowcaseTick());
    }

    /// <summary>
    /// The previews move only while this is true: the tab is showing and the window is open. Switched off, they give
    /// back their pictures and copies, and start afresh next time.
    /// </summary>
    public void SetActive(bool on)
    {
        if (on == active) return;
        active = on;
        if (on)
        {
            watch.Restart();
            lastTick = 0;
            previewTimer.Start();
            return;
        }
        previewTimer.Stop();
        foreach (var card in cards) card.Thumbnail.Release();
    }

    /// <summary>True while "Play them all" is going, which goes on with the window closed.</summary>
    public bool Showcasing => playlist.Count > 0;

    /// <summary>Stops everything and lets go of the favourites list, so the gallery can be dropped.</summary>
    public void Close()
    {
        StopShowcase(keep: true);
        SetActive(false);
        Favourites.Changed -= OnFavouritesChanged;
    }

    void OnFavouritesChanged()
    {
        foreach (var c in cards) c.Star.IsChecked = Favourites.Contains(c.Effect.Id);
        if (filter == FavouritesFilter) ApplyFilter();
    }

    static Control At(Control control, int row)
    {
        Grid.SetRow(control, row);
        return control;
    }

    static string ShortCategory(string category) => category == EffectCategory.Keyboard ? L("Keyboard's own") : L(category);

    void AddChip(WrapPanel row, string key, string label)
    {
        var chip = new ToggleButton { Content = label, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(14, 6) };
        if (key == FavouritesFilter) chip.FontFamily = Look.Symbols;
        chip.Click += (_, _) =>
        {
            filter = key;
            foreach (var (k, c) in chips) c.IsChecked = k == key;
            ApplyFilter();
            scroll.Offset = default;
        };
        chips[key] = chip;
        row.Children.Add(chip);
    }

    Card BuildCard(IEffect effect, Section section)
    {
        var thumbnail = new EffectThumbnail(effect, zoned) { Width = CardWidth - 20 };
        var badge = new Border
        {
            Background = Look.AccentFill,
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 1),
            Margin = new Thickness(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsVisible = false,
            Child = new TextBlock { Text = L("Playing"), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Look.OnAccent },
        };
        var star = new ToggleButton
        {
            Content = "★",
            FontFamily = Look.Symbols,
            FontSize = 13,
            Width = 30,
            Height = 30,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(15),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsChecked = Favourites.Contains(effect.Id),
        };
        ToolTip.SetTip(star, L("Favourite: listed first on the Lighting tab"));
        AutomationProperties.SetName(star, $"Favourite {effect.Name}");
        star.Click += (_, _) => Favourites.Set(effect.Id, star.IsChecked == true);
        DockPanel.SetDock(star, Dock.Right);

        var name = new TextBlock { Text = L(effect.Name), FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var tag = new TextBlock { Text = Describe(effect), FontSize = 12, Foreground = Look.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
        if (tag.Text.Any(ch => ch >= 0x2000)) tag.FontFamily = Look.Symbols;
        var outline = new Border
        {
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(2),
            BorderBrush = Brushes.Transparent,
            Padding = new Thickness(8),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new Panel { Children = { thumbnail, badge } },
                    new DockPanel { Children = { star, new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name, tag } } } },
                },
            },
        };
        var button = new Button
        {
            Content = outline,
            Width = CardWidth,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(16),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(button, $"{effect.Name}, {effect.Category}");
        ToolTip.SetTip(button, L("Play {0} on the keyboard", L(effect.Name)));
        button.Click += (_, _) =>
        {
            StopShowcase(keep: true);
            host.Choose(effect);
            Highlight(effect);
        };
        return new Card(effect, button, thumbnail, outline, badge, star, section);
    }

    static string Describe(IEffect effect) =>
        effect.UsesAudio ? L("♪ Follows your music")
        : effect.UsesKeys ? L("⌨ Reacts to typing")
        : effect is KeyboardEffect ? L("Runs on the keyboard itself")
        : L(effect.Category);

    void ApplyFilter()
    {
        string text = search.Text?.Trim() ?? "";
        bool any = false;
        foreach (var section in sections)
        {
            bool shown = false;
            foreach (var card in section.Cards)
            {
                bool match = filter switch
                {
                    AllFilter => true,
                    FavouritesFilter => Favourites.Contains(card.Effect.Id),
                    _ => card.Effect.Category == filter,
                };
                if (match && text.Length > 0)
                    match = L(card.Effect.Name).Contains(text, StringComparison.OrdinalIgnoreCase)
                            || card.Effect.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                            || L(card.Effect.Category).Contains(text, StringComparison.OrdinalIgnoreCase);
                card.Button.IsVisible = match;
                shown |= match;
            }
            section.Panel.IsVisible = shown;
            any |= shown;
        }
        emptyNote.Text = filter == FavouritesFilter && text.Length == 0
            ? L("No favourites yet: press the star on any effect to add it here and to the top of the Lighting tab.")
            : L("No effect matches.");
        emptyNote.IsVisible = !any;
    }

    void Highlight(IEffect? effect)
    {
        if (effect == highlighted) return;
        highlighted = effect;
        foreach (var card in cards)
        {
            bool playing = card.Effect == effect;
            card.Outline.BorderBrush = playing ? Look.Accent : Brushes.Transparent;
            card.Badge.IsVisible = playing;
        }
    }

    void Tick()
    {
        if (!active || TopLevel.GetTopLevel(this) is not Window { IsVisible: true, WindowState: not WindowState.Minimized } window) return;
        // With another window in front, the previews slow to a third of the pace: still alive, far less drawing.
        if (!window.IsActive && ticks++ % 3 != 0) return;
        double now = watch.Elapsed.TotalSeconds;
        double dt = Math.Clamp(now - lastTick, 0, 0.1);
        lastTick = now;
        demo.Time = now;

        previewPresses.Clear();
        if (now >= nextPreviewPress)
        {
            previewPresses.Add(TypingKeys[random.Next(TypingKeys.Length)]);
            nextPreviewPress = now + 0.1 + random.NextDouble() * 0.3 + (random.NextDouble() < 0.1 ? 0.6 : 0);   // bursts, with pauses
        }

        var current = host.Engine.Effect;
        Highlight(current);
        Frame? playing = null;
        if (current == Effects.Animation)
        {
            host.Engine.CopyLatestFrame(playingFrame);
            playing = playingFrame;
        }

        double viewTop = 0, viewBottom = scroll.Bounds.Height;
        foreach (var card in cards)
        {
            bool inView = card.Button.IsVisible && card.Section.Panel.IsVisible
                          && card.Button.TranslatePoint(default, scroll) is { } at
                          && at.Y + card.Button.Bounds.Height >= viewTop && at.Y <= viewBottom;
            if (inView)
            {
                card.Thumbnail.Advance(dt, host.Engine.Settings, demo, previewPresses, card.Effect == current ? playing : null);
                card.Thumbnail.LastSeen = now;
            }
            // Out of sight for a moment (scrolled away, or another collection picked): give its memory back.
            else if (card.Thumbnail.HoldsPicture && now - card.Thumbnail.LastSeen > 2) card.Thumbnail.Release();
        }
    }

    // ---- Showcase ----

    Border BuildBanner()
    {
        var previous = new Button { Content = L("Previous") };
        previous.Click += (_, _) => Show(position - 1);
        var next = new Button { Content = L("Next") };
        next.Click += (_, _) => Show(position + 1);
        var keep = new Button { Content = L("Keep this one") };
        ToolTip.SetTip(keep, L("Stops the showcase and leaves this effect playing"));
        keep.Click += (_, _) => StopShowcase(keep: true);
        var stop = new Button { Content = L("Stop") };
        ToolTip.SetTip(stop, L("Stops the showcase and goes back to what was playing before"));
        stop.Click += (_, _) => StopShowcase(keep: false);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { interval, previous, next, keep, stop } };
        DockPanel.SetDock(buttons, Dock.Right);
        return new Border
        {
            Background = Look.CardFill,
            BorderBrush = Look.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 10),
            Margin = new Thickness(0, 0, 12, 12),
            IsVisible = false,
            Child = new DockPanel
            {
                Children = { buttons, new StackPanel { Spacing = 2, Children = { bannerTitle, bannerNote } } },
            },
        };
    }

    double IntervalSeconds => interval.SelectedIndex switch { 0 => 5, 2 => 15, 3 => 30, _ => 8 };

    void StartShowcase()
    {
        // What is in view, in order, leaving out what can't be shown in a few seconds or isn't an effect of its own.
        playlist = cards.Where(c => c.Button.IsVisible && c.Section.Panel.IsVisible)
            .Select(c => c.Effect)
            .Where(e => e is not (KeyboardEffect or PaintedEffect or AnimationEffect or FocusTimerEffect))
            .ToList();
        if (playlist.Count == 0) return;
        beforeShowcase ??= host.Engine.Effect;
        host.SetDemoMusic(true);
        banner.IsVisible = true;
        playAll.IsEnabled = false;
        Show(0);
        showcaseTimer.Start();
    }

    void Show(int index)
    {
        if (playlist.Count == 0) return;
        position = (index % playlist.Count + playlist.Count) % playlist.Count;
        var effect = playlist[position];
        host.Choose(effect);
        Highlight(effect);
        showUntil = Environment.TickCount64 / 1000.0 + IntervalSeconds;
        bannerTitle.Text = L(effect.Name);
        bannerNote.Text = L("Showcase: {0} of {1}", position + 1, playlist.Count) + " · " + L(effect.Category) +
                          (effect.UsesKeys ? " · " + L("pretend typing until you type") : effect.UsesAudio ? " · " + L("the demo beat while nothing is playing") : "");
        ScrollIntoView(effect);
    }

    void ScrollIntoView(IEffect effect)
    {
        var card = cards.FirstOrDefault(c => c.Effect == effect);
        if (card is null || !active) return;
        card.Button.BringIntoView();
    }

    void ShowcaseTick()
    {
        if (playlist.Count == 0) return;
        // Picked something else (on the Lighting tab, say): that ends the showcase.
        if (host.Engine.Effect != playlist[position]) { StopShowcase(keep: true); return; }
        if (playlist[position].UsesKeys && random.NextDouble() < 0.55)
            host.Engine.NotifyKeyPress(TypingKeys[random.Next(TypingKeys.Length)]);
        if (Environment.TickCount64 / 1000.0 >= showUntil) Show(position + 1);
    }

    void StopShowcase(bool keep)
    {
        if (playlist.Count == 0 && !banner.IsVisible) return;
        showcaseTimer.Stop();
        playlist = [];
        banner.IsVisible = false;
        playAll.IsEnabled = true;
        host.SetDemoMusic(false);
        if (!keep && beforeShowcase is not null) host.Choose(beforeShowcase);
        beforeShowcase = null;
    }
}
