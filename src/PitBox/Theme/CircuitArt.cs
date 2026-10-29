using System.Windows;
using System.Windows.Media;

namespace PitBox.Theme;

internal sealed record CircuitCarLook(Pen[] Bodies, Pen[][] Trails)
{
    public const int TrailParts = 6;
    public const double TrailLength = 28, BodyLength = 8;
}

internal static class CircuitArt
{
    public const double Strength = 3;
    public const double HeaderClear = 56, HeaderFadeEnd = 96;

    private static readonly string[] CarKeys = { "Line", "Ink", "Apex", "Heading", "Gold" };

    public static Matrix Cover(Size size)
    {
        double s = Math.Max(size.Width / CircuitPlan.Width, size.Height / CircuitPlan.Height);
        var m = Matrix.Identity;
        m.Scale(s, s);
        m.Translate(size.Width - CircuitPlan.Width * s, size.Height - CircuitPlan.Height * s);
        return m;
    }

    public static Brush Fade(Size size)
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(size.Width * 0.7, size.Height),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(64, 0, 0, 0), 0.2));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));
        brush.Freeze();
        return brush;
    }

    public static Brush HeaderFade()
    {
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, HeaderClear),
            EndPoint = new Point(0, HeaderFadeEnd),
        };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 0), 1));
        brush.Freeze();
        return brush;
    }

    public static double CarFade(Size size, Point at)
    {
        double ex = size.Width * 0.7, ey = size.Height, length = ex * ex + ey * ey;
        if (length <= 0) return 0;
        double t = (at.X * ex + at.Y * ey) / length;
        double header = Math.Clamp((at.Y - HeaderClear) / (HeaderFadeEnd - HeaderClear), 0, 1);
        return Math.Clamp((t - 0.15) / 0.45, 0, 1) * header;
    }

    public static CircuitCarLook Cars(ThemeSpec spec, IReadOnlyDictionary<string, Color> p)
    {
        double body = Math.Min(1, 0.26 * Strength * spec.CircuitBoost), trail = Math.Min(0.9, 0.16 * Strength * spec.CircuitBoost);
        var bodies = new Pen[CarKeys.Length];
        var trails = new Pen[CarKeys.Length][];
        for (int i = 0; i < CarKeys.Length; i++)
        {
            var color = p[KeyFor(spec, CarKeys[i])];
            bodies[i] = Flat(Alpha(color, body), 4);
            trails[i] = new Pen[CircuitCarLook.TrailParts];
            for (int j = 0; j < CircuitCarLook.TrailParts; j++)
                trails[i][j] = Flat(Alpha(color, trail * (1 - (j + 0.5) / CircuitCarLook.TrailParts)), 2.5);
        }
        return new CircuitCarLook(bodies, trails);
    }

    public static DrawingGroup Drawing(ThemeSpec spec, IReadOnlyDictionary<string, Color> p)
    {
        var painter = new Painter(spec, p, CircuitPlan.HairpinAndEsses);
        painter.Paint();
        return painter.Done();
    }

    private static string KeyFor(ThemeSpec spec, string role) => role switch
    {
        "Line" => spec.CircuitLine,
        "Ink" => spec.CircuitInk,
        "Apex" => spec.CircuitApex,
        "Gold" => spec.CircuitGold,
        _ => role,
    };

    private static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    private static Pen Flat(Color color, double width)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        pen.Freeze();
        return pen;
    }

    private sealed class Painter
    {
        private const double HW = CircuitPlan.HalfWidth;

        private readonly DrawingGroup _group = new();
        private readonly CircuitPlan _plan;
        private readonly Color _bg, _ink, _line, _apex;
        private readonly double _boost, _glow;
        private readonly Brush _knockout;
        private readonly Dictionary<(Color, double), Brush> _brushes = new();

        public Painter(ThemeSpec spec, IReadOnlyDictionary<string, Color> p, CircuitPlan plan)
        {
            _plan = plan;
            _bg = p["Bg"];
            _ink = p[spec.CircuitInk];
            _line = p[spec.CircuitLine];
            _apex = p[spec.CircuitApex];
            _boost = spec.CircuitBoost;
            _glow = spec.CircuitGlow;
            _knockout = Freeze(new SolidColorBrush(_bg));
        }

        public DrawingGroup Done()
        {
            _group.ClipGeometry = Freeze(new RectangleGeometry(new Rect(0, 0, CircuitPlan.Width, CircuitPlan.Height)));
            return Freeze(_group);
        }

        public void Paint()
        {
            Grid();
            Lake();
            Road();
            Traps();
            Pit();
            Track();
            Buildings();
            Trees();
            Survey();
            Race();
        }

        private Brush Faint(Color c, double levels)
        {
            if (_brushes.TryGetValue((c, levels), out var cached)) return cached;
            double delta = Math.Max(Math.Abs(c.R - _bg.R), Math.Max(Math.Abs(c.G - _bg.G), Math.Max(Math.Abs(c.B - _bg.B), 1)));
            var brush = Freeze(new SolidColorBrush(Alpha(c, Math.Min(0.95, levels * Strength * _boost / delta))));
            _brushes[(c, levels)] = brush;
            return brush;
        }

        private Brush Ink(double levels) => Faint(_ink, levels);

        private static Pen Stroke(Brush brush, double width, params double[] dash)
        {
            var pen = new Pen(brush, width) { LineJoin = PenLineJoin.Round };
            if (dash.Length > 0) pen.DashStyle = Freeze(new DashStyle(dash.Select(d => d / width), 0));
            return Freeze(pen);
        }

        private static StreamGeometry Shape(IReadOnlyList<Point> points, bool closed)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                if (points.Count > 0)
                {
                    ctx.BeginFigure(points[0], closed, closed);
                    ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
                }
            }
            return Freeze(g);
        }

        private static StreamGeometry Segments(IEnumerable<(Point A, Point B)> segments)
        {
            var g = new StreamGeometry();
            using (var ctx = g.Open())
                foreach (var (a, b) in segments)
                {
                    ctx.BeginFigure(a, false, false);
                    ctx.LineTo(b, true, false);
                }
            return Freeze(g);
        }

        private static StreamGeometry Squares(IEnumerable<Point> centres, double size)
        {
            var g = new StreamGeometry();
            double h = size / 2;
            using (var ctx = g.Open())
                foreach (var c in centres)
                {
                    ctx.BeginFigure(new Point(c.X - h, c.Y - h), true, true);
                    ctx.PolyLineTo(new[] { new Point(c.X + h, c.Y - h), new Point(c.X + h, c.Y + h), new Point(c.X - h, c.Y + h) }, false, false);
                }
            return Freeze(g);
        }

        private static GeometryGroup Circles(IEnumerable<(Point At, double R)> circles)
        {
            var g = new GeometryGroup();
            foreach (var (at, r) in circles) g.Children.Add(new EllipseGeometry(at, r, r));
            return Freeze(g);
        }

        private void Draw(Geometry geometry, Brush? fill, Pen? pen) => _group.Children.Add(Freeze(new GeometryDrawing(fill, pen, geometry)));

        private void Line(IReadOnlyList<Point> points, Brush brush, double width, params double[] dash) =>
            Draw(Shape(points, false), null, Stroke(brush, width, dash));

        private void Segment(Point a, Point b, Brush brush, double width) => Draw(Segments(new[] { (a, b) }), null, Stroke(brush, width));

        private void Box(Rect r, Brush? fill, Brush edge, double width = 1) => Draw(Freeze(new RectangleGeometry(r)), fill, Stroke(edge, width));

        private void Grid()
        {
            var dots = new List<Point>();
            for (double x = 10; x < CircuitPlan.Width; x += 20)
                for (double y = 10; y < CircuitPlan.Height; y += 20) dots.Add(new Point(x, y));
            Draw(Squares(dots, 1.2), Ink(16), null);
            var crosses = new List<(Point, Point)>();
            for (double x = 100; x < CircuitPlan.Width; x += 100)
                for (double y = 100; y < CircuitPlan.Height; y += 100)
                {
                    crosses.Add((new Point(x - 5, y), new Point(x + 5, y)));
                    crosses.Add((new Point(x, y - 5), new Point(x, y + 5)));
                }
            Draw(Segments(crosses), null, Stroke(Ink(30), 1));
        }

        private static List<Point> LakeRing(double scale) =>
            Enumerable.Range(0, 97).Select(k => CircuitPlan.LakePoint(k / 96.0 * Math.PI * 2, scale)).ToList();

        private void Lake()
        {
            double[] scales = { 1, 0.8, 0.6, 0.4, 0.22 };
            for (int k = 0; k < scales.Length; k++)
                Draw(Shape(LakeRing(scales[k]), true), k == 0 ? _knockout : null, Stroke(Ink(k == 0 ? 36 : 22), k == 0 ? 1.2 : 0.8));
        }

        private void Road()
        {
            var road = _plan.Road;
            Line(road.Points, _knockout, 10);
            Line(road.Along(4), Ink(26), 1);
            Line(road.Along(-4), Ink(26), 1);
            Line(road.Points, Ink(16), 0.8, 6, 6);
        }

        private void Traps()
        {
            var track = _plan.Track;
            foreach (var g in _plan.Traps)
            {
                var inner = track.Along(g.Side * (HW + 5), g.G0, g.G1);
                var outer = track.Along((_, s) => g.Outer(s), g.G0, g.G1);
                outer.Reverse();
                Draw(Shape(inner.Concat(outer).ToList(), true), _knockout, Stroke(Ink(26), 1, 3, 3));
                var random = CircuitPlan.Random((uint)Math.Round(g.Apex.S, MidpointRounding.AwayFromZero));
                var stipple = new List<Point>();
                for (int k = 0; k < 260; k++)
                {
                    double s = g.W0 + random() * (g.W1 - g.W0);
                    int i = track.IndexAt(Math.Clamp(s, 0, track.Total));
                    double d = g.Side * (HW + 7 + (g.Depth * g.Taper(s) - 4) * random());
                    if (g.Taper(s) > 0.15) stipple.Add(track.Offset(i, d) + new Vector(0.5, 0.5));
                }
                Draw(Squares(stipple, 1), Ink(26), null);
                Draw(Circles(Spaced(track.Along((_, s) => g.Wall(s), g.W0, g.W1), 4.6).Select(p => (p, 2.0))), null, Stroke(Ink(30), 0.9));
            }
        }

        private static List<Point> Spaced(List<Point> points, double gap)
        {
            var result = new List<Point>();
            if (points.Count == 0) return result;
            result.Add(points[0]);
            double run = 0;
            for (int i = 1; i < points.Count; i++)
            {
                run += (points[i] - points[i - 1]).Length;
                if (run < gap) continue;
                result.Add(points[i]);
                run = 0;
            }
            return result;
        }

        private void Pit()
        {
            var track = _plan.Track;
            int sd = CircuitPlan.PitSide;
            double M(int i, double s) => sd * _plan.PitOffset(i, s);
            var outer = track.Along((i, s) => sd * (M(i, s) + CircuitPlan.PitHalf), CircuitPlan.PitIn0, CircuitPlan.PitOut1);
            var innerAll = track.Along((i, s) => sd * Math.Max(HW, M(i, s) - CircuitPlan.PitHalf), CircuitPlan.PitIn0, CircuitPlan.PitOut1);
            innerAll.Reverse();
            Draw(Shape(outer.Concat(innerAll).ToList(), true), _knockout, null);
            Line(outer, Ink(40), 1.5);
            List<Point> Inside(double lo, double extra)
            {
                var points = new List<Point>();
                for (int i = 0; i < track.Count; i++)
                {
                    double s = track.Lengths[i];
                    if (s < CircuitPlan.PitIn0 || s > CircuitPlan.PitOut1 || M(i, s) - CircuitPlan.PitHalf <= lo) continue;
                    points.Add(track.Offset(i, sd * (M(i, s) - CircuitPlan.PitHalf - extra)));
                }
                return points;
            }
            Line(Inside(HW + 0.5, 0), Ink(36), 1.2);
            Line(Inside(HW + CircuitPlan.PitWall + 3, CircuitPlan.PitWall), Ink(44), 2.4);
            Line(track.Along((i, s) => sd * (M(i, s) + CircuitPlan.PitHalf - CircuitPlan.PitWork), CircuitPlan.PitIn1, CircuitPlan.PitOut0), Ink(22), 0.8, 6, 5);
            var pit = _plan.PitBuilding.Box;
            var ticks = new List<(Point, Point)>();
            for (double x = pit.Left; x <= pit.Right; x += 22)
                ticks.Add((new Point(x, CircuitPlan.PitOuterY), new Point(x, CircuitPlan.PitOuterY + sd * CircuitPlan.PitWork)));
            Draw(Segments(ticks), null, Stroke(Ink(26), 0.8));
        }

        private void Kerb(int side, double s0, double s1, Brush brush)
        {
            var track = _plan.Track;
            var a = track.Along(side * (HW - 0.5), s0, s1);
            var b = track.Along(side * (HW - 5), s0, s1);
            Line(b, brush, 1);
            Draw(Segments(Enumerable.Range(0, a.Count).Where(i => i % 2 == 0).Select(i => (a[i], b[i]))), null, Stroke(brush, 0.8));
        }

        private void Track()
        {
            var track = _plan.Track;
            var left = track.Along(HW);
            var right = track.Along(-HW);
            right.Reverse();
            Draw(Shape(left.Concat(right).ToList(), true), _knockout, null);
            Line(track.Along(HW), Ink(48), 1.5);
            Line(track.Along(-HW), Ink(48), 1.5);
            foreach (var a in _plan.Apexes) Kerb(-a.Side, a.S0 - 28, a.S1 + 28, Ink(40));
            foreach (var e in _plan.Exits)
                if (e.S1 - e.S0 > 6 && e.S0 > 40 && _plan.Apexes.Any(a => e.S0 - a.S1 > -10 && e.S0 - a.S1 < 140))
                    Kerb(e.Side, e.S0 - 24, e.S1 + 24, Ink(32));
            Checker(Ink(52));
            GridBoxes(Ink(30));
        }

        private void Checker(Brush brush)
        {
            var track = _plan.Track;
            int i = track.IndexAt(CircuitPlan.StartAt);
            Point q = track.Points[i];
            Vector t = track.Tangents[i], n = track.Normals[i];
            const double size = 4;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
                for (int row = 0; row < 2; row++)
                    for (double k = -HW; k < HW; k += size)
                    {
                        if (((int)Math.Round((k + HW) / size, MidpointRounding.AwayFromZero) + row) % 2 != 0) continue;
                        var c = q + n * k + t * row * size;
                        ctx.BeginFigure(c, true, true);
                        ctx.PolyLineTo(new[] { c + n * size, c + n * size + t * size, c + t * size }, false, false);
                    }
            Draw(Freeze(g), brush, null);
        }

        private void GridBoxes(Brush brush)
        {
            var track = _plan.Track;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
                for (int k = 0; k < 8; k++)
                {
                    int i = track.IndexAt(CircuitPlan.StartAt - 30 - k * 22);
                    Vector t = track.Tangents[i], n = track.Normals[i];
                    var c = track.Offset(i, (k % 2 == 1 ? 1 : -1) * HW * 0.45);
                    ctx.BeginFigure(c + n * 6 - t * 8, false, false);
                    ctx.PolyLineTo(new[] { c + n * 6, c - n * 6, c - n * 6 - t * 8 }, true, true);
                }
            Draw(Freeze(g), null, Stroke(brush, 1));
        }

        private void Hatch(Rect r, Brush brush, double gap = 4)
        {
            var lines = new List<(Point, Point)>();
            for (double k = -r.Height; k < r.Width; k += gap)
                lines.Add((new Point(r.Left + k, r.Bottom), new Point(r.Left + k + r.Height, r.Top)));
            var hatch = new DrawingGroup { ClipGeometry = Freeze(new RectangleGeometry(r)) };
            hatch.Children.Add(Freeze(new GeometryDrawing(null, Stroke(brush, 0.7), Segments(lines))));
            _group.Children.Add(Freeze(hatch));
        }

        private void Buildings()
        {
            var edge = Ink(46);
            foreach (var m in _plan.Motorhomes) Box(m.Box, _knockout, Ink(30), 0.8);
            var park = CircuitPlan.Park;
            Box(park, _knockout, Ink(30));
            Segment(new Point(park.Left, CircuitPlan.GateY - CircuitPlan.GateHalf), new Point(park.Left, CircuitPlan.GateY + CircuitPlan.GateHalf), _knockout, 2.5);
            var stalls = new List<(Point, Point)>();
            for (double y = park.Top + 6; y + 18 <= park.Bottom - 4; y += 26)
                for (double x = park.Left + 6; x <= park.Right - 6; x += 10) stalls.Add((new Point(x, y), new Point(x, y + 18)));
            Draw(Segments(stalls), null, Stroke(Ink(20), 0.7));
            foreach (var b in _plan.Blocks)
            {
                var r = b.Box;
                Box(r, _knockout, edge);
                switch (b.Kind)
                {
                    case CircuitBlockKind.Pit:
                        bool up = CircuitPlan.PitSide > 0;
                        double bay = up ? r.Bottom - 15 : r.Top, back = up ? r.Top : r.Top + 15, rule = up ? bay : bay + 15;
                        Hatch(new Rect(r.Left, back, r.Width, r.Height - 15), Ink(18));
                        Segment(new Point(r.Left, rule), new Point(r.Right, rule), Ink(36), 1);
                        var bays = new List<(Point, Point)>();
                        for (double x = r.Left + 22; x < r.Right; x += 22) bays.Add((new Point(x, bay), new Point(x, bay + 15)));
                        Draw(Segments(bays), null, Stroke(Ink(36), 1));
                        break;
                    case CircuitBlockKind.Tower:
                        Draw(Segments(new[] { (r.TopLeft, r.BottomRight), (r.TopRight, r.BottomLeft) }), null, Stroke(Ink(32), 0.8));
                        Draw(Freeze(new EllipseGeometry(new Point(r.Left + r.Width / 2, r.Top + r.Height / 2), 5, 5)), null, Stroke(Ink(40), 1));
                        break;
                    case CircuitBlockKind.Stand:
                        var rows = new List<(Point, Point)>();
                        var sections = new List<(Point, Point)>();
                        if (b.Vertical)
                        {
                            for (double x = r.Left + 3; x < r.Right; x += 3) rows.Add((new Point(x, r.Top), new Point(x, r.Bottom)));
                            for (double y = r.Top + 24; y < r.Bottom; y += 24) sections.Add((new Point(r.Left, y), new Point(r.Right, y)));
                        }
                        else
                        {
                            for (double y = r.Top + 3; y < r.Bottom; y += 3) rows.Add((new Point(r.Left, y), new Point(r.Right, y)));
                            for (double x = r.Left + 33; x < r.Right; x += 33) sections.Add((new Point(x, r.Top), new Point(x, r.Bottom)));
                        }
                        Draw(Segments(rows), null, Stroke(Ink(24), 0.7));
                        Draw(Segments(sections), null, Stroke(Ink(38), 1));
                        break;
                    case CircuitBlockKind.Hatch:
                        Hatch(r, Ink(20));
                        break;
                }
            }
        }

        private void Trees()
        {
            Draw(Circles(_plan.Trees.Select(t => (t.At, t.Radius))), _knockout, Stroke(Ink(30), 0.9));
            Draw(Squares(_plan.Trees.Select(t => t.At), 1.4), Ink(34), null);
        }

        private void Survey()
        {
            var track = _plan.Track;
            var small = new List<(Point, Point)>();
            var big = new List<(Point, Point)>();
            for (int k = 1; k * 50.0 < track.Total; k++)
            {
                double s = k * 50.0;
                if (s < CircuitPlan.PitOut1 + 20 || _plan.Apexes.Any(a => s > a.S0 - 50 && s < a.S1 + 50)
                    || _plan.Traps.Any(g => g.Side == 1 && s > g.G0 - 10 && s < g.G1 + 10)) continue;
                bool major = k % 5 == 0;
                int i = track.IndexAt(s);
                (major ? big : small).Add((track.Offset(i, HW + 3), track.Offset(i, HW + (major ? 12 : 7))));
            }
            Draw(Segments(small), null, Stroke(Ink(30), 1));
            Draw(Segments(big), null, Stroke(Ink(44), 1));

            var boards = new List<(Point, Point)>();
            for (int k = 0; k < _plan.Traps.Count; k++)
            {
                var g = _plan.Traps[k];
                if (k > 0 && g.Apex.S0 - _plan.Traps[k - 1].Apex.S1 <= 220) continue;
                double start = Math.Min(g.G0, g.Apex.S0 - 60);
                foreach (double back in new double[] { 70, 45, 20 })
                {
                    int i = track.IndexAt(Math.Clamp(start - back, 0, track.Total));
                    boards.Add((track.Offset(i, g.Side * (HW + 6)), track.Offset(i, g.Side * (HW + 13))));
                }
            }
            Draw(Segments(boards), null, Stroke(Ink(40), 1.6));

            var scale = CircuitPlan.Scale;
            for (int k = 0; k < 5; k++)
                Box(new Rect(scale.X + k * 25, scale.Y, 25, 4), k % 2 == 0 ? Ink(36) : null, Ink(40), 0.8);
            Draw(Segments(new[] { (new Point(scale.X, scale.Y - 3), new Point(scale.X, scale.Y + 7)), (new Point(scale.X + 125, scale.Y - 3), new Point(scale.X + 125, scale.Y + 7)) }), null, Stroke(Ink(40), 0.8));

            var north = CircuitPlan.North;
            Draw(Freeze(new EllipseGeometry(north, 16, 16)), null, Stroke(Ink(34), 1));
            Draw(Shape(new[] { north + new Vector(0, -22), north + new Vector(5, 4), north, north + new Vector(-5, 4) }, true), Ink(40), null);
            Draw(Segments(new[]
            {
                (north + new Vector(0, 16), north + new Vector(0, 22)),
                (north + new Vector(-22, 0), north + new Vector(-16, 0)),
                (north + new Vector(16, 0), north + new Vector(22, 0)),
            }), null, Stroke(Ink(34), 1));
        }

        private void Race()
        {
            var line = _plan.RaceLine.Points;
            if (_glow > 0)
            {
                Line(line, Faint(_line, 14), 1.6 + _glow * 1.2, 7, 5);
                Line(line, Faint(_line, 26), 1.6 + _glow * 0.5, 7, 5);
            }
            Line(line, Faint(_line, 44), 1.6, 7, 5);
            var ring = Stroke(Faint(_apex, 60), 1.4);
            var ticks = new List<(Point, Point)>();
            foreach (var a in _plan.Apexes)
            {
                var q = line[a.Mid];
                Draw(Freeze(new EllipseGeometry(q, 6, 6)), null, ring);
                ticks.Add((q + new Vector(-10, 0), q + new Vector(-7, 0)));
                ticks.Add((q + new Vector(7, 0), q + new Vector(10, 0)));
                ticks.Add((q + new Vector(0, -10), q + new Vector(0, -7)));
                ticks.Add((q + new Vector(0, 7), q + new Vector(0, 10)));
            }
            Draw(Segments(ticks), null, Stroke(Faint(_apex, 50), 1));
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
