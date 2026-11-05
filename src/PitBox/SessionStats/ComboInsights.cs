namespace PitBox.SessionStats;

internal sealed record CornerAverage(double Pct, double Lost);

internal sealed record OffTrackTrend(double Recent, double Earlier, int RecentLaps, int EarlierLaps);

internal static class ComboInsights
{
    public const int TrendLeastLaps = 10, PerLaps = 10;

    public static double? TheoreticalBest(IEnumerable<IReadOnlyList<double>?> laps)
    {
        var usable = laps.OfType<IReadOnlyList<double>>().Where(s => s.Count > 1 && s.All(v => v > 0 && double.IsFinite(v))).ToList();
        if (usable.Count == 0) return null;
        int count = usable.GroupBy(s => s.Count).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        var same = usable.Where(s => s.Count == count).ToList();
        return Enumerable.Range(0, count).Sum(k => same.Min(s => s[k]));
    }

    public static OffTrackTrend? Trend(IReadOnlyList<int> newestFirst)
    {
        int recent = newestFirst.Count / 2, earlier = newestFirst.Count - recent;
        if (recent < TrendLeastLaps) return null;
        double Rate(IEnumerable<int> laps, int n) => laps.Sum() * (double)PerLaps / n;
        return new OffTrackTrend(Rate(newestFirst.Take(recent), recent), Rate(newestFirst.Skip(recent), earlier), recent, earlier);
    }

    public static List<CornerAverage> AverageLoss(IReadOnlyList<LapTrace> laps, LapTrace reference)
    {
        var sums = new SortedDictionary<double, (double Lost, int Laps)>();
        foreach (var lap in laps)
            foreach (var c in CornerLoss.Corners(lap, reference))
            {
                var (lost, n) = sums.GetValueOrDefault(c.Apex);
                sums[c.Apex] = (lost + c.Lost, n + 1);
            }
        return sums.Select(kv => new CornerAverage(kv.Key / reference.LengthM, kv.Value.Lost / kv.Value.Laps)).ToList();
    }

    public static List<(string Where, double Lost)> ByTurn(IEnumerable<CornerAverage> corners, Func<double, string> name) =>
        corners.GroupBy(c => name(c.Pct)).Select(g => (g.Key, g.Sum(c => c.Lost))).Where(x => x.Item2 >= CornerLoss.LeastLoss)
            .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
}
