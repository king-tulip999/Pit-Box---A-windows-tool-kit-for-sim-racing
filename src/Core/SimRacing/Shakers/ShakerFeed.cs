using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Shakers;

internal struct ShakerLevels
{
    public bool Active;
    public double EngineFraction, EngineLoad;
    public int Shifts;
    public bool Abs, Limiter;
    public double Road;
}

internal struct ShakerInput
{
    public bool Driving;
    public double? Rpm, Throttle, VertAccel, Time, IdleRpm, Redline;
    public int? Gear;
    public bool Abs, Limiter;
}

internal sealed class ShakerFeed
{
    public const double RoadFullScale = 10.1;
    public const double RoadSmoothingSeconds = 0.25;

    private double _vertAvg = double.NaN, _lastTime = double.NaN;
    private int? _gear;
    private ShakerLevels _levels;

    public ShakerLevels Levels => _levels;

    public void Reset()
    {
        _vertAvg = _lastTime = double.NaN;
        _gear = null;
        _levels = default;
    }

    public ShakerLevels Update(TelemetryFrame f) =>
        Update(f.Driving, f.Rpm, f.Throttle, f.Gear, f.AbsActive == true, (f.Warnings & EngineWarnings.RevLimiter) != 0,
            f.RawValue("VertAccel"), f.SessionTime, f.Session.IdleRpm, f.Session.RedlineRpm);

    public ShakerLevels Update(in ShakerInput i) =>
        Update(i.Driving, i.Rpm, i.Throttle, i.Gear, i.Abs, i.Limiter, i.VertAccel, i.Time, i.IdleRpm, i.Redline);

    internal ShakerLevels Update(bool driving, double? rpm, double? throttle, int? gear, bool abs, bool limiterFlag, double? vertAccel, double? time, double? idleRpm, double? redline)
    {
        if (!driving)
        {
            int shifts = _levels.Shifts;
            Reset();
            _levels.Shifts = shifts;
            return _levels;
        }
        _levels.Active = true;
        if (rpm is { } r && redline is { } red && red > 0)
        {
            double idle = idleRpm is { } i && i < red ? i : 0;
            _levels.EngineFraction = Math.Clamp((r - idle) / (red - idle), 0, 1);
            _levels.EngineLoad = r > 1 ? Math.Clamp(throttle ?? 0, 0, 1) : 0;
            _levels.Limiter = limiterFlag || r >= red;
        }
        else
        {
            _levels.EngineFraction = _levels.EngineLoad = 0;
            _levels.Limiter = limiterFlag;
        }
        if (gear is { } g)
        {
            if (_gear is { } before && before != g) _levels.Shifts++;
            _gear = g;
        }
        _levels.Abs = abs;
        if (vertAccel is { } a && double.IsFinite(a) && time is { } t)
        {
            double dt = double.IsNaN(_lastTime) ? 0 : Math.Clamp(t - _lastTime, 0, 1);
            _lastTime = t;
            _vertAvg = double.IsNaN(_vertAvg) ? a : _vertAvg + (a - _vertAvg) * (1 - Math.Exp(-dt / RoadSmoothingSeconds));
            _levels.Road = Math.Clamp(Math.Abs(a - _vertAvg) / RoadFullScale, 0, 1);
        }
        else _levels.Road = 0;
        return _levels;
    }
}
