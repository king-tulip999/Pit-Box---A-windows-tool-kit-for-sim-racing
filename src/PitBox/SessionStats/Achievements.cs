using System.Globalization;

namespace PitBox.SessionStats;

internal sealed record RaceRow(DateTime StartUtc, bool League, string Category, string Track, string Car, int Laps, double DriveS,
    int? Incidents, int StartPos, int? FinishPos, bool Finished, double RainS, double NightS, int? IrDelta)
{
    public bool Won => Finished && FinishPos == 1;
    public bool Podium => Finished && FinishPos is >= 1 and <= 3;
    public bool TopFive => Finished && FinishPos is >= 1 and <= 5;
    public bool Clean => Finished && Incidents == 0 && (Laps >= Achievements.CleanMinLaps || DriveS >= Achievements.CleanMinSeconds);
    public bool Wet => DriveS > 0 && RainS * 2 >= DriveS;
    public bool Night => DriveS > 0 && NightS * 2 >= DriveS;
    public int Gain => Finished && FinishPos > 0 ? StartPos - FinishPos.Value : 0;
}

internal sealed record AchievementInput(IReadOnlyList<RaceRow> Races, double Hours, double? PeakIRating, double? PeakSafety, RaceStreaks Streaks);

internal sealed record AchievementFamily(string Id, string Group, string Name, string Noun, double[] Tiers, Func<AchievementInput, double?> Value)
{
    public bool Lower { get; init; }
    public string? Nouns { get; init; }
    public string? Help { get; init; }
    public bool Reached(double v, double tier) => Lower ? v <= tier : v >= tier;
}

internal sealed record AchievementState(AchievementFamily Family, double? Value)
{
    public int Earned => Value is { } v ? Family.Tiers.Count(t => Family.Reached(v, t)) : 0;
    public double? Next => Value is { } v ? Family.Tiers.Where(t => !Family.Reached(v, t)).Select(t => (double?)t).FirstOrDefault() : Family.Tiers[0];

    public double Fraction => Value is { } v && Next is { } n
        ? Math.Clamp(Family.Lower ? (v <= 0 ? 1 : n / v) : v / n, 0, 1)
        : Next is null ? 1 : 0;
}

internal static class Achievements
{
    public const int CleanMinLaps = 10;
    public const double CleanMinSeconds = 20 * 60;
    public const double LongRaceSeconds = 90 * 60;
    public const int ComebackGrid = 15;
    public const int RateWindow = 20;
    private static readonly string CleanHelp = $"0 incidents over {CleanMinLaps}+ laps or {CleanMinSeconds / 60:0}+ min";
    private const string HalfHelp = "Half the race or more";

    public const string Results = "Results", Streaks = "Streaks", Driving = "Driving", Ratings = "Ratings";
    public static readonly string[] Groups = { Results, Streaks, Driving, Ratings };

    public static readonly AchievementFamily[] All =
    {
        new("races", Results, "Races", "race", new double[] { 1, 10, 25, 50, 100, 250, 500, 1000, 2000 }, i => i.Races.Count),
        new("wins", Results, "Wins", "win", new double[] { 1, 5, 10, 25, 50, 100, 250 }, i => i.Races.Count(r => r.Won)),
        new("podiums", Results, "Podiums", "podium", new double[] { 1, 10, 25, 50, 100, 250, 500 }, i => i.Races.Count(r => r.Podium)),
        new("clean", Results, "Clean races", "clean race", new double[] { 1, 10, 25, 50, 100, 250, 500 }, i => i.Races.Count(r => r.Clean)) { Help = CleanHelp },
        new("cleanwins", Results, "Clean wins", "win", new double[] { 1, 5, 10, 25, 50 }, i => i.Races.Count(r => r.Won && r.Clean)) { Help = CleanHelp },
        new("poles", Results, "Pole starts", "pole start", new double[] { 1, 5, 10, 25, 50, 100 }, i => i.Races.Count(r => r.StartPos == 1)),
        new("poletowin", Results, "Wins from pole", "win", new double[] { 1, 5, 10, 25 }, i => i.Races.Count(r => r.StartPos == 1 && r.Won)),
        new("gain", Results, "Places gained in one race", "place", new double[] { 5, 10, 15, 20 }, i => i.Races.Select(r => r.Gain).DefaultIfEmpty(0).Max()),
        new("comeback", Results, $"Podiums from {ComebackGrid}th or lower", "podium", new double[] { 1, 5, 10, 25 }, i => i.Races.Count(r => r.Podium && r.StartPos >= ComebackGrid)),
        new("debutwins", Results, "Wins in a first race at a track", "win", new double[] { 1, 3, 10 }, i => DebutWins(i.Races)),
        new("categories", Results, "Categories won in", "category", new double[] { 2, 3, 4, 5 }, i => i.Races.Where(r => r.Won && r.Category != "").Select(r => r.Category).Distinct().Count()) { Nouns = "categories" },

        new("cleanrun", Streaks, "Clean races in a row", "race", new double[] { 3, 5, 10, 15, 20, 30 }, i => i.Streaks.Clean.Best) { Help = CleanHelp },
        new("finishrun", Streaks, "Finishes in a row", "race", new double[] { 10, 25, 50, 100, 200, 500 }, i => i.Streaks.Finished.Best),
        new("top5run", Streaks, "Top fives in a row", "race", new double[] { 3, 5, 10, 15, 20 }, i => i.Streaks.TopFive.Best),
        new("weekrun", Streaks, "Race weeks in a row", "week", new double[] { 4, 8, 13, 26, 52 }, i => i.Streaks.Weeks.Best) { Help = "Race weeks start Tuesday 00:00 UTC" },
        new("irrun", Streaks, "iRating gains in a row", "race", new double[] { 3, 5, 8, 12 }, i => i.Streaks.IRating.Best) { Help = "Official races only" },

        new("laps", Driving, "Race laps", "lap", new double[] { 100, 500, 1000, 2500, 5000, 10000, 25000 }, i => i.Races.Sum(r => r.Laps)),
        new("hours", Driving, "Hours driven", "h", new double[] { 10, 50, 100, 250, 500, 1000, 2500 }, i => i.Hours),
        new("longraces", Driving, "Races of 90 min or more", "race", new double[] { 1, 5, 10, 25, 50 }, i => i.Races.Count(r => r.DriveS >= LongRaceSeconds)),
        new("wet", Driving, "Wet races", "race", new double[] { 1, 10, 25, 50 }, i => i.Races.Count(r => r.Wet)) { Help = HalfHelp },
        new("night", Driving, "Night races", "race", new double[] { 1, 10, 25, 50 }, i => i.Races.Count(r => r.Night)) { Help = HalfHelp },
        new("tracks", Driving, "Tracks raced", "track", new double[] { 5, 10, 25, 50, 75 }, i => i.Races.Where(r => r.Laps > 0).Select(r => r.Track).Distinct().Count()),
        new("cars", Driving, "Cars raced", "car", new double[] { 3, 10, 25, 50 }, i => i.Races.Where(r => r.Laps > 0).Select(r => r.Car).Distinct().Count()),
        new("league", Driving, "League races", "race", new double[] { 1, 10, 25, 50, 100, 250 }, i => i.Races.Count(r => r.League)),

        new("irating", Ratings, "Best iRating", "", new double[] { 1500, 2000, 2500, 3000, 4000, 5000, 6000, 8000 }, i => i.PeakIRating),
        new("sr", Ratings, "Best safety rating", "", new double[] { 2, 3, 4, 4.99 }, i => i.PeakSafety),
        new("incrate", Ratings, $"Fewest incidents per 100 laps over {RateWindow} races", "", new double[] { 20, 15, 10, 5, 2 }, i => BestIncidentRate(i.Races)) { Lower = true, Help = "Finished races only" },
    };

    public const string Season = "Season";

    public static readonly AchievementFamily[] SeasonGoals =
    {
        new("s.races", Season, "Races", "race", new double[] { 5, 10, 25, 50 }, i => i.Races.Count),
        new("s.wins", Season, "Wins", "win", new double[] { 1, 3, 5, 10 }, i => i.Races.Count(r => r.Won)),
        new("s.podiums", Season, "Podiums", "podium", new double[] { 1, 3, 5, 10, 25 }, i => i.Races.Count(r => r.Podium)),
        new("s.clean", Season, "Clean races", "clean race", new double[] { 1, 5, 10, 25 }, i => i.Races.Count(r => r.Clean)) { Help = CleanHelp },
        new("s.weeks", Season, "Race weeks", "week", new double[] { 4, 8, 12 }, i => i.Races.Select(r => RaceStreakReader.RaceWeek(r.StartUtc)).Distinct().Count()) { Help = "Race weeks start Tuesday 00:00 UTC" },
        new("s.laps", Season, "Race laps", "lap", new double[] { 100, 500, 1000, 2500 }, i => i.Races.Sum(r => r.Laps)),
        new("s.irgain", Season, "iRating gained", "", new double[] { 100, 250, 500, 1000 }, i => i.Races.Any(r => r.IrDelta is not null) ? i.Races.Sum(r => r.IrDelta ?? 0) : null) { Help = "Official races only" },
    };

    public static List<AchievementState> Evaluate(AchievementInput input) => Evaluate(All, input);

    public static List<AchievementState> Evaluate(IEnumerable<AchievementFamily> families, AchievementInput input) =>
        families.Select(f => new AchievementState(f, f.Value(input))).ToList();

    public static int TierCount => All.Sum(f => f.Tiers.Length);

    internal static int DebutWins(IEnumerable<RaceRow> races)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int wins = 0;
        foreach (var r in races)
            if (seen.Add(r.Track) && r.Won) wins++;
        return wins;
    }

    internal static double? BestIncidentRate(IEnumerable<RaceRow> races)
    {
        var rows = races.Where(r => r.Finished && r.Incidents is not null && r.Laps > 0).ToList();
        if (rows.Count < RateWindow) return null;
        int incidents = rows.Take(RateWindow).Sum(r => r.Incidents!.Value), laps = rows.Take(RateWindow).Sum(r => r.Laps);
        double best = incidents * 100.0 / laps;
        for (int i = RateWindow; i < rows.Count; i++)
        {
            incidents += rows[i].Incidents!.Value - rows[i - RateWindow].Incidents!.Value;
            laps += rows[i].Laps - rows[i - RateWindow].Laps;
            best = Math.Min(best, incidents * 100.0 / laps);
        }
        return best;
    }

    public static string Format(AchievementFamily family, double v) => family.Id switch
    {
        "sr" => v.ToString("0.00", CultureInfo.CurrentCulture),
        "incrate" => v.ToString("0.0", CultureInfo.CurrentCulture),
        "hours" => v.ToString(v < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " h",
        _ => Math.Floor(v).ToString("N0", CultureInfo.CurrentCulture),
    };

    public static string Target(AchievementFamily family, double v) => family.Noun switch
    {
        "" or "h" => Format(family, v),
        var noun when family.Nouns is { } nouns => $"{Math.Floor(v).ToString("N0", CultureInfo.CurrentCulture)} {(v == 1 ? noun : nouns)}",
        var noun => SimRacing.Race.RaceFormat.Count((int)v, noun),
    };

    private const string CountedRace = "session_type = 'Race' AND start_pos > 0 AND (official = 1 OR league_id > 0)";

    public static List<RaceRow> ReadRaces(StatsStore store) =>
        store.Db.Query($"SELECT start_utc, league_id, category, track, car, laps, drive_s, incidents, start_pos, finish_pos, finished, rain_s, night_s, ir_delta FROM sessions WHERE {CountedRace} ORDER BY start_utc, id")
            .Select(r => new RaceRow(StatsStore.FromUnix(Convert.ToInt64(r[0])), Convert.ToInt64(r[1] ?? 0L) > 0, r[2] as string ?? "", r[3] as string ?? "", r[4] as string ?? "",
                Convert.ToInt32(r[5] ?? 0L), Convert.ToDouble(r[6] ?? 0.0), r[7] is null ? null : Convert.ToInt32(r[7]), Convert.ToInt32(r[8] ?? 0L),
                r[9] is null ? null : Convert.ToInt32(r[9]), Convert.ToInt64(r[10] ?? 0L) != 0, Convert.ToDouble(r[11] ?? 0.0), Convert.ToDouble(r[12] ?? 0.0),
                r[13] is null ? null : Convert.ToInt32(r[13])))
            .ToList();

    public static AchievementInput Read(StatsStore store, IReadOnlyList<RaceRow> races, DateTime nowUtc)
    {
        var db = store.Db;
        double? Peak(string sql) => db.Scalar(sql) is { } v ? Convert.ToDouble(v) : null;
        return new AchievementInput(
            races,
            Convert.ToDouble(db.Scalar("SELECT COALESCE(SUM(drive_s), 0) / 3600.0 FROM sessions") ?? 0.0),
            Peak($"SELECT MAX(ir_start + COALESCE(ir_delta, 0)) FROM sessions WHERE ir_start > {RaceResult.PlaceholderIRating}"),
            Peak("SELECT MAX(sr_start + COALESCE(sr_delta, 0)) FROM sessions WHERE sr_start IS NOT NULL"),
            RaceStreakReader.Streaks(races, nowUtc));
    }

    public static AchievementInput ForSeason(IReadOnlyList<RaceRow> races, DateTime seasonStartUtc, DateTime nowUtc)
    {
        var season = races.Where(r => r.StartUtc >= seasonStartUtc).ToList();
        return new AchievementInput(season, 0, null, null, RaceStreakReader.Streaks(season, nowUtc));
    }
}
