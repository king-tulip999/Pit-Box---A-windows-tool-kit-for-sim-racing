using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PitBox.Theme;

namespace PitBox.Views;

internal static class TextFlip
{
    private static readonly DependencyProperty ShownProperty = DependencyProperty.RegisterAttached("Shown", typeof(string), typeof(TextFlip));

    public static void Set(TextBlock text, string value)
    {
        var shown = (string?)text.GetValue(ShownProperty);
        if (shown == value) return;
        text.SetValue(ShownProperty, value);
        TextCase.Set(text, value);
        if (shown is null || value.Length == 0 || !Motion.Flourish || !text.IsLoaded) return;
        if (text.RenderTransform is not ScaleTransform { IsFrozen: false } flip)
        {
            flip = new ScaleTransform();
            text.RenderTransform = flip;
            text.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        var duration = TimeSpan.FromMilliseconds(320);
        flip.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
        text.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
    }
}
