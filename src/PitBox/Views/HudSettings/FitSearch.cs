using PitBox.Config;

namespace PitBox.Views.HudSettings;

internal static class FitSearch
{
    public static double Limit(double size) => Math.Min(HudWidgetConfig.MaxSize, Math.Round(size * 2));

    public static double Smallest(double from, double to, Func<double, bool> fits)
    {
        double lo = from, hi = to;
        while (hi - lo > 1)
        {
            double mid = Math.Round((lo + hi) / 2);
            if (mid <= lo || mid >= hi) break;
            if (fits(mid)) hi = mid; else lo = mid;
        }
        return Math.Ceiling(hi);
    }
}
