namespace PitBox.SessionStats;

internal static class RaceHistory
{
    private static volatile Func<IReadOnlyList<RaceFacts>>? _source;

    public static bool Available => _source is not null;

    public static void Use(StatsStore? store) => _source = store is null ? null : () => Read(store);

    public static IReadOnlyList<RaceFacts> Read(StatsStore store)
    {
        var facts = store.AllRaceFacts();
        var laps = store.LapTimes(facts.Where(f => f.Raced).Select(f => f.Id));
        return facts.Select(f => laps.TryGetValue(f.Id, out var list) ? f with { Consistency = RaceResult.Consistency(list) } : f).ToList();
    }

    public static Task<IReadOnlyList<RaceFacts>> LoadAsync()
    {
        var source = _source;
        if (source is null) return Task.FromResult<IReadOnlyList<RaceFacts>>(Array.Empty<RaceFacts>());
        return Task.Run(() =>
        {
            try { return source(); }
            catch (Exception ex)
            {
                App.LogError(ex);
                return (IReadOnlyList<RaceFacts>)Array.Empty<RaceFacts>();
            }
        });
    }
}
