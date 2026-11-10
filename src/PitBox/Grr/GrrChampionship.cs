using PitBox.SimRacing.Race;

namespace PitBox.Grr;

public sealed record GrrTitleFight(string Leader, string? Second, double Lead, int RoundsDone, int RoundsLeft, double? OnOffer)
{
    public bool Decided => RoundsLeft == 0 && Lead > 0;
    public bool Clinched => RoundsLeft > 0 && OnOffer is { } offer && Lead > offer;
}

public enum GrrAwardKind { HardCharger, MostLed, Cleanest, FastestLap }

public sealed record GrrAward(GrrAwardKind Kind, string Driver, string Detail);

public static class GrrChampionship
{
    public static GrrSession? SessionFor(GrrLeagueData data, GrrEvent e, string classKey)
    {
        string label = data.ClassLabel(classKey);
        if (label.Length == 0) return e.Sessions.FirstOrDefault(s => s.Rows.Count > 0);
        if (e.Sessions.FirstOrDefault(s => s.Rows.Count > 0 && string.Equals(s.Label, label, StringComparison.OrdinalIgnoreCase)) is { } own) return own;
        bool Mine(GrrResult r) => string.Equals(r.Class, label, StringComparison.OrdinalIgnoreCase);
        if (e.Sessions.FirstOrDefault(s => s.Rows.Any(Mine)) is not { } mixed) return null;
        return new GrrSession(label, mixed.Rows.Where(Mine).OrderBy(r => r.Position).Select((r, n) => r with { Position = n + 1 }).ToList());
    }

    public static List<Dictionary<string, double>> RoundPoints(GrrLeagueData data, string classKey)
    {
        var rounds = new List<Dictionary<string, double>>();
        foreach (var e in data.Events ?? Array.Empty<GrrEvent>())
        {
            if (SessionFor(data, e, classKey) is not { Rows.Count: > 0 } session) continue;
            var points = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in session.Rows)
            {
                string name = GrrFeed.Squash(r.Driver);
                points[name] = points.GetValueOrDefault(name) + (r.Points ?? 0);
            }
            rounds.Add(points);
        }
        return rounds;
    }

    public static double Score(IReadOnlyList<Dictionary<string, double>> rounds, int count, string driver, int dropWeeks, int dropFrom)
    {
        string name = GrrFeed.Squash(driver);
        var each = rounds.Take(count).Select(r => r.GetValueOrDefault(name)).ToList();
        double sum = each.Sum();
        if (dropWeeks > 0 && count >= dropFrom) sum -= each.OrderBy(p => p).Take(dropWeeks).Sum();
        return sum;
    }

    public static IReadOnlyDictionary<string, int>? Moves(GrrLeagueData data, string classKey)
    {
        if (data.Standings?.GetValueOrDefault(classKey) is not { Count: > 1 } table) return null;
        var rounds = RoundPoints(data, classKey);
        if (rounds.Count < 2) return null;
        int drop = data.DropWeeks, from = data.DropFromRound ?? 1;
        if (table.Any(row => Math.Abs(Score(rounds, rounds.Count, row.Driver, drop, from) - row.Points) > 0.5)) return null;
        var before = table.Select((row, i) => (Name: GrrFeed.Squash(row.Driver), Now: i + 1, Points: Score(rounds, rounds.Count - 1, row.Driver, drop, from)))
            .OrderByDescending(x => x.Points).ThenBy(x => x.Now).ToList();
        var moves = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < before.Count; i++) moves[before[i].Name] = i + 1 - before[i].Now;
        return moves;
    }

    public static int? MoveOf(IReadOnlyDictionary<string, int>? moves, string driver) =>
        moves is not null && moves.TryGetValue(GrrFeed.Squash(driver), out var m) ? m : null;

    public static GrrTitleFight? Title(GrrLeagueData data, string classKey, DateTime now, bool pointsKnown)
    {
        if (data.Standings?.GetValueOrDefault(classKey) is not { Count: > 0 } table) return null;
        var schedule = data.Schedule ?? Array.Empty<GrrRound>();
        var ahead = schedule.Where(r => r.Start + GrrService.RaceLength > now).ToList();
        double lead = table.Count > 1 ? table[0].Points - table[1].Points : table[0].Points;
        double? offer = null;
        if (pointsKnown && ahead.Count > 0)
        {
            var best = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double any = 0;
            foreach (var e in data.Events ?? Array.Empty<GrrEvent>())
            {
                if (SessionFor(data, e, classKey) is not { Rows.Count: > 0 } session) continue;
                double top = session.Rows.Max(r => r.Points ?? 0);
                string format = data.RoundFor(e)?.Format ?? "";
                best[format] = Math.Max(best.GetValueOrDefault(format), top);
                any = Math.Max(any, top);
            }
            if (any > 0) offer = ahead.Sum(r => best.TryGetValue(r.Format ?? "", out var b) ? b : any);
        }
        return new GrrTitleFight(table[0].Driver, table.Count > 1 ? table[1].Driver : null, lead, schedule.Count - ahead.Count, ahead.Count, offer);
    }

    public static Dictionary<GrrResult, int> GridPlaces(IReadOnlyList<GrrResult> rows)
    {
        var places = new Dictionary<GrrResult, int>(ReferenceEqualityComparer.Instance);
        int n = 0;
        foreach (var r in rows.Where(r => r.Start is > 0).OrderBy(r => r.Start).ThenBy(r => r.Position)) places[r] = ++n;
        return places;
    }

    public static bool Lapped(GrrResult r) => r.Interval?.Contains("lap", StringComparison.OrdinalIgnoreCase) == true;

    public static IReadOnlyList<GrrFormRound> Recent(IReadOnlyList<GrrFormRound> form, int count) =>
        form.Where(f => f.State is GrrFormState.Finished or GrrFormState.NoResult).TakeLast(count).ToList();

    public static IReadOnlyList<GrrAward> Awards(IReadOnlyList<GrrResult> rows)
    {
        var awards = new List<GrrAward>();
        if (rows.Count < 2) return awards;
        var grid = GridPlaces(rows);
        var charger = rows.Where(grid.ContainsKey).Select(r => (Row: r, Gain: grid[r] - r.Position)).Where(x => x.Gain > 0)
            .OrderByDescending(x => x.Gain).ThenBy(x => x.Row.Position).FirstOrDefault();
        if (charger.Row is { } c) awards.Add(new GrrAward(GrrAwardKind.HardCharger, c.Driver, $"P{grid[c]} to P{c.Position}, +{charger.Gain}"));

        if (rows.Where(r => r.Led > 0).OrderByDescending(r => r.Led).ThenBy(r => r.Position).FirstOrDefault() is { Led: { } led } leader)
        {
            int laps = rows.Max(r => r.Laps ?? 0);
            awards.Add(new GrrAward(GrrAwardKind.MostLed, leader.Driver, laps >= led ? $"{led} of {RaceFormat.Count(laps, "lap")}" : RaceFormat.Count(led, "lap")));
        }

        var counted = rows.Where(r => r.Incidents is not null && (r.Status is null || r.Status.Equals("Running", StringComparison.OrdinalIgnoreCase))).ToList();
        if (counted.Count >= 2 && counted.OrderBy(r => r.Incidents).ThenBy(r => r.Position).First() is { Incidents: { } inc } clean)
            awards.Add(new GrrAward(GrrAwardKind.Cleanest, clean.Driver, RaceFormat.Count(inc, "incident")));

        if (rows.FirstOrDefault(r => r.FastestLap) is { } fastest)
            awards.Add(new GrrAward(GrrAwardKind.FastestLap, fastest.Driver, $"Finished P{fastest.Position}"));
        return awards;
    }
}
