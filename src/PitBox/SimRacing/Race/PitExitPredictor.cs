using System.IO;
using System.Text.Json;

namespace PitBox.SimRacing.Race;

internal sealed class PitExitFrame
{
    public double SessionTime;
    public int PlayerIdx;
    public int[] LapCompleted = new int[64];
    public float[] LapDistPct = new float[64];
    public float[] EstTime = new float[64];
    public bool[] OnPitRoad = new bool[64];
    public int[] TrackSurface = new int[64];
    public int[] ClassId = new int[64];
    public double ClassLapTime;
    public double FuelToAdd;
    public bool TiresPlanned;
    public double RepairTime;
    public float FuelLevel;
    public bool Caution;
}

internal readonly record struct ExitCar(int CarIdx, double Gap);

internal sealed class PitExitPrediction
{
    public bool Valid;
    public bool Calibrated;
    public bool Unreliable;
    public double PitLoss;
    public double Stationary;
    public int CurrentClassPos;
    public int ProjectedClassPos;
    public ExitCar? Ahead;
    public ExitCar? Behind;
    public float GhostPct;
    public List<ExitCar> Traffic = new();
}

internal sealed class PitExitPredictor
{
    const int Bins = 256;
    const double TrafficWindow = 1.5;

    const double FallbackLaneDelta = 25, FallbackFillRate = 2.5, FallbackTireExtra = 10, FallbackOppStop = 20;

    readonly double[] _curve = new double[Bins];
    readonly PitTrack[] _pit = new PitTrack[64];
    readonly PitLossStore _store;
    PitLossModel _m = new();
    double _lap;
    bool _seen;

    public PitExitPredictor(PitLossStore store)
    {
        _store = store;
        Array.Fill(_curve, double.NaN);
        for (int i = 0; i < 64; i++) _pit[i] = new PitTrack();
    }

    public void SetContext(int trackId, int carId)
    {
        _m = _store.Get($"{trackId}|{carId}");
        Array.Fill(_curve, double.NaN);
        foreach (var p in _pit) p.Active = false;
        _seen = false;
    }

    public PitExitPrediction Update(PitExitFrame f)
    {
        if ((uint)f.PlayerIdx >= 64) return new PitExitPrediction();
        _lap = f.ClassLapTime;
        UpdateCurve(f);
        TrackStops(f);
        _seen = true;
        return Predict(f);
    }

    void UpdateCurve(PitExitFrame f)
    {
        int cls = f.ClassId[f.PlayerIdx];
        for (int i = 0; i < 64; i++)
        {
            if (f.ClassId[i] != cls || f.TrackSurface[i] != 3 || f.OnPitRoad[i]) continue;
            float pct = f.LapDistPct[i], t = f.EstTime[i];
            if (pct < 0 || t <= 0) continue;
            int b = Math.Min(Bins - 1, (int)(pct * Bins));
            _curve[b] = double.IsNaN(_curve[b]) ? t : _curve[b] * 0.9 + t * 0.1;
        }
    }

    internal double T(double pct)
    {
        double x = pct * Bins - 0.5;
        int a = (int)Math.Floor(x), b = a + 1;
        double fa = Bin(a), fb = Bin(b);
        return fa + (fb - fa) * (x - a);
    }

    double Bin(int b)
    {
        double off = b < 0 ? -_lap : b >= Bins ? _lap : 0;
        int w = ((b % Bins) + Bins) % Bins;
        double v = _curve[w];
        return (double.IsNaN(v) ? (w + 0.5) / Bins * _lap : v) + off;
    }

    internal double Span(double fromPct, double toPct)
    {
        double d = T(toPct) - T(fromPct);
        return d < 0 ? d + _lap : d;
    }

    internal float PctAt(double t)
    {
        t = ((t % _lap) + _lap) % _lap;
        double lo = 0, hi = 1;
        for (int k = 0; k < 20; k++)
        {
            double mid = (lo + hi) / 2;
            if (T(mid) < t) lo = mid; else hi = mid;
        }
        return (float)lo;
    }

    void TrackStops(PitExitFrame f)
    {
        for (int i = 0; i < 64; i++)
        {
            var p = _pit[i];
            bool onRoad = f.OnPitRoad[i];
            bool inStall = f.TrackSurface[i] == 1;

            if (onRoad && !p.Active)
            {
                p.Active = true;
                p.Valid = !f.Caution && _seen && !inStall;
                p.EntryTime = f.SessionTime;
                p.EntryPct = f.LapDistPct[i];
                p.StallTotal = 0;
                p.StallStart = double.NaN;
                p.Fuel = f.FuelLevel;
                p.Tires = f.TiresPlanned;
                p.Repairs = f.RepairTime > 0;
            }
            if (!p.Active) continue;

            if (f.TrackSurface[i] == -1) p.Valid = false;
            if (inStall && double.IsNaN(p.StallStart)) p.StallStart = f.SessionTime;
            if (!inStall && !double.IsNaN(p.StallStart))
            {
                p.StallTotal += f.SessionTime - p.StallStart;
                p.StallStart = double.NaN;
            }

            if (!onRoad)
            {
                p.Active = false;
                if (p.Valid && !f.Caution && _lap > 0) Learn(f, i, p);
            }
        }
    }

    void Learn(PitExitFrame f, int i, PitTrack p)
    {
        double lane = (f.SessionTime - p.EntryTime) - p.StallTotal - Span(p.EntryPct, f.LapDistPct[i]);
        if (lane is > 0 and < 120) _m.LaneDelta = Blend(_m.LaneDelta, lane, ref _m.LaneSamples);

        if (i != f.PlayerIdx)
        {
            if (p.StallTotal > 2) _m.OppStop = Blend(_m.OppStop, p.StallTotal, ref _m.OppSamples);
        }
        else if (!p.Repairs && p.StallTotal > 1)
        {
            double added = f.FuelLevel - p.Fuel;
            if (!p.Tires && added > 0.5)
                _m.FillRate = Blend(_m.FillRate, added / p.StallTotal, ref _m.FuelSamples);
            else if (p.Tires && _m.FillRate is double rate)
                _m.TireExtra = Blend(_m.TireExtra, Math.Max(0, p.StallTotal - Math.Max(0, added) / rate), ref _m.TireSamples);
        }
        _store.Save();
    }

    static double Blend(double? old, double sample, ref int n)
    {
        n = Math.Min(n + 1, 5);
        return old is double o ? o + (sample - o) / n : sample;
    }

    PitExitPrediction Predict(PitExitFrame f)
    {
        var r = new PitExitPrediction();
        int me = f.PlayerIdx;
        if (_lap <= 0 || f.OnPitRoad[me] || f.TrackSurface[me] == -1 || f.LapDistPct[me] < 0) return r;

        double lane = _m.LaneDelta ?? FallbackLaneDelta;
        double fuelTime = f.FuelToAdd > 0 ? f.FuelToAdd / (_m.FillRate ?? FallbackFillRate) : 0;
        r.Stationary = fuelTime + (f.TiresPlanned ? _m.TireExtra ?? FallbackTireExtra : 0) + f.RepairTime;
        r.PitLoss = lane + r.Stationary;
        r.Calibrated = _m.LaneDelta != null && (f.FuelToAdd <= 0 || _m.FillRate != null) && (!f.TiresPlanned || _m.TireExtra != null);
        r.Unreliable = f.Caution;
        r.Valid = true;

        double myT = T(f.LapDistPct[me]);
        int cls = f.ClassId[me];
        r.CurrentClassPos = r.ProjectedClassPos = 1;

        for (int i = 0; i < 64; i++)
        {
            if (i == me || f.TrackSurface[i] == -1 || f.LapDistPct[i] < 0) continue;

            double gap = (f.LapCompleted[i] - f.LapCompleted[me]) * _lap + T(f.LapDistPct[i]) - myT;
            gap -= RemainingStopLoss(f, i, lane);
            double after = gap + r.PitLoss;

            if (f.ClassId[i] == cls)
            {
                if (gap > 0) r.CurrentClassPos++;
                if (after > 0)
                {
                    r.ProjectedClassPos++;
                    if (r.Ahead is not { } a || after < a.Gap) r.Ahead = new ExitCar(i, after);
                }
                else if (r.Behind is not { } b || after > b.Gap) r.Behind = new ExitCar(i, after);
            }

            double w = Wrap(after);
            if (Math.Abs(w) <= TrafficWindow) r.Traffic.Add(new ExitCar(i, w));
        }

        r.Traffic.Sort((x, y) => y.Gap.CompareTo(x.Gap));
        r.GhostPct = PctAt(myT - r.PitLoss);
        return r;
    }

    double RemainingStopLoss(PitExitFrame f, int i, double lane)
    {
        var p = _pit[i];
        if (!f.OnPitRoad[i] || !p.Active) return 0;
        double opp = _m.OppStop ?? FallbackOppStop;
        double stallNow = double.IsNaN(p.StallStart) ? 0 : f.SessionTime - p.StallStart;
        double stall = stallNow > 0 ? Math.Max(stallNow, opp)
                     : p.StallTotal > 0 ? p.StallTotal
                     : opp;
        double lostSoFar = (f.SessionTime - p.EntryTime) - Span(p.EntryPct, f.LapDistPct[i]);
        return Math.Max(0, lane + stall - lostSoFar);
    }

    double Wrap(double x)
    {
        x %= _lap;
        if (x > _lap / 2) x -= _lap;
        else if (x <= -_lap / 2) x += _lap;
        return x;
    }

    sealed class PitTrack
    {
        public bool Active, Valid, Tires, Repairs;
        public double EntryTime, EntryPct, StallStart, StallTotal;
        public float Fuel;
    }
}

internal sealed class PitLossModel
{
    public double? LaneDelta { get; set; }
    public double? FillRate { get; set; }
    public double? TireExtra { get; set; }
    public double? OppStop { get; set; }
    public int LaneSamples, FuelSamples, TireSamples, OppSamples;
}

internal sealed class PitLossStore
{
    static readonly JsonSerializerOptions Opts = new() { IncludeFields = true, WriteIndented = true };
    readonly string? _path;
    readonly Dictionary<string, PitLossModel> _models;

    public PitLossStore(string? path)
    {
        _path = path;
        try { _models = path is not null && File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, PitLossModel>>(File.ReadAllText(path), Opts) ?? new() : new(); }
        catch { _models = new(); }
    }

    public PitLossModel Get(string key) =>
        _models.TryGetValue(key, out var m) ? m : _models[key] = new PitLossModel();

    public void Save()
    {
        if (_path is null) return;
        string json = JsonSerializer.Serialize(_models, Opts);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
