namespace PitBox.SessionStats;

internal sealed record SectorLine(int Index, double Best, int BestLap, double Average, double Spread, int Laps)
{
    public double Lost => Average - Best;
}

internal sealed record SectorTotal(double Ideal, double Average, double Spread, int BestLap, double BestTime)
{
    public double Lost => Average - Ideal;
    public double BestToIdeal => BestTime - Ideal;
}

internal static class SectorReport
{
    public static List<SectorLine> Make(IReadOnlyList<LapRow> laps)
    {
        var use = Used(laps);
        var lines = new List<SectorLine>();
        if (use.Count == 0) return lines;
        for (int k = 0; k < use[0].Sectors!.Count; k++)
        {
            var values = use.Select(l => (l.Lap, Time: l.Sectors![k])).ToList();
            var best = values.MinBy(v => v.Time);
            var (average, spread) = Spread(values.Select(v => v.Time).ToList());
            lines.Add(new SectorLine(k, best.Time, best.Lap, average, spread, values.Count));
        }
        return lines;
    }

    public static SectorTotal? Total(IReadOnlyList<LapRow> laps)
    {
        var use = Used(laps);
        if (use.Count == 0) return null;
        int count = use[0].Sectors!.Count;
        double ideal = Enumerable.Range(0, count).Sum(k => use.Min(l => l.Sectors![k]));
        var (average, spread) = Spread(use.Select(l => l.Sectors!.Sum()).ToList());
        var best = use.MinBy(l => l.Time)!;
        return new SectorTotal(ideal, average, spread, best.Lap, best.Time);
    }

    public static int? MostToFind(IReadOnlyList<SectorLine> lines) =>
        lines.Count > 1 && lines.Max(l => l.Lost) > 0.0005 ? lines.MaxBy(l => l.Lost)!.Index : null;

    private static List<LapRow> Used(IReadOnlyList<LapRow> laps)
    {
        var withSectors = laps.Where(l => l.Clean && l.Sectors is { Count: > 1 }).ToList();
        if (withSectors.Count == 0) return withSectors;
        int count = withSectors.GroupBy(l => l.Sectors!.Count).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
        return withSectors.Where(l => l.Sectors!.Count == count).ToList();
    }

    private static (double Average, double Spread) Spread(List<double> values)
    {
        double average = values.Average();
        return (average, Math.Sqrt(values.Sum(v => (v - average) * (v - average)) / values.Count));
    }
}
