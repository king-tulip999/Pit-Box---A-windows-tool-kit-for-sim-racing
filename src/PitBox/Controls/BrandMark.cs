using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Theme;

namespace PitBox.Controls;

public class BrandMark : FrameworkElement
{
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(BrandMark), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Foreground is not SolidColorBrush ink || ActualWidth <= 0) return;
        var edge = VisualTreeHelper.GetParent(this) is Border host ? host.BorderThickness : new Thickness(0);
        var tile = new Rect(-edge.Left, -edge.Top, ActualWidth + edge.Left + edge.Right, ActualHeight + edge.Top + edge.Bottom);
        Brand.Draw(dc, tile, ink.Color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }
}
