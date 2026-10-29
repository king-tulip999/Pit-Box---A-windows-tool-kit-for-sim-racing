using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PitBox.Services;
using PitBox.SimRacing;
using PitBox.Theme;
using PitBox.Views;

namespace PitBox.Controls;

internal sealed class RigDiagram : FrameworkElement, IFrameStep
{
    private const double MorphMs = 340, IntroMs = 900, LabelRoom = 40;

    private FovResult? _from;
    private double _morphStart, _morph = 1, _introStart, _intro = 1;
    private bool _introQueued;
    private FovResult? _ideal, _idealFrom;

    public RigDiagram()
    {
        Loaded += (_, _) =>
        {
            if (_introQueued || !Motion.Flourish) return;
            _introQueued = true;
            _intro = 0;
            InvalidateVisual();
            Motion.AfterPageSettles(Dispatcher, () =>
            {
                _introStart = FrameDriver.Now;
                FrameDriver.Run(this);
            });
        };
    }
    private FovResult? _result;
    private bool _imperial;
    private IReadOnlyList<(FovResult Result, Brush Color)> _compare = Array.Empty<(FovResult, Brush)>();
    private string _colorKey = "Accent";

    public void Show(FovResult result, bool imperial, IReadOnlyList<(FovResult Result, Brush Color)>? compare = null, string colorKey = "Accent", FovResult? ideal = null)
    {
        _idealFrom = ideal is null && _ideal is not null ? _ideal : null;
        _ideal = ideal;
        var shown = Shown();
        if (shown is not null && Motion.Flourish && IsLoaded && Morphable(shown, result))
        {
            _from = shown;
            _morphStart = FrameDriver.Now;
            _morph = 0;
            FrameDriver.Run(this);
        }
        else
        {
            _from = null;
            _morph = 1;
            _idealFrom = null;
            if (_intro >= 1) FrameDriver.Stop(this);
        }
        _result = result;
        _imperial = imperial;
        _compare = compare ?? Array.Empty<(FovResult, Brush)>();
        _colorKey = colorKey;
        InvalidateVisual();
    }

    public bool Step(double nowMs)
    {
        bool more = false;
        if (_morph < 1)
        {
            _morph = Math.Min(1, (nowMs - _morphStart) / MorphMs);
            if (_morph < 1) more = true;
            else
            {
                _from = null;
                _idealFrom = null;
            }
        }
        if (_intro < 1 && _introStart > 0)
        {
            _intro = Math.Min(1, (nowMs - _introStart) / IntroMs);
            more |= _intro < 1;
        }
        InvalidateVisual();
        return more;
    }

    private double Phase(double from, double length) => FrameDriver.EaseOut((_intro - from) / length);

    private static List<Point> Partial(List<Point> points, double t)
    {
        if (t >= 1 || points.Count < 2) return points;
        double total = 0;
        for (int i = 1; i < points.Count; i++) total += (points[i] - points[i - 1]).Length;
        double left = total * Math.Max(0, t);
        var cut = new List<Point> { points[0] };
        for (int i = 1; i < points.Count && left > 0; i++)
        {
            var step = points[i] - points[i - 1];
            if (step.Length <= left)
            {
                cut.Add(points[i]);
                left -= step.Length;
                continue;
            }
            cut.Add(points[i - 1] + step * (left / step.Length));
            break;
        }
        if (cut.Count == 1) cut.Add(points[0]);
        return cut;
    }

    private FovResult? Shown() => _result is { } to && _from is { } from && _morph < 1 ? Blend(from, to, FrameDriver.EaseOut(_morph)) : _result;

    private static bool Morphable(FovResult a, FovResult b) =>
        a.Screens.Count is 1 or 3 && b.Screens.Count is 1 or 3 && a.Screens.All(s => s.Count > 0) && b.Screens.All(s => s.Count > 0);

    private static IReadOnlyList<IReadOnlyList<V2>> AsTriple(FovResult r)
    {
        if (r.Screens.Count != 1) return r.Screens;
        var centre = r.Screens[0];
        return new IReadOnlyList<V2>[] { centre, new[] { centre[^1], centre[^1] }, new[] { centre[0], centre[0] } };
    }

    private static List<V2> Resample(IReadOnlyList<V2> points, int count)
    {
        if (points.Count == count) return points.ToList();
        var lengths = new double[points.Count];
        for (int i = 1; i < points.Count; i++)
            lengths[i] = lengths[i - 1] + Math.Sqrt(Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Y - points[i - 1].Y, 2));
        double total = lengths[^1];
        var result = new List<V2>(count);
        int at = 1;
        for (int n = 0; n < count; n++)
        {
            double target = total * n / Math.Max(1, count - 1);
            while (at < points.Count - 1 && lengths[at] < target) at++;
            if (points.Count == 1 || total <= 0) { result.Add(points[0]); continue; }
            double span = lengths[at] - lengths[at - 1];
            double u = span > 0 ? Math.Clamp((target - lengths[at - 1]) / span, 0, 1) : 0;
            var p = points[at - 1];
            var q = points[at];
            result.Add(new V2(p.X + (q.X - p.X) * u, p.Y + (q.Y - p.Y) * u));
        }
        return result;
    }

    private static FovResult Blend(FovResult a, FovResult b, double k)
    {
        double L(double x, double y) => x + (y - x) * k;
        V2 P(V2 x, V2 y) => new(L(x.X, y.X), L(x.Y, y.Y));
        var from = a.Screens.Count == b.Screens.Count ? a.Screens : AsTriple(a);
        var to = a.Screens.Count == b.Screens.Count ? b.Screens : AsTriple(b);
        return b with
        {
            Screens = from.Select((screen, i) =>
            {
                int count = Math.Max(screen.Count, to[i].Count);
                var x = Resample(screen, count);
                var y = Resample(to[i], count);
                return (IReadOnlyList<V2>)x.Select((p, j) => P(p, y[j])).ToList();
            }).ToList(),
            RightEdge = P(a.RightEdge, b.RightEdge),
            FootprintWidthCm = L(a.FootprintWidthCm, b.FootprintWidthCm),
            FootprintDepthCm = L(a.FootprintDepthCm, b.FootprintDepthCm),
            RigDepthCm = L(a.RigDepthCm, b.RigDepthCm),
            SideAngle = L(a.SideAngle, b.SideAngle),
            HorizontalFovTotal = L(a.HorizontalFovTotal, b.HorizontalFovTotal),
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (Shown() is not { } r || w < 60 || h < 60)
            return;

        var accent = ThemeManager.Brush(_colorKey, Brushes.SlateBlue);
        var accentColor = ThemeManager.ColorOf(_colorKey, Colors.SlateBlue);
        var text = ThemeManager.Brush("Text", Brushes.Black);
        var muted = ThemeManager.Brush("TextMuted", Brushes.Gray);
        var faint = ThemeManager.Brush("TextFaint", Brushes.Gray);
        var line = ThemeManager.Brush("LineStrong", Brushes.LightGray);
        var peach = ThemeManager.Brush("ApricotDeep", Brushes.DarkOrange);

        var idealScreens = _ideal is { Triple: true } ideal ? ideal.Screens : Array.Empty<IReadOnlyList<V2>>();
        var points = r.Screens.Concat(_compare.SelectMany(c => c.Result.Screens)).Concat(idealScreens).SelectMany(s => s).Append(new V2(0, 0)).ToList();
        double halfWidth = Math.Max(r.FootprintWidthCm / 2, points.Max(p => Math.Abs(p.X)));
        double back = r.Screens.SelectMany(x => x).Max(p => p.Y) + (r.FootprintDepthCm - r.RigDepthCm);
        double far = Math.Max(back, points.Max(p => p.Y)) + 3;
        double near = Math.Min(-12, points.Min(p => p.Y));
        double worldW = halfWidth * 2 + 30, worldH = far - near + 28;
        double scale = Math.Min((w - LabelRoom * 2) / worldW, h / worldH);
        double cx = w / 2, top = (h - (far - near) * scale) / 2 + 6;
        Point P(V2 v) => new(cx + v.X * scale, top + (far - v.Y) * scale);

        var eye = P(new V2(0, 0));

        var left = new V2(-r.RightEdge.X, r.RightEdge.Y);
        var wedge = new StreamGeometry();
        using (var g = wedge.Open())
        {
            g.BeginFigure(eye, true, true);
            g.LineTo(P(left), true, false);
            foreach (var screen in r.Screens.Count == 3 ? new[] { r.Screens[2], r.Screens[0], r.Screens[1] } : new[] { r.Screens[0] })
                foreach (var p in screen)
                    g.LineTo(P(p), true, false);
            g.LineTo(P(r.RightEdge), true, false);
        }
        wedge.Freeze();
        var wedgeFill = new SolidColorBrush(Color.FromArgb(40, accentColor.R, accentColor.G, accentColor.B));
        double opening = Phase(0, 0.6);
        dc.PushTransform(new ScaleTransform(opening, opening, eye.X, eye.Y));
        dc.PushOpacity(opening);
        dc.DrawGeometry(wedgeFill, null, wedge);
        dc.Pop();
        dc.Pop();
        var rayPen = new Pen(accent, Hairline.Pixels(1, Hairline.ScaleOf(this))) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        dc.PushOpacity(Phase(0.3, 0.3));
        dc.DrawLine(rayPen, eye, P(r.RightEdge));
        dc.DrawLine(rayPen, eye, P(left));
        dc.Pop();

        foreach (var (other, color) in _compare)
        {
            var dash = new Pen(color, 1.2) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            dc.DrawLine(dash, eye, P(other.RightEdge));
            dc.DrawLine(dash, eye, P(new V2(-other.RightEdge.X, other.RightEdge.Y)));
            var pen = new Pen(color, Math.Max(2.5, 4 * scale / 4)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            foreach (var screen in other.Screens)
                DrawPolyline(dc, pen, screen.Select(P).ToList());
        }

        double fw = r.FootprintWidthCm / 2;
        var footprint = new Rect(P(new V2(-fw, back)), P(new V2(fw, back - r.FootprintDepthCm)));
        dc.DrawGeometry(null, new Pen(line, Hairline.Pixels(1, Hairline.ScaleOf(this))) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) }, Tile.Shape(footprint, ThemeManager.Round(4)));

        var screenPen = new Pen(text, Math.Max(3, 5 * scale / 4)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        DrawIdeal(dc, _ideal, P, scale, 1);
        if (_idealFrom is not null) DrawIdeal(dc, _idealFrom, P, scale, 1 - FrameDriver.EaseOut(_morph));
        for (int i = 0; i < r.Screens.Count; i++)
            DrawPolyline(dc, screenPen, Partial(r.Screens[i].Select(P).ToList(), i == 0 ? Phase(0.15, 0.4) : Phase(0.4, 0.4)));
        dc.PushOpacity(Phase(0.7, 0.3));

        if (r.Triple && r.SideAngle > 0.5 && r.Screens[1].Count > 1)
        {
            var hinge = P(r.Screens[1][0]);
            double radius = 18;
            double a = r.SideAngle * Math.PI / 180;
            var start = new Point(hinge.X + radius, hinge.Y);
            var end = new Point(hinge.X + radius * Math.Cos(a), hinge.Y + radius * Math.Sin(a));
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(start, false, false);
                g.ArcTo(end, new Size(radius, radius), 0, r.SideAngle > 180, SweepDirection.Clockwise, true, false);
            }
            arc.Freeze();
            dc.DrawLine(new Pen(faint, Hairline.Pixels(1, Hairline.ScaleOf(this))) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) }, hinge, new Point(hinge.X + radius + 10, hinge.Y));
            dc.DrawGeometry(null, new Pen(peach, 1.6), arc);
            Label(dc, $"{r.SideAngle:0.0}°", new Point(hinge.X + radius + 6, hinge.Y + 6), peach, 9.5, bold: true);
        }

        dc.DrawEllipse(ThemeManager.Brush("Surface", Brushes.White), new Pen(text, 1.6), eye, 9, 9);
        dc.DrawEllipse(text, null, new Point(eye.X - 3.2, eye.Y - 1.5), 1.6, 1.6);
        dc.DrawEllipse(text, null, new Point(eye.X + 3.2, eye.Y - 1.5), 1.6, 1.6);

        Label(dc, $"{r.HorizontalFovTotal:0.0}° horizontal", new Point(eye.X, eye.Y + 14), accent, 9.5, bold: true, centre: true);
        string unit = _imperial ? Units.In : Units.Cm;
        double k = _imperial ? 1 / 2.54 : 1;
        Label(dc, $"Dotted box: ≈ {r.FootprintWidthCm * k:0} × {r.FootprintDepthCm * k:0} {unit} footprint", new Point(w - 2, 0), muted, 9, alignRight: true);
        dc.Pop();
    }

    private void DrawIdeal(DrawingContext dc, FovResult? ideal, Func<V2, Point> at, double scale, double opacity)
    {
        if (ideal is not { Triple: true } || ideal.Screens.Count < 3 || opacity <= 0.01) return;
        var ghost = ThemeManager.Brush("TextMuted", Brushes.Gray);
        var pen = new Pen(ghost, Math.Max(2, 3 * scale / 4)) { DashStyle = new DashStyle(new double[] { 1.6, 1.4 }, 0), StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        dc.PushOpacity(opacity * Phase(0.7, 0.3));
        DrawPolyline(dc, pen, ideal.Screens[1].Select(at).ToList());
        DrawPolyline(dc, pen, ideal.Screens[2].Select(at).ToList());
        var end = at(ideal.Screens[1][^1]);
        Label(dc, $"ideal {ideal.SideAngle:0.0}°", new Point(end.X + 6, end.Y - 6), ghost, 9.5, bold: true);
        dc.Pop();
    }

    private static void DrawPolyline(DrawingContext dc, Pen pen, IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], false, false);
            g.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void Label(DrawingContext dc, string text, Point at, Brush brush, double size, bool bold = false, bool centre = false, bool alignRight = false)
    {
        var ft = new FormattedText(ThemeManager.Case(text), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            ThemeManager.UiFace(bold),
            size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        double x = centre ? at.X - ft.Width / 2 : alignRight ? at.X - ft.Width : at.X;
        x = Math.Max(0, Math.Min(x, ActualWidth - ft.Width));
        dc.DrawText(ft, new Point(x, at.Y));
    }
}
