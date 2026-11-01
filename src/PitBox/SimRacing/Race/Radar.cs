using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Race;

internal enum RadarLane { Left = -1, Inline = 0, Right = 1 }

internal readonly record struct RadarCar(int CarIdx, double Offset, RadarLane Lane);

internal sealed class RadarSlide
{
    public const double Seconds = 0.35;
    private const double ForgetAfter = 1;

    private readonly Dictionary<int, (double From, double To, double Start, double Seen)> _cars = new();
    private readonly List<int> _stale = new();

    public double Lateral(int car, RadarLane lane, double now, bool snap)
    {
        double to = (int)lane;
        if (snap) { _cars.Remove(car); return to; }
        if (!_cars.TryGetValue(car, out var s) || now < s.Start - 0.001 || now - s.Seen > ForgetAfter) s = (to, to, now, now);
        double at = s.From + (s.To - s.From) * RadarModel.Ease((now - s.Start) / Seconds);
        _cars[car] = to != s.To ? (at, to, now, now) : s with { Seen = now };
        return at;
    }

    public void Prune(double now)
    {
        if (_cars.Count < 16) return;
        _stale.Clear();
        foreach (var (car, s) in _cars) if (now - s.Seen > ForgetAfter) _stale.Add(car);
        foreach (var car in _stale) _cars.Remove(car);
    }
}

internal sealed class RadarModel
{
    public const int MinSamples = 5;
    private const int MaxSamples = 41;

    private readonly double[] _samples = new double[MaxSamples];
    private readonly double[] _sorted = new double[MaxSamples];
    private readonly List<RadarCar> _near = new();
    private int _count, _next;
    private CarSides _last = CarSides.Off;
    private string _car = "";

    public double? CarLength { get; private set; }

    public IReadOnlyList<RadarCar> Cars => _near;

    public void Reset()
    {
        _count = _next = 0;
        _last = CarSides.Off;
        CarLength = null;
    }

    public void Learn(TelemetryFrame f, string car)
    {
        if (car != _car)
        {
            Reset();
            _car = car;
        }
        var before = _last;
        _last = f.Sides;
        if (before != CarSides.Clear || f.Sides is not (CarSides.Left or CarSides.Right)) return;
        if (TrackMetres(f) is not { } metres || Nearest(f, metres) is not { } d) return;
        Add(d);
    }

    internal void Add(double distance)
    {
        if (!double.IsFinite(distance) || distance <= 0) return;
        _samples[_next] = distance;
        _next = (_next + 1) % MaxSamples;
        _count = Math.Min(MaxSamples, _count + 1);
        if (_count < MinSamples) return;
        Array.Copy(_samples, _sorted, _count);
        Array.Sort(_sorted, 0, _count);
        CarLength = _count % 2 == 1 ? _sorted[_count / 2] : (_sorted[_count / 2 - 1] + _sorted[_count / 2]) / 2;
    }

    public IReadOnlyList<RadarCar> Gather(TelemetryFrame f, double range)
    {
        _near.Clear();
        int me = f.PlayerCarIdx;
        if ((uint)me >= TelemetryFrame.MaxCars || f.OnPitRoad || TrackMetres(f) is not { } metres) return _near;
        double mine = f.CarLapDistPct[me];
        if (mine < 0) return _near;
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == me || f.CarSurface[i] != TrackSurface.OnTrack || f.CarOnPitRoad[i] || f.CarLapDistPct[i] < 0) continue;
            double offset = Offset(mine, f.CarLapDistPct[i], metres);
            if (Math.Abs(offset) <= range) _near.Add(new RadarCar(i, offset, RadarLane.Inline));
        }
        _near.Sort((a, b) => Math.Abs(a.Offset).CompareTo(Math.Abs(b.Offset)));
        Assign(_near, f.Sides);
        return _near;
    }

    public static double Offset(double mine, double theirs, double trackMetres)
    {
        double d = theirs - mine;
        if (d > 0.5) d -= 1;
        else if (d < -0.5) d += 1;
        return d * trackMetres;
    }

    public static int Alongside(CarSides s) => s switch
    {
        CarSides.Left or CarSides.Right => 1,
        CarSides.LeftRight or CarSides.TwoLeft or CarSides.TwoRight => 2,
        _ => 0,
    };

    internal static void Assign(List<RadarCar> nearestFirst, CarSides sides)
    {
        int n = Math.Min(Alongside(sides), nearestFirst.Count);
        for (int k = 0; k < n; k++)
        {
            var lane = sides switch
            {
                CarSides.Left or CarSides.TwoLeft => RadarLane.Left,
                CarSides.Right or CarSides.TwoRight => RadarLane.Right,
                _ => k == 0 ? RadarLane.Left : RadarLane.Right,
            };
            nearestFirst[k] = nearestFirst[k] with { Lane = lane };
        }
    }

    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }

    public static void Split(IReadOnlyList<(double Offset, double Lateral)> cars, double carLength, Span<double> shift, Span<double> width)
    {
        for (int i = 0; i < cars.Count; i++) { shift[i] = 0; width[i] = 1; }
        for (int i = 0; i < cars.Count; i++)
        {
            if (Math.Abs(cars[i].Lateral) >= 0.5) continue;
            for (int j = i + 1; j < cars.Count; j++)
            {
                if (Math.Abs(cars[j].Lateral) >= 0.5) continue;
                double apart = Math.Clamp(carLength + SplitMetres - Math.Abs(cars[i].Offset - cars[j].Offset), 0, SplitMetres) / SplitMetres;
                if (apart <= 0) continue;
                if (apart >= Math.Abs(shift[i])) shift[i] = -apart;
                if (apart >= Math.Abs(shift[j])) shift[j] = apart;
                width[i] = Math.Min(width[i], 1 - 0.5 * apart);
                width[j] = Math.Min(width[j], 1 - 0.5 * apart);
            }
        }
    }

    public const double SplitMetres = 1;

    private static double? TrackMetres(TelemetryFrame f) => f.Session.TrackLengthKm is > 0 and var km ? km * 1000 : null;

    private static double? Nearest(TelemetryFrame f, double metres)
    {
        int me = f.PlayerCarIdx;
        if ((uint)me >= TelemetryFrame.MaxCars || f.CarLapDistPct[me] < 0) return null;
        double? best = null;
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == me || f.CarSurface[i] != TrackSurface.OnTrack || f.CarLapDistPct[i] < 0) continue;
            double d = Math.Abs(Offset(f.CarLapDistPct[me], f.CarLapDistPct[i], metres));
            if (best is null || d < best) best = d;
        }
        return best;
    }
}
