using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Race;

internal readonly record struct OffTrack(int Lap, double Pct, double SessionTime);

internal sealed class TrackLimits
{
    public const double SameExcursionSeconds = 2;
    public const int MaxKept = 500;

    private readonly List<OffTrack> _offs = new();
    private bool _off;
    private double? _backOnAt;
    private double _last = double.NegativeInfinity;

    public IReadOnlyList<OffTrack> Offs => _offs;

    public int Count => _offs.Count;

    public event Action<OffTrack>? Left;

    public void Clear()
    {
        _offs.Clear();
        Rearm();
        _last = double.NegativeInfinity;
    }

    private void Rearm()
    {
        _off = false;
        _backOnAt = null;
    }

    public void Update(double time, TrackSurface? surface, bool driving, bool onPitRoad, int lapCompleted, double? pct)
    {
        if (time < _last) Rearm();
        _last = time;
        if (!driving || onPitRoad || surface is null or TrackSurface.NotInWorld or TrackSurface.InPitStall)
        {
            Rearm();
            return;
        }
        bool off = surface == TrackSurface.OffTrack;
        if (off && !_off && (_backOnAt is not { } back || time - back >= SameExcursionSeconds))
        {
            var o = new OffTrack(lapCompleted + 1, pct is { } p && double.IsFinite(p) ? Math.Clamp(p, 0, 1) : -1, time);
            _offs.Add(o);
            if (_offs.Count > MaxKept) _offs.RemoveAt(0);
            Left?.Invoke(o);
        }
        if (!off && _off) _backOnAt = time;
        _off = off;
    }

    public int OnLap(int lap)
    {
        int n = 0;
        foreach (var o in _offs) if (o.Lap == lap) n++;
        return n;
    }

    public static List<(string Where, int Count)> Hotspots(IEnumerable<double> pcts, Func<double, string> name) =>
        pcts.Where(p => p >= 0).GroupBy(name).Select(g => (g.Key, g.Count())).OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
}
