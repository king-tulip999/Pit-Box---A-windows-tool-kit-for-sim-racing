using System.Drawing;
using PitBox.Config;
using PitBox.Theme;
using WpfColor = System.Windows.Media.Color;

namespace PitBox.SimRacing.Hud;

internal static class HudThemes
{
    public static IEnumerable<(string Name, string Family, ThemeColors Colors)> Presets =>
        CustomPalette.Presets.Concat(TeamPalette.Presets);

    public static string? Known(string? name) =>
        name is null ? null : Presets.FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)).Name;

    public static ThemeColors? Seeds(string? name) =>
        Known(name) is { } known ? Presets.First(p => p.Name == known).Colors : null;

    public static ThemeColors Cleaned(ThemeColors? colors)
    {
        var d = ThemeColors.Default;
        colors ??= new ThemeColors();
        static string Fix(string? hex, string fallback) => CustomPalette.Parse(hex) is { } c ? Palettes.ToHex(c) : fallback;
        return new ThemeColors
        {
            Background = Fix(colors.Background, d.Background), Edges = Fix(colors.Edges, d.Edges), Text = Fix(colors.Text, d.Text),
            Accent = Fix(colors.Accent, d.Accent), Highlight = Fix(colors.Highlight, d.Highlight), Success = Fix(colors.Success, d.Success),
            Warning = Fix(colors.Warning, d.Warning), Error = Fix(colors.Error, d.Error),
        };
    }

    public static ThemeColors StartingColors(HudAppearance look) =>
        Seeds(look.Colors)?.Copy() ?? (look.Colors == HudAppearance.CustomColors ? look.Custom.Copy() : ThemeColors.Default.Copy());

    private static readonly object Gate = new();
    private static readonly Dictionary<string, HudPalette> Built = new(StringComparer.OrdinalIgnoreCase);
    private static ThemeSpec? _appSpec;
    private static HudPalette? _app;

    public static HudPalette For(HudAppearance look)
    {
        string pick = look.Colors ?? HudAppearance.DefaultColors;
        if (string.Equals(pick, HudAppearance.DefaultColors, StringComparison.OrdinalIgnoreCase)) return HudPalette.Default;
        lock (Gate)
        {
            if (string.Equals(pick, HudAppearance.AppColors, StringComparison.OrdinalIgnoreCase))
            {
                var spec = ThemeManager.Current;
                if (!ReferenceEquals(spec, _appSpec) || _app is null)
                {
                    _appSpec = spec;
                    _app = From(ThemeManager.Resolve(spec));
                }
                return _app;
            }
            ThemeColors? seeds;
            bool brand = false;
            string key;
            if (string.Equals(pick, HudAppearance.CustomColors, StringComparison.OrdinalIgnoreCase))
            {
                seeds = look.Custom ?? ThemeColors.Default;
                key = "custom|" + string.Join("|", seeds.Background, seeds.Edges, seeds.Text, seeds.Accent, seeds.Highlight, seeds.Success, seeds.Warning, seeds.Error);
            }
            else
            {
                seeds = CustomPalette.Preset(pick);
                if (seeds is null) { seeds = TeamPalette.Presets.FirstOrDefault(p => string.Equals(p.Name, pick, StringComparison.OrdinalIgnoreCase)).Colors; brand = true; }
                if (seeds is null) return HudPalette.Default;
                key = "preset|" + pick;
            }
            if (Built.TryGetValue(key, out var known)) return known;
            if (Built.Count >= 64) Built.Clear();
            var colors = brand ? CustomPalette.BuildBrand(seeds) : CustomPalette.Build(seeds);
            var built = From(ThemeManager.Resolve(new ThemeSpec { Id = AppTheme.Custom, Name = key, Dark = CustomPalette.IsDark(seeds), Colors = colors }));
            Built[key] = built;
            return built;
        }
    }

    internal static HudPalette From(IReadOnlyDictionary<string, WpfColor> c)
    {
        var d = HudPalette.Default;
        Color Get(string key, Color fallback) => c.TryGetValue(key, out var v) ? Color.FromArgb(255, v.R, v.G, v.B) : fallback;
        Color bg = Get("Bg", d.Bg), surface = Get("Surface", d.Surface), text = Get("Text", d.Text), lineStrong = Get("LineStrong", d.LineStrong);
        Color accent = Get("Accent", d.Accent), danger = Get("Danger", d.Danger);
        return new HudPalette
        {
            Dark = Luminance(bg) < 0.35,
            Bg = bg, Surface = surface, SurfaceAlt = Get("TileRaised", d.SurfaceAlt), Text = text, Heading = Get("Heading", d.Heading),
            Muted = Get("TextMuted", d.Muted), Faint = Get("TextFaint", d.Faint), Line = Get("Line", d.Line), LineStrong = lineStrong,
            Accent = accent, AccentText = Get("AccentText", d.AccentText), AccentSoft = Get("AccentSoft", d.AccentSoft), OnAccent = Get("OnAccent", d.OnAccent),
            Good = Get("Sage", d.Good), GoodText = Get("SageDeep", d.GoodText), GoodSoft = Get("SageSoft", d.GoodSoft),
            Warn = Get("Warn", d.Warn), WarnText = Get("WarnDeep", d.WarnText), WarnSoft = Get("WarnSoft", d.WarnSoft),
            Danger = danger, DangerText = Get("DangerDeep", d.DangerText), DangerSoft = Get("DangerSoft", d.DangerSoft),
            Gold = d.Gold, Slate = Get("TextFaint", d.Slate), Plum = d.Plum, Silver = d.Silver, Bronze = d.Bronze,
            Player = accent, PlayerText = Get("Heading", d.PlayerText),
            Highlight = HudPalette.Mix(surface, text, 0.3), HighlightEdge = accent, SoftBands = true,
            InputTrack = HudPalette.Mix(surface, text, 0.06), TraceBack = HudPalette.Mix(bg, surface, 0.5), InputTicks = lineStrong,
            RedZone = HudPalette.Mix(surface, danger, 0.25),
        };
    }

    private static double Luminance(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);

    private static double Lin(int v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
}
