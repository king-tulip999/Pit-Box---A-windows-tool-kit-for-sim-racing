using System.Windows;
using PitBox.SessionStats;

namespace PitBox.Views.Pages;

internal sealed class RaceInfo
{
    private static readonly List<(double Time, bool Clean)> NoLaps = new();
    private readonly Dictionary<long, List<Award>> _awards;
    private readonly Dictionary<long, double?> _consistency;
    private readonly Dictionary<long, List<(double Time, bool Clean)>> _laps;

    public string Driver { get; }

    private RaceInfo(Dictionary<long, List<Award>> awards, Dictionary<long, double?> consistency, Dictionary<long, List<(double Time, bool Clean)>> laps, string driver)
    {
        _awards = awards;
        _consistency = consistency;
        _laps = laps;
        Driver = driver;
    }

    public static readonly RaceInfo None = new(new(), new(), new(), "");

    public static RaceInfo Read(StatsStore store, string driver)
    {
        var facts = RaceHistory.Read(store);
        var laps = store.LapTimes(facts.Where(f => f.Raced).Select(f => f.Id));
        return new RaceInfo(RaceResult.AwardsByRace(facts), facts.ToDictionary(f => f.Id, f => f.Consistency), laps, driver);
    }

    public IReadOnlyList<Award> AwardsOf(long id) => _awards.TryGetValue(id, out var list) ? list : Array.Empty<Award>();

    public IReadOnlyList<(double Time, bool Clean)> LapsOf(long id) => _laps.TryGetValue(id, out var list) ? list : NoLaps;

    public double? ConsistencyOf(long id) => _consistency.TryGetValue(id, out var grade) ? grade : null;

    public RaceCardData Data(SessionSummary s) => new(s, LapsOf(s.Id), ConsistencyOf(s.Id), AwardsOf(s.Id), Driver);

    public FrameworkElement? Card(SessionSummary? s) => s is null ? null : RaceParts.Card(Data(s), "Last race", Theme.Sides.Right);
}
