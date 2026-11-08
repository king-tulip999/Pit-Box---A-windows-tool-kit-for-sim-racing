using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Race;

internal static class ProAm
{
    public const string Prefix = "PRO ";

    public static bool IsPro(string? name) =>
        name is { Length: > 4 } n && n.StartsWith(Prefix, StringComparison.Ordinal) && !char.IsWhiteSpace(n[Prefix.Length]);

    public static string Strip(string name) => IsPro(name) ? name[Prefix.Length..] : name;

    public static bool Active(SessionInfo session)
    {
        foreach (var d in session.Drivers)
            if (d.Races && IsPro(d.Name)) return true;
        return false;
    }

    public static bool IsPro(SessionInfo session, int car) => session.ByCar.TryGetValue(car, out var d) && IsPro(d.Name);

    public static string Tier(bool pro) => pro ? "PRO" : "AM";

    public static int TierPosition(SessionInfo session, int me, int myClassPos, int[] classPositions)
    {
        if (myClassPos <= 0 || !session.ByCar.TryGetValue(me, out var mine)) return 0;
        bool pro = IsPro(mine.Name), known = false;
        int n = 1;
        var drivers = session.Drivers;
        for (int i = 0; i < drivers.Count; i++)
        {
            var d = drivers[i];
            if (d.CarIdx == me || (uint)d.CarIdx >= (uint)classPositions.Length || d.ClassId != mine.ClassId) continue;
            int p = classPositions[d.CarIdx];
            if (p <= 0) continue;
            known = true;
            if (IsPro(d.Name) == pro && p < myClassPos) n++;
        }
        return known ? n : 0;
    }

    public static int TierPosition(SessionInfo session, int me, int myClassPos, IReadOnlyList<ResultEntry> results)
    {
        if (myClassPos <= 0 || !session.ByCar.TryGetValue(me, out var mine)) return 0;
        bool pro = IsPro(mine.Name), known = false;
        int n = 1;
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (r.CarIdx == me || r.ClassPosition <= 0 || !session.ByCar.TryGetValue(r.CarIdx, out var d) || d.ClassId != mine.ClassId) continue;
            known = true;
            if (IsPro(d.Name) == pro && r.ClassPosition < myClassPos) n++;
        }
        return known ? n : 0;
    }

    public static int[] Positions(IReadOnlyList<StandingRow> standings, SessionInfo session, int[] into, Dictionary<(int Class, bool Pro), int> counts)
    {
        Array.Clear(into);
        counts.Clear();
        foreach (var s in standings)
        {
            if ((uint)s.CarIdx >= (uint)into.Length || !session.ByCar.TryGetValue(s.CarIdx, out var d)) continue;
            var key = (d.ClassId, IsPro(d.Name));
            int n = counts.GetValueOrDefault(key) + 1;
            counts[key] = n;
            into[s.CarIdx] = n;
        }
        return into;
    }
}
