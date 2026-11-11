namespace Core.SimRacing.Race;

internal sealed class WeatherTrend
{
    public const double SampleSeconds = 30, WindowSeconds = 600, MinSpanSeconds = 300;

    private readonly List<(double T, double Track, double? Rain)> _samples = new();

    public void Clear() => _samples.Clear();

    public void Update(double sessionTime, double? trackTemp, double? rain)
    {
        if (trackTemp is not { } temp || !double.IsFinite(temp) || temp is < -50 or > 90) return;
        if (_samples.Count > 0 && sessionTime < _samples[^1].T) _samples.Clear();
        if (_samples.Count > 0 && sessionTime - _samples[^1].T < SampleSeconds) return;
        _samples.Add((sessionTime, temp, rain is { } r && double.IsFinite(r) ? r : null));
        while (_samples.Count > 0 && sessionTime - _samples[0].T > WindowSeconds) _samples.RemoveAt(0);
    }

    public double Span => _samples.Count < 2 ? 0 : _samples[^1].T - _samples[0].T;

    public double? TrackPerWindow => Slope(s => s.Track);

    private double? Slope(Func<(double T, double Track, double? Rain), double> value)
    {
        if (_samples.Count < 3 || Span < MinSpanSeconds) return null;
        double mt = _samples.Average(s => s.T), mv = _samples.Average(value);
        double num = 0, den = 0;
        foreach (var s in _samples)
        {
            num += (s.T - mt) * (value(s) - mv);
            den += (s.T - mt) * (s.T - mt);
        }
        return den > 0 ? num / den * WindowSeconds : null;
    }
}
