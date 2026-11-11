using System.Globalization;
using Core.Services;

namespace Core.SessionStats;

internal static class ConditionsText
{
    private static readonly CultureInfo C = CultureInfo.CurrentCulture;

    public static readonly string[] SkyNames = { "Clear", "Partly cloudy", "Mostly cloudy", "Overcast" };

    public static readonly string[] WetnessNames = { "", "Dry", "Mostly dry", "Very lightly wet", "Lightly wet", "Moderately wet", "Very wet", "Extremely wet" };

    private static readonly string[] CompassPoints = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    public const int FirstWet = 3;

    public static string Compass(double radians) =>
        CompassPoints[(int)Math.Round((radians % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) / (Math.PI / 4)) % 8];

    public static string Span(string lo, string hi, string unit) => lo == hi ? hi + unit : $"{lo}–{hi}{unit}";

    public static string? Temp(double? lo, double? hi, double? single, bool imperial)
    {
        string N(double v) => (imperial ? v * 9 / 5 + 32 : v).ToString("0", C);
        string unit = imperial ? " °F" : " °C";
        if (lo is { } a && hi is { } b) return Span(N(a), N(b), unit);
        return single is { } s ? N(s) + unit : null;
    }

    public static string? Wind(double? lo, double? hi, double? single, bool imperial)
    {
        string N(double kph) => (imperial ? kph * 0.621371 : kph).ToString("0", C);
        string unit = " " + (imperial ? Units.Mph : Units.Kmh);
        if (lo is { } a && hi is { } b) return Span(N(a), N(b), unit);
        return single is { } s ? N(s) + unit : null;
    }

    public static string? Humidity(double? lo, double? hi, double? single)
    {
        string N(double share) => (share * 100).ToString("0", C);
        if (lo is { } a && hi is { } b) return Span(N(a), N(b), "%");
        return single is { } s ? N(s) + "%" : null;
    }

    public static (string Value, string Note)? Surface(int? wettest, double? rainMax, double rainS, double driveS)
    {
        if (wettest is null && rainMax is null) return null;
        bool wet = wettest >= FirstWet || rainS > 0;
        if (!wet) return ("Dry", "whole race");
        string value = wettest is >= FirstWet and < 8 and var w ? WetnessNames[w] : "Wet";
        if (rainS <= 0 || driveS <= 0) return (value, "");
        double share = Math.Min(1, rainS / driveS);
        return (value, share >= 0.99 ? "whole race" : $"{share * 100:0}% of the race");
    }

    public static string Rubber(string rubber) => rubber.Length > 0 ? char.ToUpperInvariant(rubber[0]) + rubber[1..] : "";
}
