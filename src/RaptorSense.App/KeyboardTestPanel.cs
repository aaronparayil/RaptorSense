using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using RaptorSense.Hardware;
using RaptorSense.Lighting;
using RaptorSense.Thermal;

namespace RaptorSense.App;

/// <summary>
/// The opt-in test of a 4-zone keyboard (<see cref="FourZoneTrial"/>). Nothing is sent until its owner presses Start, and
/// then only the keyboard's colours. Their answer decides whether RaptorSense uses the keyboard from the next start on.
/// </summary>
sealed class KeyboardTestPanel : UserControl
{
    readonly StackPanel body = new() { Spacing = 12 };
    readonly List<(FourZoneOrder Order, string Outcome, string? Detail)> attempts = [];
    bool busy;

    public KeyboardTestPanel()
    {
        var page = new StackPanel { Spacing = 14, Margin = new Thickness(28, 12, 28, 16), MaxWidth = 760 };
        page.Children.Add(Look.Card(body, L("Try this laptop's 4-zone keyboard")));
        Content = new ScrollViewer { Content = page };
        ShowStart();
    }

    void ShowStart(string? problem = null)
    {
        body.Children.Clear();
        body.Children.Add(Words(L("Other owners of {0} lit its keyboard in four zones the way RaptorSense lights a Nitro 5, but nobody has tried it with RaptorSense yet. This test shows red, green, blue and white, left to right, and asks what you see.", LaptopModels.Describe(ThisLaptop.Model))));
        body.Children.Add(Words(L("It changes only the keyboard's colours, never the fans or power. Close PredatorSense or NitroSense first. The colours may stay until the laptop restarts.")));
        if (problem is not null) body.Children.Add(Words(problem, Look.Warning));
        body.Children.Add(Buttons((L("Start the test"), () => Run(FourZoneOrder.ZonesFirst))));
    }

    async Task Run(FourZoneOrder order)
    {
        if (busy) return;
        busy = true;
        body.IsEnabled = false;
        try
        {
            var status = await ThermalClient.SetKeyboardAsync(FourZoneTrial.Test(order));
            // No test was made: the helper is missing, from an older RaptorSense, or didn't answer.
            if (status.Reading is null && status.HelperRevision == 0)
                ShowStart(status.Error == ThermalClient.HelperMissing
                    ? L("The test needs the keyboard helper. Install it on the Performance tab, then come back.")
                    : status.Error);
            else if (status.HelperRevision < ThermalProtocol.TrialRevision)
                ShowStart(L("The helper installed is from an older RaptorSense that can't light this keyboard. Update it on the Performance tab."));
            else if (!status.Ok) Failed(order, status.Error, status.KeyboardDetail);
            else Ask(order, status.KeyboardDetail);
        }
        finally
        {
            busy = false;
            body.IsEnabled = true;
        }
    }

    void Ask(FourZoneOrder order, string? detail)
    {
        body.Children.Clear();
        body.Children.Add(Words(L("Does the keyboard now show these colours, from left to right?")));
        var swatches = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var c in FourZoneTrial.TestColors)
            swatches.Children.Add(new Border
            {
                Width = 90, Height = 34, CornerRadius = new CornerRadius(8), BorderBrush = Look.CardBorder, BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)),
            });
        body.Children.Add(swatches);
        body.Children.Add(Buttons(
            (L("Yes, all four"), () => Answer(order, FourZoneSeen.AllRight, detail)),
            (L("Lit, but not like that"), () => Answer(order, FourZoneSeen.MixedUp, detail)),
            (L("Nothing changed, or it went dark"), () => Answer(order, FourZoneSeen.Nothing, detail))));
    }

    Task Answer(FourZoneOrder order, FourZoneSeen seen, string? detail)
    {
        attempts.Add((order, FourZoneTrial.Describe(seen), detail));
        if (seen == FourZoneSeen.AllRight) ShowWorks(order);
        else ShowDidNot(order, seen);
        return Task.CompletedTask;
    }

    void Failed(FourZoneOrder order, string? error, string? detail)
    {
        attempts.Add((order, "refused: " + (error ?? "no reason given"), detail));
        ShowDidNot(order, FourZoneSeen.Nothing);
    }

    void ShowWorks(FourZoneOrder order)
    {
        body.Children.Clear();
        body.Children.Add(Words(L("It works. RaptorSense can use this keyboard from the next start: effects for four zones, painting, animations and your own colours.")));
        body.Children.Add(Buttons((L("Use it and restart"), () =>
        {
            ThisLaptop.RememberTried(order);
            App.Restart();
            return Task.CompletedTask;
        })));
        AddReport();
    }

    void ShowDidNot(FourZoneOrder order, FourZoneSeen seen)
    {
        body.Children.Clear();
        bool another = order == FourZoneOrder.ZonesFirst && seen == FourZoneSeen.Nothing;
        body.Children.Add(Words(another
            ? L("Some laptops need the same calls in another order. Try that one too?")
            : L("RaptorSense won't use this keyboard as it is. Your report shows what it needs.")));
        var buttons = new List<(string, Func<Task>)>();
        if (another) buttons.Add((L("Try the other order"), () => Run(FourZoneOrder.StaticFirst)));
        buttons.Add((L("Start the keyboard's own wave"), PutBack));
        body.Children.Add(Buttons([.. buttons]));
        AddReport();
    }

    async Task PutBack()
    {
        if (busy) return;
        busy = true;
        try
        {
            var status = await ThermalClient.SetKeyboardAsync(FourZoneTrial.PutBack());
            if (!status.Ok) body.Children.Add(Words(L("Could not set the keyboard effect: {0}", status.Error ?? L(ThermalClient.HelperMissing)), Look.Warning));
        }
        finally { busy = false; }
    }

    void AddReport()
    {
        var text = new TextBox
        {
            Text = FourZoneTrial.Report(ThisLaptop.Model, AppInfo.Version, attempts),
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        };
        body.Children.Add(Words(L("Please paste this into a laptop report on GitHub, also when it worked: it is how this model gets added for everyone. It names only the laptop model and what the test did.")));
        body.Children.Add(text);
        body.Children.Add(Buttons((L("Copy the report"), async () =>
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text.Text);
        })));
    }

    static TextBlock Words(string text, IBrush? colour = null) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = colour ?? Look.Muted };

    static WrapPanel Buttons(params (string Text, Func<Task> Click)[] all)
    {
        var row = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, click) in all)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += async (_, _) => await click();
            row.Children.Add(button);
        }
        return row;
    }
}
