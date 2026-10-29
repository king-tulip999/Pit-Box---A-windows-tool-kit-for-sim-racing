using System.Globalization;

namespace PitBox.Services;

internal static class Dates
{
    private static CultureInfo C => CultureInfo.CurrentCulture;

    public static string Date(DateTime t) => t.ToString("d", C);

    public static string Time(DateTime t) => t.ToString("t", C);

    public static string TimeWithSeconds(DateTime t) => t.ToString("T", C);

    public static string DateTime(DateTime t) => Date(t) + " · " + Time(t);

    public static string ShortPattern => C.DateTimeFormat.MonthDayPattern.Replace("MMMM", "MMM");

    public static string Short(DateTime t) => t.ToString(ShortPattern, C);

    public static string Day(DateTime t) => Day(t, System.DateTime.Now);

    public static string Day(DateTime t, DateTime now) => t.Year == now.Year ? Short(t) : Short(t) + " " + t.Year.ToString(C);

    public static string Weekday(DateTime t) => t.ToString("ddd", C);

    public static string ShortWithDay(DateTime t) => t.ToString("dddd", C) + " " + Short(t);

    public static string ShortTime(DateTime t) => Short(t) + " · " + Time(t);

    public static string MonthYear(DateTime t) => t.ToString(C.DateTimeFormat.YearMonthPattern, C);

    public static bool TwelveHour => C.DateTimeFormat.ShortTimePattern.Contains('h');

    public static string Hour(int hour) => new DateTime(2000, 1, 1, hour % 24, 0, 0).ToString(TwelveHour ? "h tt" : "HH:mm", C);

    public static string HourLabel(int hour) => TwelveHour ? Hour(hour) : (hour % 24).ToString("00", C);

    public static string ShortMonthYear(DateTime t) => t.ToString(C.DateTimeFormat.YearMonthPattern.Replace("MMMM", "MMM"), C);
}
