namespace PitBox.SessionStats;

internal readonly record struct Streak(int Current, int Best);

internal sealed record RaceStreaks(Streak Clean, Streak Finished, Streak TopFive, Streak Weeks, Streak IRating, int Races);

internal static class RaceStreakReader
{
    private static readonly DateTime FirstRaceWeek = new(1970, 1, 6, 0, 0, 0, DateTimeKind.Utc);

    private const long WeekTicks = TimeSpan.TicksPerDay * 7;

    internal static long RaceWeek(DateTime utc)
    {
        long ticks = (utc - FirstRaceWeek).Ticks;
        return ticks >= 0 ? ticks / WeekTicks : (ticks - WeekTicks + 1) / WeekTicks;
    }

    internal static Streak Run(IEnumerable<bool> hits)
    {
        int run = 0, best = 0;
        foreach (bool hit in hits)
        {
            run = hit ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return new Streak(run, best);
    }

    internal static Streak Weeks(IEnumerable<DateTime> startsUtc, DateTime nowUtc)
    {
        var weeks = startsUtc.Select(RaceWeek).Distinct().Order().ToList();
        if (weeks.Count == 0) return default;
        int run = 1, best = 1;
        for (int i = 1; i < weeks.Count; i++)
        {
            run = weeks[i] == weeks[i - 1] + 1 ? run + 1 : 1;
            best = Math.Max(best, run);
        }
        return new Streak(weeks[^1] >= RaceWeek(nowUtc) - 1 ? run : 0, best);
    }

    internal static RaceStreaks Streaks(IReadOnlyList<RaceRow> races, DateTime nowUtc) => new(
        Run(races.Select(r => r.Clean)),
        Run(races.Select(r => r.Finished)),
        Run(races.Select(r => r.TopFive)),
        Weeks(races.Select(r => r.StartUtc), nowUtc),
        Run(races.Where(r => r.IrDelta is not null).Select(r => r.IrDelta > 0)),
        races.Count);
}
