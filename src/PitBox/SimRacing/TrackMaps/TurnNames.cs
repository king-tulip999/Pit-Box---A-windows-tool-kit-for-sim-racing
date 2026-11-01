using System.Globalization;

namespace PitBox.SimRacing.TrackMaps;

internal sealed class TurnNames
{
    public static readonly TurnNames None = new(Array.Empty<(double, string)>(), null);

    private readonly (double Pct, string Name)[] _turns;
    private readonly double? _lengthKm;

    public TurnNames(IEnumerable<(double Pct, string Text)> turns, double? lengthKm)
    {
        _turns = turns.Where(t => double.IsFinite(t.Pct) && t.Text.Trim().Length > 0).Select(t => (t.Pct - Math.Floor(t.Pct), Label(t.Text))).OrderBy(t => t.Item1).ToArray();
        _lengthKm = lengthKm is > 0 ? lengthKm : null;
    }

    public static TurnNames From(TrackMap map, double? lengthKm) => new(map.Turns.Select(t => (map.PctOf(t.At), t.Text)), lengthKm);

    public bool HasTurns => _turns.Length > 0;

    public IReadOnlyList<(double Pct, string Name)> All => _turns;

    public static string Label(string text)
    {
        string t = text.Trim();
        return t.Length > 0 && t.All(char.IsAsciiDigit) ? "T" + t : t;
    }

    public string Where(double pct, bool caps = false)
    {
        if (!double.IsFinite(pct) || pct < 0) return Race.RaceFormat.Missing;
        pct -= Math.Floor(pct);
        if (_turns.Length > 0)
        {
            string name = _turns[0].Name;
            double best = double.MaxValue;
            foreach (var (at, n) in _turns)
            {
                double d = Math.Abs(at - pct);
                d = Math.Min(d, 1 - d);
                if (d < best) { best = d; name = n; }
            }
            return name;
        }
        if (_lengthKm is { } km)
        {
            double at = pct * km;
            return at < 1 ? $"{at * 1000:0} {(caps ? Services.CapsUnits.M : Services.Units.M)}"
                : at.ToString("0.0", caps ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture) + " " + (caps ? Services.CapsUnits.Km : Services.Units.Km);
        }
        return $"{pct * 100:0}%";
    }
}
