using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PitBox.Views;

internal sealed class LaunchLights : Adorner
{
    private const int Count = 5;
    private const double Size = 13, Gap = 6, StepMs = 130, HoldMs = 260;

    private int _lit;

    private LaunchLights(UIElement brand) : base(brand) => IsHitTestVisible = false;

    public static void Play(FrameworkElement brand)
    {
        if (!Motion.Flourish || AdornerLayer.GetAdornerLayer(brand) is not { } layer) return;
        var lights = new LaunchLights(brand);
        layer.Add(lights);
        brand.Opacity = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(StepMs) };
        timer.Tick += (_, _) =>
        {
            if (lights._lit < Count)
            {
                lights._lit++;
                lights.InvalidateVisual();
                if (lights._lit == Count) timer.Interval = TimeSpan.FromMilliseconds(HoldMs);
                return;
            }
            timer.Stop();
            layer.Remove(lights);
            GoGreen(brand);
        };
        timer.Start();
    }

    private static void GoGreen(FrameworkElement brand)
    {
        brand.Opacity = 1;
        var move = new TranslateTransform();
        brand.RenderTransform = move;
        var duration = TimeSpan.FromMilliseconds(420);
        var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-30, 0, duration) { EasingFunction = spring, FillBehavior = FillBehavior.Stop });
        brand.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { FillBehavior = FillBehavior.Stop });
    }

    protected override void OnRender(DrawingContext dc)
    {
        var on = Theme.ThemeManager.Brush("Danger", Brushes.Red);
        var off = Theme.ThemeManager.Brush("MeterTrack", Brushes.DimGray);
        double top = (AdornedElement.RenderSize.Height - Size) / 2;
        for (int i = 0; i < Count; i++)
            dc.DrawRectangle(i < _lit ? on : off, null, new Rect(i * (Size + Gap), top, Size, Size));
    }
}
