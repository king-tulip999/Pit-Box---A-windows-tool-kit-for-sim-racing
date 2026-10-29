using System.Windows;

namespace PitBox.Theme;

internal sealed record CircuitApex(double S0, double S1, int Mid, double K, int Side)
{
    public double S { get; init; }
}

internal sealed record CircuitTrap(CircuitApex Apex, double G0, double G1, double W0, double W1, double Depth)
{
    public int Side => Apex.Side;

    public double Taper(double s) => CircuitPlan.Smooth(G0, G0 + 50, s) * CircuitPlan.Smooth(G1, G1 - 70, s);

    public double Outer(double s) => Side * (CircuitPlan.HalfWidth + 5 + Depth * Taper(s));

    public double Wall(double s) => Side * (CircuitPlan.HalfWidth + 5 + (Depth + 9) * Taper(s));
}

internal enum CircuitBlockKind { Pit, Tower, Stand, Hatch, Plain }

internal sealed record CircuitBlock(Rect Box, CircuitBlockKind Kind, bool Vertical = false);

internal readonly record struct CircuitTree(Point At, double Radius);

internal sealed class CircuitPlan
{
    public const double Width = 1000, Height = 620, HalfWidth = 24, Reach = HalfWidth - 5;
    public const double TopSpeed = 0.17;
    private const double Lateral = 0.000075, Accel = 0.00006, Brake = 0.00014;
    private const double Contact = 3.5;

    public const double PitY = 470, PitIn0 = 20, PitIn1 = 160, PitOut0 = 520, PitOut1 = 660;
    public const int PitSide = -1;
    public const double PitCentre = HalfWidth + 24, PitHalf = 14, PitWall = 5, PitWork = 12;
    public const double PitOuterY = PitY - PitSide * (PitCentre + PitHalf);
    public const double StartAt = 300;

    public static readonly Point North = new(950, 70), Scale = new(760, 40);
    public static readonly Rect Park = new(800, 530, 176, 84);
    public const double GateY = 572, GateHalf = 6;
    public static readonly Point LakeCentre = new(290, 250);
    public const double LakeRx = 78, LakeRy = 42;

    private static readonly Point[] TrackPoints = P(
        -140, 470, 0, 470, 150, 470, 300, 470, 450, 470, 600, 470, 720, 470, 790, 468, 840, 458, 872, 436,
        882, 410, 872, 384, 845, 368, 806, 362, 764, 362, 724, 358, 688, 346, 656, 324, 642, 292, 652, 262, 680, 240,
        706, 216, 712, 186, 694, 160, 660, 146, 622, 136, 592, 112, 584, 80, 596, 46, 612, 12, 616, -60, 610, -140);

    private static readonly Point[] RoadPoints = P(-20, 604, 120, 604, 260, 604, 400, 604, 520, 600, 640, 598, 720, 590, 770, 578, 800, GateY);

    private static readonly (double X, double Y, double Spread, int Count)[] Clusters =
    {
        (400, 340, 70, 22), (180, 340, 80, 20), (460, 160, 60, 14), (260, 120, 70, 16), (740, 380, 26, 5), (780, 200, 60, 14),
        (920, 250, 50, 12), (880, 120, 60, 12), (700, 60, 50, 10), (930, 470, 30, 6), (470, 250, 30, 8),
    };

    private static readonly Lazy<CircuitPlan> Shared = new(() => new CircuitPlan());

    public static CircuitPlan HairpinAndEsses => Shared.Value;

    public CircuitPath Track { get; }
    public double[] Curvature { get; }
    public double[] RaceOffset { get; }
    public CircuitPath RaceLine { get; }
    public double[] Speeds { get; }
    public IReadOnlyList<CircuitApex> Apexes { get; }
    public IReadOnlyList<CircuitApex> Exits { get; }
    public IReadOnlyList<CircuitTrap> Traps { get; }
    public IReadOnlyList<CircuitBlock> Blocks { get; }
    public IReadOnlyList<CircuitBlock> Motorhomes { get; }
    public CircuitPath Road { get; }
    public IReadOnlyList<CircuitTree> Trees { get; }

    public CircuitPlan()
    {
        Track = CircuitPath.Through(TrackPoints, 40);
        Curvature = Curvatures(Track, 5);
        RaceOffset = RacingOffsets();
        RaceLine = new CircuitPath(Track.Along((i, _) => RaceOffset[i]).ToArray());
        Speeds = SpeedProfile(RaceLine);
        Apexes = Runs(i => Touching(i) && Math.Sign(RaceOffset[i]) == -Math.Sign(Curvature[i]) && Math.Abs(Curvature[i]) > 0.003)
            .Select(r => r with { Side = Math.Sign(r.K), S = Track.Lengths[r.Mid] }).ToList();
        Exits = Runs(i => Touching(i) && Math.Sign(RaceOffset[i]) != -Math.Sign(Curvature[i]))
            .Select(r => r with { Side = Math.Sign(RaceOffset[r.Mid]) }).ToList();
        Traps = Apexes.Where(a => Math.Abs(a.K) > 0.0065).Select(TrapFor).ToList();
        Blocks = new List<CircuitBlock>
        {
            new(new Rect(30, PitOuterY + 3, 330, 37), CircuitBlockKind.Pit),
            new(new Rect(372, PitOuterY + 6, 22, 22), CircuitBlockKind.Tower),
            new(new Rect(60, PitY - HalfWidth - 9 - 22, 300, 22), CircuitBlockKind.Stand),
            new(new Rect(816, 150, 18, 92), CircuitBlockKind.Stand, true),
            new(new Rect(560, 552, 130, 26), CircuitBlockKind.Hatch),
            new(new Rect(420, 556, 70, 16), CircuitBlockKind.Hatch),
        };
        var homes = new List<CircuitBlock>();
        for (int x = 40; x < 340; x += 36)
            if (x * 7 % 5 != 0) homes.Add(new CircuitBlock(new Rect(x, PitOuterY + 52, 26, 12), CircuitBlockKind.Plain));
        Motorhomes = homes;
        Road = CircuitPath.Through(RoadPoints, 30);
        Trees = PlantTrees();
    }

    public CircuitBlock PitBuilding => Blocks[0];

    public double PitOffset(int i, double s)
    {
        double centreY = PitY - PitSide * PitCentre;
        double blend = Smooth(PitIn0, PitIn1, s) * Smooth(PitOut1, PitOut0, s);
        return PitSide * (HalfWidth - PitHalf + (PitSide * (Track.Points[i].Y - centreY) - HalfWidth + PitHalf) * blend);
    }

    public static double Smooth(double e0, double e1, double v)
    {
        double t = Math.Clamp((v - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static double LakeRadius(double angle, double scale) =>
        scale * (1 + 0.07 * Math.Sin(3 * angle + 1.1) + 0.045 * Math.Sin(5 * angle + 0.4) + 0.03 * Math.Sin(8 * angle + 2));

    public static Point LakePoint(double angle, double scale)
    {
        double r = LakeRadius(angle, scale);
        return new Point(LakeCentre.X + Math.Cos(angle) * LakeRx * r, LakeCentre.Y + Math.Sin(angle) * LakeRy * r);
    }

    public double SpeedAt(double s) => Speeds[RaceLine.IndexAt(Math.Clamp(s, 0, RaceLine.Total))];

    public static Func<double> Random(uint seed) => () =>
    {
        unchecked
        {
            seed += 0x6D2B79F5;
            uint t = (seed ^ (seed >> 15)) * (1 | seed);
            t = (t + (t ^ (t >> 7)) * (61 | t)) ^ t;
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    };

    private bool Touching(int i) => Math.Abs(RaceOffset[i]) >= Reach - Contact;

    private static Point[] P(params double[] xy)
    {
        var points = new Point[xy.Length / 2];
        for (int i = 0; i < points.Length; i++) points[i] = new Point(xy[2 * i], xy[2 * i + 1]);
        return points;
    }

    private static double[] Curvatures(CircuitPath path, int reach)
    {
        int n = path.Count;
        var k = new double[n];
        for (int i = 0; i < n; i++)
        {
            int a = Math.Max(0, i - reach), b = Math.Min(n - 1, i + reach);
            var ta = path.Tangents[a];
            var tb = path.Tangents[b];
            double ds = path.Lengths[b] - path.Lengths[a];
            k[i] = (ta.X * tb.Y - ta.Y * tb.X) / (ds == 0 ? 1 : ds);
        }
        return k;
    }

    private static double[] Average(double[] values, int reach, bool clampToReach = false)
    {
        int n = values.Length;
        var result = new double[n];
        for (int i = 0; i < n; i++)
        {
            double sum = 0;
            int count = 0;
            for (int j = Math.Max(0, i - reach); j <= Math.Min(n - 1, i + reach); j++) { sum += values[j]; count++; }
            result[i] = clampToReach ? Math.Clamp(sum / count, -Reach, Reach) : sum / count;
        }
        return result;
    }

    private double TurnAround(double s, double reach)
    {
        var a = Track.Tangents[Track.IndexAt(Math.Max(0, s - reach))];
        var b = Track.Tangents[Track.IndexAt(Math.Min(Track.Total, s + reach))];
        return Math.Atan2(a.X * b.Y - a.Y * b.X, a.X * b.X + a.Y * b.Y);
    }

    private sealed record Key(double S, double D, bool Apex);

    private double[] RacingOffsets()
    {
        const double minK = 0.006, turnIn = 30, trackOut = 55, late = 0.62, wide = 0.92, gap = 40, link = 300;
        int n = Track.Count;
        var k = Average(Curvature, 10);
        var keys = new List<Key>();
        for (int i = 1; i < n - 1; i++)
        {
            double a = Math.Abs(k[i]);
            if (a < minK || a < Math.Abs(k[i - 1]) || a < Math.Abs(k[i + 1])) continue;
            int lo = i, hi = i;
            while (lo > 0 && Math.Abs(k[lo - 1]) > a * 0.45 && Math.Sign(k[lo - 1]) == Math.Sign(k[i])) lo--;
            while (hi < n - 1 && Math.Abs(k[hi + 1]) > a * 0.45 && Math.Sign(k[hi + 1]) == Math.Sign(k[i])) hi++;
            double s0 = Track.Lengths[lo], s1 = Track.Lengths[hi];
            int side = Math.Sign(k[i]);
            keys.Add(new Key(s0 - turnIn, side * Reach * wide, false));
            keys.Add(new Key(s0 + (s1 - s0) * late, -side * Reach, true));
            keys.Add(new Key(s1 + trackOut, side * Reach * wide, false));
            i = hi;
        }
        var merged = new List<Key>();
        foreach (var key in keys.OrderBy(key => key.S))
        {
            if (merged.Count > 0 && key.S - merged[^1].S < gap)
            {
                var last = merged[^1];
                if (key.Apex && !last.Apex) merged[^1] = key;
                else if (key.Apex == last.Apex) merged[^1] = new Key((key.S + last.S) / 2, (key.D + last.D) / 2, key.Apex);
            }
            else merged.Add(key);
        }
        for (int j = merged.Count - 2; j > 0; j--)
        {
            if (merged[j].Apex) continue;
            int prev = j - 1;
            while (prev >= 0 && !merged[prev].Apex) prev--;
            int next = j + 1;
            while (next < merged.Count && !merged[next].Apex) next++;
            if (prev >= 0 && next < merged.Count && merged[next].S - merged[prev].S < link) merged.RemoveAt(j);
        }
        if (merged.Count == 0) return new double[n];
        merged.Insert(0, new Key(0, merged[0].Apex ? -merged[0].D : merged[0].D, false));
        merged.Add(new Key(Track.Total, merged[^1].D, false));
        var offsets = new double[n];
        for (int i = 0, j = 0; i < n; i++)
        {
            double s = Track.Lengths[i];
            while (j < merged.Count - 2 && merged[j + 1].S < s) j++;
            var a = merged[j];
            var b = merged[j + 1];
            double t = b.S > a.S ? Math.Clamp((s - a.S) / (b.S - a.S), 0, 1) : 0;
            offsets[i] = a.D + (b.D - a.D) * t * t * (3 - 2 * t);
        }
        return Average(Average(offsets, 6, true), 6, true);
    }

    private static double[] SpeedProfile(CircuitPath path)
    {
        int n = path.Count;
        var bend = new double[n];
        for (int i = 0; i < n; i++)
        {
            int a = Math.Max(0, i - 4), b = Math.Min(n - 1, i + 4);
            var ta = path.Tangents[a];
            var tb = path.Tangents[b];
            double ds = path.Lengths[b] - path.Lengths[a];
            bend[i] = Math.Abs(ta.X * tb.Y - ta.Y * tb.X) / (ds == 0 ? 1 : ds);
        }
        bend = Average(bend, 6);
        var v = new double[n];
        for (int i = 0; i < n; i++) v[i] = Math.Min(TopSpeed, Math.Sqrt(Lateral / Math.Max(bend[i], 1e-6)));
        for (int i = 1; i < n; i++) v[i] = Math.Min(v[i], Math.Sqrt(v[i - 1] * v[i - 1] + 2 * Accel * (path.Lengths[i] - path.Lengths[i - 1])));
        for (int i = n - 2; i >= 0; i--) v[i] = Math.Min(v[i], Math.Sqrt(v[i + 1] * v[i + 1] + 2 * Brake * (path.Lengths[i + 1] - path.Lengths[i])));
        return v;
    }

    private List<CircuitApex> Runs(Func<int, bool> test)
    {
        var result = new List<CircuitApex>();
        int start = -1;
        for (int i = 0; i <= Track.Count; i++)
        {
            bool on = i < Track.Count && test(i);
            if (on && start < 0) start = i;
            if (on || start < 0) continue;
            int mid = (int)Math.Round((start + i - 1) / 2.0, MidpointRounding.AwayFromZero);
            int peak = start;
            for (int j = start; j < i; j++)
                if (Math.Abs(Curvature[j]) > Math.Abs(Curvature[peak])) peak = j;
            result.Add(new CircuitApex(Track.Lengths[start], Track.Lengths[i - 1], mid, Curvature[peak], 0));
            start = -1;
        }
        return result;
    }

    private CircuitTrap TrapFor(CircuitApex a)
    {
        bool hairpin = Math.Abs(TurnAround(a.S, 110)) > 2.2;
        double before = hairpin ? 110 : 50, after = hairpin ? 160 : 75, most = hairpin ? 40 : 32;
        double s0 = Math.Max(a.S0 - 40, a.S - before), s1 = Math.Min(a.S1 + 90, a.S + after);
        double depth = Math.Clamp(Math.Abs(a.K) * 4200, Math.Min(30, most), most);
        var trap = new CircuitTrap(a, s0, s1, s0, s1, depth);
        double w0 = s0, w1 = s1;
        while (w0 < s1 && trap.Taper(w0) < 0.4) w0 += 2;
        while (w1 > s0 && trap.Taper(w1) < 0.4) w1 -= 2;
        return trap with { W0 = w0, W1 = w1 };
    }

    private List<CircuitTree> PlantTrees()
    {
        var taken = new Occupancy();
        for (int i = 0; i < Track.Count; i++) taken.Disc(Track.Points[i], HalfWidth + 16);
        foreach (var g in Traps)
            for (int i = 0; i < Track.Count; i++)
            {
                double s = Track.Lengths[i];
                if (s < g.G0 || s > g.G1) continue;
                double outer = Math.Abs(g.Wall(s)) + 14;
                taken.Disc(Track.Offset(i, g.Side * (HalfWidth + outer) / 2), (outer - HalfWidth) / 2 + 1);
            }
        for (int i = 0; i < Track.Count; i++)
        {
            double s = Track.Lengths[i];
            if (s >= PitIn0 && s <= PitOut1) taken.Disc(Track.Offset(i, PitOffset(i, s)), PitHalf + 10);
        }
        foreach (var p in Road.Points) taken.Disc(p, 15);
        foreach (var b in Blocks.Concat(Motorhomes).Select(b => b.Box).Append(Park)) taken.Box(new Rect(b.X - 8, b.Y - 8, b.Width + 16, b.Height + 16));
        taken.Lake(1.22);
        taken.Disc(North, 26);
        taken.Box(new Rect(Scale.X - 6, Scale.Y - 12, 136, 30));

        var random = Random(7);
        var trees = new List<CircuitTree>();
        bool Free(double x, double y, double r)
        {
            if (taken.Has(x, y)) return false;
            for (int k = 0; k < 8; k++)
            {
                double a = k / 8.0 * Math.PI * 2;
                if (taken.Has(x + Math.Cos(a) * (r + 2), y + Math.Sin(a) * (r + 2))) return false;
            }
            return !trees.Any(t => Math.Sqrt((t.At.X - x) * (t.At.X - x) + (t.At.Y - y) * (t.At.Y - y)) < t.Radius + r + 2);
        }
        foreach (var (cx, cy, spread, count) in Clusters)
            for (int planted = 0, tries = 0; planted < count && tries < count * 60; tries++)
            {
                double a = random() * Math.PI * 2, d = Math.Sqrt(random()) * spread;
                double x = cx + Math.Cos(a) * d, y = cy + Math.Sin(a) * d, r = 3.5 + random() * 5;
                if (!Free(x, y, r)) continue;
                trees.Add(new CircuitTree(new Point(x, y), r));
                planted++;
            }
        return trees;
    }

    private sealed class Occupancy
    {
        private const int W = (int)Width, H = (int)Height;
        private readonly bool[] _cells = new bool[W * H];

        public bool Has(double x, double y)
        {
            int ix = (int)Math.Floor(x + 0.5), iy = (int)Math.Floor(y + 0.5);
            return ix >= 0 && iy >= 0 && ix < W && iy < H && _cells[iy * W + ix];
        }

        public void Disc(Point c, double r)
        {
            int y0 = Math.Max(0, (int)Math.Floor(c.Y - r)), y1 = Math.Min(H - 1, (int)Math.Ceiling(c.Y + r));
            int x0 = Math.Max(0, (int)Math.Floor(c.X - r)), x1 = Math.Min(W - 1, (int)Math.Ceiling(c.X + r));
            double r2 = r * r;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if ((x - c.X) * (x - c.X) + (y - c.Y) * (y - c.Y) <= r2) _cells[y * W + x] = true;
        }

        public void Box(Rect b)
        {
            int y0 = Math.Max(0, (int)Math.Floor(b.Top)), y1 = Math.Min(H - 1, (int)Math.Ceiling(b.Bottom));
            int x0 = Math.Max(0, (int)Math.Floor(b.Left)), x1 = Math.Min(W - 1, (int)Math.Ceiling(b.Right));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) _cells[y * W + x] = true;
        }

        public void Lake(double scale)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    double u = (x - LakeCentre.X) / LakeRx, v = (y - LakeCentre.Y) / LakeRy;
                    if (u * u + v * v > 2 * scale * scale) continue;
                    if (Math.Sqrt(u * u + v * v) <= LakeRadius(Math.Atan2(v, u), scale)) _cells[y * W + x] = true;
                }
        }
    }
}
