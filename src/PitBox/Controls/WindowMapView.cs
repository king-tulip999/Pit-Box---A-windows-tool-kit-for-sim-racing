using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using PitBox.Theme;

namespace PitBox.Controls;

public sealed record MapWindow(string Key, Rect Area, string Label, bool Current);

public sealed class WindowMapView : FrameworkElement, Views.IFrameStep
{
    private readonly Dictionary<string, Rect> _shownScreens = new(), _shownWindows = new();
    private Dictionary<string, Rect>? _fromScreens, _fromWindows;
    private List<(MapWindow Window, Rect Box)> _leaving = new();
    private double _morphStart = double.NaN, _pulseStart = double.NaN;
    private string? _pulse;
    private const double MorphMs = 520, PulseMs = 650;

    private IReadOnlyList<LayoutTile> _screens = Array.Empty<LayoutTile>();
    private IReadOnlyList<MapWindow> _windows = Array.Empty<MapWindow>();
    private readonly List<(MapWindow Window, Rect Box)> _hits = new();
    private readonly Dictionary<(string, double, bool, Brush), FormattedText> _labels = new();
    private string? _highlight, _hover;
    private Pen? _screenPen, _targetPen, _targetBoldPen, _currentPen;
    private Brush? _targetFill, _targetBoldFill;
    private Brush? _accent;
    private double _px;

    private static Brush ScreenFill => ThemeManager.Brush("TileRaised", Brushes.WhiteSmoke);
    private static Brush ScreenStroke => ThemeManager.Brush("TileEdge", Brushes.LightGray);
    private static Brush TextBrush => ThemeManager.Brush("Text", Brushes.Black);
    private static Brush MutedBrush => ThemeManager.Brush("TextMuted", Brushes.Gray);
    private const double LabelSize = 12, NumberSize = 13, Pad = 6;

    public event Action<string>? WindowClicked;

    public void SetScreens(IReadOnlyList<LayoutTile> screens)
    {
        if (screens.SequenceEqual(_screens)) return;
        BeginMorph();
        _screens = screens;
        InvalidateVisual();
    }

    public void SetWindows(IReadOnlyList<MapWindow> windows)
    {
        if (windows.SequenceEqual(_windows)) return;
        BeginMorph();
        var leaving = _windows.Where(w => !w.Current && windows.All(n => n.Key != w.Key || n.Current) && _shownWindows.ContainsKey(w.Key)).Select(w => (w, _shownWindows[w.Key])).ToList();
        if (Morphing) _leaving = leaving;
        _windows = windows;
        InvalidateVisual();
    }

    public void Pulse(string key)
    {
        if (!Views.Motion.Flourish) return;
        _pulse = key;
        _pulseStart = Views.FrameDriver.Now;
        Views.FrameDriver.Run(this);
    }

    private bool Morphing => !double.IsNaN(_morphStart);

    private void BeginMorph()
    {
        if (!Views.Motion.Flourish || _shownScreens.Count == 0 || !IsLoaded) return;
        if (!Morphing || Views.FrameDriver.Now - _morphStart > 1)
        {
            _fromScreens = new Dictionary<string, Rect>(_shownScreens);
            _fromWindows = new Dictionary<string, Rect>(_shownWindows);
        }
        _morphStart = Views.FrameDriver.Now;
        Views.FrameDriver.Run(this);
    }

    public bool Step(double now)
    {
        InvalidateVisual();
        bool morphing = Morphing && now - _morphStart < MorphMs;
        if (Morphing && !morphing)
        {
            _morphStart = double.NaN;
            _fromScreens = _fromWindows = null;
            _leaving.Clear();
        }
        bool pulsing = !double.IsNaN(_pulseStart) && now - _pulseStart < PulseMs;
        if (!pulsing) { _pulseStart = double.NaN; _pulse = null; }
        return morphing || pulsing;
    }

    private double MorphAt => Morphing ? Math.Clamp((Views.FrameDriver.Now - _morphStart) / MorphMs, 0, 1) : 1;

    private static Rect Lerp(Rect a, Rect b, double e) =>
        new(a.X + (b.X - a.X) * e, a.Y + (b.Y - a.Y) * e, Math.Max(1, a.Width + (b.Width - a.Width) * e), Math.Max(1, a.Height + (b.Height - a.Height) * e));

    public void Highlight(string? key)
    {
        if (_highlight == key) return;
        _highlight = key;
        InvalidateVisual();
    }

    private string? Active => _hover ?? _highlight;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var key = HitKey(e.GetPosition(this));
        Cursor = key is null ? null : Cursors.Hand;
        if (_hover == key) return;
        _hover = key;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover is null) return;
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (HitKey(e.GetPosition(this)) is { } key)
        {
            e.Handled = true;
            WindowClicked?.Invoke(key);
        }
    }

    private string? HitKey(Point at)
    {
        string? found = null;
        double area = double.MaxValue;
        foreach (var (window, box) in _hits)
        {
            if (!box.Contains(at) || box.Width * box.Height >= area) continue;
            area = box.Width * box.Height;
            found = window.Key;
        }
        return found;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        _hits.Clear();
        if (_screens.Count == 0 || w <= 16 || h <= 16) return;

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in _screens)
        {
            minX = Math.Min(minX, s.X);
            minY = Math.Min(minY, s.Y);
            maxX = Math.Max(maxX, s.X + s.Width);
            maxY = Math.Max(maxY, s.Y + s.Height);
        }
        foreach (var t in _windows)
        {
            if (t.Current) continue;
            minX = Math.Min(minX, t.Area.X);
            minY = Math.Min(minY, t.Area.Y);
            maxX = Math.Max(maxX, t.Area.Right);
            maxY = Math.Max(maxY, t.Area.Bottom);
        }
        if (maxX - minX <= 0 || maxY - minY <= 0) return;
        double scale = Math.Min((w - 2) / (maxX - minX), (h - 2) / (maxY - minY));
        double ox = (w - (maxX - minX) * scale) / 2, oy = (h - (maxY - minY) * scale) / 2;
        Rect Place(Rect r) => new(ox + (r.X - minX) * scale, oy + (r.Y - minY) * scale, Math.Max(2, r.Width * scale), Math.Max(2, r.Height * scale));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double px = Hairline.ScaleOf(this);
        Prepare(px);

        var bounds = new Rect(0, 0, w, h);
        var taken = new List<Rect>();
        double k = MorphAt, e = Views.FrameDriver.Spring(k);
        _shownScreens.Clear();
        foreach (var s in _screens)
        {
            var r = Place(new Rect(s.X, s.Y, s.Width, s.Height));
            if (_fromScreens is not null && _fromScreens.TryGetValue(s.Label, out var was)) r = Lerp(was, r, e);
            _shownScreens[s.Label] = r;
            r.Inflate(-2, -2);
            if (r.Width <= 4 || r.Height <= 4) continue;
            dc.DrawGeometry(ScreenFill, _screenPen, Tile.Shape(Hairline.SnapBox(r, px), ThemeManager.Round(6)));
            if (s.Label.Length > 0 && (Number(s.Label, _accent!, dpi) is { } big && big.Height + Pad * 2 < r.Height && big.Width + Pad * 2 < r.Width ? big : Label(s.Label, NumberSize, true, MutedBrush, dpi)) is { } number && number.Height + Pad * 2 < r.Height)
            {
                var spot = new Rect(r.X + Pad + 2, r.Y + Pad, number.Width, number.Height);
                taken.Add(Spaced(spot));
                dc.DrawText(number, spot.Location);
            }
        }

        string? active = Active;
        var targets = _windows.Where(x => !x.Current).OrderByDescending(x => x.Area.Width * x.Area.Height).ToList();
        _shownWindows.Clear();
        foreach (var (gone, box) in _leaving)
        {
            dc.PushOpacity(1 - k);
            dc.DrawRectangle(_targetFill, _targetPen, Hairline.SnapBox(box, px));
            dc.Pop();
        }
        foreach (var t in targets)
        {
            var r = Place(t.Area);
            bool fresh = _fromWindows is not null && !_fromWindows.ContainsKey(t.Key);
            if (_fromWindows is not null && _fromWindows.TryGetValue(t.Key, out var was)) r = Lerp(was, r, e);
            _shownWindows[t.Key] = r;
            bool bold = t.Key == active;
            if (fresh) dc.PushOpacity(k);
            dc.DrawRectangle(bold ? _targetBoldFill : _targetFill, bold ? _targetBoldPen : _targetPen, Hairline.SnapBox(r, px, bold ? 2 : 1));
            if (t.Key == _pulse && !double.IsNaN(_pulseStart))
            {
                dc.PushOpacity(0.55 * (1 - Math.Clamp((Views.FrameDriver.Now - _pulseStart) / PulseMs, 0, 1)));
                dc.DrawRectangle(_accent, null, Hairline.SnapBox(r, px));
                dc.Pop();
            }
            if (fresh) dc.Pop();
            _hits.Add((t, r));
        }
        if (active is not null)
        {
            foreach (var c in _windows.Where(x => x.Current && x.Key == active))
            {
                var r = Place(c.Area);
                r.Intersect(bounds);
                if (r.IsEmpty) continue;
                dc.DrawRectangle(null, _currentPen, Hairline.SnapBox(r, px));
                if (Label("Now", LabelSize, false, MutedBrush, dpi) is not { } now || now.Width + Pad * 2 >= r.Width || now.Height + Pad * 2 >= r.Height) continue;
                var spot = new Rect(r.Right - Pad - now.Width, r.Bottom - Pad - now.Height, now.Width, now.Height);
                if (taken.Any(o => o.IntersectsWith(spot))) continue;
                taken.Add(Spaced(spot));
                dc.DrawText(now, spot.Location);
            }
        }

        foreach (var (t, r) in _hits.OrderByDescending(x => x.Window.Key == active).ThenBy(x => x.Box.Width * x.Box.Height))
        {
            if (Label(t.Label, LabelSize, t.Key == active, _accent!, dpi) is not { } text) continue;
            if (text.Width + Pad * 2 > r.Width || text.Height + 4 > r.Height) continue;
            var spots = new[] { r.Y + (r.Height - text.Height) / 2, r.Y + Pad, r.Bottom - Pad - text.Height }
                .Where(y => y >= r.Y + 2 && y + text.Height <= r.Bottom - 2)
                .Select(y => (Rect?)new Rect(r.X + Pad, y, text.Width, text.Height));
            if (spots.FirstOrDefault(s => !taken.Any(o => o.IntersectsWith(s!.Value))) is not { } spot) continue;
            taken.Add(Spaced(spot));
            dc.DrawText(text, spot.Location);
        }
    }

    private static Rect Spaced(Rect r)
    {
        r.Inflate(4, 2);
        return r;
    }

    private void Prepare(double px)
    {
        var accent = ThemeManager.Brush("AccentText", Brushes.SlateBlue);
        if (_accent == accent && _px == px && _screenPen?.Brush == ScreenStroke && _currentPen?.Brush == MutedBrush) return;
        _accent = accent;
        _px = px;
        _labels.Clear();
        _screenPen = Hairline.Pen(ScreenStroke, px);
        _targetPen = Hairline.Pen(accent, px);
        _targetBoldPen = Hairline.Pen(accent, px, 2);
        _currentPen = new Pen(MutedBrush, Hairline.Pixels(1, px)) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        _currentPen.Freeze();
        _targetFill = Tint(accent, 0.12);
        _targetBoldFill = Tint(accent, 0.26);
    }

    private static Brush Tint(Brush brush, double opacity)
    {
        var tint = brush.Clone();
        tint.Opacity = opacity;
        tint.Freeze();
        return tint;
    }

    private readonly Dictionary<(string, Brush), FormattedText> _numbers = new();

    private const double BigNumberSize = 34;

    private FormattedText? Number(string text, Brush brush, double dpi)
    {
        if (_numbers.TryGetValue((text, brush), out var cached) && cached.PixelsPerDip == dpi) return cached;
        var formatted = new FormattedText(ThemeManager.Case(text), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, ThemeManager.RaceFace, BigNumberSize, brush, dpi);
        _numbers[(text, brush)] = formatted;
        return formatted;
    }

    private FormattedText? Label(string text, double size, bool bold, Brush brush, double dpi)
    {
        if (text.Length == 0) return null;
        var key = (text, size, bold, brush);
        if (_labels.TryGetValue(key, out var cached) && cached.PixelsPerDip == dpi) return cached;
        var face = bold ? ThemeManager.DisplayFace : ThemeManager.UiFace();
        var formatted = new FormattedText(ThemeManager.Case(text), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, dpi);
        _labels[key] = formatted;
        return formatted;
    }
}
