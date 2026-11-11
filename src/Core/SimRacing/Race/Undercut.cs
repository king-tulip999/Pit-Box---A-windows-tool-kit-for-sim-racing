namespace Core.SimRacing.Race;

internal readonly record struct UndercutRival(int CarIdx, int ClassPosition, double Gap);

internal static class Undercut
{
    public const int Laps = 3, FreshLaps = 3, MinStintLaps = 6;

    public static double Needed(double gap, int laps) => Math.Abs(gap) / Math.Max(1, laps);

    public static int? FirstLap(double gap, double? tyreGain)
    {
        if (tyreGain is not { } gain) return null;
        for (int k = 1; k <= Laps; k++)
            if (Needed(gap, k) <= gain) return k;
        return null;
    }

    public static double? TyreGain(IReadOnlyList<LapRecord> laps, int stintStartLap)
    {
        var stint = new List<double>();
        foreach (var l in laps)
            if (l.Lap > stintStartLap + 1 && l.Clean && !l.PitLap && !l.Caution) stint.Add(l.Time);
        if (stint.Count < MinStintLaps) return null;
        double fresh = stint.Take(FreshLaps).Min();
        double recent = stint.Skip(stint.Count - FreshLaps).Average();
        return Math.Max(0, recent - fresh);
    }

    public static (UndercutRival? Ahead, UndercutRival? Behind) Rivals(IReadOnlyList<StandingRow> standings, int me, Func<int, int> classOf)
    {
        StandingRow? mine = null;
        foreach (var r in standings) if (r.CarIdx == me) mine = r;
        if (mine is not { ClassPosition: > 0, GapToClassLeader: { } myGap } m) return (null, null);
        int myClass = classOf(me);
        UndercutRival? ahead = null, behind = null;
        foreach (var r in standings)
        {
            if (r.CarIdx == me || classOf(r.CarIdx) != myClass || r.GapToClassLeader is not { } theirs || r.ClassLapsDown != m.ClassLapsDown) continue;
            if (r.ClassPosition == m.ClassPosition - 1) ahead = new UndercutRival(r.CarIdx, r.ClassPosition, myGap - theirs);
            else if (r.ClassPosition == m.ClassPosition + 1) behind = new UndercutRival(r.CarIdx, r.ClassPosition, myGap - theirs);
        }
        return (ahead, behind);
    }
}
