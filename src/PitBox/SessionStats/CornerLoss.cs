namespace PitBox.SessionStats;

internal sealed record CornerTime(double Apex, double From, double To, double Lost, double? BrakeEarlier, double? ApexSpeed, double? ThrottleLater);

internal static class CornerLoss
{
    public const double LeastLoss = 0.02, BrakeOn = 0.15, BrakeOff = 0.05, BrakeQuietM = 30, ThrottleOn = 0.98, ApexWindowM = 60;

    public static List<CornerTime> Corners(LapTrace lap, LapTrace reference)
    {
        var result = new List<CornerTime>();
        if (reference[TraceChannel.Speed] is not { } refSpeed || lap[TraceChannel.Speed] is not { } speed) return result;
        double length = Math.Min(lap.LengthM, reference.LengthM);
        var apexes = LapReplay.Corners(refSpeed, reference.StepM).Where(a => a < length).ToList();
        if (apexes.Count == 0) return result;
        for (int k = 0; k < apexes.Count; k++)
        {
            double from = k == 0 ? 0 : (apexes[k - 1] + apexes[k]) / 2, to = k + 1 < apexes.Count ? (apexes[k] + apexes[k + 1]) / 2 : length;
            double lost = LapReplay.Gap(lap, reference, to).Seconds - LapReplay.Gap(lap, reference, from).Seconds;
            double apex = apexes[k];
            double? brake = BrakeAt(lap, from, apex) is { } mine && BrakeAt(reference, from, apex) is { } theirs ? theirs - mine : null;
            double? minSpeed = Lowest(speed, lap.StepM, apex) - Lowest(refSpeed, reference.StepM, apex);
            double? throttle = ThrottleAt(lap, apex, to) is { } m2 && ThrottleAt(reference, apex, to) is { } t2 ? m2 - t2 : null;
            result.Add(new CornerTime(apex, from, to, lost, brake, minSpeed, throttle));
        }
        return result;
    }

    public static List<CornerTime> Worst(LapTrace lap, LapTrace reference, int most = 3) =>
        Corners(lap, reference).Where(c => c.Lost >= LeastLoss).OrderByDescending(c => c.Lost).Take(most).ToList();

    private static double? BrakeAt(LapTrace t, double from, double apex)
    {
        if (t[TraceChannel.Brake] is not { } brake) return null;
        double? found = null;
        foreach (double d in LapReplay.Onsets(brake, t.StepM, BrakeOn, BrakeOff, BrakeQuietM))
            if (d >= from && d <= apex) found = d;
        return found;
    }

    private static double? ThrottleAt(LapTrace t, double apex, double to)
    {
        if (t[TraceChannel.Throttle] is not { } throttle) return null;
        int a = Math.Max(0, (int)(apex / t.StepM)), b = Math.Min(throttle.Length - 1, (int)(to / t.StepM));
        for (int i = a; i <= b; i++) if (throttle[i] >= ThrottleOn) return i * t.StepM;
        return null;
    }

    private static double Lowest(double[] speed, double step, double apex)
    {
        int a = Math.Max(0, (int)((apex - ApexWindowM) / step)), b = Math.Min(speed.Length - 1, (int)((apex + ApexWindowM) / step));
        double low = double.MaxValue;
        for (int i = a; i <= b; i++) low = Math.Min(low, speed[i]);
        return low == double.MaxValue ? 0 : low;
    }
}
