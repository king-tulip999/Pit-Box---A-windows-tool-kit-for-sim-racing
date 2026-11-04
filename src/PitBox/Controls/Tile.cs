using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Theme;

namespace PitBox.Controls;

public class Tile : Border
{
    public static Geometry Shape(Rect bounds, CornerRadius radius)
    {
        double r = Math.Min(radius.TopLeft, Math.Min(bounds.Width, bounds.Height) / 2);
        var g = new RectangleGeometry(bounds, r, r);
        g.Freeze();
        return g;
    }

    public static Geometry CardShape(Rect bounds, double radius) => Shape(bounds, ThemeManager.Round(radius));

    protected override Size MeasureOverride(Size constraint)
    {
        var size = base.MeasureOverride(constraint);
        if (LayoutPadding() is not { } whole) return size;
        var padding = Padding;
        return new Size(
            Math.Max(0, size.Width + whole.Left + whole.Right - padding.Left - padding.Right),
            Math.Max(0, size.Height + whole.Top + whole.Bottom - padding.Top - padding.Bottom));
    }

    public bool ClipChild { get; set; }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Child is not { } child || LayoutPadding() is not { } whole)
        {
            if (Child is { } only)
            {
                var b = BorderThickness;
                var p = Padding;
                ClipToInside(only, new Size(Math.Max(0, finalSize.Width - b.Left - b.Right - p.Left - p.Right), Math.Max(0, finalSize.Height - b.Top - b.Bottom - p.Top - p.Bottom)));
            }
            return base.ArrangeOverride(finalSize);
        }
        var dpi = VisualTreeHelper.GetDpi(this);
        var edge = Hairline.WholePixels(BorderThickness, dpi.DpiScaleX, dpi.DpiScaleY);
        double left = edge.Left + whole.Left, top = edge.Top + whole.Top;
        var inside = new Rect(left, top,
            Math.Max(0, finalSize.Width - left - edge.Right - whole.Right),
            Math.Max(0, finalSize.Height - top - edge.Bottom - whole.Bottom));
        ClipToInside(child, inside.Size);
        child.Arrange(inside);
        return finalSize;
    }

    private void ClipToInside(UIElement child, Size size)
    {
        if (!ClipChild) return;
        double r = Math.Max(0, CornerRadius.TopLeft - Math.Max(BorderThickness.Left + Padding.Left, BorderThickness.Top + Padding.Top));
        if (child.Clip is { } old && old.Bounds.Size == size && _clipRadius == r) return;
        _clipRadius = r;
        var clip = new RectangleGeometry(new Rect(size), r, r);
        clip.Freeze();
        child.Clip = clip;
    }

    private double _clipRadius = -1;

    private Thickness? LayoutPadding()
    {
        if (!UseLayoutRounding) return null;
        var padding = Padding;
        var dpi = VisualTreeHelper.GetDpi(this);
        var whole = Hairline.WholePixels(padding, dpi.DpiScaleX, dpi.DpiScaleY);
        return whole == padding ? null : whole;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (IsSquareWithEdge) DrawSquare(dc);
        else if (EvenEdge && BorderBrush is not null) DrawRounded(dc);
        else base.OnRender(dc);
    }

    public bool EvenEdge { get; set; }

    private (double W, double H, double R, Thickness Edge, Geometry Ring, Geometry Fill)? _rounded;

    private void DrawRounded(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var e = DeviceEdge(BorderThickness);
        double r = Math.Min(CornerRadius.TopLeft, Math.Min(w, h) / 2);
        if (_rounded is not { } cached || cached.W != w || cached.H != h || cached.R != r || cached.Edge != e)
        {
            var inner = new Rect(e.Left, e.Top, Math.Max(0, w - e.Left - e.Right), Math.Max(0, h - e.Top - e.Bottom));
            var fill = new RectangleGeometry(inner, Math.Max(0, r - e.Left), Math.Max(0, r - e.Top));
            var ring = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { new RectangleGeometry(new Rect(0, 0, w, h), r, r), fill } };
            fill.Freeze();
            ring.Freeze();
            cached = (w, h, r, e, ring, fill);
            _rounded = cached;
        }
        dc.PushGuidelineSet(new GuidelineSet(new[] { 0, e.Left, w - e.Right, w }, new[] { 0, e.Top, h - e.Bottom, h }));
        if (Background is { } background) dc.DrawGeometry(background, null, cached.Fill);
        dc.DrawGeometry(BorderBrush, null, cached.Ring);
        dc.Pop();
    }

    private bool IsSquareWithEdge
    {
        get
        {
            var r = CornerRadius;
            var e = BorderThickness;
            return r.TopLeft == 0 && r.TopRight == 0 && r.BottomLeft == 0 && r.BottomRight == 0 && BorderBrush is not null
                && (e.Left > 0 || e.Top > 0 || e.Right > 0 || e.Bottom > 0);
        }
    }

    private sealed class Outline
    {
        public required (double W, double H, Thickness Edge) Key { get; init; }
        public Geometry? Ring { get; init; }
        public required Geometry Fill { get; init; }
    }

    private Outline? _outline;

    private void DrawSquare(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var e = EdgeShows ? DeviceEdge(BorderThickness) : default;
        var key = (w, h, e);
        if (_outline is null || !_outline.Key.Equals(key))
            _outline = BuildOutline(key);
        dc.PushGuidelineSet(new GuidelineSet(new[] { 0, e.Left, w - e.Right, w }, new[] { 0, e.Top, h - e.Bottom, h }));
        if (_outline.Ring is { } ring && BorderBrush is { } border)
            dc.DrawGeometry(border, null, ring);
        if (Background is { } background)
            dc.DrawGeometry(background, null, _outline.Fill);
        dc.Pop();
    }

    private bool _awaitingSource;

    private Thickness DeviceEdge(Thickness edge)
    {
        if (PresentationSource.FromVisual(this) is not { RootVisual: { } root, CompositionTarget: { } target })
        {
            if (!_awaitingSource)
            {
                _awaitingSource = true;
                PresentationSource.AddSourceChangedHandler(this, OnSourceChanged);
            }
            return edge;
        }
        var toRoot = Matrix.Identity;
        DependencyObject? at = this;
        while (at is Visual v && !ReferenceEquals(v, root))
        {
            var t = v is FrameworkElement fe ? fe.LayoutTransform : VisualTreeHelper.GetTransform(v);
            if (t is not null && !t.Value.IsIdentity) toRoot.Append(t.Value);
            at = VisualTreeHelper.GetParent(v);
        }
        if (!ReferenceEquals(at, root)) return edge;
        var unit = new Vector(toRoot.M11 + toRoot.M21, toRoot.M12 + toRoot.M22);
        var device = Services.ScreenshotRunner.DrawsAt96 ? Matrix.Identity : target.TransformToDevice;
        double sx = Math.Abs(unit.X) * device.M11, sy = Math.Abs(unit.Y) * device.M22;
        if (sx < 0.01 || sy < 0.01) return edge;
        return Hairline.Edge(edge, sx, sy);
    }

    private bool EdgeShows => BorderBrush is { } brush && !(brush is SolidColorBrush { Color.A: 0 } || brush.Opacity <= 0);

    private void OnSourceChanged(object sender, SourceChangedEventArgs e)
    {
        if (e.NewSource is null) return;
        PresentationSource.RemoveSourceChangedHandler(this, OnSourceChanged);
        _awaitingSource = false;
        InvalidateVisual();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    private static Outline BuildOutline((double W, double H, Thickness Edge) key)
    {
        var e = key.Edge;
        var outer = new Rect(0, 0, key.W, key.H);
        var inner = new Rect(e.Left, e.Top, Math.Max(0, key.W - e.Left - e.Right), Math.Max(0, key.H - e.Top - e.Bottom));
        var fill = new RectangleGeometry(inner);
        fill.Freeze();
        Geometry? ring = null;
        if (e.Left > 0 || e.Top > 0 || e.Right > 0 || e.Bottom > 0)
        {
            ring = new GeometryGroup { FillRule = FillRule.EvenOdd, Children = { new RectangleGeometry(outer), new RectangleGeometry(inner) } };
            ring.Freeze();
        }
        return new Outline { Key = key, Ring = ring, Fill = fill };
    }
}
