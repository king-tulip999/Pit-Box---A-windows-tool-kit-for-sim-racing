namespace Core.SessionStats;

internal sealed record RaceLine(DateTime StartUtc, string Car, string Track, string Config, string Category, int Start, int? Finish, bool Finished,
    int? Incidents, int? IrStart, int? IrDelta, double? SrStart, double? SrDelta, int? Field, int? LapsLed)
{
    public int? Gain => Finished && Finish is > 0 and var f ? Start - f : null;
}

internal sealed record Racecraft(IReadOnlyList<RaceLine> Races, int? Incidents, double DistanceKm)
{
    public const int StripRaces = 20;

    public static readonly Racecraft Empty = new(Array.Empty<RaceLine>(), null, 0);

    private IEnumerable<RaceLine> Placed => Races.Where(r => r.Finished && r.Finish > 0);

    public int Starts => Races.Count;
    public int Finishes => Races.Count(r => r.Finished);
    public int Wins => Placed.Count(r => r.Finish == 1);
    public int Podiums => Placed.Count(r => r.Finish <= 3);
    public int TopFives => Placed.Count(r => r.Finish <= 5);
    public int TopTens => Placed.Count(r => r.Finish <= 10);
    public int Compared => Placed.Count();
    public int AheadOfStart => Placed.Count(r => r.Gain > 0);
    public int? LapsLed => Races.Any(r => r.LapsLed is not null) ? Races.Sum(r => r.LapsLed ?? 0) : null;

    public double? AverageStart => Races.Count > 0 ? Races.Average(r => r.Start) : null;
    public double? AverageFinish => Placed.Any() ? Placed.Average(r => r.Finish!.Value) : null;
    public int? Gained => Placed.Any() ? Placed.Sum(r => r.Gain!.Value) : null;
    public double? GainedPerRace => Placed.Any() ? Placed.Average(r => r.Gain!.Value) : null;
    public RaceLine? BiggestGain => Placed.Where(r => r.Gain > 0).OrderByDescending(r => r.Gain).ThenByDescending(r => r.StartUtc).FirstOrDefault();
    public double? AverageField => Races.Any(r => r.Field > 0) ? Races.Where(r => r.Field > 0).Average(r => r.Field!.Value) : null;

    public int? IrChange => Races.Any(r => r.IrDelta is not null) ? Races.Sum(r => r.IrDelta ?? 0) : null;
    public double? SrChange => Races.Any(r => r.SrDelta is not null) ? Races.Sum(r => r.SrDelta ?? 0) : null;

    public RaceLine? Latest => Races.Count > 0 ? Races[^1] : null;

    public int? IrNow => Latest is { } last && Races.LastOrDefault(r => r.Category == last.Category && r.IrStart > RaceResult.PlaceholderIRating) is { IrStart: { } ir } rated
        ? ir + (rated.IrDelta ?? 0) : null;

    public double? SrNow => Latest is { } last && Races.LastOrDefault(r => r.Category == last.Category && r.SrStart is not null) is { SrStart: { } sr } rated
        ? sr + (rated.SrDelta ?? 0) : null;

    public int CleanRaces => Races.Count(r => r.Finished && r.Incidents == 0);
    public double? IncidentsPerRace => Races.Any(r => r.Incidents is not null) ? Races.Where(r => r.Incidents is not null).Average(r => r.Incidents!.Value) : null;
    public double? IncidentsPer100Km => Incidents is { } n && DistanceKm >= 1 ? n * 100 / DistanceKm : null;

    public IReadOnlyList<RaceLine> Strip => Races.Count > StripRaces ? Races.Skip(Races.Count - StripRaces).ToList() : Races;

    public static int Share(int part, int whole) => whole > 0 ? (int)Math.Round(100.0 * part / whole) : 0;

    public static Racecraft Read(StatsStore store, StatQuery q)
    {
        var db = store.Db;
        var races = db.Query(StatRegistry.Scope + """
            SELECT start_utc, car, track, track_config, category, start_pos, finish_pos, finished, incidents, ir_start, ir_delta, sr_start, sr_delta, field, laps_led
            FROM s WHERE session_type = 'Race' AND start_pos > 0 ORDER BY start_utc, id
            """, q.From, q.To)
            .Select(r => new RaceLine(StatsStore.FromUnix(Convert.ToInt64(r[0])), (string)(r[1] ?? ""), (string)(r[2] ?? ""), (string)(r[3] ?? ""), (string)(r[4] ?? ""),
                Convert.ToInt32(r[5]), Int(r[6]), Convert.ToInt64(r[7] ?? 0L) != 0, Int(r[8]), Int(r[9]), Int(r[10]), Real(r[11]), Real(r[12]), Int(r[13]), Int(r[14])))
            .ToList();
        var totals = db.Query(StatRegistry.Scope + "SELECT SUM(incidents), SUM(distance_km) FROM s", q.From, q.To);
        int? incidents = totals.Count > 0 ? Int(totals[0][0]) : null;
        double distance = totals.Count > 0 ? Real(totals[0][1]) ?? 0 : 0;
        return new Racecraft(races, incidents, distance);
    }

    private static int? Int(object? v) => v is null ? null : Convert.ToInt32(v);

    private static double? Real(object? v) => v is null ? null : Convert.ToDouble(v);
}
