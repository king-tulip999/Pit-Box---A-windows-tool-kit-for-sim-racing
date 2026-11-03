using PitBox.Config;

namespace PitBox.SessionStats;

internal static class GoalRewards
{
    public const double LiveWindowMinutes = 15;

    public static double? NextTarget(StatGoal g)
    {
        var (_, max, _) = StatGoal.Limits(g.Kind);
        if (g.Target >= max) return null;
        double next = g.Kind switch
        {
            GoalMetric.IRating => Math.Round((g.Target + 250) / 50) * 50,
            GoalMetric.SafetyRating => Math.Round(g.Target + 0.5, 2),
            _ => Achievements.All.FirstOrDefault(f => f.Goal == g.Kind)?.Tiers.FirstOrDefault(t => t > g.Target) is > 0 and var tier ? tier : Math.Round(g.Target * 2),
        };
        return Math.Min(next, max);
    }

    public static StatGoal FollowUp(StatGoal reached, double? current, DateTime nowUtc)
    {
        var next = new StatGoal
        {
            Kind = reached.Kind, Target = NextTarget(reached) ?? reached.Target, Car = reached.Car, Track = reached.Track, ThisSeason = reached.ThisSeason,
            Category = reached.Category, From = StatGoal.IsRating(reached.Kind) ? current : null, CreatedUtc = nowUtc,
        };
        next.Clean();
        return next;
    }

    public static List<StatGoal> Reach(SessionStatsSettings settings, IEnumerable<GoalState> states, DateTime nowUtc, long? session)
    {
        var reached = new List<StatGoal>();
        foreach (var state in states)
        {
            var g = state.Goal;
            if (!state.Done || !settings.Goals.Contains(g)) continue;
            g.ReachedUtc = nowUtc;
            g.ReachedSession = session;
            g.Open = true;
            g.Seen = false;
            settings.Goals.Remove(g);
            settings.Reached.Add(g);
            reached.Add(g);
        }
        if (settings.Reached.Count > SessionStatsSettings.KeptReached)
            settings.Reached.RemoveRange(0, settings.Reached.Count - SessionStatsSettings.KeptReached);
        return reached;
    }

    public static bool Live(DateTime? sessionEndUtc, DateTime nowUtc) =>
        sessionEndUtc is { } end && nowUtc - end <= TimeSpan.FromMinutes(LiveWindowMinutes) && end <= nowUtc.AddMinutes(1);

    public static TimeSpan? Took(StatGoal g) => g is { CreatedUtc: { } from, ReachedUtc: { } to } && to >= from ? to - from : null;
}
