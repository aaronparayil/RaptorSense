using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RaptorSense.Lighting;

namespace RaptorSense.App;

/// <summary>
/// Hue around the wheel (0° = red, to the right, counter-clockwise), saturation from the centre outwards.
/// Value (brightness) only dims the drawing; it is set by a separate slider.
/// </summary>
sealed class ColorWheel : Control
{
    const int BitmapSize = 320;
    static WriteableBitmap? wheel;   // the same picture for every wheel; drawn once

    public double Hue { get; private set; }
    public double Saturation { get; private set; }
    public double Value { get; private set; } = 1;

    /// <summary>Raised when the user drags on the wheel.</summary>
    public event Action? Changed;

    public ColorWheel()
    {
        Width = Height = 190;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public void SetHsv(double hue, double saturation, double value)
    {
        (Hue, Saturation, Value) = (hue, Math.Clamp(saturation, 0, 1), Math.Clamp(value, 0, 1));
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var area = Area();
        context.DrawImage(Picture(), area);
        if (Value < 1) context.DrawEllipse(new SolidColorBrush(Colors.Black, 1 - Value), null, area.Center, area.Width / 2, area.Height / 2);

        double radius = Saturation * area.Width / 2, angle = Hue * Math.PI / 180;
        var marker = area.Center + new Vector(Math.Cos(angle) * radius, -Math.Sin(angle) * radius);
        context.DrawEllipse(null, new Pen(Brushes.Black, 1.5), marker, 9, 9);
        context.DrawEllipse(null, new Pen(Brushes.White, 2.5), marker, 7, 7);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        e.Pointer.Capture(this);   // keep following the drag outside the wheel
        Pick(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) Pick(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e) => e.Pointer.Capture(null);

    void Pick(Point point)
    {
        var area = Area();
        double dx = point.X - area.Center.X, dy = area.Center.Y - point.Y;
        Hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        Saturation = Math.Min(1, Math.Sqrt(dx * dx + dy * dy) / (area.Width / 2));
        InvalidateVisual();
        Changed?.Invoke();
    }

    Rect Area()
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        return new Rect((Bounds.Width - size) / 2, (Bounds.Height - size) / 2, size, size);
    }

    static WriteableBitmap Picture()
    {
        if (wheel is not null) return wheel;
        var bitmap = new WriteableBitmap(new PixelSize(BitmapSize, BitmapSize), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        double center = (BitmapSize - 1) / 2.0;
        var row = new byte[BitmapSize * 4];
        using var buffer = bitmap.Lock();
        for (int y = 0; y < BitmapSize; y++)
        {
            for (int x = 0; x < BitmapSize; x++)
            {
                double dx = x - center, dy = center - y;
                double distance = Math.Sqrt(dx * dx + dy * dy) / center;
                double alpha = Math.Clamp((1 - distance) * center + 0.5, 0, 1);   // soft edge, one pixel wide
                var c = Rgb.FromHsv(Math.Atan2(dy, dx) * 180 / Math.PI, Math.Min(1, distance));
                int i = x * 4;
                row[i] = (byte)(c.B * alpha);
                row[i + 1] = (byte)(c.G * alpha);
                row[i + 2] = (byte)(c.R * alpha);
                row[i + 3] = (byte)(255 * alpha);
            }
            Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
        }
        return wheel = bitmap;
    }
}
