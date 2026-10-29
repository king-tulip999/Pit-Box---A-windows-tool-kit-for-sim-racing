namespace PitBox.SessionStats;

internal enum Award { Win, Podium, CleanRace, PersonalBest, MostConsistent, Charger, BestFinishHere, LongestRace }

internal sealed record RaceFacts(long Id, DateTime StartUtc, string Type, string Car, string Track, string Config, int? StartPos, int? FinishPos, bool Finished,
    int? Incidents, double DriveS, double? CleanBest, double? Consistency)
{
    public bool Raced => Type == "Race" && Finished && FinishPos is > 0;
    public int? Gained => Raced && StartPos is > 0 and var start ? start - FinishPos!.Value : null;
}

internal static class RaceResult
{
    public const int PlaceholderIRating = 1;
    public const double PaceWindow = 1.07;
    public const double SpreadWeight = 15;
    public const double PaceShare = 0.7;
    public const int MinPaceLaps = 3;
    public const int ChargerPositions = 8;
    public const double LongerBy = 1.1;

    public static bool KnownRating(int? irating) => irating is > PlaceholderIRating;

    public static double? Consistency(IReadOnlyList<(double Time, bool Clean)> laps)
    {
        if (laps.Count < MinPaceLaps) return null;
        var sorted = laps.Select(l => l.Time).OrderBy(t => t).ToList();
        double median = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
        var pace = laps.Where(l => l.Clean && l.Time <= median * PaceWindow).Select(l => l.Time).ToList();
        if (pace.Count < MinPaceLaps) return null;
        double mean = pace.Average();
        double spread = Math.Sqrt(pace.Sum(t => (t - mean) * (t - mean)) / pace.Count) / mean * 100;
        double cleanShare = 100.0 * laps.Count(l => l.Clean) / laps.Count;
        double paceScore = Math.Max(0, 100 - SpreadWeight * spread);
        return Math.Round(PaceShare * paceScore + (1 - PaceShare) * cleanShare, 1);
    }

    public static List<Award> Awards(RaceFacts race, IReadOnlyList<RaceFacts> before)
    {
        var awards = new List<Award>();
        if (!race.Raced) return awards;
        int finish = race.FinishPos!.Value;
        bool SamePlace(RaceFacts f) => f.Track == race.Track && f.Config == race.Config;
        bool SameCombo(RaceFacts f) => SamePlace(f) && f.Car == race.Car;
        var races = before.Where(f => f.Raced && f.StartUtc < race.StartUtc).ToList();
        var here = races.Where(SamePlace).ToList();

        if (finish == 1) awards.Add(Award.Win);
        else if (finish <= 3) awards.Add(Award.Podium);
        if (race.Incidents == 0) awards.Add(Award.CleanRace);
        var comboBests = before.Where(f => f.StartUtc < race.StartUtc && SameCombo(f) && f.CleanBest is not null).Select(f => f.CleanBest!.Value).ToList();
        if (race.CleanBest is { } best && comboBests.Count > 0 && best < comboBests.Min()) awards.Add(Award.PersonalBest);
        var grades = here.Where(f => f.Car == race.Car && f.Consistency is not null).Select(f => f.Consistency!.Value).ToList();
        if (race.Consistency is { } grade && grades.Count > 0 && grade > grades.Max()) awards.Add(Award.MostConsistent);
        if (race.Gained >= ChargerPositions) awards.Add(Award.Charger);
        if (here.Count > 0 && finish < here.Min(f => f.FinishPos!.Value)) awards.Add(Award.BestFinishHere);
        if (races.Count > 0 && race.DriveS > races.Max(f => f.DriveS) * LongerBy) awards.Add(Award.LongestRace);
        return awards;
    }

    public static Dictionary<long, List<Award>> AwardsByRace(IReadOnlyList<RaceFacts> all)
    {
        var ordered = all.OrderBy(f => f.StartUtc).ToList();
        var result = new Dictionary<long, List<Award>>();
        for (int i = 0; i < ordered.Count; i++)
            if (ordered[i].Raced) result[ordered[i].Id] = Awards(ordered[i], ordered.Take(i).ToList());
        return result;
    }

    public static string Name(Award award) => award switch
    {
        Award.Win => "Win", Award.Podium => "Podium", Award.CleanRace => "Clean race", Award.PersonalBest => "Personal best",
        Award.MostConsistent => "Most consistent yet", Award.Charger => "Charger", Award.BestFinishHere => "Best finish here", _ => "Longest race",
    };

    public static string Why(Award award) => award switch
    {
        Award.Win => "Won the race (class position in multiclass)",
        Award.Podium => "Finished in the top three",
        Award.CleanRace => "No incidents",
        Award.PersonalBest => "Fastest clean lap you've done in this car at this track",
        Award.MostConsistent => "Your best consistency in a race in this car at this track",
        Award.Charger => $"Gained {ChargerPositions} or more positions",
        Award.BestFinishHere => "Your best finish at this track",
        _ => "At least 10% longer than any race before it",
    };

    public static string Icon(Award award) => award switch
    {
        Award.Win or Award.Podium => "I.Trophy", Award.CleanRace => "I.Shield", Award.PersonalBest => "I.Zap",
        Award.MostConsistent => "I.Target", Award.Charger => "I.TrendingUp", Award.BestFinishHere => "I.Flag", _ => "I.Clock",
    };
}
