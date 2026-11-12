using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using RaptorSense.Layout;
using RaptorSense.Lighting;
using Key = RaptorSense.Layout.Key;

namespace RaptorSense.App;

/// <summary>The keyboard drawn to scale, one coloured cap per key. Click or drag to paint; right button erases.</summary>
sealed class KeyboardView : UserControl
{
    // Key caps show the symbols printed on them; anything not listed shows its key-map label.
    static readonly Dictionary<string, string> CapText = new()
    {
        ["`"] = "~\n`", ["-"] = "_\n-", ["="] = "+\n=", ["["] = "{\n[", ["]"] = "}\n]", ["\\"] = "|\n\\",
        [";"] = ":\n;", ["'"] = "\"\n'", [","] = "<\n,", ["."] = ">\n.", ["/"] = "?\n/",
        ["Bksp"] = "Backspace", ["Caps"] = "Caps Lock", ["RShift"] = "Shift", ["RCtrl"] = "Ctrl",
        ["Up"] = "▲", ["Down"] = "▼", ["Left"] = "◀", ["Right"] = "▶",
        ["Prev"] = "⏮", ["Play"] = "⏯", ["Next"] = "⏭", ["Power"] = "⏻",
        ["Num/"] = "/", ["Num*"] = "*", ["Num-"] = "-", ["Num+"] = "+", ["NumEnt"] = "Enter", ["Num."] = ".",
        ["Num0"] = "0", ["Num1"] = "1", ["Num2"] = "2", ["Num3"] = "3", ["Num4"] = "4",
        ["Num5"] = "5", ["Num6"] = "6", ["Num7"] = "7", ["Num8"] = "8", ["Num9"] = "9",
    };

    readonly Dictionary<int, (SolidColorBrush Fill, TextBlock Label)> keys = [];

    /// <summary>Raised for every key the pointer presses or drags over: the key, and true for the right (erase) button.</summary>
    public event Action<Key, bool>? KeyPainted;

    public KeyboardView()
    {
        var canvas = new Canvas { Width = PhysicalLayout.Width, Height = PhysicalLayout.Height, Background = Brushes.Transparent };
        foreach (var key in KeyMap.Keys)
        {
            if (PhysicalLayout.For(key.Label) is not { } rect) continue;
            var fill = new SolidColorBrush(Colors.Black);
            var text = CapText.GetValueOrDefault(key.Label, key.Label);
            // Shrink long labels to fit the key's width (about 0.62 em per character) instead of wrapping.
            int longest = text.Split('\n').Max(line => line.Length);
            double fontSize = Math.Clamp((rect.Width - 10) / (longest * 0.62), 8, rect.Height < 40 ? 10 : 13);
            var label = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
                LineHeight = fontSize * 1.3,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var view = new Border
            {
                Width = rect.Width,
                Height = rect.Height,
                CornerRadius = new CornerRadius(7),
                Background = fill,
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 62, 70)),
                BorderThickness = new Thickness(1),
                Child = label,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            ToolTip.SetTip(view, $"{key.Label} (cell {key.Cell})");
            Canvas.SetLeft(view, rect.X);
            Canvas.SetTop(view, rect.Y);
            canvas.Children.Add(view);
            keys[key.Cell] = (fill, label);
        }

        // Handled on the canvas, not the keys: the pressed element captures the pointer, so a drag would
        // never reach the other keys. Hit-testing by position paints every key the pointer crosses.
        canvas.PointerPressed += (_, e) => PaintAt(canvas, e);
        canvas.PointerMoved += (_, e) => PaintAt(canvas, e);
        Content = new Viewbox { Child = canvas };
    }

    /// <summary>Shows one colour per lighting cell.</summary>
    public void Show(ReadOnlySpan<Rgb> cells)
    {
        foreach (var (cell, view) in keys)
        {
            var c = cell < cells.Length ? cells[cell] : Rgb.Black;
            var color = Color.FromRgb(c.R, c.G, c.B);
            if (view.Fill.Color == color) continue;
            view.Fill.Color = color;
            view.Label.Foreground = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 140 ? Brushes.Black : Brushes.White;
        }
    }

    void PaintAt(Canvas canvas, PointerEventArgs e)
    {
        var point = e.GetCurrentPoint(canvas);
        bool paint = point.Properties.IsLeftButtonPressed, erase = point.Properties.IsRightButtonPressed;
        if (!paint && !erase) return;

        var hit = KeyMap.Keys.FirstOrDefault(k => PhysicalLayout.For(k.Label)?.Contains(point.Position.X, point.Position.Y) == true);
        if (hit is null) return;   // between keys
        KeyPainted?.Invoke(hit, erase);
        e.Handled = true;
    }
}
