using System.Globalization;
using Core.Config;
using Units = Core.Services.CapsUnits;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Race;

internal static class RaceFormat
{
    public const string Missing = "–";

    public static string About(string value) => PooledText.Concat("≈", value);

    public static string Count(long n, string word)
    {
        var t = new PooledBuilder(stackalloc char[PooledText.Room]);
        t.Add(n, "N0", Inv);
        t.Add(' ');
        t.Add(word);
        if (n != 1) t.Add(PluralEnding(word));
        return t.Done();
    }

    public static string Plural(string word) => word + PluralEnding(word);

    private static string PluralEnding(string word) =>
        word.EndsWith("s", StringComparison.Ordinal) || word.EndsWith("x", StringComparison.Ordinal) || word.EndsWith("ch", StringComparison.Ordinal) || word.EndsWith("sh", StringComparison.Ordinal) ? "es" : "s";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly string[] SecondFormats = { "0.0", "0.00", "0.000" }, WideSecondFormats = { "00.0", "00.00", "00.000" };

    public static string LapTime(double? seconds, int decimals = 3)
    {
        if (seconds is not { } s || s <= 0 || !double.IsFinite(s)) return Missing;
        decimals = Math.Clamp(decimals, 1, 3);
        double r = Math.Round(s, decimals);
        int min = (int)(r / 60);
        double rest = Math.Max(0, r - min * 60);
        if (min <= 0) return PooledText.Number("", rest, SecondFormats[decimals - 1], Inv);
        var t = new PooledBuilder(stackalloc char[PooledText.Room]);
        t.Add(min);
        t.Add(':');
        t.Add(rest, WideSecondFormats[decimals - 1], Inv);
        return t.Done();
    }

    public static string Gap(double? seconds, int decimals = 1)
    {
        if (seconds is not { } s || !double.IsFinite(s)) return Missing;
        string format = decimals == 2 ? "0.00" : decimals == 3 ? "0.000" : "0.0";
        Span<char> buffer = stackalloc char[64];
        if (!Math.Abs(s).TryFormat(buffer[1..], out int written, format, Inv))
        {
            string v = Math.Abs(s).ToString(format, Inv);
            if (double.Parse(v, Inv) == 0) return v;
            return (s > 0 ? "+" : "−") + v;
        }
        var digits = buffer.Slice(1, written);
        if (double.Parse(digits, Inv) == 0) return PooledText.Of(digits);
        buffer[0] = s > 0 ? '+' : '−';
        return PooledText.Of(buffer[..(written + 1)]);
    }

    public static string Signed(double value, string format, IFormatProvider? culture = null)
    {
        string magnitude = Math.Abs(value).ToString(format, culture ?? Inv);
        return magnitude.Any(c => c is >= '1' and <= '9') ? (value < 0 ? "−" : "+") + magnitude : magnitude;
    }

    private static string AbsFormat(int decimals) => decimals switch { <= 0 => "0", 2 => "0.00", >= 3 => "0.000", _ => "0.0" };

    public static string Abs(double? seconds, int decimals = 1) =>
        seconds is { } s && double.IsFinite(s) ? PooledText.Number("", Math.Abs(s), AbsFormat(decimals), Inv) : Missing;

    public static string Plus(double? seconds, int decimals = 1) =>
        seconds is { } s && double.IsFinite(s) ? PooledText.Number("+", Math.Abs(s), AbsFormat(decimals), Inv) : "+" + Missing;

    public static string LapTime(double? seconds, string format)
    {
        if (seconds is not { } s || s <= 0 || !double.IsFinite(s)) return Missing;
        int decimals = format switch { "mixed" or "smixed" => 1, "minutes" or "s" => 0, _ => 3 };
        if (format is "sfull" or "smixed" or "s")
        {
            double r = Math.Round(s, decimals);
            return PooledText.Number("", r - Math.Floor(r / 60) * 60, decimals switch { 0 => "0", 1 => "0.0", _ => "0.000" }, Inv);
        }
        if (decimals == 0)
        {
            int whole = (int)Math.Round(s);
            var t = new PooledBuilder(stackalloc char[PooledText.Room]);
            t.Add(whole / 60);
            t.Add(':');
            t.Add(whole % 60, "00", CultureInfo.CurrentCulture);
            return t.Done();
        }
        return LapTime(s, decimals);
    }

    private const int Remembered = 1024;

    [ThreadStatic] private static Dictionary<(string Name, string Format, bool NoDigits), string>? _names;

    public static string Name(string name, string format, bool noDigits = false)
    {
        var names = _names ??= new();
        var key = (name, format, noDigits);
        if (names.TryGetValue(key, out var known)) return known;
        if (names.Count >= Remembered) names.Clear();
        return names[key] = FormatName(name, format, noDigits);
    }

    private static string FormatName(string name, string format, bool noDigits)
    {
        name = ProAm.Strip(name);
        if (noDigits)
        {
            var kept = new System.Text.StringBuilder(name.Length);
            foreach (char c in name) if (!char.IsDigit(c)) kept.Append(c);
            name = string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return name;
        string first = words[0], last = string.Join(' ', words[1..]);
        return format switch
        {
            "initial" => $"{first[0]}. {last}",
            "last" => last,
            "first" => $"{first} {last[0]}.",
            _ => name,
        };
    }

    public static string Delta(double? seconds) => Gap(seconds, seconds is { } s && Math.Abs(s) < 10 ? 3 : 1);

    [ThreadStatic] private static long _clockMinute;
    [ThreadStatic] private static string? _clockText;

    public static string TimeOfDay(DateTime t)
    {
        long minute = t.Ticks / TimeSpan.TicksPerMinute;
        if (_clockText is not null && minute == _clockMinute) return _clockText;
        _clockMinute = minute;
        return _clockText = t.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string Clock(double? seconds)
    {
        if (seconds is not { } s || s < 0 || !double.IsFinite(s)) return Missing;
        var span = TimeSpan.FromSeconds(Math.Floor(s));
        var current = CultureInfo.CurrentCulture;
        var t = new PooledBuilder(stackalloc char[PooledText.Room]);
        if (span.TotalHours >= 1)
        {
            t.Add((int)span.TotalHours);
            t.Add(':');
            t.Add(span.Minutes, "00", current);
        }
        else t.Add(span.Minutes);
        t.Add(':');
        t.Add(span.Seconds, "00", current);
        return t.Done();
    }

    public static string Speed(double metresPerSecond, HudUnits u) =>
        u == HudUnits.Imperial ? PooledText.Number("", metresPerSecond * 2.23694, "0", CultureInfo.CurrentCulture, " " + Units.Mph)
            : PooledText.Number("", metresPerSecond * 3.6, "0", CultureInfo.CurrentCulture, " " + Units.Kmh);

    public static string SpeedKph(double kph, HudUnits u) =>
        u == HudUnits.Imperial ? PooledText.Number("", kph / 1.609344, "0", CultureInfo.CurrentCulture, " " + Units.Mph)
            : PooledText.Number("", kph, "0", CultureInfo.CurrentCulture, " " + Units.Kmh);

    public static double SpeedValue(double metresPerSecond, HudUnits u) => u == HudUnits.Imperial ? metresPerSecond * 2.23694 : metresPerSecond * 3.6;

    public static string SpeedUnit(HudUnits u) => u == HudUnits.Imperial ? Units.Mph : Units.Kmh;

    public static double FuelValue(double litres, HudUnits u) => u == HudUnits.Imperial ? litres / 3.785411784 : litres;

    public static string FuelUnit(HudUnits u) => u == HudUnits.Imperial ? Units.Gal : Units.L;

    public static string Fuel(double? litres, HudUnits u)
    {
        if (litres is not { } l || !double.IsFinite(l)) return Missing;
        double v = FuelValue(l, u);
        return PooledText.Number("", v, "0.00", Inv, u == HudUnits.Imperial ? " " + Units.Gal : " " + Units.L);
    }

    public static string Temp(double? celsius, HudUnits u)
    {
        if (celsius is not { } c || !double.IsFinite(c)) return Missing;
        return u == HudUnits.Imperial ? PooledText.Number("", c * 9 / 5 + 32, "0", CultureInfo.CurrentCulture, "°F")
            : PooledText.Number("", c, "0", CultureInfo.CurrentCulture, "°C");
    }

    public static string TempDelta(double celsius, HudUnits u) =>
        u == HudUnits.Imperial ? PooledText.Number("", celsius * 9 / 5, "0.0", Inv, "°F") : PooledText.Number("", celsius, "0.0", Inv, "°C");

    public static string Spread(double? seconds) =>
        seconds is { } s && double.IsFinite(s) && s >= 0 ? PooledText.Number("±", s / 2, s / 2 < 10 ? "0.00" : "0.0", Inv) : Missing;

    public static string Pressure(double? kpa, HudUnits u)
    {
        if (kpa is not { } p || !double.IsFinite(p) || p <= 0) return Missing;
        return u == HudUnits.Imperial ? PooledText.Number("", p * 0.145038, "0.0", CultureInfo.CurrentCulture, " " + Units.Psi)
            : PooledText.Number("", p, "0", CultureInfo.CurrentCulture, " " + Units.KPa);
    }

    public static string Distance(double metres, HudUnits u) =>
        u == HudUnits.Imperial ? PooledText.Number("", metres * 3.28084, "0", CultureInfo.CurrentCulture, " " + Units.Ft)
            : PooledText.Number("", metres, "0", CultureInfo.CurrentCulture, " " + Units.M);

    public static string Gear(int? gear) => gear switch { null => Missing, -1 => "R", 0 => "N", { } g => g.ToString(Inv) };

    public static string Warnings(EngineWarnings w)
    {
        var parts = new List<string>();
        if (w.HasFlag(EngineWarnings.WaterTemp)) parts.Add("Water temp");
        if (w.HasFlag(EngineWarnings.OilTemp)) parts.Add("Oil temp");
        if (w.HasFlag(EngineWarnings.OilPressure)) parts.Add("Oil pressure");
        if (w.HasFlag(EngineWarnings.FuelPressure)) parts.Add("Fuel pressure");
        if (w.HasFlag(EngineWarnings.Stalled)) parts.Add("Stalled");
        return string.Join(" · ", parts);
    }

    public static string Wetness(int? w) => w switch
    {
        null or <= 0 => Missing,
        1 => "Dry",
        2 => "Mostly dry",
        3 => "Very lightly wet",
        4 => "Lightly wet",
        5 => "Moderately wet",
        6 => "Very wet",
        _ => "Extremely wet",
    };

    public static string Pos(int position) => position > 0 ? PooledText.Number("P", position) : Missing;

    private static readonly string[] CompassPoints = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public static string Compass(double? radians)
    {
        if (radians is not { } r || !double.IsFinite(r)) return Missing;
        double deg = (r * 180 / Math.PI % 360 + 360) % 360;
        return CompassPoints[(int)Math.Round(deg / 45) % 8];
    }

    public static string License(string lic)
    {
        if (!lic.StartsWith("Rookie", StringComparison.OrdinalIgnoreCase)) return lic;
        var t = new PooledBuilder(stackalloc char[PooledText.Room]);
        t.Add('R');
        t.AddSpan(lic.AsSpan(6));
        return t.Done();
    }

    public static string IRating(int? ir) => ir is not { } v ? Missing
        : v >= 1000 ? PooledText.Number("", v / 1000.0, "0.0", Inv, "k")
        : v >= 0 ? PooledText.Number("", v) : v.ToString(Inv);
}
