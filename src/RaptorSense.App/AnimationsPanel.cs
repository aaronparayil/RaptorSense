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
    static readonly FilePickerFileType AnimationFile = new(L("RaptorSense animation")) { Patterns = ["*.json"] };

    static readonly (MusicReaction Reaction, string Label, string Note)[] MusicChoices =
    [
        (MusicReaction.None, L("Off"), ""),
        (MusicReaction.Beat, L("Step on the beat"), L("Each beat moves to the next frame. \"Stays for\" isn't used, and \"Then fades\" is how long the change takes.")),
        (MusicReaction.Speed, L("Faster when louder"), L("Plays slowly when it's quiet and faster the louder the music gets.")),
        (MusicReaction.Pulse, L("Pulse with the bass"), L("Plays at its own times and gets brighter with the bass.")),
    ];

    // Undo keeps whole copies of the frames, so it is capped by how many frames it holds in total (about 2 MB).
    const int MaxUndoSteps = 100, MaxUndoFrames = 5000;

    readonly EffectEngine engine;
    readonly Action useAnimation;
    readonly KeyboardView keyboard = new();
    readonly ComboBox picker = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = L("Your animations") };
    readonly TextBox nameBox = new() { PlaceholderText = L("Name") };
    readonly Button deleteAnimation = new() { Content = L("Delete") };
    readonly Button exportAnimation = new() { Content = L("Export") }, copyAnimation = new() { Content = L("Make a copy") };
    readonly Button undoButton = new() { Content = L("Undo") }, redoButton = new() { Content = L("Redo") };
    readonly ToggleButton play = new() { Content = "▶  " + L("Play on the keyboard"), FontFamily = Look.Symbols, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox musicPicker = new() { Width = 190, ItemsSource = MusicChoices.Select(c => c.Label).ToList(), SelectedIndex = 0 };
    readonly TextBlock musicNote = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.65, IsVisible = false };
    readonly NumericUpDown hold = Seconds(AnimationFrame.MaxHold), fade = Seconds(AnimationFrame.MaxFade);
    readonly ColorEditor brush = new([L("Brush")], [Rgb.PredatorTeal], withEyedropper: false);
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
    MusicReaction music;
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
        musicPicker.SelectionChanged += (_, _) => { if (!loading) SetMusic(MusicChoices[Math.Max(musicPicker.SelectedIndex, 0)].Reaction); };

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
            Text = L("Click or drag over keys to paint the selected frame with the brush · right-click to erase"),
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

        panel.Children.Add(Heading(L("Animation")));
        panel.Children.Add(picker);
        var create = new Button { Content = L("New") };
        create.Click += (_, _) => CreateNew();
        ToolTip.SetTip(create, L("A blank animation with one empty frame"));
        var starters = new MenuFlyout();
        foreach (var starter in AnimationStarters.Names)
        {
            var item = new MenuItem { Header = starter };
            item.Click += (_, _) => CreateFromStarter(starter);
            starters.Items.Add(item);
        }
        var fromStarter = new Button { Content = L("From a starter") + " ▾", Flyout = starters };
        ToolTip.SetTip(fromStarter, L("A ready-made animation in the brush colour, to change into your own"));
        copyAnimation.Click += (_, _) => CopyAnimation();
        panel.Children.Add(Row(create, fromStarter, copyAnimation));

        var import = new Button { Content = L("Import") };
        ToolTip.SetTip(import, L("Add animation files someone shared with you"));
        import.Click += (_, _) => Import();
        ToolTip.SetTip(exportAnimation, L("Save this animation as a file you can share"));
        exportAnimation.Click += (_, _) => Export();
        deleteAnimation.Click += (_, _) => DeleteAnimation();
        panel.Children.Add(Row(import, exportAnimation, deleteAnimation));
        var shared = new Button { Content = L("Shared by others") + " ▾", HorizontalAlignment = HorizontalAlignment.Stretch, Flyout = BuildSharedFlyout() };
        ToolTip.SetTip(shared, L("Animations people shared on the RaptorSense website, to add with one click"));
        panel.Children.Add(shared);
        var rename = new Button { Content = L("Rename") };
        rename.Click += (_, _) => Rename();
        nameBox.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Rename(); };
        panel.Children.Add(new DockPanel { Children = { Docked(rename, Dock.Right), nameBox } });

        var edit = new StackPanel { Spacing = 8 };
        edit.Children.Add(play);
        ToolTip.SetTip(musicPicker, L("Makes this animation follow the music the laptop is playing. It never listens to the microphone."));
        edit.Children.Add(Labelled(L("With music"), musicPicker));
        edit.Children.Add(musicNote);
        ToolTip.SetTip(undoButton, "Ctrl+Z");
        ToolTip.SetTip(redoButton, "Ctrl+Y");
        undoButton.Click += (_, _) => Undo();
        redoButton.Click += (_, _) => Redo();
        edit.Children.Add(Row(undoButton, redoButton));

        edit.Children.Add(Heading(L("Selected frame")));
        edit.Children.Add(Labelled(L("Stays for (s)"), hold));
        edit.Children.Add(Labelled(L("Then fades (s)"), fade));
        var allTimes = new Button { Content = L("Use these times for every frame") };
        allTimes.Click += (_, _) => ApplyTimesToAll();
        edit.Children.Add(allTimes);

        var add = new Button { Content = L("New frame") };
        add.Click += (_, _) => Insert(new EditFrame { Hold = frames[selected].Hold, Fade = frames[selected].Fade });
        var copy = new Button { Content = L("Copy frame") };
        copy.Click += (_, _) => Insert(frames[selected].Copy());
        var remove = new Button { Content = L("Delete frame") };
        remove.Click += (_, _) => RemoveFrame();
        edit.Children.Add(Row(add, copy, remove));

        var earlier = new Button { Content = L("Move earlier") };
        earlier.Click += (_, _) => MoveFrame(-1);
        var later = new Button { Content = L("Move later") };
        later.Click += (_, _) => MoveFrame(1);
        edit.Children.Add(Row(earlier, later));

        edit.Children.Add(new TextBlock { Text = L("Copy the frame moved one key:"), FontSize = 12, Opacity = 0.65 });
        var moves = Row();
        foreach (var (arrow, direction) in new[] { ("◀", KeyShift.Direction.Left), ("▶", KeyShift.Direction.Right), ("▲", KeyShift.Direction.Up), ("▼", KeyShift.Direction.Down) })
        {
            var button = new Button { Content = arrow, Width = 48, FontFamily = Look.Symbols, HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(button, L("Adds a new frame after this one with every lit key moved one key over. Press it again to keep going."));
            button.Click += (_, _) =>
            {
                var moved = frames[selected].Copy();
                moved.Cells = KeyShift.Shift(frames[selected].Cells, direction);
                Insert(moved);
            };
            moves.Children.Add(button);
        }
        edit.Children.Add(moves);

        edit.Children.Add(Heading(L("Brush")));
        edit.Children.Add(brush);
        var fill = new Button { Content = L("Fill frame") };
        fill.Click += (_, _) => { Remember(); Array.Fill(frames[selected].Cells, brush.CurrentColor); Edited(); };
        var clear = new Button { Content = L("Clear frame") };
        clear.Click += (_, _) => { Remember(); Array.Clear(frames[selected].Cells); Edited(); };
        edit.Children.Add(Row(fill, clear));
        var fromKeyboard = new Button { Content = L("Use the keyboard's current lighting") };
        ToolTip.SetTip(fromKeyboard, L("Copies what the keyboard shows right now into this frame"));
        fromKeyboard.Click += (_, _) => CopyKeyboard();
        edit.Children.Add(fromKeyboard);

        edit.Children.Add(new TextBlock
        {
            Text = L("Pick \"Your animation\" on the Lighting tab to use it any time. Speed and brightness there apply to it, a typing overlay plays on top, and saving a profile remembers which animation it uses."),
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
        if (ShowingThis && Effects.Animation.PinnedFrame >= 0)
        {
            Effects.Animation.Show(Compile(), fromStart: true);
            EnsureAudio();
        }
    }

    bool ShowingThis => engine.Effect == Effects.Animation && current is not null && Effects.Animation.Animation?.Name == current;

    void Open(string name)
    {
        if (saveTimer.IsEnabled) Save();
        var animation = AnimationStore.Load(name);
        if (animation is null) { Problem(L("Could not read the animation '{0}'.", name)); return; }
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
        music = MusicReactions.Parse(animation.Music);
        ShowMusic();
        ForgetHistory();
    }

    void CreateNew()
    {
        StopPlaying();
        if (saveTimer.IsEnabled) Save();   // the animation being left
        LoadFrames(new KeyAnimation());
        StartAnimation(AnimationStore.UniqueName(L("Animation")));
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
        StartAnimation(AnimationStore.UniqueName(L("{0} copy", current)));
    }

    async void Import()
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = L("Import animations"),
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
        else if (imported == 1) Note(L("Imported '{0}'.", last));
        else if (imported > 1) Note(L("Imported {0} animations.", imported));
    }

    // ---- Shared on the website ----

    static readonly Lazy<HttpClient> Http = new(() => SharedAnimations.CreateClient());

    /// <summary>
    /// The website's list, fetched each time it opens (and only then), each animation with an Add button. The app is
    /// otherwise offline; this asks the website for its list and the files picked, and sends nothing else.
    /// </summary>
    Flyout BuildSharedFlyout()
    {
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Look.Muted, FontSize = 13 };
        var rows = new StackPanel { Spacing = 10 };
        var website = new Button { Content = L("Open the website") };
        website.Click += (_, _) => TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(SharedAnimations.Page);
        var panel = new StackPanel
        {
            Width = 380,
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = L("Shared on the RaptorSense website"), FontWeight = FontWeight.SemiBold },
                status,
                new ScrollViewer { Content = rows, MaxHeight = 380, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                website,
            },
        };
        var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedLeft };
        CancellationTokenSource? loading = null;
        flyout.Opened += async (_, _) =>
        {
            loading?.Cancel();
            var cancel = loading = new CancellationTokenSource();
            rows.Children.Clear();
            status.Text = L("Getting the list from the website…");
            try
            {
                var list = await SharedAnimations.ListAsync(Http.Value, cancel.Token);
                if (cancel.IsCancellationRequested) return;
                status.Text = list.Count == 0 ? L("Nothing has been shared yet.") : L("{0} animations, newest first.", list.Count);
                foreach (var shared in list) rows.Children.Add(SharedRow(shared, flyout));
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                status.Text = L("Could not reach the RaptorSense website. Check the internet connection and try again.");
            }
            catch (InvalidDataException e) { status.Text = e.Message; }
        };
        flyout.Closed += (_, _) => loading?.Cancel();
        return flyout;
    }

    Control SharedRow(SharedAnimation shared, Flyout flyout)
    {
        var details = L("by {0}", shared.Author) + " · " + L("{0} frames", shared.Frames)
            + (shared.Music is null ? "" : " · ♪ " + L("follows the music"));
        var add = new Button { Content = L("Add"), VerticalAlignment = VerticalAlignment.Center };
        add.Click += (_, _) => { flyout.Hide(); AddShared(shared.Slug); };
        var text = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = shared.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = details, Foreground = Look.Muted, FontSize = 12, FontFamily = Look.Symbols, TextTrimming = TextTrimming.CharacterEllipsis },
            },
        };
        if (shared.About.Length > 0)
            text.Children.Add(new TextBlock { Text = shared.About, Foreground = Look.Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxHeight = 34 });
        ToolTip.SetTip(text, shared.About.Length > 0 ? shared.About : shared.Name);
        return new DockPanel { Children = { Docked(add, Dock.Right), text } };
    }

    /// <summary>
    /// Fetches the website's animation <paramref name="slug"/>, adds it like an import (a clashing name gets a number),
    /// opens it and plays it on the keyboard.
    /// </summary>
    public async void AddShared(string slug)
    {
        Note(L("Getting the animation from the website…"));
        try
        {
            var animation = await SharedAnimations.AddAsync(Http.Value, slug);
            if (saveTimer.IsEnabled) Save();
            StopPlaying();
            Open(animation.Name);
            if (current is not null) play.IsChecked = true;   // it was asked for to be seen
            Note(L("Added '{0}' from the website.", animation.Name));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            Problem(L("Could not reach the RaptorSense website. Check the internet connection and try again."));
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Problem(L("Could not add the animation: {0}", e.Message));
        }
    }

    async void Export()
    {
        if (current is null || TopLevel.GetTopLevel(this) is not { } top) return;
        if (saveTimer.IsEnabled) Save();
        var invalid = Path.GetInvalidFileNameChars();
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = L("Export animation"),
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
            Note(L("Exported to {0}", path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Problem(L("Could not export it: {0}", e.Message));
        }
    }

    KeyAnimation ToAnimation() => new()
    {
        Name = current ?? "",
        Music = MusicReactions.ToText(music),
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
            deleteAnimation.Content = L("Click again to delete");
            DispatcherTimer.RunOnce(() => { deleteArmed = false; deleteAnimation.Content = L("Delete"); }, TimeSpan.FromSeconds(3));
            return;
        }
        deleteArmed = false;
        deleteAnimation.Content = L("Delete");
        saveTimer.Stop();
        StopPlaying();
        bool wasShowing = ShowingThis;
        try { AnimationStore.Delete(current); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Problem(L("Could not delete it: {0}", e.Message)); return; }
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
            Problem(L("There is already an animation called '{0}'.", name));
            return;
        }
        var old = current;
        bool wasShowing = ShowingThis;
        current = name;
        if (!Save()) { current = old; return; }
        if (!string.Equals(name, old, StringComparison.OrdinalIgnoreCase))
        {
            try { AnimationStore.Delete(old); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Problem(L("Saved as '{0}', but could not remove '{1}': {2}", name, old, e.Message)); }
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
            Problem(L("Could not save the animation: {0}", e.Message));
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
        if (current is null) summary.Text = names.Count == 0 ? L("No animations yet. Press New to start one.") : "";
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

    /// <summary>Changes how the animation follows the music. What the keyboard is doing (playing or one still frame) carries on.</summary>
    void SetMusic(MusicReaction reaction)
    {
        if (current is null || reaction == music) return;
        music = reaction;
        ShowMusic();
        ShowSelected();
        if (ShowingThis)
        {
            Effects.Animation.Show(Compile(), Effects.Animation.PinnedFrame);
            useAnimation();   // starts listening, or stops when nothing needs it any more
        }
        saveTimer.Stop();
        saveTimer.Start();
    }

    void ShowMusic()
    {
        int index = Math.Max(Array.FindIndex(MusicChoices, c => c.Reaction == music), 0);
        bool was = loading;
        loading = true;
        musicPicker.SelectedIndex = index;
        loading = was;
        musicNote.Text = MusicChoices[index].Note;
        musicNote.IsVisible = music != MusicReaction.None;
    }

    /// <summary>A music animation that plays needs the app listening; one frame held still for editing doesn't.</summary>
    void EnsureAudio()
    {
        if (engine.Effect == Effects.Animation && Effects.Animation.UsesAudio && engine.Audio is null) useAnimation();
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
        if (frames.Count >= KeyAnimation.MaxFrames) { Problem(L("An animation can have up to {0} frames.", KeyAnimation.MaxFrames)); return; }
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
            Note(L("The keyboard is already showing this animation. Pick another effect on the Lighting tab, then come back and press this."));
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
            else EnsureAudio();
            if (!engine.OutputEnabled) Problem(L("The keyboard is set to Preview only, so it plays here but not on the keys."));
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

    CompiledAnimation Compile() => new(current ?? "", frames.Select(f => (f.Cells, f.Hold, f.Fade)).ToList(), music);

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
            summary.Text = music == MusicReaction.Beat
                ? L("Frame {0} of {1}", selected + 1, frames.Count) + "   ·   " + L("one frame per beat")
                : L("Frame {0} of {1}", selected + 1, frames.Count) + "   ·   " + L("one loop takes {0:0.##} s at normal speed", Compile().Length);
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
        label.Text = frame.Fade > 0 ? $"{index + 1}  ·  " + L("{0:0.##} s + {1:0.##} fade", frame.Hold, frame.Fade) : $"{index + 1}  ·  " + L("{0:0.##} s", frame.Hold);
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
