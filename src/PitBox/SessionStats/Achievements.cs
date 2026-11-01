using System.Globalization;
using PitBox.Config;

namespace PitBox.SessionStats;

internal sealed record AchievementTotals(int Races, int Wins, int Podiums, int CleanRaces, int Poles, int BestGain, double Laps, double Hours,
    int Tracks, int Cars, double? PeakIRating, double? PeakSafety, RaceStreaks Streaks);

internal sealed record AchievementFamily(string Id, string Name, string Noun, double[] Tiers, Func<AchievementTotals, double?> Value, GoalMetric? Goal = null);

internal sealed record AchievementState(AchievementFamily Family, double? Value)
{
    public int Earned => Value is { } v ? Family.Tiers.Count(t => v >= t) : 0;
    public double? Next => Value is { } v ? Family.Tiers.Where(t => v < t).Select(t => (double?)t).FirstOrDefault() : Family.Tiers[0];
    public double Fraction => Value is { } v && Next is { } n ? Math.Clamp(v / n, 0, 1) : Next is null ? 1 : 0;
}

internal static class Achievements
{
    public const int MaxSuggestions = 3;

    public static readonly AchievementFamily[] All =
    {
        new("races", "Races", "race", new double[] { 1, 10, 25, 50, 100, 250, 500 }, t => t.Races, GoalMetric.Races),
        new("wins", "Wins", "win", new double[] { 1, 5, 10, 25, 50 }, t => t.Wins, GoalMetric.Wins),
        new("podiums", "Podiums", "podium", new double[] { 1, 10, 25, 50, 100 }, t => t.Podiums, GoalMetric.Podiums),
        new("clean", "Clean races", "clean race", new double[] { 1, 10, 25, 50, 100 }, t => t.CleanRaces, GoalMetric.CleanRaces),
        new("poles", "Pole starts", "pole start", new double[] { 1, 5, 10, 25 }, t => t.Poles),
        new("gain", "Places gained in one race", "place", new double[] { 5, 10, 15 }, t => t.BestGain),
        new("cleanrun", "Clean races in a row", "race", new double[] { 3, 5, 10, 20 }, t => t.Streaks.Clean.Best),
        new("finishrun", "Finishes in a row", "race", new double[] { 10, 25, 50, 100 }, t => t.Streaks.Finished.Best),
        new("top5run", "Top fives in a row", "race", new double[] { 3, 5, 10 }, t => t.Streaks.TopFive.Best),
        new("laps", "Laps", "lap", new double[] { 100, 1000, 5000, 10000, 25000 }, t => t.Laps, GoalMetric.Laps),
        new("hours", "Hours driven", "h", new double[] { 10, 50, 100, 250, 500 }, t => t.Hours, GoalMetric.DrivingHours),
        new("tracks", "Tracks driven", "track", new double[] { 5, 10, 25, 50 }, t => t.Tracks),
        new("cars", "Cars driven", "car", new double[] { 3, 10, 25 }, t => t.Cars),
        new("irating", "Best iRating", "", new double[] { 1500, 2000, 2500, 3000, 4000, 5000 }, t => t.PeakIRating, GoalMetric.IRating),
        new("sr", "Best safety rating", "", new double[] { 2, 3, 4, 4.99 }, t => t.PeakSafety, GoalMetric.SafetyRating),
    };

    public static List<AchievementState> Evaluate(AchievementTotals totals) => All.Select(f => new AchievementState(f, f.Value(totals))).ToList();

    public static int TierCount => All.Sum(f => f.Tiers.Length);

    public static string Format(AchievementFamily family, double v) => family.Id switch
    {
        "sr" => v.ToString("0.00", CultureInfo.CurrentCulture),
        "hours" => v.ToString(v < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " h",
        _ => Math.Floor(v).ToString("N0", CultureInfo.CurrentCulture),
    };

    public static string Target(AchievementFamily family, double v) => family.Noun switch
    {
        "" or "h" => Format(family, v),
        var noun => SimRacing.Race.RaceFormat.Count((int)v, noun),
    };

    public static List<StatGoal> Suggest(IEnumerable<AchievementState> states, IEnumerable<StatGoal> goals, int room)
    {
        var taken = goals.Where(g => g.Car is null && g.Track is null && !g.ThisSeason).Select(g => g.Kind).ToHashSet();
        var picks = new List<StatGoal>();
        foreach (var s in states.Where(s => s.Family.Goal is { } k && !taken.Contains(k) && s.Value is not null && s.Next is not null)
                     .OrderByDescending(s => s.Fraction).ThenBy(s => Array.IndexOf(All, s.Family)))
        {
            if (picks.Count >= Math.Min(room, MaxSuggestions)) break;
            var goal = new StatGoal { Kind = s.Family.Goal!.Value, Target = s.Next!.Value };
            goal.Clean();
            picks.Add(goal);
        }
        return picks;
    }

    public static AchievementTotals Read(StatsStore store, RaceStreaks streaks)
    {
        var db = store.Db;
        const string race = "session_type = 'Race' AND start_pos > 0";
        int Count(string sql) => Convert.ToInt32(db.Scalar(sql) ?? 0L);
        double Sum(string sql) => Convert.ToDouble(db.Scalar(sql) ?? 0.0);
        double? Peak(string sql) => db.Scalar(sql) is { } v ? Convert.ToDouble(v) : null;
        return new AchievementTotals(
            Count($"SELECT COUNT(*) FROM sessions WHERE {race}"),
            Count($"SELECT COUNT(*) FROM sessions WHERE {race} AND finished = 1 AND finish_pos = 1"),
            Count($"SELECT COUNT(*) FROM sessions WHERE {race} AND finished = 1 AND finish_pos BETWEEN 1 AND 3"),
            Count($"SELECT COUNT(*) FROM sessions WHERE {race} AND finished = 1 AND incidents = 0"),
            Count($"SELECT COUNT(*) FROM sessions WHERE {race} AND start_pos = 1"),
            Count($"SELECT COALESCE(MAX(start_pos - finish_pos), 0) FROM sessions WHERE {race} AND finished = 1 AND finish_pos > 0"),
            Sum("SELECT COALESCE(SUM(laps), 0) FROM sessions"),
            Sum("SELECT COALESCE(SUM(drive_s), 0) / 3600.0 FROM sessions"),
            Count("SELECT COUNT(DISTINCT track) FROM sessions WHERE laps > 0"),
            Count("SELECT COUNT(DISTINCT car) FROM sessions WHERE laps > 0"),
            Peak($"SELECT MAX(ir_start + COALESCE(ir_delta, 0)) FROM sessions WHERE ir_start > {RaceResult.PlaceholderIRating}"),
            Peak("SELECT MAX(sr_start + COALESCE(sr_delta, 0)) FROM sessions WHERE sr_start IS NOT NULL"),
            streaks);
    }
}
