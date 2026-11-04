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
    public int[] FastRepairsUsed = new int[64];
    public bool[] Penalty = new bool[64];
    public bool[] Meatball = new bool[64];
    public double ClassLapTime;
    public double FuelRequest;
    public double Tank;
    public double FuelPerLap;
    public int TireCount;
    public double RepairTime;
    public bool ServiceActive;
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
    public double FuelAdded;
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
    internal const double TrafficWindow = 1.5;
    const double StillStep = 0.19, StillPct = 5e-7;

    const double FallbackLane = 25, FallbackTyreTime = 10, FallbackOppStop = 20;
    const double FallbackFuelRate = 2.5, FallbackFuelStart = 0.7, FallbackFuelTail = 0.45, FallbackSettle = 0.06;

    readonly double[] _curve = new double[Bins];
    readonly PitTrack[] _pit = new PitTrack[64];
    readonly double[] _lastStop = new double[64];
    readonly ServiceTrack _service = new();
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
        Array.Clear(_lastStop);
        _service.Reset();
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
        double now = f.SessionTime;
        for (int i = 0; i < 64; i++)
        {
            var p = _pit[i];
            bool onRoad = f.OnPitRoad[i];
            float pct = f.LapDistPct[i];

            if (onRoad && !p.Active)
            {
                p.Active = true;
                p.Seen = _seen && f.TrackSurface[i] != 1;
                p.Valid = p.Seen && !f.Caution;
                p.Irregular = false;
                p.FastRepairs = f.FastRepairsUsed[i];
                p.EntryTime = p.LastTime = now;
                p.EntryPct = p.LastPct = pct;
                p.Stopped = 0;
                p.Stopping = false;
                if (i == f.PlayerIdx) _service.Reset();
            }
            if (!p.Active) continue;

            if (f.TrackSurface[i] == -1) p.Valid = false;
            if (f.TrackSurface[i] == -1 || f.Penalty[i] || f.Meatball[i] || f.FastRepairsUsed[i] > p.FastRepairs) p.Irregular = true;
            if (now - p.LastTime >= StillStep)
            {
                double moved = Math.Abs(pct - p.LastPct);
                if (Math.Min(moved, 1 - moved) < StillPct)
                {
                    if (!p.Stopping)
                    {
                        p.Stopping = true;
                        p.StopFrom = p.LastTime;
                    }
                    p.StopTo = now;
                }
                else p.EndStop();
                p.LastTime = now;
                p.LastPct = pct;
            }
            if (i == f.PlayerIdx) _service.Sample(f);

            if (!onRoad)
            {
                p.EndStop();
                p.Active = false;
                bool sameClass = f.ClassId[i] == f.ClassId[f.PlayerIdx];
                bool learned = p.Valid && sameClass && !f.Caution && _lap > 0 && LearnLane(f, i, p);
                if (i == f.PlayerIdx) learned |= LearnService(p);
                else if (p.Seen && !p.Irregular && p.Stopped > 2)
                {
                    _lastStop[i] = p.Stopped;
                    if (sameClass)
                    {
                        _m.AddOppStop(p.Stopped);
                        learned = true;
                    }
                }
                if (learned) _store.Save();
            }
        }
    }

    bool LearnLane(PitExitFrame f, int i, PitTrack p)
    {
        double lane = (f.SessionTime - p.EntryTime) - p.Stopped - Span(p.EntryPct, f.LapDistPct[i]);
        if (lane is > 0 and < 120) _m.Lane = Blend(_m.Lane, lane, ref _m.LaneSamples);
        _m.EntryPct = p.EntryPct;
        return true;
    }

    bool LearnService(PitTrack p)
    {
        var s = _service;
        if (!s.SeenIdle || !s.Started || !s.Ended) return false;
        double service = s.End - s.Start;
        int n = s.Tyres;
        bool fuelled = s.RiseAmount > 0.3 && s.RiseTime > 1;

        if (fuelled) _m.FuelRate = Blend(_m.FuelRate, s.RiseAmount / s.RiseTime, ref _m.FuelSamples);
        if (s.Repair)
        {
            if (fuelled) _m.RepairParallel = s.RepairOverlap;
        }
        else if (fuelled && s.Segments == 1)
        {
            _m.FuelStart[n] = Blend(_m.FuelStart[n], s.FirstRise - s.Start, ref _m.FuelStartSamples[n]);
            double tail = s.End - s.LastRise;
            if (n == 0 || tail <= (_m.FuelTail ?? FallbackFuelTail) + 0.5)
            {
                _m.FuelTail = Blend(_m.FuelTail, tail, ref _m.TailSamples);
                if (n > 0) _m.TyreBound[n] = Math.Min(_m.TyreBound[n] ?? double.MaxValue, service);
            }
            else _m.TyreTime[n] = Blend(_m.TyreTime[n], service, ref _m.TyreSamples[n]);
        }
        else if (!fuelled && s.RiseAmount <= 0 && n > 0)
            _m.TyreTime[n] = Blend(_m.TyreTime[n], service, ref _m.TyreSamples[n]);

        double settle = p.Stopped - service;
        if (p.Valid && service > 0 && Math.Abs(settle) < 3) _m.Settle = Blend(_m.Settle, settle, ref _m.SettleSamples);
        return true;
    }

    static double Blend(double? old, double sample, ref int n)
    {
        n = Math.Min(n + 1, 5);
        return old is double o ? o + (sample - o) / n : sample;
    }

    internal double FuelToAdd(PitExitFrame f)
    {
        if (f.FuelRequest <= 0) return 0;
        if (f.Tank <= 0) return f.FuelRequest;
        float me = f.LapDistPct[f.PlayerIdx];
        double toEntry = _m.EntryPct is double e && me >= 0 ? ((e - me) % 1 + 1) % 1 : 0;
        double arrival = Math.Max(0, f.FuelLevel - f.FuelPerLap * toEntry);
        return Math.Min(f.FuelRequest, Math.Max(0, f.Tank - arrival));
    }

    PitExitPrediction Predict(PitExitFrame f)
    {
        var r = new PitExitPrediction();
        int me = f.PlayerIdx;
        if (_lap <= 0 || f.OnPitRoad[me] || f.TrackSurface[me] == -1 || f.LapDistPct[me] < 0) return r;

        double lane = _m.Lane ?? FallbackLane;
        double fuel = FuelToAdd(f);
        int n = Math.Clamp(f.TireCount, 0, 4);
        double fuelPart = fuel > 0
            ? (_m.FuelStart[n] ?? _m.FuelStart[0] ?? FallbackFuelStart) + fuel / (_m.FuelRate ?? FallbackFuelRate) + (_m.FuelTail ?? FallbackFuelTail)
            : 0;
        double tyrePart = n > 0 ? _m.TyreTime[n] ?? Math.Min(FallbackTyreTime, _m.TyreBound[n] ?? double.MaxValue) : 0;
        double service = Math.Max(fuelPart, tyrePart);
        bool serviced = service > 0;
        if (f.RepairTime > 0) service = _m.RepairParallel == true ? Math.Max(service, f.RepairTime) : service + f.RepairTime;

        bool fuelKnown = fuel <= 0 || (_m.FuelRate != null && _m.FuelStart[n] != null && _m.FuelTail != null);
        bool tyresKnown = n == 0 || _m.TyreTime[n] != null || (_m.TyreBound[n] is double bound && fuelKnown && fuelPart >= bound);
        bool repairKnown = f.RepairTime <= 0 || !serviced || _m.RepairParallel != null;

        r.FuelAdded = fuel;
        r.Stationary = service > 0 ? service + (_m.Settle ?? FallbackSettle) : 0;
        r.PitLoss = lane + r.Stationary;
        r.Calibrated = _m.Lane != null && fuelKnown && tyresKnown && repairKnown;
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
        double opp = ExpectedStop(i);
        double stop = p.Stopping ? Math.Max(p.Stopped + p.StopTo - p.StopFrom, opp)
                    : p.Stopped > 0 ? p.Stopped
                    : opp;
        double lostSoFar = (f.SessionTime - p.EntryTime) - Span(p.EntryPct, f.LapDistPct[i]);
        return Math.Max(0, lane + stop - lostSoFar);
    }

    internal double ExpectedStop(int i) =>
        _pit[i].Active && _pit[i].Irregular ? 0 : _lastStop[i] > 0 ? _lastStop[i] : _m.OppStop() ?? FallbackOppStop;

    double Wrap(double x)
    {
        x %= _lap;
        if (x > _lap / 2) x -= _lap;
        else if (x <= -_lap / 2) x += _lap;
        return x;
    }

    sealed class PitTrack
    {
        public bool Active, Seen, Valid, Irregular, Stopping;
        public int FastRepairs;
        public double EntryTime, EntryPct, LastTime, LastPct, StopFrom, StopTo, Stopped;

        public void EndStop()
        {
            if (!Stopping) return;
            Stopped += StopTo - StopFrom;
            Stopping = false;
        }
    }

    sealed class ServiceTrack
    {
        public bool SeenIdle, Started, Ended, Rising, Repair, RepairOverlap;
        public double Start, End, LastTime, FirstRise, LastRise, RiseTime, RiseAmount;
        public float LastLevel;
        public int Tyres, Segments;

        public void Reset()
        {
            SeenIdle = Started = Ended = Rising = Repair = RepairOverlap = false;
            Start = End = LastTime = FirstRise = LastRise = RiseTime = RiseAmount = 0;
            LastLevel = 0;
            Tyres = Segments = 0;
        }

        public void Sample(PitExitFrame f)
        {
            double now = f.SessionTime;
            float level = f.FuelLevel;
            if (!f.ServiceActive)
            {
                if (!Started) SeenIdle = true;
                else if (!Ended)
                {
                    Ended = true;
                    End = now;
                }
                return;
            }
            if (Ended) return;
            if (!Started)
            {
                Started = true;
                Start = now;
                Tyres = Math.Clamp(f.TireCount, 0, 4);
                Repair = f.RepairTime > 0;
            }
            else if (now > LastTime)
            {
                if (level > LastLevel + 1e-4f)
                {
                    if (!Rising && ++Segments == 1) FirstRise = LastTime;
                    Rising = true;
                    LastRise = now;
                    RiseTime += now - LastTime;
                    RiseAmount += level - LastLevel;
                    if (f.RepairTime > 0.5) RepairOverlap = true;
                }
                else Rising = false;
            }
            LastTime = now;
            LastLevel = level;
        }
    }
}

internal sealed class PitLossModel
{
    public const int Slots = 5;

    public double? Lane { get; set; }
    public double? EntryPct { get; set; }
    public double? FuelRate { get; set; }
    public double? FuelTail { get; set; }
    public double? Settle { get; set; }
    public bool? RepairParallel { get; set; }
    public double?[] FuelStart { get; set; } = new double?[Slots];
    public double?[] TyreTime { get; set; } = new double?[Slots];
    public double?[] TyreBound { get; set; } = new double?[Slots];
    public double[] OppRecent { get; set; } = [];
    public int LaneSamples, FuelSamples, TailSamples, SettleSamples;
    public int[] FuelStartSamples = new int[Slots], TyreSamples = new int[Slots];

    const int OppKept = 9;

    public double? OppStop()
    {
        if (OppRecent.Length == 0) return null;
        var sorted = OppRecent.Order().ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    public void AddOppStop(double stop) => OppRecent = [.. OppRecent.TakeLast(OppKept - 1), stop];

    public PitLossModel Sized()
    {
        OppRecent ??= [];
        FuelStart = Fit(FuelStart);
        TyreTime = Fit(TyreTime);
        TyreBound = Fit(TyreBound);
        FuelStartSamples = Fit(FuelStartSamples);
        TyreSamples = Fit(TyreSamples);
        return this;
    }

    static T[] Fit<T>(T[]? a)
    {
        if (a is { Length: Slots }) return a;
        var b = new T[Slots];
        if (a is not null) Array.Copy(a, b, Math.Min(a.Length, Slots));
        return b;
    }
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
        _models.TryGetValue(key, out var m) && m is not null ? m.Sized() : _models[key] = new PitLossModel();

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
