using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PitBox.Views;

internal static class RaceArrival
{
    private const double SlamMs = 420, CarFromMs = 160, CarMs = 640, BarsFromMs = 620, BarsMs = 760;

    private static readonly IEasingFunction Slam = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
    private static readonly IEasingFunction Drive = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };

    public static void When(FrameworkElement card, FrameworkElement position, FrameworkElement climb, Stripe stripe, LapStrip? laps)
    {
        bool played = false;
        card.Loaded += (_, _) =>
        {
            if (played || !Motion.Flourish) return;
            played = true;
            Hold(position, climb, stripe.Car, laps);
            Motion.AfterPageSettles(card.Dispatcher, () => Play(position, climb, stripe, laps));
        };
    }

    private static void Hold(FrameworkElement position, FrameworkElement climb, FrameworkElement? car, LapStrip? laps)
    {
        position.Opacity = 0;
        climb.Opacity = 0;
        if (car is not null) car.Opacity = 0;
        if (laps is not null) laps.Grow = 0;
    }

    private static void Play(FrameworkElement position, FrameworkElement climb, Stripe stripe, LapStrip? laps)
    {
        position.Opacity = 1;
        climb.Opacity = 1;
        if (stripe.Car is { } shownCar) shownCar.Opacity = 1;
        if (laps is not null) laps.Grow = 1;
        if (!Motion.Flourish) return;

        var scale = new ScaleTransform();
        position.RenderTransformOrigin = new Point(0.15, 0.8);
        position.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, Keys(SlamMs, Slam, (0, 1.9), (SlamMs, 1)));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Keys(SlamMs, Slam, (0, 1.9), (SlamMs, 1)));
        position.BeginAnimation(UIElement.OpacityProperty, Keys(SlamMs * 0.55, null, (0, 0), (SlamMs * 0.55, 1)));

        var slide = new TranslateTransform();
        climb.RenderTransform = slide;
        slide.BeginAnimation(TranslateTransform.XProperty, Keys(SlamMs + 380, Motion.Ease, (0, -14), (SlamMs - 60, -14), (SlamMs + 320, 0)));
        climb.BeginAnimation(UIElement.OpacityProperty, Keys(SlamMs + 320, Motion.Ease, (0, 0), (SlamMs - 60, 0), (SlamMs + 320, 1)));

        if (stripe.Car is { } car && stripe.ActualWidth > 0)
        {
            var move = new TranslateTransform();
            car.RenderTransform = move;
            double from = stripe.ActualWidth * 0.7;
            move.BeginAnimation(TranslateTransform.XProperty, Keys(CarFromMs + CarMs, Drive, (0, from), (CarFromMs, from), (CarFromMs + CarMs, 0)));
            car.BeginAnimation(UIElement.OpacityProperty, Keys(CarFromMs + CarMs * 0.3, null, (0, 0), (CarFromMs, 0), (CarFromMs + CarMs * 0.3, 1)));
            SpeedLines(stripe, car);
        }

        laps?.BeginAnimation(LapStrip.GrowProperty, Keys(BarsFromMs + BarsMs, null, (0, 0), (BarsFromMs, 0), (BarsFromMs + BarsMs, 1)));
    }

    private static void SpeedLines(Stripe stripe, FrameworkElement car)
    {
        double[] rows = { 0.42, 0.56, 0.68 };
        var lines = new List<Border>();
        for (int i = 0; i < rows.Length; i++)
        {
            double begin = CarFromMs + i * 60, reach = stripe.ActualWidth * 0.36;
            var line = new Border
            {
                Height = 2, Width = reach, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
                Margin = new Thickness(Math.Max(0, stripe.ActualWidth * 0.38 - reach), stripe.ActualHeight * rows[i], 0, 0), Opacity = 0,
            };
            line.SetResourceReference(Border.BackgroundProperty, "Heading");
            var clip = new RectangleGeometry(Rect.Empty);
            line.Clip = clip;
            stripe.Children.Add(line);
            lines.Add(line);
            var sweep = new RectAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(begin + 700), FillBehavior = FillBehavior.Stop };
            sweep.KeyFrames.Add(new DiscreteRectKeyFrame(new Rect(reach, 0, 0, 2), KeyTime.FromTimeSpan(TimeSpan.Zero)));
            sweep.KeyFrames.Add(new DiscreteRectKeyFrame(new Rect(reach, 0, 0, 2), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(begin))));
            sweep.KeyFrames.Add(new EasingRectKeyFrame(new Rect(0, 0, reach, 2), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(begin + 180)), Motion.Ease));
            sweep.KeyFrames.Add(new EasingRectKeyFrame(new Rect(0, 0, 0, 2), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(begin + 700)), Motion.Ease));
            clip.BeginAnimation(RectangleGeometry.RectProperty, sweep);
            line.BeginAnimation(UIElement.OpacityProperty, Keys(begin + 700, null, (0, 0), (begin, 0), (begin + 180, 0.7), (begin + 700, 0)));
        }
        var cleanup = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(CarFromMs + 1000) };
        cleanup.Tick += (_, _) =>
        {
            cleanup.Stop();
            foreach (var line in lines) stripe.Children.Remove(line);
        };
        cleanup.Start();
    }

    private static DoubleAnimationUsingKeyFrames Keys(double totalMs, IEasingFunction? ease, params (double At, double Value)[] keys)
    {
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(totalMs), FillBehavior = FillBehavior.Stop };
        foreach (var (at, value) in keys)
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at)), ease));
        return anim;
    }
}
