using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PitBox.Views;

internal static class Glide
{
    public const double Ms = 360;

    public static Rect? Of(FrameworkElement? element, Visual layer)
    {
        if (element is not { IsLoaded: true, IsVisible: true }) return null;
        try { return element.TransformToVisual(layer).TransformBounds(new Rect(element.RenderSize)); }
        catch (InvalidOperationException) { return null; }
    }

    public static void To(Canvas layer, Rect from, FrameworkElement target, Action<Border> look, Action landed)
    {
        if (!Motion.Flourish)
        {
            landed();
            return;
        }
        layer.Dispatcher.BeginInvoke(() =>
        {
            if (Of(target, layer) is not { } to || to == from)
            {
                landed();
                return;
            }
            var marker = new Border { Width = from.Width, Height = from.Height, IsHitTestVisible = false };
            look(marker);
            var move = new TranslateTransform(from.X, from.Y);
            marker.RenderTransform = move;
            layer.Children.Add(marker);
            var d = TimeSpan.FromMilliseconds(Ms);
            DoubleAnimation Go(double a, double b) => new(a, b, d) { EasingFunction = Motion.Ease };
            var y = Go(from.Y, to.Y);
            y.Completed += (_, _) =>
            {
                layer.Children.Remove(marker);
                landed();
            };
            move.BeginAnimation(TranslateTransform.XProperty, Go(from.X, to.X));
            marker.BeginAnimation(FrameworkElement.WidthProperty, Go(from.Width, to.Width));
            marker.BeginAnimation(FrameworkElement.HeightProperty, Go(from.Height, to.Height));
            move.BeginAnimation(TranslateTransform.YProperty, y);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
