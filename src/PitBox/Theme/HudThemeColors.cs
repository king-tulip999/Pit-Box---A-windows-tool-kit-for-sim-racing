using System.Windows.Media;
using PitBox.Config;

namespace PitBox.Theme;

internal static class HudThemeColors
{
    public static HudCustomColors From(ThemeSpec spec)
    {
        var p = ThemeManager.Resolve(spec);
        var defaults = new HudCustomColors();
        string H(string key) => Rgb(p[key]);
        return new HudCustomColors
        {
            Background = H("Bg"), Surface = H("TileRaised"), Lines = H("Line"),
            Text = H("Text"), Heading = H("Heading"), Muted = H("TextMuted"),
            Accent = H("AccentText"), YourRow = H("Accent"), YourRowText = Rgb(OnRow(p["Accent"], p["OnAccent"])),
            Good = H("Sage"), Warn = H("Warn"), Danger = H("Danger"),
            FastestLap = defaults.FastestLap, Lapping = defaults.Lapping, Lapped = defaults.Lapped,
            MapTrack = H("LineStrong"), MapEdge = H("TextFaint"), MapPit = H("Line"), MapCars = H("Text"),
        };
    }

    public static bool Same(HudCustomColors a, HudCustomColors b)
    {
        var other = b.All().ToDictionary(e => e.Id, e => e.Get());
        return a.All().All(e => string.Equals(e.Get(), other[e.Id], StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<(string Name, ThemeSpec Spec)> Choices()
    {
        yield return (Themes.Dark.Name, Themes.Dark);
        yield return (Themes.DefaultCustomName, Themes.Custom);
        foreach (var preset in CustomPalette.Presets)
            yield return (preset.Name, Themes.ForSet(AppTheme.Custom, preset.Name));
        foreach (var set in ThemeSets.All)
            foreach (var preset in set.Presets)
                yield return (preset.Name, Themes.ForSet(set.Id, preset.Name));
    }

    public static string? NameOf(HudCustomColors colors) =>
        Choices().FirstOrDefault(c => Same(From(c.Spec), colors)).Name;

    private static Color OnRow(Color row, Color onAccent)
    {
        if (ThemeManager.Contrast(onAccent, row) >= 4.5) return onAccent;
        var dark = ThemeManager.Mix(Colors.Black, row, 0.1);
        return ThemeManager.Contrast(dark, row) >= ThemeManager.Contrast(Colors.White, row) ? dark : Colors.White;
    }

    private static string Rgb(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
