using System.Windows;

namespace PitBox.Theme;

internal sealed class CircuitPath
{
    public Point[] Points { get; }
    public Vector[] Tangents { get; }
    public Vector[] Normals { get; }
    public double[] Lengths { get; }
    public double Total => Lengths[^1];
    public int Count => Points.Length;

    public CircuitPath(Point[] points)
    {
        int m = points.Length;
        Points = points;
        Tangents = new Vector[m];
        Normals = new Vector[m];
        Lengths = new double[m];
        for (int i = 0; i < m; i++)
        {
            var t = points[Math.Min(i + 1, m - 1)] - points[Math.Max(i - 1, 0)];
            if (t.Length > 0) t.Normalize();
            Tangents[i] = t;
            Normals[i] = new Vector(t.Y, -t.X);
            if (i > 0) Lengths[i] = Lengths[i - 1] + (points[i] - points[i - 1]).Length;
        }
    }

    public static CircuitPath Through(IReadOnlyList<Point> waypoints, int smoothPasses, double step = 2) =>
        new(Resample(Relax(Resample(Catmull(waypoints, 16), step), smoothPasses), step).ToArray());

    public int IndexAt(double s)
    {
        int lo = 0, hi = Lengths.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (Lengths[mid] < s) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    public Point Offset(int i, double d) => Points[i] + Normals[i] * d;

    public Point At(double s, double d = 0) => Offset(IndexAt(Math.Clamp(s, 0, Total)), d);

    public List<Point> Along(Func<int, double, double> d, double s0 = 0, double s1 = double.PositiveInfinity)
    {
        var result = new List<Point>();
        for (int i = 0; i < Points.Length; i++)
            if (Lengths[i] >= s0 && Lengths[i] <= s1) result.Add(Offset(i, d(i, Lengths[i])));
        return result;
    }

    public List<Point> Along(double d, double s0 = 0, double s1 = double.PositiveInfinity) => Along((_, _) => d, s0, s1);

    private static List<Point> Catmull(IReadOnlyList<Point> w, int steps)
    {
        var pts = new List<Point>();
        int n = w.Count;
        for (int i = 0; i < n - 1; i++)
        {
            Point p0 = w[Math.Max(i - 1, 0)], p1 = w[i], p2 = w[i + 1], p3 = w[Math.Min(i + 2, n - 1)];
            for (int k = 0; k < steps; k++)
            {
                double t = (double)k / steps, t2 = t * t, t3 = t2 * t;
                double F(double a, double b, double c, double d) => 0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
                pts.Add(new Point(F(p0.X, p1.X, p2.X, p3.X), F(p0.Y, p1.Y, p2.Y, p3.Y)));
            }
        }
        pts.Add(w[n - 1]);
        return pts;
    }

    private static List<Point> Resample(List<Point> pts, double step)
    {
        var cum = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + (pts[i] - pts[i - 1]).Length;
        double total = cum[^1];
        int n = Math.Max(2, (int)Math.Round(total / step));
        var result = new List<Point>(n + 1);
        for (int k = 0, i = 1; k <= n; k++)
        {
            double s = total * k / n;
            while (i < pts.Count - 1 && cum[i] < s) i++;
            double u = cum[i] > cum[i - 1] ? (s - cum[i - 1]) / (cum[i] - cum[i - 1]) : 0;
            result.Add(pts[i - 1] + (pts[i] - pts[i - 1]) * u);
        }
        return result;
    }

    private static List<Point> Relax(List<Point> pts, int passes)
    {
        var a = pts.ToArray();
        var b = new Point[a.Length];
        for (int p = 0; p < passes; p++)
        {
            b[0] = a[0];
            b[^1] = a[^1];
            for (int i = 1; i < a.Length - 1; i++)
                b[i] = new Point(0.5 * a[i].X + 0.25 * (a[i - 1].X + a[i + 1].X), 0.5 * a[i].Y + 0.25 * (a[i - 1].Y + a[i + 1].Y));
            (a, b) = (b, a);
        }
        return a.ToList();
    }
}
