using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PitBox.Views;

internal static class Reveal
{
    private static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 };

    public static void Once(FrameworkElement element, Action hold, Action play)
    {
        bool played = false;
        element.Loaded += (_, _) =>
        {
            if (played || !Motion.Flourish) return;
            played = true;
            hold();
            Motion.AfterPageSettles(element.Dispatcher, play);
        };
    }

    public static void PopIn(FrameworkElement element, double delayMs = 0, double from = 0.4)
    {
        if (!Motion.Flourish) return;
        var scale = new ScaleTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        double end = delayMs + 320;
        var grow = Keys(end, Pop, (0, from), (delayMs, from), (end, 1));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        element.BeginAnimation(UIElement.OpacityProperty, Keys(delayMs + 160, null, (0, 0), (delayMs, 0), (delayMs + 160, 1)));
    }

    public static DoubleAnimationUsingKeyFrames Keys(double totalMs, IEasingFunction? ease, params (double At, double Value)[] keys)
    {
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs), FillBehavior = FillBehavior.Stop };
        foreach (var (at, value) in keys)
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at)), ease));
        return anim;
    }
}
