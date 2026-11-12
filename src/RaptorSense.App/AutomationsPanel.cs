using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using RaptorSense.Automation;
using RaptorSense.Profiles;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>Rules that switch the lighting, power mode and fans when an app opens or the power source changes.</summary>
sealed class AutomationsPanel : UserControl
{
    const string NoChange = "No change";

    static readonly string[] TriggerNames = ["An app is open", "On battery", "Plugged in"];
    static readonly AutomationTrigger[] Triggers = [AutomationTrigger.AppOpen, AutomationTrigger.OnBattery, AutomationTrigger.PluggedIn];
    static readonly PowerProfile[] PowerModes = Enum.GetValues<PowerProfile>();
    static readonly FanMode[] FanModes = [FanMode.Auto, FanMode.Max];

    readonly AutomationRunner runner;
    readonly ToggleSwitch master = new() { OnContent = "Automations on", OffContent = "Automations off" };
    readonly StackPanel cards = new() { Spacing = 12 };
    readonly TextBlock problem = new() { Foreground = Look.Warning, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    readonly List<TextBlock> activeTags = [];

    public AutomationsPanel(AutomationRunner runner)
    {
        this.runner = runner;

        master.IsChecked = runner.Settings.Enabled;
        master.IsCheckedChanged += (_, _) =>
        {
            bool on = master.IsChecked == true;
            if (on != runner.Settings.Enabled) runner.Update(runner.Settings with { Enabled = on });
        };

        var add = new Button { Content = "Add rule" };
        add.Click += (_, _) =>
        {
            runner.Update(runner.Settings with { Rules = [.. runner.Settings.Rules, new AutomationRule()] });
            Rebuild();
        };

        var intro = new StackPanel { Spacing = 10 };
        intro.Children.Add(new TextBlock
        {
            Text = "Rules change your lighting, power mode and fans by themselves. When a rule ends, everything goes back to how it " +
                   "was before it started, including anything you changed by hand in between. If two rules set the same thing, the higher one wins.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Look.Muted,
        });
        intro.Children.Add(master);

        var page = new StackPanel { Spacing = 14, Margin = new Thickness(28, 12, 28, 16), MaxWidth = 1100 };
        page.Children.Add(Look.Card(intro, "Automations"));
        page.Children.Add(cards);
        page.Children.Add(add);
        page.Children.Add(problem);
        page.Children.Add(Look.Note(
            "An app counts as open from when it is running until it closes, so switching to another window doesn't end the rule. " +
            "Power mode and fans need fan control installed. RaptorSense only looks for the apps you name here, and nothing leaves your laptop."));
        Content = new ScrollViewer { Content = page };

        runner.Changed += ShowState;
        runner.EnabledChanged += () => master.IsChecked = runner.Settings.Enabled;
        AttachedToVisualTree += (_, _) => Rebuild();   // picks up profiles saved since the tab was last open
    }

    void Rebuild()
    {
        cards.Children.Clear();
        activeTags.Clear();
        var rules = runner.Settings.Rules;
        for (int i = 0; i < rules.Count; i++) cards.Children.Add(Card(i, rules[i]));
        if (rules.Count == 0) cards.Children.Add(new TextBlock { Text = "No rules yet.", Foreground = Look.Muted, Margin = new Thickness(4, 0, 0, 0) });
        ShowState();
    }

    void ShowState()
    {
        var rules = runner.Settings.Rules;
        var state = runner.State;
        for (int i = 0; i < activeTags.Count && i < rules.Count; i++)
            activeTags[i].IsVisible = runner.Settings.Enabled && rules[i].IsActive(state);
        problem.Text = runner.LastProblem;
        problem.IsVisible = runner.LastProblem is not null;
    }

    Control Card(int index, AutomationRule rule)
    {
        var enabled = new CheckBox { Content = "On", IsChecked = rule.Enabled };
        var trigger = new ComboBox { ItemsSource = TriggerNames, SelectedIndex = Array.IndexOf(Triggers, rule.Trigger), MinWidth = 150 };
        var app = new TextBox { Text = rule.App, PlaceholderText = "App name, e.g. VALORANT", Width = 220 };
        var runningApps = new ComboBox { PlaceholderText = "Choose a running app", MinWidth = 190 };
        var appControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { app, runningApps }, IsVisible = rule.Trigger == AutomationTrigger.AppOpen };

        var profiles = new List<string> { NoChange };
        profiles.AddRange(ProfileStore.List());
        if (rule.LightingProfile is { } chosen && !profiles.Contains(chosen)) profiles.Add(chosen);   // deleted since: keep it visible
        var lighting = new ComboBox { ItemsSource = profiles, SelectedItem = rule.LightingProfile ?? NoChange, MinWidth = 180 };
        var power = Picker(PowerModes, rule.Power);
        var fans = Picker(FanModes, rule.Fans);

        var active = new TextBlock { Text = "Active now", Foreground = Look.Accent, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
        activeTags.Add(active);

        var up = new Button { Content = "Up", IsEnabled = index > 0 };
        var down = new Button { Content = "Down", IsEnabled = index < runner.Settings.Rules.Count - 1 };
        var remove = new Button { Content = "Remove" };

        // Connected after the initial values are set, so building a card doesn't count as a change.
        enabled.IsCheckedChanged += (_, _) => Replace(index, r => r with { Enabled = enabled.IsChecked == true });
        trigger.SelectionChanged += (_, _) =>
        {
            appControls.IsVisible = Triggers[trigger.SelectedIndex] == AutomationTrigger.AppOpen;
            Replace(index, r => r with { Trigger = Triggers[trigger.SelectedIndex] });
        };
        app.LostFocus += (_, _) =>
        {
            // Also fires while this card is being replaced, when the rule may already be gone.
            var rules = runner.Settings.Rules;
            if (index < rules.Count && (app.Text ?? "") != rules[index].App) Replace(index, r => r with { App = app.Text ?? "" });
        };
        runningApps.DropDownOpened += (_, _) => runningApps.ItemsSource = RunningApps();
        runningApps.SelectionChanged += (_, _) =>
        {
            if (runningApps.SelectedItem is not string name) return;
            app.Text = name;
            Replace(index, r => r with { App = name });
        };
        lighting.SelectionChanged += (_, _) => Replace(index, r => r with { LightingProfile = lighting.SelectedItem is string name && name != NoChange ? name : null });
        power.SelectionChanged += (_, _) => Replace(index, r => r with { Power = Chosen(PowerModes, power) });
        fans.SelectionChanged += (_, _) => Replace(index, r => r with { Fans = Chosen(FanModes, fans) });
        up.Click += (_, _) => Move(index, -1);
        down.Click += (_, _) => Move(index, 1);
        remove.Click += (_, _) =>
        {
            runner.Update(runner.Settings with { Rules = runner.Settings.Rules.Where((_, i) => i != index).ToList() });
            Rebuild();
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { up, down, remove } };
        DockPanel.SetDock(buttons, Dock.Right);
        var when = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 6 };
        when.Children.Add(enabled);
        when.Children.Add(Caption("When"));
        when.Children.Add(trigger);
        when.Children.Add(appControls);
        when.Children.Add(active);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 14, LineSpacing = 6 };
        actions.Children.Add(Labelled("Lighting profile", lighting));
        actions.Children.Add(Labelled("Power mode", power));
        actions.Children.Add(Labelled("Fans", fans));

        return Look.Card(new StackPanel
        {
            Spacing = 12,
            Children = { new DockPanel { Children = { buttons, when } }, actions },
        });
    }

    void Replace(int index, Func<AutomationRule, AutomationRule> change)
    {
        var rules = runner.Settings.Rules.ToList();
        if (index >= rules.Count) return;
        rules[index] = change(rules[index]);
        runner.Update(runner.Settings with { Rules = rules });
    }

    void Move(int index, int by)
    {
        var rules = runner.Settings.Rules.ToList();
        (rules[index], rules[index + by]) = (rules[index + by], rules[index]);
        runner.Update(runner.Settings with { Rules = rules });
        Rebuild();
    }

    // Programs with a window, by the name the rules match on. Only read when the list is opened.
    static List<string> RunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try { if (process.MainWindowHandle != IntPtr.Zero) names.Add(process.ProcessName); }
                catch (Exception e) when (e is InvalidOperationException or NotSupportedException) { }   // it has already closed
            }
        }
        names.Remove(AppInfo.Name);
        return [.. names];
    }

    static ComboBox Picker<T>(T[] values, T? current) where T : struct, Enum
    {
        var items = new List<string> { NoChange };
        items.AddRange(values.Select(v => v.ToString()));
        return new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = current is { } value ? Array.IndexOf(values, value) + 1 : 0,
            MinWidth = 140,
        };
    }

    static T? Chosen<T>(T[] values, ComboBox picker) where T : struct, Enum =>
        picker.SelectedIndex > 0 ? values[picker.SelectedIndex - 1] : null;

    static TextBlock Caption(string text) => new() { Text = text, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center };

    static Control Labelled(string label, Control control) => new StackPanel
    {
        Spacing = 4,
        Children = { new TextBlock { Text = label, Opacity = 0.7, FontSize = 12 }, control },
    };
}
