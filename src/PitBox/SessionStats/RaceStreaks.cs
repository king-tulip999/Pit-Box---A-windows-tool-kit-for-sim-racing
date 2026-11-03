namespace PitBox.SessionStats;

internal readonly record struct Streak(int Current, int Best);

internal sealed record RaceStreaks(Streak Clean, Streak Finished, Streak TopFive, int Races);

internal static class RaceStreakReader
{
    internal static RaceStreaks Streaks(IReadOnlyList<(bool Finished, int? Incidents, int? Position)> races)
    {
        static Streak Run(IEnumerable<bool> hits)
        {
            int run = 0, best = 0;
            foreach (bool hit in hits)
            {
                run = hit ? run + 1 : 0;
                best = Math.Max(best, run);
            }
            return new Streak(run, best);
        }
        return new RaceStreaks(
            Run(races.Select(r => r.Finished && r.Incidents == 0)),
            Run(races.Select(r => r.Finished)),
            Run(races.Select(r => r.Finished && r.Position is >= 1 and <= 5)),
            races.Count);
    }

    public static RaceStreaks Read(StatsStore store) =>
        Streaks(store.Db.Query("SELECT finished, incidents, finish_pos FROM sessions WHERE session_type = 'Race' AND start_pos > 0 ORDER BY start_utc, id")
            .Select(r => (Convert.ToInt64(r[0] ?? 0L) != 0, r[1] is null ? (int?)null : Convert.ToInt32(r[1]), r[2] is null ? (int?)null : Convert.ToInt32(r[2]))).ToList());
}
