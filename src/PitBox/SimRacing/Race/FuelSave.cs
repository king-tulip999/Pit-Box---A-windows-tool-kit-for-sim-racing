namespace PitBox.SimRacing.Race;

internal readonly record struct FuelSaveTarget(int StopsNow, double PerLap, double Target, bool Proven)
{
    public double Save => PerLap - Target;
    public double Share => PerLap > 0 ? Save / PerLap : 0;
    public bool Spare => StopsNow == 0;
}

internal static class FuelSave
{
    public static FuelSaveTarget? Target(FuelPlan? plan, double fuel, double? tank, double? leastOnOneLap)
    {
        if (plan is not { LapsToGo: { } togo and > 0.5, ToFinish: { } need, Stops: { } stops } || !(plan.PerLap > 0)) return null;
        double reserve = Math.Max(0, need - togo * plan.PerLap);
        double available;
        if (stops == 0) available = fuel;
        else if (tank is > 0 and var t) available = fuel + (stops - 1) * t;
        else return null;
        double target = (available - reserve) / togo;
        if (!(target > 0) || !double.IsFinite(target)) return null;
        return new FuelSaveTarget(stops, plan.PerLap, target, leastOnOneLap is { } least && least <= target);
    }
}
