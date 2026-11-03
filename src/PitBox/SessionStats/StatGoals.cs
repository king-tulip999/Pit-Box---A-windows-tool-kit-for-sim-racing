using PitBox.Config;

namespace PitBox.SessionStats;

internal readonly record struct Streak(int Current, int Best);

internal sealed record RaceStreaks(Streak Clean, Streak Finished, Streak TopFive, int Races);

internal sealed record GoalState(StatGoal Goal, double? Current, string? Category)
{
    public double Fraction => Current is not { } c ? 0
        : Done ? 1
        : Goal.From is { } from && from < Goal.Target ? Math.Clamp((c - from) / (Goal.Target - from), 0, 1)
        : Goal.Target > 0 ? Math.Clamp(c / Goal.Target, 0, 1) : 0;
    public bool Done => Current is { } c && c >= Goal.Target;
}

internal static class StatGoals
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

    public static RaceStreaks ReadStreaks(StatsStore store) =>
        Streaks(store.Db.Query("SELECT finished, incidents, finish_pos FROM sessions WHERE session_type = 'Race' AND start_pos > 0 ORDER BY start_utc, id")
            .Select(r => (Convert.ToInt64(r[0] ?? 0L) != 0, r[1] is null ? (int?)null : Convert.ToInt32(r[1]), r[2] is null ? (int?)null : Convert.ToInt32(r[2]))).ToList());

    public static GoalState Read(StatsStore store, StatGoal goal, DateTime nowUtc)
    {
        var db = store.Db;
        if (StatGoal.IsRating(goal.Kind))
        {
            var (rating, category) = LatestRating(store, goal.Kind, goal.Category);
            return new GoalState(goal, rating, category);
        }
        var where = new List<string>();
        var args = new List<object?>();
        if (goal.Car is { } car) { args.Add(car); where.Add($"car = ?{args.Count}"); }
        if (goal.Track is { } track) { args.Add(track); where.Add($"track = ?{args.Count}"); }
        if (goal.ThisSeason)
        {
            var (from, to) = TimeRanges.Of(StatRange.Season, nowUtc, null);
            args.Add(StatsStore.Unix(from));
            where.Add($"start_utc >= ?{args.Count}");
            args.Add(StatsStore.Unix(to));
            where.Add($"start_utc < ?{args.Count}");
        }
        string race = "session_type = 'Race' AND start_pos > 0";
        string sql = goal.Kind switch
        {
            GoalMetric.DrivingHours => "SELECT SUM(drive_s) / 3600.0 FROM sessions",
            GoalMetric.Laps => "SELECT SUM(laps) FROM sessions",
            GoalMetric.Races => $"SELECT COUNT(*) FROM sessions WHERE {race}",
            GoalMetric.Wins => $"SELECT COUNT(*) FROM sessions WHERE {race} AND finished = 1 AND finish_pos = 1",
            GoalMetric.Podiums => $"SELECT COUNT(*) FROM sessions WHERE {race} AND finished = 1 AND finish_pos BETWEEN 1 AND 3",
            _ => $"SELECT COUNT(*) FROM sessions WHERE {race} AND finished = 1 AND incidents = 0",
        };
        if (where.Count > 0) sql += (sql.Contains(" WHERE ") ? " AND " : " WHERE ") + string.Join(" AND ", where);
        var value = db.Scalar(sql, args.ToArray());
        return new GoalState(goal, value is null ? 0 : Convert.ToDouble(value), null);
    }

    public static (double? Value, string? Category) LatestRating(StatsStore store, GoalMetric kind, string? category)
    {
        string column = kind == GoalMetric.IRating ? "ir" : "sr";
        string sql = $"SELECT {column}_start + COALESCE({column}_delta, 0), category FROM sessions WHERE ir_start > {RaceResult.PlaceholderIRating} AND {column}_start IS NOT NULL";
        var rows = category is null
            ? store.Db.Query(sql + " ORDER BY start_utc DESC LIMIT 1")
            : store.Db.Query(sql + " AND category = ?1 ORDER BY start_utc DESC LIMIT 1", category);
        return rows.Count == 0 || rows[0][0] is null ? (null, category) : (Convert.ToDouble(rows[0][0]), rows[0][1] as string ?? category);
    }

    public static List<string> RatedCategories(StatsStore store) =>
        store.Db.Query($"SELECT category, MAX(start_utc) latest FROM sessions WHERE ir_start > {RaceResult.PlaceholderIRating} AND category IS NOT NULL AND category <> '' GROUP BY category ORDER BY latest DESC")
            .Select(r => (string)r[0]!).ToList();

    public static string CategoryName(string category)
    {
        var words = System.Text.RegularExpressions.Regex.Replace(category.Trim(), "(?<=[a-z])(?=[A-Z])", " ");
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..].ToLowerInvariant();
    }

    public static string Label(StatGoal g) => $"{Name(g.Kind)} {Format(g.Kind, g.Target)}";

    public static string Name(GoalMetric kind) => kind switch
    {
        GoalMetric.IRating => "iRating",
        GoalMetric.SafetyRating => "Safety rating",
        GoalMetric.DrivingHours => "Hours driven",
        GoalMetric.Laps => "Laps",
        GoalMetric.Races => "Races",
        GoalMetric.Wins => "Wins",
        GoalMetric.Podiums => "Podiums",
        _ => "Clean races",
    };

    public static string Format(GoalMetric kind, double v) => kind switch
    {
        GoalMetric.SafetyRating => v.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture),
        GoalMetric.DrivingHours => v.ToString(v < 10 ? "0.0" : "0", System.Globalization.CultureInfo.CurrentCulture) + " h",
        _ => Math.Floor(v).ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
    };
}
