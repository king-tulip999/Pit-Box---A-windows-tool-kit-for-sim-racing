using PitBox.SessionStats;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Race;

internal sealed record FinishCard(string Driver, string Car, string CarClass, string Track, string Config, int Finish, int? Start, int? Incidents,
    bool Official, int? IRating, double? IRatingChange, IReadOnlyList<(double Time, bool Clean)> Laps, int PitStops, IReadOnlyList<Award> Awards, int? Overall = null)
{
    public int? Gained => Start is > 0 and var start ? start - Finish : null;
    public double? Best => Laps.Where(l => l.Clean).Select(l => (double?)l.Time).Min();
    public double? AverageClean => RaceFinish.AverageClean(Laps);
    public double? Consistency => RaceResult.Consistency(Laps);
    public double RaceTime => Laps.Sum(l => l.Time);
    public string Place => Config.Length > 0 ? $"{Track} · {Config}" : Track;

    public static FinishCard Sample { get; } = new("Sam Driver", "Dallara IR18", "IR18", "WeatherTech Raceway at Laguna Seca", "Full Course", 3, 9, 2, true, 2286, 41,
        new (double, bool)[]
        {
            (71.42, true), (69.31, true), (69.05, true), (68.96, true), (69.88, false), (68.91, true), (68.80, true), (69.12, true), (68.95, true), (73.60, false),
            (69.20, true), (68.99, true), (69.07, true), (68.86, true), (69.01, true), (68.94, true), (69.25, true), (68.90, true),
        }, 1, new[] { Award.Podium, Award.Charger, Award.PersonalBest }, 5);
}

internal sealed class RaceFinish
{
    public const double HistoryGapSeconds = 60;

    private string _key = "";
    private int? _lapsAtCheckered;
    private DateTime _startUtc;

    public FinishCard? Card { get; private set; }
    public double FinishedAt { get; private set; }

    public static bool Crossed(SessionPhase phase, int? lapsAtCheckered, int? lapCompleted) =>
        phase == SessionPhase.CoolDown || lapsAtCheckered is { } at && lapCompleted is { } done && done > at;

    public static double? AverageClean(IReadOnlyList<(double Time, bool Clean)> laps)
    {
        if (laps.Count == 0) return null;
        var sorted = laps.Select(l => l.Time).OrderBy(t => t).ToList();
        double median = sorted[sorted.Count / 2];
        var pace = laps.Where(l => l.Clean && l.Time <= median * RaceResult.PaceWindow).Select(l => l.Time).ToList();
        return pace.Count > 0 ? pace.Average() : null;
    }

    public static List<Award> AwardsFor(RaceFacts race, IReadOnlyList<RaceFacts> history) =>
        RaceResult.Awards(race, history.Where(h => h.StartUtc < race.StartUtc.AddSeconds(-HistoryGapSeconds)).ToList());

    public void Update(TelemetryFrame f, RaceState race, double now, DateTime utcNow)
    {
        string key = $"{f.Session.SubSessionId}|{f.SessionNum}|{f.Session.TrackId}|{f.Session.Player?.CarPath}";
        if (key != _key)
        {
            _key = key;
            _lapsAtCheckered = null;
            Card = null;
            _startUtc = utcNow.AddSeconds(-(f.SessionTime ?? 0));
        }
        if (Card is not null || f.SessionKind != SessionKind.Race || f.Session.Player is not { } me) return;
        if (f.Phase == SessionPhase.Checkered) _lapsAtCheckered ??= f.LapCompleted;
        if (!Crossed(f.Phase, _lapsAtCheckered, f.LapCompleted)) return;
        int car = f.PlayerCarIdx;
        int finish = f.LapCompleted is { } done && f.Session.ResultOf(f.SessionNum, car) is { ClassPosition: > 0 } r && r.LapsComplete >= done ? r.ClassPosition
            : (uint)car < TelemetryFrame.MaxCars && f.CarClassPosition[car] > 0 ? f.CarClassPosition[car] : f.PlayerClassPosition ?? 0;
        if (finish <= 0) return;
        int? start = f.Session.GridClassPositions.TryGetValue(car, out var grid) && grid > 0 ? grid : null;
        int? overall = f.LapCompleted is { } lapsDone && f.Session.ResultOf(f.SessionNum, car) is { Position: > 0 } o && o.LapsComplete >= lapsDone ? o.Position
            : (uint)car < TelemetryFrame.MaxCars && f.CarPosition[car] > 0 ? f.CarPosition[car] : null;
        var laps = race.Laps.Laps.Select(l => (l.Time, l.Clean)).ToList();
        string carName = me.CarName.Length > 0 ? me.CarName : me.CarShortName;
        var facts = new RaceFacts(0, _startUtc, "Race", carName, f.Session.TrackDisplayName, f.Session.TrackConfig, start, finish, true, f.Incidents,
            laps.Sum(l => l.Time), laps.Where(l => l.Clean).Select(l => (double?)l.Time).Min(), RaceResult.Consistency(laps));
        Card = new FinishCard(me.Name, carName, me.ClassName.Length > 0 ? me.ClassName : me.CarShortName, f.Session.TrackDisplayName, f.Session.TrackConfig,
            finish, start, f.Incidents, f.Session.Official, RaceResult.KnownRating(me.IRating) ? me.IRating : null, f.Session.Official ? race.IRatingChangeOf(car) : null,
            laps, race.StopsOf(car), AwardsFor(facts, Array.Empty<RaceFacts>()), overall);
        FinishedAt = now;
        if (!RaceHistory.Available) return;
        var card = Card;
        string at = _key;
        RaceHistory.LoadAsync().ContinueWith(t =>
        {
            if (_key == at && ReferenceEquals(Card, card)) Card = card with { Awards = AwardsFor(facts, t.Result) };
        }, TaskScheduler.Default);
    }
}
