namespace PitBox.Config;

public sealed class SessionStatsSettings
{
    public bool Imperial { get; set; }

    public string? DriverNumber { get; set; }

    public string Range { get; set; } = "season";

    public string? TimelineCombo { get; set; }

    public long? TimelinePickedUtc { get; set; }

    public bool TraceRecord { get; set; } = true;

    public int TraceKeepDays { get; set; } = 365;

    public int TraceLapsPerSession { get; set; }

    public bool TraceKeepBests { get; set; } = true;

    public List<string>? AnalysisLanes { get; set; }

    public string ReplaySync { get; set; } = "distance";

    public double ReplaySpeed { get; set; } = 1;

    public double ReplayZoom { get; set; } = 0.5;

    public bool ReplayTurn { get; set; } = true;

    public bool ReplayShowReference { get; set; } = true;

    public bool ReplayFree { get; set; }

    public bool ReplayTips { get; set; } = true;

    public bool AnalysisWind { get; set; } = true;

    public List<StatGoal> Goals { get; set; } = new();

    public const int MaxGoals = 12;

    public static readonly int[] KeepDayChoices = { 30, 90, 182, 365, 730, 0 };
    public static readonly int[] LapChoices = { 0, 10, 3, 1 };
    public static readonly string[] Lanes = { "delta", "speed", "pedals", "steering", "gear", "rpm", "latg", "bias", "abs", "tc" };
    public static readonly string[] DefaultLanes = { "delta", "speed", "pedals", "steering", "gear" };

    public void Normalize()
    {
        if (!KeepDayChoices.Contains(TraceKeepDays)) TraceKeepDays = 365;
        if (!LapChoices.Contains(TraceLapsPerSession)) TraceLapsPerSession = 0;
        if (AnalysisLanes is not null) AnalysisLanes = AnalysisLanes.Where(l => Lanes.Contains(l)).Distinct().ToList();
        DriverNumber = string.IsNullOrWhiteSpace(DriverNumber) ? null : DriverNumber.Trim().TrimStart('#');
        if (Range is not ("week" or "month" or "season" or "all")) Range = "season";
        if (ReplaySync is not ("distance" or "time")) ReplaySync = "distance";
        if (!SessionStats.LapReplay.Speeds.Contains(ReplaySpeed)) ReplaySpeed = 1;
        ReplayZoom = double.IsFinite(ReplayZoom) ? Math.Clamp(ReplayZoom, 0, 1) : 0.5;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Goals = (Goals ?? new()).Where(g => g is not null && Enum.IsDefined(g.Kind)).Take(MaxGoals).ToList();
        foreach (var g in Goals)
        {
            g.Clean();
            if (string.IsNullOrWhiteSpace(g.Id) || !ids.Add(g.Id)) { g.Id = Guid.NewGuid().ToString("N"); ids.Add(g.Id); }
        }
    }
}

public enum GoalMetric { IRating, SafetyRating, DrivingHours, Laps, Races, Wins, Podiums, CleanRaces }

public sealed class StatGoal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public GoalMetric Kind { get; set; }
    public double Target { get; set; }
    public string? Car { get; set; }
    public string? Track { get; set; }
    public bool ThisSeason { get; set; }

    public static (double Min, double Max, double Step) Limits(GoalMetric kind) => kind switch
    {
        GoalMetric.IRating => (100, 15000, 50),
        GoalMetric.SafetyRating => (0.01, 4.99, 0.01),
        GoalMetric.DrivingHours => (1, 2000, 1),
        GoalMetric.Laps => (10, 100000, 10),
        _ => (1, 1000, 1),
    };

    public void Clean()
    {
        var (min, max, step) = Limits(Kind);
        Target = double.IsFinite(Target) ? Math.Clamp(Math.Round(Target / step) * step, min, max) : min;
        Car = string.IsNullOrWhiteSpace(Car) ? null : Car.Trim();
        Track = string.IsNullOrWhiteSpace(Track) ? null : Track.Trim();
        if (Kind is GoalMetric.IRating or GoalMetric.SafetyRating) { Car = Track = null; ThisSeason = false; }
    }
}
