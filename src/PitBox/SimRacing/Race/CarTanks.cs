using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Race;

internal static class CarTanks
{
    public static double? Usable(SessionInfo session) =>
        session.FuelMaxLiters is { } max ? max * (session.MaxFuelPct ?? 1) : null;
}
