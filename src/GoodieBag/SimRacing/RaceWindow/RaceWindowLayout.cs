using GoodieBag.Config;

namespace GoodieBag.SimRacing.RaceWindow;

internal sealed record RwSize(double MinW, double MinH, double PrefW, double PrefH, double MaxW, double MaxH, double GrowX, double GrowY)
{
    public RwSize Scaled(double k) => new(
        Math.Round(MinW * k), Math.Round(MinH * k), Math.Round(PrefW * k), Math.Round(PrefH * k), Math.Round(MaxW * k), Math.Round(MaxH * k), GrowX, GrowY);
}

internal sealed record RwItem(string Id, RwSize Size);

internal sealed record RwSlot(IReadOnlyList<string> Tabs, double X, double Y, double W, double H)
{
    public string Id => Tabs[0];
}

internal sealed record RwLayout(IReadOnlyList<RwSlot> Slots, double Score, bool RowsFirst, IReadOnlyList<IReadOnlyList<string>> Groups, IReadOnlyList<IReadOnlyList<string>> AllGroups, bool Arranged)
{
    public static readonly RwLayout Empty = new(Array.Empty<RwSlot>(), 0, false, Array.Empty<IReadOnlyList<string>>(), Array.Empty<IReadOnlyList<string>>(), false);

    public RwSlot? SlotOf(string id) => Slots.FirstOrDefault(s => s.Tabs.Contains(id, StringComparer.OrdinalIgnoreCase));
}

internal enum RwZone { Left, Right, Top, Bottom, Swap }

internal static class RaceWindowLayout
{
    public const double Gap = 4, TabBar = 24;
    private const int MaxGroups = 5;
    private const double Eps = 1e-6;

    private sealed class Node
    {
        public required IReadOnlyList<string> Tabs;
        public double MinW, MinH, PrefW, PrefH, MaxW, MaxH, GrowX, GrowY;
        public string Id => Tabs[0];

        public static Node Of(RwItem item) => new()
        {
            Tabs = new[] { item.Id },
            MinW = item.Size.MinW, MinH = item.Size.MinH, PrefW = item.Size.PrefW, PrefH = item.Size.PrefH,
            MaxW = item.Size.MaxW, MaxH = item.Size.MaxH, GrowX = item.Size.GrowX, GrowY = item.Size.GrowY,
        };
    }

    private sealed record Spec(double Min, double Pref, double Max, double Grow, double? Fixed = null, bool Locked = false);

    private sealed record Sized(List<(Node Node, double X, double Y, double W, double H)> Rects, double Score, bool RowsFirst, List<List<Node>> Groups);

    private readonly record struct Attempt(Sized? Best, double Short);

    public static IReadOnlyList<RwItem> Scale(IEnumerable<RwItem> items, double textScale) =>
        items.Select(i => i with { Size = i.Size.Scaled(textScale) }).ToList();

    internal static int[]? Distribute(double total, IReadOnlyList<(double Min, double Pref, double Max, double Grow, double? Fixed)> specs) =>
        Distribute(total, specs.Select(s => new Spec(s.Min, s.Pref, s.Max, s.Grow, s.Fixed)).ToList());

    private static int[]? Distribute(double total, List<Spec> specs)
    {
        double minSum = specs.Sum(p => p.Min);
        if (minSum > total + Eps) return null;
        if (specs.Any(p => p.Fixed is not null))
        {
            double want = specs.Sum(p => p.Fixed is { } f ? Math.Max(p.Min, f) : p.Min);
            if (want <= total + Eps)
                specs = specs.Select(p => p.Fixed is not { } f ? p : new Spec(Math.Max(p.Min, f), Math.Max(p.Min, f), Math.Max(p.Min, f), 0, f, true)).ToList();
            if (specs.All(p => p.Locked)) specs = specs.Select(p => p with { Max = double.PositiveInfinity, Grow = 1 }).ToList();
        }
        var s = specs.Select(p => p.Min).ToArray();
        double rest = total - s.Sum();
        void Pour(Func<Spec, double> cap, Func<Spec, double> weight)
        {
            for (int it = 0; it < 24 && rest > 0.01; it++)
            {
                var act = new List<int>();
                for (int i = 0; i < s.Length; i++)
                    if (s[i] < cap(specs[i]) - 0.01 && weight(specs[i]) > 0) act.Add(i);
                if (act.Count == 0) return;
                double tw = act.Sum(i => weight(specs[i]));
                double used = 0;
                foreach (int i in act)
                {
                    double add = Math.Min(rest * weight(specs[i]) / tw, cap(specs[i]) - s[i]);
                    s[i] += add;
                    used += add;
                }
                rest -= used;
                if (used < 0.01) return;
            }
        }
        Pour(p => p.Pref, p => Math.Max(1, p.Pref - p.Min));
        Pour(p => p.Max, p => p.Grow);
        Pour(p => p.Max, p => Math.Max(1, p.Max - p.Pref));
        Pour(p => p.Locked ? p.Max : double.PositiveInfinity, p => 1 + p.Grow * 3);
        var outp = new int[s.Length];
        double acc = 0;
        int prev = 0;
        for (int i = 0; i < s.Length; i++)
        {
            acc += s[i];
            int r = (int)Math.Round(acc, MidpointRounding.AwayFromZero);
            outp[i] = r - prev;
            prev = r;
        }
        return outp;
    }

    private static Spec Across(Node d, bool rowsFirst) => rowsFirst ? new(d.MinH, d.PrefH, d.MaxH, d.GrowY) : new(d.MinW, d.PrefW, d.MaxW, d.GrowX);

    private static Spec Along(Node d, bool rowsFirst) => rowsFirst ? new(d.MinW, d.PrefW, d.MaxW, d.GrowX) : new(d.MinH, d.PrefH, d.MaxH, d.GrowY);

    private static double FitOf(Node d, double w, double h) => Math.Min(1, w / d.PrefW) * Math.Min(1, h / d.PrefH);

    private static Sized? SizeGroups(List<List<Node>> groups, double W, double H, bool rowsFirst, RaceWindowArrangement? fix)
    {
        double A = rowsFirst ? H : W, B = rowsFirst ? W : H;
        int k = groups.Count;
        double totalA = A - Gap * (k - 1);
        var gSpecs = new List<Spec>(k);
        foreach (var g in groups)
        {
            double min = 0, pref = 0, max = 0, grow = 0;
            double? share = null;
            foreach (var d in g)
            {
                var sp = Across(d, rowsFirst);
                min = Math.Max(min, sp.Min); pref = Math.Max(pref, sp.Pref); max = Math.Max(max, sp.Max); grow = Math.Max(grow, sp.Grow);
                if (fix is not null && ShareOf(fix.ShareA, d) is { } f) share = Math.Max(share ?? 0, f);
            }
            gSpecs.Add(new Spec(min, pref, max, grow, share is { } v ? v * totalA : null));
        }
        var sizesA = Distribute(totalA, gSpecs);
        if (sizesA is null) return null;
        var rects = new List<(Node, double, double, double, double)>();
        double offA = 0;
        for (int gi = 0; gi < k; gi++)
        {
            var g = groups[gi];
            double totalB = B - Gap * (g.Count - 1);
            var specs = g.Select(d => Along(d, rowsFirst) with { Fixed = fix is not null && ShareOf(fix.ShareB, d) is { } f ? f * totalB : null }).ToList();
            var sizesB = Distribute(totalB, specs);
            if (sizesB is null) return null;
            double offB = 0;
            for (int i = 0; i < g.Count; i++)
            {
                rects.Add(rowsFirst ? (g[i], offB, offA, sizesB[i], sizesA[gi]) : (g[i], offA, offB, sizesA[gi], sizesB[i]));
                offB += sizesB[i] + Gap;
            }
            offA += sizesA[gi] + Gap;
        }
        double score = 0;
        foreach (var (d, _, _, w, h) in rects)
            score += FitOf(d, w, h) - 0.25 * Math.Min(3, Math.Max(0, w / d.MaxW - 1) + Math.Max(0, h / d.MaxH - 1));
        return new Sized(rects, score, rowsFirst, groups);
    }

    private static double? ShareOf(Dictionary<string, double> shares, Node d)
    {
        double? best = null;
        foreach (var id in d.Tabs)
            if (shares.TryGetValue(id, out var v)) best = Math.Max(best ?? 0, v);
        return best;
    }

    private static Node Merge(Node a, Node b, double tabBar)
    {
        var tabs = a.Tabs.Concat(b.Tabs).ToList();
        double extraA = a.Tabs.Count > 1 ? tabBar : 0, extraB = b.Tabs.Count > 1 ? tabBar : 0;
        return new Node
        {
            Tabs = tabs,
            MinW = Math.Max(a.MinW, b.MinW), PrefW = Math.Max(a.PrefW, b.PrefW), MaxW = Math.Max(a.MaxW, b.MaxW),
            MinH = Math.Max(a.MinH - extraA, b.MinH - extraB) + tabBar,
            PrefH = Math.Max(a.PrefH - extraA, b.PrefH - extraB) + tabBar,
            MaxH = Math.Max(a.MaxH - extraA, b.MaxH - extraB) + tabBar,
            GrowX = Math.Max(a.GrowX, b.GrowX), GrowY = Math.Max(a.GrowY, b.GrowY),
        };
    }

    private static double Deficit(List<List<Node>> groups, double W, double H, bool rowsFirst)
    {
        double A = rowsFirst ? H : W, B = rowsFirst ? W : H;
        double MinA(Node d) => rowsFirst ? d.MinH : d.MinW;
        double MinB(Node d) => rowsFirst ? d.MinW : d.MinH;
        double over = Math.Max(0, groups.Sum(g => g.Max(MinA)) + Gap * (groups.Count - 1) - A);
        foreach (var g in groups) over += Math.Max(0, g.Sum(MinB) + Gap * (g.Count - 1) - B);
        return over;
    }

    private static IEnumerable<int[]> Cuts(int n, int k)
    {
        var acc = new int[k - 1];
        IEnumerable<int[]> Rec(int depth, int start)
        {
            if (depth == k - 1) { yield return acc; yield break; }
            for (int i = start; i <= n - (k - 1 - depth); i++)
            {
                acc[depth] = i;
                foreach (var r in Rec(depth + 1, i + 1)) yield return r;
            }
        }
        return Rec(0, 1);
    }

    private static Attempt BestSplit(List<Node> items, double W, double H)
    {
        Sized? best = null;
        double shortest = double.PositiveInfinity;
        int n = items.Count;
        var maxA = new double[n, n + 1];
        var sumB = new double[n, n + 1];
        foreach (bool rowsFirst in new[] { false, true })
        {
            double A = rowsFirst ? H : W, B = rowsFirst ? W : H;
            for (int i = 0; i < n; i++)
            {
                double m = 0, sum = 0;
                for (int j = i + 1; j <= n; j++)
                {
                    var d = items[j - 1];
                    m = Math.Max(m, rowsFirst ? d.MinH : d.MinW);
                    sum += rowsFirst ? d.MinW : d.MinH;
                    maxA[i, j] = m;
                    sumB[i, j] = sum + Gap * (j - i - 1);
                }
            }
            for (int k = 1; k <= Math.Min(n, MaxGroups); k++)
                foreach (var cuts in Cuts(n, k))
                {
                    double acrossSum = Gap * (k - 1), over = 0;
                    int from = 0;
                    for (int g = 0; g < k; g++)
                    {
                        int to = g < k - 1 ? cuts[g] : n;
                        acrossSum += maxA[from, to];
                        over += Math.Max(0, sumB[from, to] - B);
                        from = to;
                    }
                    over += Math.Max(0, acrossSum - A);
                    if (over > Eps) { shortest = Math.Min(shortest, over); continue; }
                    var groups = new List<List<Node>>(k);
                    from = 0;
                    for (int g = 0; g < k; g++)
                    {
                        int to = g < k - 1 ? cuts[g] : n;
                        groups.Add(items.GetRange(from, to - from));
                        from = to;
                    }
                    var r = SizeGroups(groups, W, H, rowsFirst, null);
                    if (r is not null) { if (best is null || r.Score > best.Score + Eps) best = r; }
                    else shortest = Math.Min(shortest, Deficit(groups, W, H, rowsFirst));
                }
        }
        return new Attempt(best, shortest);
    }

    private static bool Better(Attempt a, Attempt? b) =>
        b is not { } o || (a.Best is not null ? o.Best is null || a.Best.Score > o.Best.Score + Eps : o.Best is null && a.Short < o.Short - Eps);

    private static RwLayout Finish(Sized best, bool arranged) => new(
        best.Rects.Select(r => new RwSlot(r.Node.Tabs, r.X, r.Y, r.W, r.H)).ToList(),
        best.Score,
        best.RowsFirst,
        best.Groups.Select(g => (IReadOnlyList<string>)g.Select(d => d.Id).ToList()).ToList(),
        best.Groups.Select(g => (IReadOnlyList<string>)g.SelectMany(d => d.Tabs).ToList()).ToList(),
        arranged);

    private static Sized WholeWindow(List<Node> items, double W, double H, bool rowsFirst, double tabBar)
    {
        var t = items.Count == 1 ? items[0] : items.Skip(1).Aggregate(items[0], (a, b) => Merge(a, b, tabBar));
        return new Sized(new() { (t, 0, 0, W, H) }, 0, rowsFirst, new() { new() { t } });
    }

    public static RwLayout Solve(IReadOnlyList<RwItem> items, double W, double H, double tabBar = TabBar)
    {
        if (items.Count == 0) return RwLayout.Empty;
        var cur = items.Select(Node.Of).ToList();
        for (;;)
        {
            var r = BestSplit(cur, W, H);
            if (r.Best is not null) return Finish(r.Best, false);
            if (cur.Count == 1) return Finish(WholeWindow(cur, W, H, false, tabBar), false);
            List<Node>? pick = null;
            Attempt? pickR = null;
            for (int i = 0; i < cur.Count - 1; i++)
            {
                var cand = cur.Take(i).Append(Merge(cur[i], cur[i + 1], tabBar)).Concat(cur.Skip(i + 2)).ToList();
                var cr = BestSplit(cand, W, H);
                if (Better(cr, pickR)) { pick = cand; pickR = cr; }
            }
            cur = pick!;
        }
    }

    public static RwLayout Arranged(RaceWindowArrangement arr, IReadOnlyList<RwItem> items, double W, double H, double tabBar = TabBar)
    {
        var byId = items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        var groups = arr.Groups.Select(g => g.Where(byId.ContainsKey).Select(id => Node.Of(byId[id])).ToList()).Where(g => g.Count > 0).ToList();
        if (groups.Count == 0) return RwLayout.Empty with { RowsFirst = arr.RowsFirst, Arranged = true };
        Attempt Try(List<List<Node>> gs)
        {
            var r = SizeGroups(gs, W, H, arr.RowsFirst, arr);
            return r is not null ? new Attempt(r, 0) : new Attempt(null, Deficit(gs, W, H, arr.RowsFirst));
        }
        for (;;)
        {
            var r = Try(groups);
            if (r.Best is not null) return Finish(r.Best, true);
            var cands = new List<List<List<Node>>>();
            for (int gi = 0; gi < groups.Count; gi++)
                for (int i = 0; i < groups[gi].Count - 1; i++)
                {
                    int g0 = gi, i0 = i;
                    cands.Add(groups.Select((h, j) => j != g0 ? h : h.Take(i0).Append(Merge(h[i0], h[i0 + 1], tabBar)).Concat(h.Skip(i0 + 2)).ToList()).ToList());
                }
            for (int gi = 0; gi < groups.Count - 1; gi++)
                cands.Add(groups.Take(gi).Append(groups[gi].Concat(groups[gi + 1]).ToList()).Concat(groups.Skip(gi + 2)).ToList());
            if (cands.Count == 0) return Finish(WholeWindow(groups.SelectMany(g => g).ToList(), W, H, arr.RowsFirst, tabBar), true);
            List<List<Node>>? pick = null;
            Attempt? pickR = null;
            foreach (var c in cands)
            {
                var cr = Try(c);
                if (Better(cr, pickR)) { pick = c; pickR = cr; }
            }
            groups = pick!;
        }
    }

    public static RwLayout Layout(RaceWindowArrangement? arr, IReadOnlyList<RwItem> items, double W, double H, double tabBar = TabBar)
    {
        if (arr is null) return Solve(items, W, H, tabBar);
        Sync(arr, items, W, H);
        return Arranged(arr, items, W, H, tabBar);
    }

    public static RaceWindowArrangement FromLayout(RwLayout layout) => new()
    {
        RowsFirst = layout.RowsFirst,
        Groups = layout.AllGroups.Select(g => g.ToList()).ToList(),
    };

    public static void Sync(RaceWindowArrangement arr, IReadOnlyList<RwItem> items, double W, double H)
    {
        var byId = items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        arr.Groups = arr.Groups.Select(g => g.Where(byId.ContainsKey).ToList()).Where(g => g.Count > 0).ToList();
        foreach (var item in items)
        {
            if (arr.Groups.Any(g => g.Contains(item.Id, StringComparer.OrdinalIgnoreCase))) continue;
            var options = new List<List<List<string>>>();
            for (int i = 0; i < arr.Groups.Count; i++)
            {
                int at = i;
                options.Add(arr.Groups.Select((h, j) => j == at ? h.Append(item.Id).ToList() : h.ToList()).ToList());
            }
            options.Add(arr.Groups.Select(h => h.ToList()).Append(new List<string> { item.Id }).ToList());
            List<List<string>>? best = null;
            double bestScore = double.NegativeInfinity;
            foreach (var o in options)
            {
                var r = SizeGroups(o.Select(g => g.Select(id => Node.Of(byId[id])).ToList()).ToList(), W, H, arr.RowsFirst, null);
                if (r is not null && r.Score > bestScore) { bestScore = r.Score; best = o; }
            }
            arr.Groups = best ?? (arr.Groups.Count > 0
                ? arr.Groups.Select((g, i) => i == arr.Groups.Count - 1 ? g.Append(item.Id).ToList() : g).ToList()
                : new List<List<string>> { new() { item.Id } });
        }
        var ids = new HashSet<string>(arr.Groups.SelectMany(g => g), StringComparer.OrdinalIgnoreCase);
        foreach (var k in arr.ShareA.Keys.Where(k => !ids.Contains(k)).ToList()) arr.ShareA.Remove(k);
        foreach (var k in arr.ShareB.Keys.Where(k => !ids.Contains(k)).ToList()) arr.ShareB.Remove(k);
    }

    public static void Move(RaceWindowArrangement arr, string id, string target, RwZone zone)
    {
        if (string.Equals(id, target, StringComparison.OrdinalIgnoreCase)) return;
        arr.ShareA.Remove(id);
        arr.ShareB.Remove(id);
        (int G, int I)? Where(string k)
        {
            for (int i = 0; i < arr.Groups.Count; i++)
            {
                int j = arr.Groups[i].FindIndex(x => string.Equals(x, k, StringComparison.OrdinalIgnoreCase));
                if (j >= 0) return (i, j);
            }
            return null;
        }
        var from = Where(id);
        if (Where(target) is not { } to0) return;
        if (zone == RwZone.Swap && from is { } f)
        {
            arr.Groups[f.G][f.I] = arr.Groups[to0.G][to0.I];
            arr.Groups[to0.G][to0.I] = id;
            return;
        }
        if (from is { } f2) arr.Groups[f2.G].RemoveAt(f2.I);
        arr.Groups = arr.Groups.Where(g => g.Count > 0).ToList();
        var (gi, ii) = Where(target)!.Value;
        bool within = arr.RowsFirst ? zone is RwZone.Left or RwZone.Right : zone is RwZone.Top or RwZone.Bottom;
        bool after = zone is RwZone.Right or RwZone.Bottom;
        if (zone == RwZone.Swap) arr.Groups[gi].Insert(ii, id);
        else if (within) arr.Groups[gi].Insert(ii + (after ? 1 : 0), id);
        else arr.Groups.Insert(gi + (after ? 1 : 0), new List<string> { id });
    }

    public static void Reorder(RaceWindowArrangement arr, IReadOnlyList<string> order, string id)
    {
        int i = order.ToList().FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        var placed = new HashSet<string>(arr.Groups.SelectMany(g => g).Where(k => !string.Equals(k, id, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        var next = order.Skip(i + 1).FirstOrDefault(placed.Contains);
        var prev = order.Take(i).Reverse().FirstOrDefault(placed.Contains);
        var (before, behind) = arr.RowsFirst ? (RwZone.Left, RwZone.Right) : (RwZone.Top, RwZone.Bottom);
        if (next is not null) Move(arr, id, next, before);
        else if (prev is not null) Move(arr, id, prev, behind);
    }

    public static RwZone ZoneOf(RwSlot s, double x, double y)
    {
        double u = (x - s.X) / s.W, v = (y - s.Y) / s.H;
        if (u > 0.3 && u < 0.7 && v > 0.3 && v < 0.7) return RwZone.Swap;
        var d = new (RwZone Zone, double Dist)[] { (RwZone.Left, u), (RwZone.Right, 1 - u), (RwZone.Top, v), (RwZone.Bottom, 1 - v) };
        return d.OrderBy(t => t.Dist).First().Zone;
    }

    public static (double X, double Y, double W, double H) ZoneRect(RwSlot s, RwZone zone)
    {
        double fw = Math.Round(s.W * 0.35), fh = Math.Round(s.H * 0.35);
        return zone switch
        {
            RwZone.Left => (s.X, s.Y, fw, s.H),
            RwZone.Right => (s.X + s.W - fw, s.Y, fw, s.H),
            RwZone.Top => (s.X, s.Y, s.W, fh),
            RwZone.Bottom => (s.X, s.Y + s.H - fh, s.W, fh),
            _ => (s.X, s.Y, s.W, s.H),
        };
    }

    internal sealed record Divider(bool AlongX, bool CrossAxis, int Group, int Index, double X, double Y, double W, double H, IReadOnlyList<string> Before, IReadOnlyList<string> After, double Total, bool Set);

    public static IReadOnlyList<Divider> Dividers(RwLayout layout, RaceWindowArrangement? arr, double W, double H, double thickness = 8)
    {
        var list = new List<Divider>();
        if (layout.Slots.Count == 0) return list;
        bool rf = layout.RowsFirst;
        var shareA = arr?.ShareA ?? new Dictionary<string, double>();
        var shareB = arr?.ShareB ?? new Dictionary<string, double>();
        RwSlot Rect(string id) => layout.Slots.First(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        var groups = layout.Groups;
        IEnumerable<string> TabsOf(IEnumerable<string> heads) => heads.SelectMany(h => Rect(h).Tabs);
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi];
            var r0 = Rect(g[0]);
            if (gi < groups.Count - 1)
            {
                var before = TabsOf(g).ToList();
                var after = TabsOf(groups[gi + 1]).ToList();
                bool set = before.Concat(after).Any(shareA.ContainsKey);
                double total = (rf ? H : W) - Gap * (groups.Count - 1);
                list.Add(rf
                    ? new Divider(false, true, gi, -1, 0, r0.Y + r0.H + Gap / 2 - thickness / 2, W, thickness, before, after, total, set)
                    : new Divider(true, true, gi, -1, r0.X + r0.W + Gap / 2 - thickness / 2, 0, thickness, H, before, after, total, set));
            }
            for (int i = 0; i < g.Count - 1; i++)
            {
                var r = Rect(g[i]);
                var before = Rect(g[i]).Tabs;
                var after = Rect(g[i + 1]).Tabs;
                bool set = before.Concat(after).Any(shareB.ContainsKey);
                double total = (rf ? W : H) - Gap * (g.Count - 1);
                list.Add(rf
                    ? new Divider(true, false, gi, i, r.X + r.W + Gap / 2 - thickness / 2, r.Y, thickness, r.H, before, after, total, set)
                    : new Divider(false, false, gi, i, r.X, r.Y + r.H + Gap / 2 - thickness / 2, r.W, thickness, before, after, total, set));
            }
        }
        return list;
    }

    public static void DragDivider(RaceWindowArrangement arr, Divider d, RwLayout start, IReadOnlyList<RwItem> items, double delta)
    {
        var byId = items.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);
        double SizeOf(RwSlot s) => d.AlongX ? s.W : s.H;
        double MinOf(string id) => byId.TryGetValue(id, out var it) ? d.AlongX ? it.Size.MinW : it.Size.MinH : 0;
        var a = start.SlotOf(d.Before[0])!;
        var b = start.SlotOf(d.After[0])!;
        double a0 = SizeOf(a), b0 = SizeOf(b);
        double minA = d.Before.Max(MinOf), minB = d.After.Max(MinOf);
        double na = Math.Max(minA, Math.Min(a0 + b0 - minB, a0 + delta));
        var store = d.CrossAxis ? arr.ShareA : arr.ShareB;
        foreach (var k in d.Before) store[k] = na / d.Total;
        foreach (var k in d.After) store[k] = (a0 + b0 - na) / d.Total;
    }

    public static void ResetDivider(RaceWindowArrangement arr, Divider d)
    {
        var store = d.CrossAxis ? arr.ShareA : arr.ShareB;
        foreach (var k in d.Before.Concat(d.After)) store.Remove(k);
    }
}
