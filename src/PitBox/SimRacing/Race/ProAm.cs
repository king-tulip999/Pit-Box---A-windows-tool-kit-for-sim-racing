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
