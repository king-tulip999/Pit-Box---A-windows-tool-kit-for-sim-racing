using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PitBox.Controls;
using PitBox.Services;

namespace PitBox.Views;

internal static class Motion
{
    public static long PageSettlesAt { get; set; }

    public static long PageAnimatesFrom { get; set; }

    public static Task PageSettled(System.Windows.Threading.Dispatcher dispatcher)
    {
        if (PageSettlesAt <= Environment.TickCount64) return Task.CompletedTask;
        var done = new TaskCompletionSource();
        WhenSettled(dispatcher, () => done.TrySetResult());
        return done.Task;
    }

    public static void AfterPageSettles(System.Windows.Threading.Dispatcher dispatcher, Action work) =>
        dispatcher.BeginInvoke(() => WhenSettled(dispatcher, work), System.Windows.Threading.DispatcherPriority.Background);

    private static void WhenSettled(System.Windows.Threading.Dispatcher dispatcher, Action work)
    {
        long wait = PageSettlesAt - Environment.TickCount64;
        if (wait <= 0)
        {
            work();
            return;
        }
        bool started = PageAnimatesFrom <= Environment.TickCount64;
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(started ? wait : Math.Min(wait, 40)) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            WhenSettled(dispatcher, work);
        };
        timer.Start();
    }

    public const int UserIdleMs = 350, UserIdleWaitMs = 4000;

    public static void WhenUserIdle(System.Windows.Threading.Dispatcher dispatcher, Action work)
    {
        long giveUp = Environment.TickCount64 + UserIdleWaitMs;
        AfterPageSettles(dispatcher, () => WaitForStillInput(dispatcher, work, giveUp));
    }

    private static void WaitForStillInput(System.Windows.Threading.Dispatcher dispatcher, Action work, long giveUp)
    {
        long since = SinceLastInput();
        if (since >= UserIdleMs || Environment.TickCount64 >= giveUp)
        {
            dispatcher.BeginInvoke(work, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromMilliseconds(UserIdleMs - since) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            WaitForStillInput(dispatcher, work, giveUp);
        };
        timer.Start();
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private static long SinceLastInput()
    {
        var info = new LastInputInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) : long.MaxValue;
    }

    public const int PageFadeMs = 160, SwapMs = 240, RevealMs = 200;

    public const double SwapDistance = 12;

    public static readonly IEasingFunction Ease = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    internal static IEasingFunction Frozen(EasingFunctionBase ease)
    {
        ease.Freeze();
        return ease;
    }

    public static bool Enabled => AppServices.Current?.Animations ?? true;

    public static bool Flourish => Enabled && !ScreenshotRunner.Active;

    public static bool WindowActive { get; private set; } = true;

    public static bool FollowFocus { get; set; } = true;

    public static event Action? WindowActiveChanged;

    public static void Refresh() => WindowActiveChanged?.Invoke();

    public static void SetWindowActive(bool active, bool force = false)
    {
        if (!FollowFocus && !force || WindowActive == active) return;
        WindowActive = active;
        WindowActiveChanged?.Invoke();
    }

    public static void Enter(UIElement element, double delayMs = 0, double distance = 10, double durationMs = 280)
    {
        if (!Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            return;
        }

        var move = element.RenderTransform as TranslateTransform;
        if (move is null || move.IsFrozen)
        {
            move = new TranslateTransform();
            element.RenderTransform = move;
        }

        element.Opacity = 1;
        move.Y = 0;
        element.BeginAnimation(UIElement.OpacityProperty, Held(0, 1, delayMs, durationMs));
        move.BeginAnimation(TranslateTransform.YProperty, Held(distance, 0, delayMs, durationMs));
    }

    private static DoubleAnimationUsingKeyFrames Held(double from, double to, double delayMs, double durationMs)
    {
        var anim = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(delayMs + durationMs), FillBehavior = FillBehavior.Stop };
        anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        if (delayMs > 0) anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs))));
        anim.KeyFrames.Add(new EasingDoubleKeyFrame(to, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + durationMs)), Ease));
        anim.Freeze();
        return anim;
    }

    public static void InTurns(IEnumerable<UIElement> elements)
    {
        var later = elements.Where(e => e.Visibility == Visibility.Visible).ToList();
        if (later.Count == 0) return;
        foreach (var element in later) element.Visibility = Visibility.Collapsed;
        int next = 0;
        void Reveal()
        {
            later[next++].Visibility = Visibility.Visible;
            if (next < later.Count) later[next].Dispatcher.BeginInvoke(Reveal, System.Windows.Threading.DispatcherPriority.Background);
        }
        later[0].Dispatcher.BeginInvoke(Reveal, System.Windows.Threading.DispatcherPriority.Background);
    }

    public static void FadeIn(UIElement element, double durationMs = PageFadeMs)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
        if (!Enabled || durationMs <= 0) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(durationMs)) { FillBehavior = FillBehavior.Stop });
    }

    public static void FadeTo(UIElement element, double to, double durationMs = 200, Action? done = null)
    {
        if (!Enabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = to;
            done?.Invoke();
            return;
        }
        double from = element.Opacity;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = to;
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = Ease, FillBehavior = FillBehavior.Stop };
        if (done is not null)
            anim.Completed += (_, _) => done();
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private static readonly IEasingFunction Spring = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 });

    public static void InstallPress()
    {
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ButtonBase), UIElement.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler((s, e) => { if (Innermost(s, e.OriginalSource)) Squeeze((FrameworkElement)s, true); }));
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ButtonBase), UIElement.PreviewMouseLeftButtonUpEvent,
            new System.Windows.Input.MouseButtonEventHandler((s, _) => Squeeze((FrameworkElement)s, false)), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(System.Windows.Controls.Primitives.ButtonBase), UIElement.MouseLeaveEvent,
            new System.Windows.Input.MouseEventHandler((s, _) => Squeeze((FrameworkElement)s, false)), handledEventsToo: true);
    }

    public static void InstallSwitches()
    {
        var slide = new RoutedEventHandler((s, _) => SlideKnob((CheckBox)s));
        EventManager.RegisterClassHandler(typeof(CheckBox), System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, slide);
        EventManager.RegisterClassHandler(typeof(CheckBox), System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, slide);
    }

    private static void SlideKnob(CheckBox box)
    {
        if (!box.IsLoaded || !Enabled || box.Template?.FindName("Knob", box) is not FrameworkElement knob || knob.RenderTransform is not TranslateTransform move) return;
        bool on = box.IsChecked == true;
        move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(on ? 0 : KnobOffset.Travel, on ? KnobOffset.Travel : 0, TimeSpan.FromMilliseconds(180))
            { EasingFunction = Ease, FillBehavior = FillBehavior.Stop });
    }

    private static bool Innermost(object button, object source)
    {
        for (var d = source as DependencyObject; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.ButtonBase) return ReferenceEquals(d, button);
        return false;
    }

    private static void Squeeze(FrameworkElement button, bool down)
    {
        if (button is not Control { Template: { } template } control || template.FindName("Bd", control) is not FrameworkElement bd
            || bd.RenderTransform is not ScaleTransform { IsFrozen: false } scale) return;
        if (down && (!Enabled || !button.IsEnabled)) return;
        if (!down && scale.ScaleX == 1 && scale.ScaleY == 1 && !scale.HasAnimatedProperties) return;
        double sx = down ? 1 - Math.Min(0.06, 4 / Math.Max(1, bd.ActualWidth)) : 1;
        double sy = down ? 1 - Math.Min(0.06, 3 / Math.Max(1, bd.ActualHeight)) : 1;
        var d = TimeSpan.FromMilliseconds(down ? 70 : 240);
        var ease = down ? Ease : Spring;
        var fill = down ? FillBehavior.HoldEnd : FillBehavior.Stop;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(sx, d) { EasingFunction = ease, FillBehavior = fill });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(sy, d) { EasingFunction = ease, FillBehavior = fill });
    }

    public static void Pressable(FrameworkElement element)
    {
        var scale = new ScaleTransform();
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = scale;
        void Go(double sx, double sy, double ms, IEasingFunction ease)
        {
            if (!Enabled) return;
            var d = TimeSpan.FromMilliseconds(ms);
            var fill = sx == 1 && sy == 1 ? FillBehavior.Stop : FillBehavior.HoldEnd;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(sx, d) { EasingFunction = ease, FillBehavior = fill });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(sy, d) { EasingFunction = ease, FillBehavior = fill });
        }
        void Release() => Go(1, 1, 240, Spring);
        element.PreviewMouseLeftButtonDown += (_, _) => Go(
            1 - Math.Min(0.06, 8 / Math.Max(1, element.ActualWidth)), 1 - Math.Min(0.06, 6 / Math.Max(1, element.ActualHeight)), 70, Ease);
        element.PreviewMouseLeftButtonUp += (_, _) => Release();
        element.MouseLeave += (_, _) => Release();
    }

    public const double BarStep = 0.015;

    public static void To(DependencyObject target, DependencyProperty property, double to, double durationMs = 450, double glideFrom = 1e-3)
    {
        if (target is not IAnimatable animatable)
            return;
        if (!Enabled || target.GetValue(property) is double current && double.IsNaN(current))
        {
            animatable.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }
        if (target.GetValue(property) is double now && Math.Abs(now - to) < glideFrom)
        {
            if (now != to)
            {
                animatable.BeginAnimation(property, null);
                target.SetValue(property, to);
            }
            return;
        }
        animatable.BeginAnimation(property, new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs)) { EasingFunction = Ease });
    }
}

internal interface IPage
{
    void OnShown();
    void OnHidden();
}

internal interface IRevealAnchor
{
    bool Reveal(string anchor);
}

internal interface IShowAnchor
{
    void ShowAnchor(DependencyObject target);
}

internal interface IPageHistory
{
    object? HistoryState { get; }
    void RestoreHistory(object? state);
}
