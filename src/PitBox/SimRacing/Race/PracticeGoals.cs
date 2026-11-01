namespace PitBox.SimRacing.Race;

internal enum GoalKind { CleanStreak, Consistent, NearBest, UnderTime, CleanLaps, CleanMinutes }

internal readonly record struct GoalProgress(GoalKind Kind, int Current, int Best, int Target)
{
    public bool Done => Best >= Target;

    public bool InARow => Kind is GoalKind.CleanStreak or GoalKind.Consistent;

    public int ToGo => Math.Max(0, Target - Current);
}

internal static class PracticeGoals
{
    private static bool Counts(LapRecord l) => l.Clean && !l.PitLap;

    public static GoalProgress CleanStreak(IReadOnlyList<LapRecord> laps, int target)
    {
        int run = 0, best = 0;
        foreach (var l in laps)
        {
            run = Counts(l) ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return new GoalProgress(GoalKind.CleanStreak, run, best, Math.Max(1, target));
    }

    public static GoalProgress Consistent(IReadOnlyList<LapRecord> laps, int target, double within)
    {
        var (current, best) = ConsistentRun(laps, within);
        return new GoalProgress(GoalKind.Consistent, current, best, Math.Max(1, target));
    }

    private static (int Current, int Best) ConsistentRun(IReadOnlyList<LapRecord> laps, double within)
    {
        int best = 0, current = 0, start = 0;
        for (int i = 0; i < laps.Count; i++)
        {
            if (!Counts(laps[i])) { start = i + 1; current = 0; continue; }
            while (start < i && Spread(laps, start, i) > within + 1e-9) start++;
            current = i - start + 1;
            best = Math.Max(best, current);
        }
        return (current, best);
    }

    private static double Spread(IReadOnlyList<LapRecord> laps, int from, int to)
    {
        double min = double.MaxValue, max = double.MinValue;
        for (int i = from; i <= to; i++)
        {
            min = Math.Min(min, laps[i].Time);
            max = Math.Max(max, laps[i].Time);
        }
        return max - min;
    }

    private static double? Best(IReadOnlyList<LapRecord> laps)
    {
        double? best = null;
        foreach (var l in laps) if (Counts(l) && (best is null || l.Time < best)) best = l.Time;
        return best;
    }

    public static GoalProgress NearBest(IReadOnlyList<LapRecord> laps, int target, double within)
    {
        int count = Best(laps) is { } b ? laps.Count(l => Counts(l) && l.Time <= b + within + 1e-9) : 0;
        return new GoalProgress(GoalKind.NearBest, count, count, Math.Max(1, target));
    }

    public static GoalProgress UnderTime(IReadOnlyList<LapRecord> laps, int target, double seconds)
    {
        int count = laps.Count(l => Counts(l) && l.Time <= seconds + 1e-9);
        return new GoalProgress(GoalKind.UnderTime, count, count, Math.Max(1, target));
    }

    public static GoalProgress CleanLaps(IReadOnlyList<LapRecord> laps, int target)
    {
        int count = laps.Count(Counts);
        return new GoalProgress(GoalKind.CleanLaps, count, count, Math.Max(1, target));
    }

    public static GoalProgress CleanMinutes(IReadOnlyList<LapRecord> laps, int targetMinutes)
    {
        int minutes = (int)Math.Floor(laps.Where(Counts).Sum(l => l.Time) / 60 + 1e-9);
        return new GoalProgress(GoalKind.CleanMinutes, minutes, minutes, Math.Max(1, targetMinutes));
    }

    public static bool Counted(GoalKind kind, IReadOnlyList<LapRecord> laps, int index, GoalProgress progress, double within, double seconds)
    {
        if (index < 0 || index >= laps.Count || !Counts(laps[index])) return false;
        return kind switch
        {
            GoalKind.CleanStreak or GoalKind.Consistent => index >= laps.Count - progress.Current,
            GoalKind.NearBest => Best(laps) is { } b && laps[index].Time <= b + within + 1e-9,
            GoalKind.UnderTime => laps[index].Time <= seconds + 1e-9,
            _ => true,
        };
    }
}
