using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
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

    static readonly IBrush Accent = Look.Accent;
    static readonly IBrush Unselected = Look.Unselected;
    static readonly FilePickerFileType AnimationFile = new("RaptorSense animation") { Patterns = ["*.json"] };

    // Undo keeps whole copies of the frames, so it is capped by how many frames it holds in total (about 2 MB).
    const int MaxUndoSteps = 100, MaxUndoFrames = 5000;

    readonly EffectEngine engine;
    readonly Action useAnimation;
    readonly KeyboardView keyboard = new();
    readonly ComboBox picker = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Your animations" };
    readonly TextBox nameBox = new() { PlaceholderText = "Name" };
    readonly Button deleteAnimation = new() { Content = "Delete" };
    readonly Button exportAnimation = new() { Content = "Export" }, copyAnimation = new() { Content = "Make a copy" };
    readonly Button undoButton = new() { Content = "Undo" }, redoButton = new() { Content = "Redo" };
    readonly ToggleButton play = new() { Content = "▶  Play on the keyboard", FontFamily = Look.Symbols, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly NumericUpDown hold = Seconds(AnimationFrame.MaxHold), fade = Seconds(AnimationFrame.MaxFade);
    readonly ColorEditor brush = new(["Brush"], [Rgb.PredatorTeal], withEyedropper: false);
    readonly StackPanel strip = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    readonly ScrollViewer stripScroll;
    readonly TextBlock summary = new() { Foreground = Look.Muted, Margin = new Thickness(4, 12, 0, 8) };
    readonly TextBlock problem = new() { Foreground = Look.Warning, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(4, 10, 0, 0) };
    readonly Control editor;
    readonly DispatcherTimer saveTimer, playTimer;
    readonly Frame live = new();
    readonly List<EditFrame> frames = [];
    readonly List<(Border Card, FrameThumb Thumb, TextBlock Label)> thumbs = [];
    readonly List<(List<EditFrame> Frames, int Selected)> undo = [], redo = [];
    string? lastMerge;
    DateTime lastMergeAt;
    bool strokeOpen;
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

        keyboard.StrokeStarted += () => { strokeOpen = true; Focus(); };   // focus here, so Ctrl+Z works after painting
        keyboard.KeyPainted += Paint;
        Focusable = true;
        FocusAdorner = null;
        KeyDown += OnShortcut;
        picker.SelectionChanged += (_, _) => { if (!loading && picker.SelectedItem is string name && name != current) Open(name); };
        hold.ValueChanged += (_, e) => SetTime(e.NewValue, isHold: true);
        fade.ValueChanged += (_, e) => SetTime(e.NewValue, isHold: false);
        play.IsCheckedChanged += (_, _) => { if (!loading) SetPlaying(play.IsChecked == true); };

        stripScroll = new ScrollViewer
        {
            Content = strip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 0, 10),
        };

        var controls = BuildControls(out editor);
        Grid.SetColumn(controls, 1);
        var hint = new TextBlock
        {
            Text = "Click or drag over keys to paint the selected frame with the brush · right-click to erase",
            Foreground = Look.Muted,
            FontSize = 13,
            Margin = new Thickness(4, 4, 0, 12),
        };
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(stripScroll, Dock.Bottom);
        DockPanel.SetDock(summary, Dock.Bottom);
        DockPanel.SetDock(problem, Dock.Bottom);   // above the frame strip, where it is always in view

        var well = new Border
        {
            Background = Look.Well,
            BorderBrush = Look.WellBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(24),
            Padding = new Thickness(22),
            Child = keyboard,
        };
        var main = new DockPanel();
        main.Children.Add(hint);
        main.Children.Add(stripScroll);
        main.Children.Add(summary);
        main.Children.Add(problem);
        main.Children.Add(well);
        Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(28, 12, 28, 24),
            Children = { main, controls },
        };

        AttachedToVisualTree += (_, _) => Opened();
        DetachedFromVisualTree += (_, _) => Closed();
        Refresh();
    }

    // ---- Layout ----

    Control BuildControls(out Control editorPart)
    {
        var panel = new StackPanel { Spacing = 8, Width = 340 };

        panel.Children.Add(Heading("Animation"));
        panel.Children.Add(picker);
        var create = new Button { Content = "New" };
        create.Click += (_, _) => CreateNew();
        ToolTip.SetTip(create, "A blank animation with one empty frame");
        var starters = new MenuFlyout();
        foreach (var starter in AnimationStarters.Names)
        {
            var item = new MenuItem { Header = starter };
            item.Click += (_, _) => CreateFromStarter(starter);
            starters.Items.Add(item);
        }
        var fromStarter = new Button { Content = "From a starter ▾", Flyout = starters };
        ToolTip.SetTip(fromStarter, "A ready-made animation in the brush colour, to change into your own");
        copyAnimation.Click += (_, _) => CopyAnimation();
        panel.Children.Add(Row(create, fromStarter, copyAnimation));

        var import = new Button { Content = "Import" };
        ToolTip.SetTip(import, "Add animation files someone shared with you");
        import.Click += (_, _) => Import();
        ToolTip.SetTip(exportAnimation, "Save this animation as a file you can share");
        exportAnimation.Click += (_, _) => Export();
        deleteAnimation.Click += (_, _) => DeleteAnimation();
        panel.Children.Add(Row(import, exportAnimation, deleteAnimation));
        var rename = new Button { Content = "Rename" };
        rename.Click += (_, _) => Rename();
        nameBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Rename(); };
        panel.Children.Add(new DockPanel { Children = { Docked(rename, Dock.Right), nameBox } });

        var edit = new StackPanel { Spacing = 8 };
        edit.Children.Add(play);
        ToolTip.SetTip(undoButton, "Ctrl+Z");
        ToolTip.SetTip(redoButton, "Ctrl+Y");
        undoButton.Click += (_, _) => Undo();
        redoButton.Click += (_, _) => Redo();
        edit.Children.Add(Row(undoButton, redoButton));

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
            var button = new Button { Content = arrow, Width = 48, FontFamily = Look.Symbols, HorizontalContentAlignment = HorizontalAlignment.Center };
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
        fill.Click += (_, _) => { Remember(); Array.Fill(frames[selected].Cells, brush.CurrentColor); Edited(); };
        var clear = new Button { Content = "Clear frame" };
        clear.Click += (_, _) => { Remember(); Array.Clear(frames[selected].Cells); Edited(); };
        edit.Children.Add(Row(fill, clear));
        var fromKeyboard = new Button { Content = "Use the keyboard's current lighting" };
        ToolTip.SetTip(fromKeyboard, "Copies what the keyboard shows right now into this frame");
        fromKeyboard.Click += (_, _) => CopyKeyboard();
        edit.Children.Add(fromKeyboard);

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
        editorPart = edit;
        return new ScrollViewer { Content = Look.Card(panel), Margin = new Thickness(16, 0, 0, 0) };
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

        LoadFrames(animation);
        current = animation.Name;
        selected = 0;
        Problem(null);
        Refresh();
        RebuildStrip();
        if (wasPlaying) SetPlaying(true);   // keeps playing, now this one
        else ShowSelected();
    }

    /// <summary>Replaces the frames being edited. Times and keys from a file are cleaned on the way in.</summary>
    void LoadFrames(KeyAnimation animation)
    {
        frames.Clear();
        foreach (var frame in animation.Cleaned().Frames)
            frames.Add(new EditFrame { Cells = frame.ToCells(), Hold = frame.Hold, Fade = frame.Fade });
        if (frames.Count == 0) frames.Add(new EditFrame());
        ForgetHistory();
    }

    void CreateNew()
    {
        StopPlaying();
        if (saveTimer.IsEnabled) Save();   // the animation being left
        LoadFrames(new KeyAnimation());
        StartAnimation(AnimationStore.UniqueName("Animation"));
    }

    void CreateFromStarter(string starter)
    {
        if (AnimationStarters.Create(starter, brush.CurrentColor) is not { } animation) return;
        StopPlaying();
        if (saveTimer.IsEnabled) Save();
        LoadFrames(animation);
        StartAnimation(AnimationStore.UniqueName(starter));
        if (current is not null) play.IsChecked = true;   // a starter is there to be seen
    }

    void CopyAnimation()
    {
        if (current is null) return;
        if (saveTimer.IsEnabled) Save();
        ForgetHistory();
        StartAnimation(AnimationStore.UniqueName(current + " copy"));
    }

    async void Import()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import animations",
            AllowMultiple = true,
            FileTypeFilter = [AnimationFile],
            SuggestedStartLocation = await top.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Downloads),
        });
        string? last = null;
        int imported = 0;
        var failures = new List<string>();
        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is not { } path) continue;
            try
            {
                last = AnimationStore.Import(path).Name;
                imported++;
            }
            catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                failures.Add($"{Path.GetFileName(path)}: {e.Message}");
            }
        }
        if (last is not null)
        {
            StopPlaying();
            Open(last);
        }
        if (failures.Count > 0) Problem(string.Join("\n", failures));
        else if (imported == 1) Note($"Imported '{last}'.");
        else if (imported > 1) Note($"Imported {imported} animations.");
    }

    async void Export()
    {
        if (current is null || TopLevel.GetTopLevel(this) is not { } top) return;
        if (saveTimer.IsEnabled) Save();
        var invalid = Path.GetInvalidFileNameChars();
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export animation",
            SuggestedFileName = new string(current.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()),
            DefaultExtension = "json",
            FileTypeChoices = [AnimationFile],
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await top.StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents),
        });
        if (file?.TryGetLocalPath() is not { } path || current is null) return;
        try
        {
            AnimationStore.Export(ToAnimation(), path);
            Note($"Exported to {path}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Problem($"Could not export it: {e.Message}");
        }
    }

    KeyAnimation ToAnimation() => new()
    {
        Name = current ?? "",
        Frames = frames.Select(f => AnimationFrame.FromCells(f.Cells, f.Hold, f.Fade)).ToList(),
    };

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
        ForgetHistory();
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

    bool Save()
    {
        saveTimer.Stop();
        if (current is null) return false;
        try
        {
            AnimationStore.Save(ToAnimation());
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
        nameBox.IsEnabled = deleteAnimation.IsEnabled = exportAnimation.IsEnabled = copyAnimation.IsEnabled = current is not null;
        ShowHistory();
        if (current is null) summary.Text = names.Count == 0 ? "No animations yet. Press New to start one." : "";
    }

    // ---- Editing ----

    void Paint(Key key, bool erase)
    {
        if (current is null || frames.Count == 0) return;
        var color = erase ? Rgb.Black : brush.CurrentColor;
        if (frames[selected].Cells[key.Cell] == color && play.IsChecked != true) return;   // dragging over a key already that colour
        if (strokeOpen) Remember();   // one undo step for a whole drag
        strokeOpen = false;
        frames[selected].Cells[key.Cell] = color;
        Edited();
    }

    void SetTime(decimal? value, bool isHold)
    {
        if (loading || current is null || frames.Count == 0 || value is not { } seconds) return;
        Remember($"{(isHold ? "hold" : "fade")} {selected}");   // spinning a number is one undo step
        if (isHold) frames[selected].Hold = (double)seconds;
        else frames[selected].Fade = (double)seconds;
        Edited();
    }

    void ApplyTimesToAll()
    {
        Remember();
        foreach (var frame in frames) (frame.Hold, frame.Fade) = (frames[selected].Hold, frames[selected].Fade);
        foreach (int i in Enumerable.Range(0, thumbs.Count)) UpdateThumb(i);
        Edited();
    }

    void Insert(EditFrame frame)
    {
        if (frames.Count >= KeyAnimation.MaxFrames) { Problem($"An animation can have up to {KeyAnimation.MaxFrames} frames."); return; }
        Remember();
        frames.Insert(selected + 1, frame);
        selected++;
        RebuildStrip();
        Edited();
        Dispatcher.UIThread.Post(() => thumbs[selected].Card.BringIntoView(), DispatcherPriority.Background);
    }

    void RemoveFrame()
    {
        Remember();
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
        Remember();
        (frames[selected], frames[to]) = (frames[to], frames[selected]);
        selected = to;
        RebuildStrip();
        Edited();
    }

    void CopyKeyboard()
    {
        if (ShowingThis)
        {
            Note("The keyboard is already showing this animation. Pick another effect on the Lighting tab, then come back and press this.");
            return;
        }
        Remember();
        engine.CopyLatestFrame(live);
        live.Cells.CopyTo(frames[selected].Cells);
        Edited();
    }

    // ---- Undo ----

    /// <summary>
    /// Keeps the frames as they are now, to come back to. Call it before changing anything. Changes with the same
    /// <paramref name="merge"/> key made in quick succession count as one step.
    /// </summary>
    void Remember(string? merge = null)
    {
        var now = DateTime.UtcNow;
        bool sameStep = merge is not null && merge == lastMerge && now - lastMergeAt < TimeSpan.FromSeconds(1.5);
        lastMerge = merge;
        lastMergeAt = now;
        if (sameStep) return;
        undo.Add(Snapshot());
        redo.Clear();
        while (undo.Count > 1 && (undo.Count > MaxUndoSteps || undo.Sum(step => step.Frames.Count) > MaxUndoFrames)) undo.RemoveAt(0);
        ShowHistory();
    }

    (List<EditFrame> Frames, int Selected) Snapshot() => (frames.Select(f => f.Copy()).ToList(), selected);

    void Undo() => StepHistory(undo, redo);

    void Redo() => StepHistory(redo, undo);

    void StepHistory(List<(List<EditFrame> Frames, int Selected)> from, List<(List<EditFrame> Frames, int Selected)> to)
    {
        if (current is null || from.Count == 0) return;
        to.Add(Snapshot());
        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        frames.Clear();
        frames.AddRange(step.Frames);
        selected = Math.Clamp(step.Selected, 0, frames.Count - 1);
        lastMerge = null;
        ShowHistory();
        RebuildStrip();
        Edited();
        if (selected < thumbs.Count) thumbs[selected].Card.BringIntoView();
    }

    void ForgetHistory()
    {
        undo.Clear();
        redo.Clear();
        lastMerge = null;
        ShowHistory();
    }

    void ShowHistory()
    {
        undoButton.IsEnabled = undo.Count > 0;
        redoButton.IsEnabled = redo.Count > 0;
    }

    void OnShortcut(object? sender, KeyEventArgs e)
    {
        // In a text box, Ctrl+Z belongs to the typing.
        if (e.Source is TextBox || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (e.Key == Avalonia.Input.Key.Z && !shift) Undo();
        else if (e.Key == Avalonia.Input.Key.Y || (e.Key == Avalonia.Input.Key.Z && shift)) Redo();
        else return;
        e.Handled = true;
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
        problem.Foreground = Brushes.Orange;
        problem.IsVisible = message is not null;
    }

    /// <summary>Shows something that went fine, in the same place problems appear.</summary>
    void Note(string message)
    {
        problem.Text = message;
        problem.Foreground = Accent;
        problem.IsVisible = true;
    }

    // ---- Helpers ----

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
        static readonly IBrush Off = new ImmutableSolidColorBrush(Color.FromRgb(40, 39, 74));
        static readonly IBrush Back = new ImmutableSolidColorBrush(Look.GroundColor);
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
