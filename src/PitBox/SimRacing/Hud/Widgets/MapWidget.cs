using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using PitBox.SimRacing.Telemetry;
using PitBox.SimRacing.TrackMaps;

namespace PitBox.SimRacing.Hud.Widgets;

internal sealed class MapWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Number("zoom", "Zoom", 1, 1, 8, 0.5, null, "1 shows the whole track; above that the map closes in on your car"),
        HudOption.Bool("rotate", "Turn with my car", false, "Your direction of travel points up"),
        HudOption.Bool("turns", "Turn numbers", true, "Numbers that would overlap are left out"),
        HudOption.Bool("positions", "Positions on the dots", true, "Class positions inside the dots once they are big enough"),
        HudOption.Bool("pit", "Cars on pit road", true, "Dimmed where iRacing places them along the track"),
    };

    public static bool Moves(HudOptions o) => o.Bool("rotate") || o.Number("zoom") > 1;

    private const double HeadingLookMetres = 25;
    private const double TurnSettleSeconds = 0.3;

    private Task<TrackMap?>? _loading;
    private (int? Id, string Name, string Display, bool Sample) _for;
    private TrackMap? _map;
    private TrackMap? _builtFor;
    private RectangleF _extent;
    private GraphicsPath? _surface, _centre, _pit, _pitLines;
    private float _pitWidth;
    private float _angle;
    private bool _angleSet;
    private long _angleAt;
    private MapView _view;
    private readonly Matrix _toScreen = new();
    private readonly Matrix _back = new();
    private readonly List<RectangleF> _labels = new();

    private TrackMap? Map(HudPainter p, TelemetryFrame f)
    {
        var s = f.Session;
        bool sample = f.Simulated;
        var key = (s.TrackId, s.TrackName, s.TrackDisplayName, sample);
        if (_loading is null || key != _for)
        {
            _for = key;
            _map = null;
            _angleSet = false;
            _loading = sample ? TrackMapLibrary.ByKey(TrackMapLibrary.SampleKey) : TrackMapLibrary.ForSession(s.TrackId, s.TrackName, s.TrackDisplayName, s.TrackConfig);
        }
        if (!_loading.IsCompleted && p.IsPreview) _loading.Wait(TimeSpan.FromSeconds(3));
        if (_loading.IsCompletedSuccessfully) _map = _loading.Result;
        return _map;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Track map")) return;
        var f = v.Frame;
        var area = p.Header("Track map", f.Session.TrackDisplayName);
        if (Map(p, f) is not { } map)
        {
            string text = _loading is { IsCompleted: false } ? "Loading the map" : "No map for this track";
            p.Text(text, p.Body, p.P.Muted, area, HAlign.Center, VAlign.Center);
            return;
        }
        float d = p.Density;
        float side = Math.Min(area.Width, area.Height);
        float dot = Math.Clamp(side / 60, 4, 8) * d;
        Build(map);
        _view = View(p, v, map, area, dot + 2 * d);
        var track = p.P.MapTrack ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.3);
        var edge = p.P.MapEdge ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.55);
        var pitColor = p.P.MapPit ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.18);
        var g = p.G;
        g.SetClip(area);
        _toScreen.Reset();
        _toScreen.Translate(_view.Screen.X, _view.Screen.Y);
        _toScreen.Rotate(_view.Angle * 180 / MathF.PI);
        _toScreen.Scale(_view.Scale, _view.Scale);
        _toScreen.Translate(-_view.Centre.X, -_view.Centre.Y);
        _back.Reset();
        _back.Multiply(_toScreen);
        _back.Invert();
        g.MultiplyTransform(_toScreen);
        float k = _view.Scale;
        if (_pit is not null) g.FillPath(p.Brush(pitColor), _pit);
        if (_pitLines is not null) g.DrawPath(p.Pen(pitColor, Math.Max(1, _pitWidth * k) / k), _pitLines);
        if (_surface is not null)
        {
            g.FillPath(p.Brush(track), _surface);
            g.DrawPath(p.Pen(edge, 1 / k), _surface);
        }
        else if (_centre is not null) g.DrawPath(p.Pen(track, Math.Max(2, map.Width * k) / k), _centre);
        g.MultiplyTransform(_back);
        if (map.StartLine is { } sf)
        {
            var a = _view.ToScreen(sf.A);
            var b = _view.ToScreen(sf.B);
            p.Line(a.X, a.Y, b.X, b.Y, p.P.Heading, Math.Max(2, 3 * d));
        }
        if (v.Options.Bool("turns")) Turns(p, map, area);
        Cars(p, v, map, dot);
        g.ResetClip();
    }

    private MapView View(HudPainter p, HudView v, TrackMap map, RectangleF area, float margin)
    {
        var f = v.Frame;
        bool turning = v.Options.Bool("rotate");
        float zoom = (float)v.Options.Number("zoom");
        var room = new SizeF(Math.Max(1, area.Width - margin * 2), Math.Max(1, area.Height - margin * 2));
        float k = MapView.FitScale(_extent, room, turning) * zoom;
        var middle = new PointF(_extent.X + _extent.Width / 2, _extent.Y + _extent.Height / 2);
        int me = f.PlayerCarIdx;
        double pct = me >= 0 && me < TelemetryFrame.MaxCars ? f.CarLapDistPct[me] : -1;
        var centre = middle;
        if (pct >= 0)
        {
            var car = map.At(pct);
            float follow = Math.Clamp(zoom - 1, 0, 1);
            centre = new PointF(middle.X + (car.X - middle.X) * follow, middle.Y + (car.Y - middle.Y) * follow);
        }
        float target = 0;
        if (turning && pct >= 0) target = MapView.UpAngle(Heading(map, pct, f.Session.TrackLengthKm));
        long now = Stopwatch.GetTimestamp();
        if (!_angleSet || p.IsPreview) _angle = MapView.Wrap(target);
        else _angle = MapView.Ease(_angle, target, Stopwatch.GetElapsedTime(_angleAt, now).TotalSeconds, TurnSettleSeconds);
        _angleSet = true;
        _angleAt = now;
        return new MapView(new PointF(area.X + area.Width / 2, area.Y + area.Height / 2), centre, k, _angle);
    }

    private static PointF Heading(TrackMap map, double pct, double? trackKm)
    {
        double look = trackKm is > 0 and var km ? HeadingLookMetres / (km * 1000) : 0.005;
        var a = map.At(pct - look);
        var b = map.At(pct + look);
        double len = TrackMap.Distance(a, b);
        return len <= 0 ? map.Heading(pct) : new PointF((float)((b.X - a.X) / len), (float)((b.Y - a.Y) / len));
    }

    private void Turns(HudPainter p, TrackMap map, RectangleF area)
    {
        _labels.Clear();
        var font = p.Tiny;
        float h = p.LineHeight(font);
        foreach (var t in map.Turns)
        {
            var at = _view.ToScreen(t.At);
            float w = p.Measure(t.Text, font) + 4;
            var box = new RectangleF(at.X - w / 2, at.Y - h / 2, w, h);
            if (!area.Contains(box)) continue;
            bool overlaps = false;
            foreach (var other in _labels)
                if (other.IntersectsWith(box)) { overlaps = true; break; }
            if (overlaps) continue;
            _labels.Add(box);
            p.Text(t.Text, font, p.P.Muted, box, HAlign.Center, VAlign.Center, false);
        }
    }

    private void Cars(HudPainter p, HudView v, TrackMap map, float dot)
    {
        var f = v.Frame;
        var s = f.Session;
        bool multiclass = s.NumCarClasses > 1;
        bool numbers = v.Options.Bool("positions") && dot >= 7 * p.Density;
        bool pit = v.Options.Bool("pit");
        int me = f.PlayerCarIdx;
        var others = p.P.MapCars ?? HudPalette.Mix(p.P.Surface, p.P.Text, 0.8);
        for (int pass = 0; pass < 2; pass++)
            for (int car = 0; car < TelemetryFrame.MaxCars; car++)
            {
                bool mine = car == me;
                if (mine != (pass == 1)) continue;
                double pct = f.CarLapDistPct[car];
                if (pct < 0 || !s.ByCar.TryGetValue(car, out var driver) || !driver.Races || !v.ClassShown(car)) continue;
                bool onPit = f.CarOnPitRoad[car];
                if (onPit && !pit && !mine) continue;
                var at = _view.ToScreen(map.At(pct));
                float x = at.X, y = at.Y;
                var color = mine ? p.P.Player : multiclass ? p.ClassColor(s, driver.ClassId) : others;
                if (onPit && !mine) color = HudPalette.Alpha(color, 110);
                float r = mine ? dot + 2 * p.Density : dot;
                p.Dot(x, y, r + (mine ? 2 : 1), mine ? p.P.Heading : p.P.Bg);
                p.Dot(x, y, r, color);
                if (numbers && !mine && f.CarClassPosition[car] > 0)
                    p.Text(f.CarClassPosition[car].ToString(System.Globalization.CultureInfo.InvariantCulture), p.Font(Math.Max(8, r * 1.3f / p.Density), true),
                        HudPalette.On(color), new RectangleF(x - r, y - r, r * 2, r * 2), HAlign.Center, VAlign.Center, false);
            }
    }

    private void Build(TrackMap map)
    {
        if (ReferenceEquals(map, _builtFor)) return;
        _builtFor = map;
        _extent = Extent(map);
        foreach (var old in new[] { _surface, _centre, _pit, _pitLines }) old?.Dispose();
        _surface = map.Surface.Count > 0 ? Path(map.Surface.Select(s => (s, true))) : null;
        _centre = Path(new[] { (map.Centerline, true) });
        var filled = map.Pit.Where(s => s.Filled).SelectMany(s => s.Figures).Select(fg => (fg.Points, fg.Closed)).ToList();
        var lines = map.Pit.Where(s => !s.Filled).ToList();
        _pit = filled.Count > 0 ? Path(filled.Select(x => (x.Points, true))) : null;
        _pitLines = lines.Count > 0 ? Path(lines.SelectMany(s => s.Figures).Select(fg => (fg.Points, fg.Closed))) : null;
        _pitWidth = lines.Count > 0 ? lines.Max(s => s.StrokeWidth) : 0;
    }

    private static GraphicsPath Path(IEnumerable<(PointF[] Points, bool Closed)> figures)
    {
        var path = new GraphicsPath(FillMode.Alternate);
        foreach (var (pts, closed) in figures)
        {
            if (pts.Length < 2) continue;
            path.StartFigure();
            path.AddLines(pts);
            if (closed) path.CloseFigure();
        }
        return path;
    }

    internal static RectangleF Extent(TrackMap map)
    {
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        void Add(PointF pt, float r = 0)
        {
            x0 = Math.Min(x0, pt.X - r); y0 = Math.Min(y0, pt.Y - r);
            x1 = Math.Max(x1, pt.X + r); y1 = Math.Max(y1, pt.Y + r);
        }
        foreach (var fig in map.Surface) foreach (var pt in fig) Add(pt);
        foreach (var pt in map.Centerline) Add(pt, map.Width / 2);
        foreach (var s in map.Pit) foreach (var fig in s.Figures) foreach (var pt in fig.Points) Add(pt, s.Filled ? 0 : s.StrokeWidth / 2);
        if (map.StartLine is { } sf) { Add(sf.A); Add(sf.B); }
        foreach (var t in map.Turns) Add(t.At);
        return x0 > x1 ? RectangleF.Empty : RectangleF.FromLTRB(x0, y0, x1, y1);
    }
}
