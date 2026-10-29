using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PitBox.Config;
using PitBox.Overlay;
using PitBox.Theme;

namespace PitBox.Views;

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
    private readonly Path _shade = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border _ring = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Border _startSize = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TranslateTransform _move = new();
    private readonly ScaleTransform _grow = new();
    private int _generation;

    public double Shown { get; private set; } = 1;

    public OverlayPreviewMotion(Canvas scene, Image preview, Func<IReadOnlyList<OverlayHit>> hits)
    {
        _scene = scene;
        _preview = preview;
        _hits = hits;
        _preview.RenderTransform = new TransformGroup { Children = { _grow, _move } };
        Hairline.Frame(_ring);
        Hairline.Frame(_startSize);
        _ring.SetResourceReference(Border.BorderBrushProperty, "AccentText");
        _startSize.SetResourceReference(Border.BorderBrushProperty, "AccentText");
        _startSize.Opacity = 0.6;
        var shade = Ui.Res<Brush>("Bg").CloneCurrentValue();
        shade.Opacity = 0.55;
        shade.Freeze();
        _shade.Fill = shade;
        scene.Children.Add(_layer);
        scene.Children.Add(_shade);
        scene.Children.Add(_ring);
        scene.Children.Add(_startSize);
    }

    private static double Seconds(double ms) => ms / 1000;

    public Frame? Capture()
    {
        if (_preview.Source is not BitmapSource source || source.PixelWidth == 0 || _preview.ActualWidth <= 0) return null;
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
        _preview.Opacity = Shown;
    }

    public void SetShown(bool shown, bool animate)
    {
        double to = shown ? 1 : 0.4;
        if (to == Shown) return;
        Shown = to;
        _layer.Opacity = to;
        if (animate && Motion.Flourish) Motion.To(_preview, UIElement.OpacityProperty, to, 240);
        else
        {
            _preview.BeginAnimation(UIElement.OpacityProperty, null);
            _preview.Opacity = to;
        }
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

    public static BitmapSource? Crop(BitmapSource source, Rect box)
    {
        var pixels = new Int32Rect((int)Math.Floor(box.X), (int)Math.Floor(box.Y), (int)Math.Ceiling(box.Width) + 1, (int)Math.Ceiling(box.Height) + 1);
        pixels.Width = Math.Min(pixels.Width, source.PixelWidth - pixels.X);
        pixels.Height = Math.Min(pixels.Height, source.PixelHeight - pixels.Y);
        if (pixels.X < 0 || pixels.Y < 0 || pixels.Width <= 0 || pixels.Height <= 0) return null;
        var copy = new CroppedBitmap(source, pixels);
        copy.Freeze();
        return copy;
    }

    private Image? Piece(Frame frame, Rect pixels)
    {
        if (Crop(frame.Source, pixels) is not { } crop) return null;
        var place = frame.Place(new Rect(Math.Floor(pixels.X), Math.Floor(pixels.Y), crop.PixelWidth, crop.PixelHeight));
        var image = new Image { Source = crop, Width = place.Width, Height = place.Height, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        Canvas.SetLeft(image, place.X);
        Canvas.SetTop(image, place.Y);
        _layer.Children.Add(image);
        return image;
    }

    private static Color? Backdrop(BitmapSource source)
    {
        int w = source.PixelWidth, h = source.PixelHeight;
        if (w < 8 || h < 8) return null;
        var pixel = new byte[4];
        Color? best = null;
        foreach (var (x, y) in new[] { (w / 2, 1), (1, h / 2), (w - 2, h / 2), (w / 2, h - 2), (4, 4), (w - 5, h - 5) })
        {
            source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            if (pixel[3] > (best?.A ?? 0)) best = Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        }
        return best;
    }

    private void RevealPreview(double ms, double from)
    {
        int generation = _generation;
        var fade = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms), FillBehavior = FillBehavior.Stop };
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        fade.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * from))));
        fade.KeyFrames.Add(new EasingDoubleKeyFrame(Shown, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))) { EasingFunction = Motion.Ease });
        fade.Completed += (_, _) => { if (generation == _generation) _layer.Children.Clear(); };
        _preview.Opacity = Shown;
        _preview.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    public void Morph(Frame before, double ms = 340)
    {
        Stop();
        if (!Motion.Flourish || Settled() is not { } now) return;
        var was = Boxes(before.Hits);
        var next = Boxes(now.Hits);
        if (was.Count == next.Count && was.All(p => next.TryGetValue(p.Key, out var r) && r == p.Value) && before.Bounds.Size == now.Bounds.Size) return;
        var duration = TimeSpan.FromMilliseconds(ms);

        if (Backdrop(now.Source) is { } color)
        {
            var backdrop = new Rectangle { Fill = new SolidColorBrush(color), Width = now.Bounds.Width, Height = now.Bounds.Height };
            Canvas.SetLeft(backdrop, now.At.X);
            Canvas.SetTop(backdrop, now.At.Y);
            _layer.Children.Add(backdrop);
            backdrop.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(before.Bounds.Width, now.Bounds.Width, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
            backdrop.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(before.Bounds.Height, now.Bounds.Height, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
        }

        foreach (var (key, rect) in next)
        {
            if (Piece(now, rect) is not { } image) continue;
            if (was.TryGetValue(key, out var old))
            {
                var from = before.Place(old);
                var to = now.Place(rect);
                var shift = new TranslateTransform();
                image.RenderTransform = shift;
                shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from.X - to.X, 0, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
                shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from.Y - to.Y, 0, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
            }
            else
            {
                var enter = new DoubleAnimationUsingKeyFrames { Duration = duration, FillBehavior = FillBehavior.Stop };
                enter.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                enter.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms * 0.3))));
                enter.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(duration)) { EasingFunction = Motion.Ease });
                image.BeginAnimation(UIElement.OpacityProperty, enter);
                var wipe = new RectangleGeometry(new Rect(0, 0, image.Width, image.Height));
                image.Clip = wipe;
                wipe.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(new Rect(0, 0, 0, image.Height), new Rect(0, 0, image.Width, image.Height), duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
            }
        }

        foreach (var (key, rect) in was)
        {
            if (next.ContainsKey(key) || Piece(before, rect) is not { } image) continue;
            image.Opacity = 0;
            image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(ms * 0.6)) { FillBehavior = FillBehavior.Stop });
            var lift = new TranslateTransform();
            image.RenderTransform = lift;
            lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -5, TimeSpan.FromMilliseconds(ms * 0.6)) { FillBehavior = FillBehavior.Stop });
        }

        RevealPreview(ms, 0.75);
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

    public void Hide()
    {
        if (!Motion.Flourish) return;
        Stop();
        _preview.Clip = new RectangleGeometry(Rect.Empty);
    }

    public void Boot(double ms = 520)
    {
        Stop();
        if (!Motion.Flourish || Settled() is not { } now) return;
        var bounds = now.Bounds;
        var duration = TimeSpan.FromMilliseconds(ms);
        if (Backdrop(now.Source) is { } color)
        {
            var backdrop = new Rectangle { Fill = new SolidColorBrush(color), Width = bounds.Width, Height = bounds.Height };
            Canvas.SetLeft(backdrop, bounds.X);
            Canvas.SetTop(backdrop, bounds.Y);
            backdrop.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(0, bounds.Height, duration) { FillBehavior = FillBehavior.Stop });
            _layer.Children.Add(backdrop);
        }

        var scan = new Rectangle { Width = bounds.Width, Height = 2, Fill = Ui.Res<Brush>("AccentText"), Opacity = 0 };
        Canvas.SetLeft(scan, bounds.X);
        Canvas.SetTop(scan, bounds.Y);
        var down = new TranslateTransform();
        scan.RenderTransform = down;
        down.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, bounds.Height, duration) { FillBehavior = FillBehavior.Stop });
        var show = new DoubleAnimationUsingKeyFrames { Duration = duration + TimeSpan.FromMilliseconds(160), FillBehavior = FillBehavior.Stop };
        show.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        show.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(duration)));
        show.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(duration + TimeSpan.FromMilliseconds(160))));
        scan.BeginAnimation(UIElement.OpacityProperty, show);
        _layer.Children.Add(scan);

        foreach (var rect in Rows(now))
        {
            if (Piece(now, rect) is not { } row) continue;
            var delay = TimeSpan.FromMilliseconds(ms * rect.Y / Math.Max(1, now.Source.PixelHeight));
            var slide = new TranslateTransform();
            row.RenderTransform = slide;
            var appear = new DoubleAnimationUsingKeyFrames { Duration = delay + TimeSpan.FromMilliseconds(260), FillBehavior = FillBehavior.Stop };
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            appear.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(delay)));
            appear.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(260))) { EasingFunction = Motion.Ease });
            row.BeginAnimation(UIElement.OpacityProperty, appear);
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
        }
        RevealPreview(ms + 420, 0.8);
    }

    private static IEnumerable<Rect> Rows(Frame frame) =>
        frame.Hits.GroupBy(h => (int)Math.Round(h.Rect.Y + h.Rect.Height / 2))
            .Select(g => g.Select(h => new Rect(h.Rect.X, h.Rect.Y, h.Rect.Width, h.Rect.Height)).Aggregate(Rect.Union));

    public void Glide(Point from)
    {
        if (!Motion.Flourish) return;
        var to = new Point(Canvas.GetLeft(_preview), Canvas.GetTop(_preview));
        if (double.IsNaN(from.X) || double.IsNaN(to.X) || (Math.Abs(from.X - to.X) < 1 && Math.Abs(from.Y - to.Y) < 1)) return;
        var duration = TimeSpan.FromMilliseconds(380);
        _move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from.X - to.X, 0, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
        _move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(from.Y - to.Y, 0, duration) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
    }

    public void Grow(double ratio, bool right, bool bottom)
    {
        if (!Motion.Flourish || double.IsNaN(ratio) || Math.Abs(ratio - 1) < 0.005) return;
        _preview.RenderTransformOrigin = new Point(right ? 1 : 0, bottom ? 1 : 0);
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
            _shade.Visibility = _ring.Visibility = Visibility.Collapsed;
            return;
        }
        var spot = frame.Place(pixels);
        spot.Inflate(2, 2);
        _shade.Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(frame.Bounds), new RectangleGeometry(spot));
        _ring.Width = spot.Width;
        _ring.Height = spot.Height;
        Canvas.SetLeft(_ring, spot.X);
        Canvas.SetTop(_ring, spot.Y);
        _shade.Visibility = _ring.Visibility = Visibility.Visible;
    }
}
