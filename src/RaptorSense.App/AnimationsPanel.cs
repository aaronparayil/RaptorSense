using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using RaptorSense.Profiles;
using Key = RaptorSense.Layout.Key;

namespace RaptorSense.App;

/// <summary>
/// Builds key animations: a row of frames, each painted on the keyboard, held for a while and faded into the next.
/// Editing shows the frame on the real keyboard; Play loops the whole animation there. Every change saves itself.
/// </summary>
sealed class AnimationsPanel : UserControl
{
    sealed class EditFrame
    {
        public Rgb[] Cells = new Rgb[Frame.CellCount];
        public double Hold = 0.5;
        public double Fade;

        public EditFrame Copy() => new() { Cells = (Rgb[])Cells.Clone(), Hold = Hold, Fade = Fade };
    }

    static readonly IBrush Accent = new SolidColorBrush(Color.FromRgb(0, 174, 199));
    static readonly IBrush Unselected = new SolidColorBrush(Color.FromRgb(45, 47, 54));

    readonly EffectEngine engine;
    readonly Action useAnimation;
    readonly KeyboardView keyboard = new() { Margin = new Thickness(24, 12, 24, 12) };
    readonly ComboBox picker = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Your animations" };
    readonly TextBox nameBox = new() { PlaceholderText = "Name" };
    readonly Button deleteAnimation = new() { Content = "Delete" };
    readonly ToggleButton play = new() { Content = "▶  Play on the keyboard", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown hold = Seconds(AnimationFrame.MaxHold), fade = Seconds(AnimationFrame.MaxFade);
    readonly ColorEditor brush = new(["Brush"], [Rgb.PredatorTeal], withEyedropper: false);
    readonly StackPanel strip = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    readonly ScrollViewer stripScroll;
    readonly TextBlock summary = new() { Opacity = 0.7, Margin = new Thickness(24, 0, 24, 6) };
    readonly TextBlock problem = new() { Foreground = Brushes.Orange, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    readonly Control editor;
    readonly DispatcherTimer saveTimer, playTimer;
    readonly Frame live = new();
    readonly List<EditFrame> frames = [];
    readonly List<(Border Card, FrameThumb Thumb, TextBlock Label)> thumbs = [];
    string? current;
    int selected;
    bool loading, deleteArmed;

    /// <param name="useAnimation">Switches the lighting to "Your animation" (and highlights it on the Lighting tab).</param>
    public AnimationsPanel(EffectEngine engine, Action useAnimation)
    {
        this.engine = engine;
        this.useAnimation = useAnimation;
        // Not the constructors that take a callback: those start the timer straight away.
        saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(600) };
        saveTimer.Tick += (_, _) => Save();
        playTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        playTimer.Tick += (_, _) =>
        {
            engine.CopyLatestFrame(live);
            keyboard.Show(live.Cells);
        };

        keyboard.KeyPainted += Paint;
        picker.SelectionChanged += (_, _) => { if (!loading && picker.SelectedItem is string name && name != current) Open(name); };
        hold.ValueChanged += (_, e) => SetTime(e.NewValue, isHold: true);
        fade.ValueChanged += (_, e) => SetTime(e.NewValue, isHold: false);
        play.IsCheckedChanged += (_, _) => { if (!loading) SetPlaying(play.IsChecked == true); };

        stripScroll = new ScrollViewer
        {
            Content = strip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(24, 0, 24, 12),
        };

        var controls = BuildControls(out editor);
        DockPanel.SetDock(controls, Dock.Right);
        var hint = new TextBlock
        {
            Text = "Click or drag over keys to paint the selected frame with the brush · right-click to erase",
            Opacity = 0.6,
            Margin = new Thickness(24, 16, 24, 0),
        };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(stripScroll, Dock.Bottom);
        DockPanel.SetDock(summary, Dock.Bottom);

        var page = new DockPanel();
        page.Children.Add(controls);
        page.Children.Add(hint);
        page.Children.Add(stripScroll);
        page.Children.Add(summary);
        page.Children.Add(keyboard);
        Content = page;

        AttachedToVisualTree += (_, _) => Opened();
        DetachedFromVisualTree += (_, _) => Closed();
        Refresh();
    }

    // ---- Layout ----

    Control BuildControls(out Control editorPart)
    {
        var panel = new StackPanel { Spacing = 8, Width = 360, Margin = new Thickness(0, 16, 20, 16) };

        panel.Children.Add(Heading("Animation"));
        panel.Children.Add(picker);
        var create = new Button { Content = "New" };
        create.Click += (_, _) => CreateNew();
        var duplicate = new Button { Content = "Make a copy" };
        duplicate.Click += (_, _) => CopyAnimation();
        deleteAnimation.Click += (_, _) => DeleteAnimation();
        panel.Children.Add(Row(create, duplicate, deleteAnimation));
        var rename = new Button { Content = "Rename" };
        rename.Click += (_, _) => Rename();
        nameBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Rename(); };
        panel.Children.Add(new DockPanel { Children = { Docked(rename, Dock.Right), nameBox } });

        var edit = new StackPanel { Spacing = 8 };
        edit.Children.Add(play);

        edit.Children.Add(Heading("Selected frame"));
        edit.Children.Add(Labelled("Stays for (s)", hold));
        edit.Children.Add(Labelled("Then fades (s)", fade));
        var allTimes = new Button { Content = "Use these times for every frame" };
        allTimes.Click += (_, _) => ApplyTimesToAll();
        edit.Children.Add(allTimes);

        var add = new Button { Content = "New frame" };
        add.Click += (_, _) => Insert(new EditFrame { Hold = frames[selected].Hold, Fade = frames[selected].Fade });
        var copy = new Button { Content = "Copy frame" };
        copy.Click += (_, _) => Insert(frames[selected].Copy());
        var remove = new Button { Content = "Delete frame" };
        remove.Click += (_, _) => RemoveFrame();
        edit.Children.Add(Row(add, copy, remove));

        var earlier = new Button { Content = "Move earlier" };
        earlier.Click += (_, _) => MoveFrame(-1);
        var later = new Button { Content = "Move later" };
        later.Click += (_, _) => MoveFrame(1);
        edit.Children.Add(Row(earlier, later));

        edit.Children.Add(new TextBlock { Text = "Copy the frame moved one key:", FontSize = 12, Opacity = 0.65 });
        var moves = Row();
        foreach (var (arrow, direction) in new[] { ("◀", KeyShift.Direction.Left), ("▶", KeyShift.Direction.Right), ("▲", KeyShift.Direction.Up), ("▼", KeyShift.Direction.Down) })
        {
            var button = new Button { Content = arrow, Width = 44, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(button, "Adds a new frame after this one with every lit key moved one key over. Press it again to keep going.");
            button.Click += (_, _) =>
            {
                var moved = frames[selected].Copy();
                moved.Cells = KeyShift.Shift(frames[selected].Cells, direction);
                Insert(moved);
            };
            moves.Children.Add(button);
        }
        edit.Children.Add(moves);

        edit.Children.Add(Heading("Brush"));
        edit.Children.Add(brush);
        var fill = new Button { Content = "Fill frame" };
        fill.Click += (_, _) => { Array.Fill(frames[selected].Cells, brush.CurrentColor); Edited(); };
        var clear = new Button { Content = "Clear frame" };
        clear.Click += (_, _) => { Array.Clear(frames[selected].Cells); Edited(); };
        edit.Children.Add(Row(fill, clear));

        edit.Children.Add(new TextBlock
        {
            Text = "Pick \"Your animation\" on the Lighting tab to use it any time. Speed and brightness there apply to it, " +
                   "a typing overlay plays on top, and saving a profile remembers which animation it uses.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.65,
            Margin = new Thickness(0, 8, 0, 0),
        });

        panel.Children.Add(edit);
        panel.Children.Add(problem);
        editorPart = edit;
        return new ScrollViewer { Content = panel };
    }

    // ---- Opening, saving, naming ----

    void Opened()
    {
        Refresh();
        if (current is not null && !AnimationStore.Exists(current)) current = null;   // deleted outside the app
        if (current is null && AnimationStore.List().FirstOrDefault() is { } first) Open(first);
        else ShowSelected();
    }

    void Closed()
    {
        if (saveTimer.IsEnabled) Save();
        playTimer.Stop();
        loading = true;
        play.IsChecked = false;
        loading = false;
        // Leaving the tab mid-edit: the keyboard goes on to play the animation instead of freezing on one frame.
        if (ShowingThis && Effects.Animation.PinnedFrame >= 0) Effects.Animation.Show(Compile(), fromStart: true);
    }

    bool ShowingThis => engine.Effect == Effects.Animation && current is not null && Effects.Animation.Animation?.Name == current;

    void Open(string name)
    {
        if (saveTimer.IsEnabled) Save();
        var animation = AnimationStore.Load(name);
        if (animation is null) { Problem($"Could not read the animation '{name}'."); return; }
        bool wasPlaying = play.IsChecked == true;

        frames.Clear();
        foreach (var frame in animation.Frames.Take(KeyAnimation.MaxFrames))
            frames.Add(new EditFrame { Cells = frame.ToCells(), Hold = Clean(frame.Hold, AnimationFrame.MaxHold), Fade = Clean(frame.Fade, AnimationFrame.MaxFade) });
        if (frames.Count == 0) frames.Add(new EditFrame());
        current = animation.Name;
        selected = 0;
        Problem(null);
        Refresh();
        RebuildStrip();
        if (wasPlaying) SetPlaying(true);   // keeps playing, now this one
        else ShowSelected();
    }

    void CreateNew()
    {
        StopPlaying();
        frames.Clear();
        frames.Add(new EditFrame());
        StartAnimation(UniqueName("Animation"));
    }

    void CopyAnimation()
    {
        if (current is null) return;
        StartAnimation(UniqueName(current + " copy"));
    }

    void StartAnimation(string name)
    {
        current = name;
        selected = 0;
        if (!Save()) return;
        Refresh();
        RebuildStrip();
        ShowSelected();
    }

    void DeleteAnimation()
    {
        if (current is null) return;
        if (!deleteArmed)
        {
            // Animations take a while to make, so deleting takes a second click.
            deleteArmed = true;
            deleteAnimation.Content = "Click again to delete";
            DispatcherTimer.RunOnce(() => { deleteArmed = false; deleteAnimation.Content = "Delete"; }, TimeSpan.FromSeconds(3));
            return;
        }
        deleteArmed = false;
        deleteAnimation.Content = "Delete";
        saveTimer.Stop();
        StopPlaying();
        bool wasShowing = ShowingThis;
        try { AnimationStore.Delete(current); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Problem($"Could not delete it: {e.Message}"); return; }
        if (wasShowing) Effects.Animation.Show(null);
        current = null;
        frames.Clear();
        Refresh();
        if (AnimationStore.List().FirstOrDefault() is { } next) Open(next);
        else { RebuildStrip(); ShowSelected(); }
    }

    void Rename()
    {
        var name = nameBox.Text?.Trim();
        if (current is null || string.IsNullOrEmpty(name) || name == current) return;
        if (!string.Equals(name, current, StringComparison.OrdinalIgnoreCase) && AnimationStore.Exists(name))
        {
            Problem($"There is already an animation called '{name}'.");
            return;
        }
        var old = current;
        bool wasShowing = ShowingThis;
        current = name;
        if (!Save()) { current = old; return; }
        if (!string.Equals(name, old, StringComparison.OrdinalIgnoreCase))
        {
            try { AnimationStore.Delete(old); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Problem($"Saved as '{name}', but could not remove '{old}': {e.Message}"); }
        }
        if (wasShowing) Effects.Animation.Show(Compile(), Effects.Animation.PinnedFrame);
        Refresh();
    }

    static string UniqueName(string stem)
    {
        for (int i = 1; ; i++)
        {
            var name = i == 1 && !stem.Equals("Animation", StringComparison.Ordinal) ? stem : $"{stem} {i}";
            if (!AnimationStore.Exists(name)) return name;
        }
    }

    bool Save()
    {
        saveTimer.Stop();
        if (current is null) return false;
        try
        {
            AnimationStore.Save(new KeyAnimation
            {
                Name = current,
                Frames = frames.Select(f => AnimationFrame.FromCells(f.Cells, f.Hold, f.Fade)).ToList(),
            });
            Problem(null);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Problem($"Could not save the animation: {e.Message}");
            return false;
        }
    }

    /// <summary>Updates the name box, the picker and what can be pressed, without opening anything.</summary>
    void Refresh()
    {
        loading = true;
        var names = AnimationStore.List();
        picker.ItemsSource = names;
        picker.SelectedItem = current is not null && names.Contains(current) ? current : null;
        nameBox.Text = current ?? "";
        loading = false;
        editor.IsEnabled = current is not null && frames.Count > 0;
        nameBox.IsEnabled = deleteAnimation.IsEnabled = current is not null;
        if (current is null) summary.Text = names.Count == 0 ? "No animations yet. Press New to start one." : "";
    }

    // ---- Editing ----

    void Paint(Key key, bool erase)
    {
        if (current is null || frames.Count == 0) return;
        var color = erase ? Rgb.Black : brush.CurrentColor;
        if (frames[selected].Cells[key.Cell] == color && play.IsChecked != true) return;   // dragging over a key already that colour
        frames[selected].Cells[key.Cell] = color;
        Edited();
    }

    void SetTime(decimal? value, bool isHold)
    {
        if (loading || current is null || frames.Count == 0 || value is not { } seconds) return;
        if (isHold) frames[selected].Hold = (double)seconds;
        else frames[selected].Fade = (double)seconds;
        Edited();
    }

    void ApplyTimesToAll()
    {
        foreach (var frame in frames) (frame.Hold, frame.Fade) = (frames[selected].Hold, frames[selected].Fade);
        foreach (int i in Enumerable.Range(0, thumbs.Count)) UpdateThumb(i);
        Edited();
    }

    void Insert(EditFrame frame)
    {
        if (frames.Count >= KeyAnimation.MaxFrames) { Problem($"An animation can have up to {KeyAnimation.MaxFrames} frames."); return; }
        frames.Insert(selected + 1, frame);
        selected++;
        RebuildStrip();
        Edited();
        Dispatcher.UIThread.Post(() => thumbs[selected].Card.BringIntoView(), DispatcherPriority.Background);
    }

    void RemoveFrame()
    {
        if (frames.Count <= 1) { Array.Clear(frames[0].Cells); Edited(); return; }   // the last frame just goes blank
        frames.RemoveAt(selected);
        selected = Math.Min(selected, frames.Count - 1);
        RebuildStrip();
        Edited();
    }

    void MoveFrame(int by)
    {
        int to = selected + by;
        if (to < 0 || to >= frames.Count) return;
        (frames[selected], frames[to]) = (frames[to], frames[selected]);
        selected = to;
        RebuildStrip();
        Edited();
    }

    void Select(int index)
    {
        selected = Math.Clamp(index, 0, Math.Max(frames.Count - 1, 0));
        StopPlaying();
        for (int i = 0; i < thumbs.Count; i++) thumbs[i].Card.BorderBrush = i == selected ? Accent : Unselected;
        ShowSelected();
        if (ShowingThis) Effects.Animation.Show(Effects.Animation.Animation, selected);   // the keyboard follows the selection
    }

    /// <summary>After any change: redraw it here, show it on the keyboard, and save shortly.</summary>
    void Edited()
    {
        StopPlaying();
        UpdateThumb(selected);
        ShowSelected();
        Effects.Animation.Show(Compile(), selected);
        if (engine.Effect != Effects.Animation) useAnimation();
        saveTimer.Stop();
        saveTimer.Start();
    }

    void SetPlaying(bool on)
    {
        if (current is null) return;
        if (on)
        {
            Effects.Animation.Show(Compile(), fromStart: true);
            if (engine.Effect != Effects.Animation) useAnimation();
            if (!engine.OutputEnabled) Problem("The keyboard is set to Preview only, so it plays here but not on the keys.");
            foreach (var (card, _, _) in thumbs) card.BorderBrush = Unselected;
            playTimer.Start();
        }
        else
        {
            playTimer.Stop();
            if (ShowingThis) Effects.Animation.Show(Effects.Animation.Animation, selected);
            Select(selected);
        }
    }

    void StopPlaying()
    {
        if (play.IsChecked != true) return;
        loading = true;
        play.IsChecked = false;
        loading = false;
        playTimer.Stop();
        for (int i = 0; i < thumbs.Count; i++) thumbs[i].Card.BorderBrush = i == selected ? Accent : Unselected;
    }

    CompiledAnimation Compile() => new(current ?? "", frames.Select(f => (f.Cells, f.Hold, f.Fade)).ToList());

    void ShowSelected()
    {
        bool any = current is not null && frames.Count > 0;
        keyboard.Show(any ? frames[selected].Cells : new Rgb[Frame.CellCount]);
        loading = true;
        hold.Value = any ? (decimal)frames[selected].Hold : 0;
        fade.Value = any ? (decimal)frames[selected].Fade : 0;
        loading = false;
        if (any)
        {
            double length = Compile().Length;
            summary.Text = $"Frame {selected + 1} of {frames.Count}   ·   one loop takes {length:0.##} s at normal speed";
        }
    }

    // ---- Frame strip ----

    void RebuildStrip()
    {
        strip.Children.Clear();
        thumbs.Clear();
        for (int i = 0; i < frames.Count; i++)
        {
            int index = i;
            var thumb = new FrameThumb(frames[i].Cells);
            var label = new TextBlock { FontSize = 11, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center };
            var card = new Border
            {
                BorderThickness = new Thickness(2),
                BorderBrush = i == selected ? Accent : Unselected,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Spacing = 3, Children = { thumb, label } },
            };
            card.PointerPressed += (_, _) => Select(index);
            strip.Children.Add(card);
            thumbs.Add((card, thumb, label));
            UpdateThumb(i);
        }
    }

    void UpdateThumb(int index)
    {
        if (index < 0 || index >= thumbs.Count || index >= frames.Count) return;
        var (_, thumb, label) = thumbs[index];
        var frame = frames[index];
        thumb.Cells = frame.Cells;
        thumb.InvalidateVisual();
        label.Text = frame.Fade > 0 ? $"{index + 1}  ·  {frame.Hold:0.##} s + {frame.Fade:0.##} fade" : $"{index + 1}  ·  {frame.Hold:0.##} s";
    }

    void Problem(string? message)
    {
        problem.Text = message ?? "";
        problem.IsVisible = message is not null;
    }

    // ---- Helpers ----

    // Hand-edited files can hold anything; the number boxes need a real number in range.
    static double Clean(double seconds, double max) => double.IsFinite(seconds) ? Math.Clamp(seconds, 0, max) : 0;

    static NumericUpDown Seconds(double max) => new()
    {
        Minimum = 0, Maximum = (decimal)max, Increment = 0.05m, FormatString = "0.00", Width = 140, Value = 0,
    };

    static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children) row.Children.Add(child);
        return row;
    }

    static Control Labelled(string label, Control input) => new DockPanel
    {
        Children = { Docked(input, Dock.Right), new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 } },
    };

    static Control Docked(Control control, Dock dock)
    {
        DockPanel.SetDock(control, dock);
        control.Margin = new Thickness(8, 0, 0, 0);
        return control;
    }

    /// <summary>A small drawing of one frame. Drawn directly rather than from controls, so a long strip stays cheap.</summary>
    sealed class FrameThumb : Control
    {
        const double ThumbWidth = 150;
        static readonly double Scale = ThumbWidth / PhysicalLayout.Width;
        static readonly IBrush Off = new ImmutableSolidColorBrush(Color.FromRgb(34, 36, 42));
        static readonly IBrush Back = new ImmutableSolidColorBrush(Color.FromRgb(12, 13, 16));
        static readonly (int Cell, Rect Rect)[] KeyRects = KeyMap.Keys
            .Select(k => (k.Cell, Rect: PhysicalLayout.For(k.Label)))
            .Where(k => k.Rect is not null)
            .Select(k => (k.Cell, new Rect(k.Rect!.Value.X * Scale, k.Rect.Value.Y * Scale, k.Rect.Value.Width * Scale, k.Rect.Value.Height * Scale)))
            .ToArray();

        public FrameThumb(Rgb[] cells)
        {
            Cells = cells;
            Width = ThumbWidth;
            Height = PhysicalLayout.Height * Scale;
        }

        public Rgb[] Cells { get; set; }

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(Back, null, new Rect(Bounds.Size));
            foreach (var (cell, rect) in KeyRects)
            {
                var c = Cells[cell];
                context.DrawRectangle(c == Rgb.Black ? Off : new ImmutableSolidColorBrush(Color.FromRgb(c.R, c.G, c.B)), null, rect, 1.2, 1.2);
            }
        }
    }
}
