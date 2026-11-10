using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using PitBox.Theme;

namespace PitBox.Views;

internal static class Dividers
{
    private static readonly Dictionary<(Color, int), (Brush Halo, Brush Core, Brush Fade)> _made = [];

    public static Color? AccentOf(FrameworkElement element) => (element.TryFindResource("Accent") as SolidColorBrush)?.Color;

    public static void Draw(DrawingContext dc, double left, double right, double y, bool fromStart = false, Color? accent = null)
    {
        if (right - left < 40 || Brushes(vertical: false, fromStart, wanted: accent) is not var (halo, core, fade)) return;
        dc.PushOpacityMask(fade);
        dc.DrawRectangle(halo, null, new Rect(left, y - 5, right - left, 11));
        dc.DrawRectangle(core, null, new Rect(left, y, right - left, 1));
        dc.Pop();
    }

    public const double BendInset = 12, BendRadius = 8;

    public static Point? Bend { get; private set; }

    private static readonly List<DividerLine> _titles = [];
    private static DividerLine? _rule;
    private static bool _refreshQueued;

    public static void Track(DividerLine line, bool on)
    {
        if (line.IsWindowRule) _rule = on ? line : ReferenceEquals(_rule, line) ? null : _rule;
        else
        {
            _titles.Remove(line);
            if (on) _titles.Add(line);
        }
        if (_refreshQueued) return;
        _refreshQueued = true;
        line.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            _refreshQueued = false;
            RefreshBend();
        });
    }

    public static void RefreshBend()
    {
        Point? at = null;
        double best = 0;
        if (_rule is { IsVisible: true } rule && PresentationSource.FromVisual(rule) is not null)
            for (int i = _titles.Count - 1; i >= 0; i--)
            {
                var line = _titles[i];
                if (!line.IsVisible || PresentationSource.FromVisual(line) is null) continue;
                double seen = Seen(line);
                if (seen <= best) continue;
                try
                {
                    at = line.TranslatePoint(new Point(0, line.CoreY), rule);
                    best = seen;
                }
                catch (InvalidOperationException) { }
            }
        if (Bend is { } old && at is { } now && Math.Abs(old.X - now.X) < 0.25 && Math.Abs(old.Y - now.Y) < 0.25) return;
        if (Bend is null && at is null) return;
        Bend = at;
        _rule?.InvalidateVisual();
    }

    private static double Seen(DependencyObject line)
    {
        double seen = 1;
        for (var node = line; node is not null && seen > 0; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement element) seen *= element.Opacity;
        return seen;
    }

    public static void DrawUpright(DrawingContext dc, double x, double top, double bottom, Color? accent = null)
    {
        if (bottom - top < 0.5 || Brushes(vertical: true, wanted: accent) is not var (halo, core, _)) return;
        x = Math.Floor(x);
        dc.DrawRectangle(halo, null, new Rect(x - 5, top, 11, bottom - top));
        dc.DrawRectangle(core, null, new Rect(x, top, 1, bottom - top));
    }

    public static void DrawBend(DrawingContext dc, double x, double top, double y, double end, Color? accent = null)
    {
        if (Pens(accent) is not var (haloPen, corePen)) return;
        x = Math.Floor(x) + 0.5;
        double line = Math.Floor(y) + 0.5, r = BendRadius;
        if (line - r - top < 0 || end <= x + r) return;
        var path = new StreamGeometry();
        using (var g = path.Open())
        {
            g.BeginFigure(new Point(x, line - r), false, false);
            g.ArcTo(new Point(x + r, line), new Size(r, r), 0, false, SweepDirection.Counterclockwise, true, false);
            g.LineTo(new Point(end, line), true, false);
        }
        path.Freeze();
        DrawUpright(dc, x - 0.5, top, line - r, accent);
        dc.DrawGeometry(null, haloPen, path);
        dc.DrawGeometry(null, corePen, path);
    }

    private static (Pen Halo, Pen Core)? _pens;
    private static Color _pensAccent;
    private static double _pensScale;

    private static (Pen, Pen)? Pens(Color? wanted)
    {
        if ((wanted ?? GlobalAccent()) is not { } accent) return null;
        if (_pens is { } cached && _pensAccent == accent && _pensScale == Hairline.Scale && ReferenceEquals(_spec, ThemeManager.Current)) return cached;
        Brushes(vertical: true, wanted: accent);
        var spec = ThemeManager.Current;
        double glow = spec.GlowStrength * (spec.Dark ? 0.22 : 0.16), line = spec.Dark ? 0.7 : 0.85;
        Color A(double alpha) => Color.FromArgb((byte)(255 * alpha), accent.R, accent.G, accent.B);
        var halo = new Pen(new SolidColorBrush(A(glow * 0.5)), 7);
        var core = new Pen(new SolidColorBrush(A(line)), Hairline.Width);
        halo.Freeze();
        core.Freeze();
        _pensAccent = accent;
        _pensScale = Hairline.Scale;
        _pens = (halo, core);
        return _pens;
    }

    private static ThemeSpec? _spec;

    private static Color? GlobalAccent() => Ui.Res<Brush>("Accent") is SolidColorBrush { Color: var accent } ? accent : null;

    private static (Brush, Brush, Brush)? Brushes(bool vertical, bool fromStart = false, Color? wanted = null)
    {
        if ((wanted ?? GlobalAccent()) is not { } accent) return null;
        if (!ReferenceEquals(_spec, ThemeManager.Current) || _made.Count > 12)
        {
            _spec = ThemeManager.Current;
            _made.Clear();
        }
        int kind = vertical ? 0 : fromStart ? 1 : 2;
        if (_made.TryGetValue((accent, kind), out var made)) return made;
        made = Make(accent, vertical, !vertical && fromStart);
        _made[(accent, kind)] = made;
        return made;
    }

    private static (Brush, Brush, Brush) Make(Color accent, bool vertical, bool fromStart)
    {
        var spec = ThemeManager.Current;
        double glow = spec.GlowStrength * (spec.Dark ? 0.22 : 0.16), line = spec.Dark ? 0.7 : 0.85;
        Color A(double alpha) => Color.FromArgb((byte)(255 * alpha), accent.R, accent.G, accent.B);
        Point across = vertical ? new Point(1, 0) : new Point(0, 1), along = vertical ? new Point(0, 1) : new Point(1, 0);
        var halo = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = across };
        halo.GradientStops.Add(new GradientStop(A(0), 0));
        halo.GradientStops.Add(new GradientStop(A(glow), 0.5));
        halo.GradientStops.Add(new GradientStop(A(0), 1));
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = along };
        if (fromStart)
        {
            fade.GradientStops.Add(new GradientStop(Colors.Black, 0));
            fade.GradientStops.Add(new GradientStop(Colors.Black, 0.25));
            fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        }
        else
        {
            fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            fade.GradientStops.Add(new GradientStop(Colors.Black, 0.18));
            fade.GradientStops.Add(new GradientStop(Colors.Black, 0.82));
            fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        }
        var core = new SolidColorBrush(A(line));
        halo.Freeze();
        fade.Freeze();
        core.Freeze();
        return (halo, core, fade);
    }
}

internal sealed class DividerLine : FrameworkElement
{
    public DividerLine()
    {
        IsHitTestVisible = false;
        Height = 9;
        Loaded += (_, _) => Track(true);
        Unloaded += (_, _) => Track(false);
    }

    public bool FromStart { get; set; }

    public bool IsPageTitle { get; set; }

    public bool IsWindowRule { get; set; }

    internal double CoreY => Math.Floor(ActualHeight / 2);

    private void Track(bool on)
    {
        if (!IsWindowRule && !IsPageTitle) return;
        if (IsWindowRule)
        {
            LayoutUpdated -= OnLayoutUpdated;
            if (on) LayoutUpdated += OnLayoutUpdated;
        }
        Dividers.Track(this, on);
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e) => Dividers.RefreshBend();

    protected override void OnRender(DrawingContext dc)
    {
        var accent = Dividers.AccentOf(this);
        Dividers.Draw(dc, 0, ActualWidth, CoreY, FromStart, accent);
        if (!IsWindowRule || Dividers.Bend is not { } bend) return;
        double x = bend.X - Dividers.BendInset, below = ActualHeight;
        if (bend.Y - Dividers.BendRadius <= below)
        {
            Dividers.DrawUpright(dc, x, CoreY, below, accent);
            return;
        }
        var reach = new GeometryGroup { Children = { new RectangleGeometry(new Rect(x - 6, CoreY - 6, 13, below - CoreY + 6)), new RectangleGeometry(new Rect(x - 6, below, 1e5, 1e5)) } };
        reach.Freeze();
        dc.PushClip(reach);
        Dividers.DrawBend(dc, x, CoreY, bend.Y, bend.X, accent);
        dc.Pop();
    }
}
