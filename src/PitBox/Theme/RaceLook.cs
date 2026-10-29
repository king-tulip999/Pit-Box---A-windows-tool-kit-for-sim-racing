using System.Windows;
using System.Windows.Media;

namespace PitBox.Theme;

internal static class RaceLook
{
    public const int Places = 4;
    public const string FontPath = "./Assets/Fonts/Condensed/#Barlow Condensed";

    private static readonly Color[] DarkPlaces = { Color.FromRgb(0xFF, 0xB2, 0x24), Color.FromRgb(0xC9, 0xCE, 0xD6), Color.FromRgb(0xD9, 0x9A, 0x63) };
    private static readonly Color[] LightPlaces = { Color.FromRgb(0xB8, 0x86, 0x0B), Color.FromRgb(0x6E, 0x76, 0x81), Color.FromRgb(0xA0, 0x61, 0x2A) };

    public static string Key(int? finish) => finish is >= 1 and <= 3 ? "Place" + finish : "Place" + Places;

    public static void Install(ThemeSpec spec, IReadOnlyDictionary<string, Color> p, ResourceDictionary res)
    {
        var surface = p["Surface"];
        for (int n = 1; n <= Places; n++)
        {
            var fill = n < Places ? (spec.Dark ? DarkPlaces : LightPlaces)[n - 1] : p["Accent"];
            var text = n < Places ? fill : p["AccentText"];
            string key = "Place" + n;
            res[key] = Brush(fill);
            res[key + "Text"] = Brush(text);
            res[key + "Wash"] = Brush(ThemeManager.Mix(surface, fill, 0.13));
            res[key + "Faint"] = Brush(ThemeManager.Mix(surface, fill, 0.07));
            res[key + "Band"] = Brush(ThemeManager.Mix(surface, fill, 0.22));
            res[key + "Soft"] = Brush(ThemeManager.Mix(p["SurfaceAlt"], fill, 0.1));
            res[key + "Edge"] = Brush(ThemeManager.Mix(surface, fill, 0.55));
            res[key + "Fade"] = Freeze(new LinearGradientBrush(ThemeManager.Mix(surface, fill, 0.14), Color.FromArgb(0, surface.R, surface.G, surface.B), new Point(0, 0.5), new Point(0.45, 0.5)));
        }
        res["RaceFont"] = ThemeManager.Family(FontPath);
    }

    private static SolidColorBrush Brush(Color c) => Freeze(new SolidColorBrush(c));

    private static T Freeze<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
