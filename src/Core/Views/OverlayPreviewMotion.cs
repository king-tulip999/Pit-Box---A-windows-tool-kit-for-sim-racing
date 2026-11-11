using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Core.Config;
using Core.Overlay;
using Core.Theme;

namespace Core.Views;

internal sealed class OverlayPreviewMotion
{
    internal readonly record struct Frame(BitmapSource Source, IReadOnlyList<OverlayHit> Hits, Point At, double Scale)
    {
        public Rect Bounds => new(At.X, At.Y, Source.PixelWidth * Scale, Source.PixelHeight * Scale);
        public Rect Place(Rect pixels) => new(At.X + pixels.X * Scale, At.Y + pixels.Y * Scale, pixels.Width * Scale, pixels.Height * Scale);
    }

    private readonly Canvas _scene;
    private readonly Image _preview;
    private readonly Func<IReadOnlyList<OverlayHit>> _hits;
    private readonly Canvas _layer = new() { IsHitTestVisible = false };
    private readonly Border _ring = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border _startSize = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly ScaleTransform _grow = new();
    private int _generation;
    private bool _holding;
    private const double HoverWash = 0.14;
    private const double HeldWash = 0.26;

    public bool Holding
    {
        get => _holding;
        set
        {
            _holding = value;
            _ring.Opacity = value ? HeldWash : HoverWash;
        }
    }

    public OverlayPreviewMotion(Canvas scene, Image preview, Func<IReadOnlyList<OverlayHit>> hits)
    {
        _scene = scene;
        _preview = preview;
        _hits = hits;
        _preview.RenderTransform = _grow;
        Hairline.Frame(_startSize);
        _ring.SetResourceReference(Border.BackgroundProperty, "AccentText");
        _ring.Opacity = HoverWash;
        _startSize.SetResourceReference(Border.BorderBrushProperty, "AccentText");
        _startSize.Opacity = 0.6;
        scene.Children.Add(_layer);
        scene.Children.Add(_ring);
        scene.Children.Add(_startSize);
    }

    private static double Seconds(double ms) => ms / 1000;

    public Frame? Capture(bool keepPixels = false)
    {
        if (_preview.Source is not BitmapSource source || source.PixelWidth == 0 || _preview.ActualWidth <= 0) return null;
        if (keepPixels && !source.IsFrozen) source = source.GetCurrentValueAsFrozen() as BitmapSource ?? source;
        return new Frame(source, _hits().ToList(), new Point(Canvas.GetLeft(_preview), Canvas.GetTop(_preview)), _preview.ActualWidth / source.PixelWidth);
    }

    private Frame? Settled()
    {
        _scene.UpdateLayout();
        return Capture();
    }

    public void Stop()
    {
        _generation++;
        foreach (UIElement child in _layer.Children)
            child.BeginAnimation(UIElement.OpacityProperty, null);
        _layer.Children.Clear();
        _preview.Clip = null;
        _preview.BeginAnimation(UIElement.OpacityProperty, null);
        _preview.Opacity = 1;
    }

    private static Dictionary<(string Group, OverlayMetric? Metric), Rect> Boxes(IReadOnlyList<OverlayHit> hits)
    {
        var boxes = new Dictionary<(string, OverlayMetric?), Rect>();
        foreach (var hit in hits)
        {
            var r = new Rect(hit.Rect.X, hit.Rect.Y, hit.Rect.Width, hit.Rect.Height);
            boxes[(hit.Group, hit.Metric)] = boxes.TryGetValue((hit.Group, hit.Metric), out var b) ? Rect.Union(b, r) : r;
        }
        return boxes;
    }

    public void Wipe(Frame before, bool glow, double ms = 450)
    {
        Stop();
        if (!Motion.Flourish || Settled() is null) return;
        var bounds = before.Bounds;
        var old = new Image { Source = before.Source, Width = bounds.Width, Height = bounds.Height, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        Canvas.SetLeft(old, bounds.X);
        Canvas.SetTop(old, bounds.Y);
        var clip = new RectangleGeometry(new Rect(bounds.Width, 0, 0, bounds.Height));
        old.Clip = clip;
        var duration = TimeSpan.FromMilliseconds(ms);
        clip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(new Rect(0, 0, bounds.Width, bounds.Height), new Rect(bounds.Width, 0, 0, bounds.Height), duration) { EasingFunction = glow ? null : Motion.Ease, FillBehavior = FillBehavior.Stop });
        _layer.Children.Add(old);

        double width = glow ? 26 : 2;
        var edge = new Rectangle { Width = width, Height = bounds.Height, Fill = glow ? Glow() : Ui.Res<Brush>("AccentText") };
        Canvas.SetLeft(edge, bounds.X - width / 2);
        Canvas.SetTop(edge, bounds.Y);
        var slide = new TranslateTransform(bounds.Width, 0);
        edge.RenderTransform = slide;
        edge.Opacity = 0;
        int generation = _generation;
        var run = new DoubleAnimation(0, bounds.Width, duration) { EasingFunction = glow ? null : Motion.Ease, FillBehavior = FillBehavior.Stop };
        run.Completed += (_, _) => { if (generation == _generation) _layer.Children.Clear(); };
        slide.BeginAnimation(TranslateTransform.XProperty, run);
        edge.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 1, duration) { FillBehavior = FillBehavior.Stop });
        _layer.Children.Add(edge);
    }

    private static Brush Glow()
    {
        var color = Ui.Res<Brush>("AccentText") is SolidColorBrush s ? s.Color : Colors.White;
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(140, color.R, color.G, color.B), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1));
        brush.Freeze();
        return brush;
    }

    public void Fade(Frame before, double ms = 180)
    {
        Stop();
        if (!Motion.Flourish || Settled() is null) return;
        var bounds = before.Bounds;
        var old = new Image { Source = before.Source, Width = bounds.Width, Height = bounds.Height, Stretch = Stretch.Fill, SnapsToDevicePixels = true, Opacity = 0 };
        Canvas.SetLeft(old, bounds.X);
        Canvas.SetTop(old, bounds.Y);
        int generation = _generation;
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => { if (generation == _generation) _layer.Children.Clear(); };
        old.BeginAnimation(UIElement.OpacityProperty, fade);
        _layer.Children.Add(old);
    }

    public void Grow(double ratio)
    {
        if (!Motion.Flourish || double.IsNaN(ratio) || Math.Abs(ratio - 1) < 0.005) return;
        _preview.RenderTransformOrigin = new Point(0.5, 0.5);
        var duration = TimeSpan.FromMilliseconds(220);
        _grow.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(ratio, 1, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
        _grow.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(ratio, 1, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
    }

    public void MarkStartSize(bool on)
    {
        if (on && Motion.Flourish && Capture() is { } frame)
        {
            _startSize.BeginAnimation(UIElement.OpacityProperty, null);
            _startSize.Opacity = 0.6;
            var b = frame.Bounds;
            _startSize.Width = b.Width;
            _startSize.Height = b.Height;
            Canvas.SetLeft(_startSize, b.X);
            Canvas.SetTop(_startSize, b.Y);
            _startSize.Visibility = Visibility.Visible;
            return;
        }
        if (_startSize.Visibility != Visibility.Visible) return;
        var fade = new DoubleAnimation(0.6, 0, TimeSpan.FromMilliseconds(400)) { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) => { if (_startSize.Opacity == 0) _startSize.Visibility = Visibility.Collapsed; };
        _startSize.Opacity = 0;
        _startSize.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    public void Spotlight(string? group, OverlayMetric? metric)
    {
        if (group is null || Capture() is not { } frame || Boxes(frame.Hits).TryGetValue((group, metric), out var pixels) is false)
        {
            _ring.Visibility = Visibility.Collapsed;
            return;
        }
        var spot = frame.Place(pixels);
        spot.Inflate(2, 2);
        spot = new Rect(Math.Round(spot.X), Math.Round(spot.Y), Math.Round(spot.Width), Math.Round(spot.Height));
        _ring.Width = spot.Width;
        _ring.Height = spot.Height;
        Canvas.SetLeft(_ring, spot.X);
        Canvas.SetTop(_ring, spot.Y);
        _ring.Visibility = Visibility.Visible;
    }
}
