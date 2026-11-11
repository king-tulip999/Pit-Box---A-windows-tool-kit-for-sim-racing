using System.Numerics;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal sealed class PitExitFeed
{
    private const double Interval = 0.2;
    private const PitServices Tyres = PitServices.LeftFront | PitServices.RightFront | PitServices.LeftRear | PitServices.RightRear;

    private readonly PitExitPredictor _live;
    private PitExitPredictor? _simulated;
    private readonly PitExitFrame _frame = new();
    private (bool Simulated, int Track, int Car) _context = (false, int.MinValue, int.MinValue);
    private double _nextAt = double.NegativeInfinity;

    public PitExitFeed(PitLossStore store) => _live = new PitExitPredictor(store);

    public PitExitPrediction? Latest { get; private set; }

    public void Reset()
    {
        Latest = null;
        _nextAt = double.NegativeInfinity;
    }

    public void Update(TelemetryFrame f, FuelPlan? plan, double? tank)
    {
        if (f.Session.Player is not { } player || f.Session.TrackId is not { } track || f.SessionTime is not { } now || (uint)f.PlayerCarIdx >= TelemetryFrame.MaxCars)
        {
            Latest = null;
            return;
        }
        var predictor = f.Simulated ? _simulated ??= new PitExitPredictor(new PitLossStore(null)) : _live;
        var context = (f.Simulated, track, player.CarId);
        if (context != _context)
        {
            _context = context;
            predictor.SetContext(track, player.CarId);
            _nextAt = double.NegativeInfinity;
        }
        if (!f.OnPitRoad && now < _nextAt && now > _nextAt - Interval * 25) return;
        _nextAt = now + Interval;
        Fill(f, plan, tank, player);
        Latest = predictor.Update(_frame);
    }

    private void Fill(TelemetryFrame f, FuelPlan? plan, double? tank, DriverEntry player)
    {
        var x = _frame;
        x.SessionTime = f.SessionTime ?? 0;
        x.PlayerIdx = f.PlayerCarIdx;
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            bool racing = f.Session.ByCar.TryGetValue(i, out var d) && d.Races;
            x.LapCompleted[i] = f.CarLapCompleted[i];
            x.LapDistPct[i] = (float)f.CarLapDistPct[i];
            x.EstTime[i] = (float)f.CarEstTime[i];
            x.OnPitRoad[i] = f.CarOnPitRoad[i];
            x.TrackSurface[i] = racing ? (int)f.CarSurface[i] : -1;
            x.ClassId[i] = racing ? d!.ClassId : -1;
            x.FastRepairsUsed[i] = f.CarFastRepairsUsed[i];
            x.Penalty[i] = (f.CarFlags[i] & RaceFlags.Black) != 0;
            x.Meatball[i] = (f.CarFlags[i] & RaceFlags.Repair) != 0;
        }
        x.ClassLapTime = player.ClassEstLapTime ?? f.Session.PlayerEstLapTime ?? 0;
        x.FuelRequest = FuelRequest(f.PitFlags, f.PitFuel, plan);
        x.Tank = tank ?? 0;
        x.FuelPerLap = plan?.PerLap ?? 0;
        x.TireCount = BitOperations.PopCount((uint)(f.PitFlags & Tyres));
        x.RepairTime = RepairTime(f.PitRepairLeft, f.PitFlags, f.FastRepairsAvailable);
        x.ServiceActive = f.PitstopActive;
        x.FuelLevel = (float)(f.Fuel ?? 0);
        x.Caution = (f.Flags & (RaceFlags.Caution | RaceFlags.CautionWaving)) != 0;
    }

    internal static double FuelRequest(PitServices flags, double? menu, FuelPlan? plan)
    {
        if (menu is { } amount) return (flags & PitServices.Fuel) != 0 ? Math.Max(0, amount) : 0;
        return plan is { Stops: > 0, AddAtNextStop: { } add } && add > 0 ? add : 0;
    }

    internal static double RepairTime(double? required, PitServices flags, int? fastRepairs)
    {
        if (required is not > 0) return 0;
        return (flags & PitServices.FastRepair) != 0 && fastRepairs is > 0 ? 0 : required.Value;
    }
}
