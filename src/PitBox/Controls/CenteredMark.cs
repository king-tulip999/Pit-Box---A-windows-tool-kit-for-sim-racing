using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PitBox.Controls;

public sealed class CenteredMark : Border
{
    public double Box { get; init; } = 15;
    public double MarkWidth { get; init; }
    public double MarkHeight { get; init; }

    public CenteredMark()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        Loaded += (_, _) => Fit(VisualTreeHelper.GetDpi(this));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Fit(newDpi);
    }

    private void Fit(DpiScale dpi)
    {
        Width = Matched(MarkWidth, Box, dpi.DpiScaleX);
        Height = Matched(MarkHeight, Box, dpi.DpiScaleY);
    }

    internal static double Matched(double mark, double box, double scale)
    {
        if (scale <= 0) return mark;
        double boxPixels = Math.Round(box * scale);
        double markPixels = Math.Max(1, Math.Round(mark * scale));
        if ((boxPixels - markPixels) % 2 != 0) markPixels++;
        return markPixels / scale;
    }
}
