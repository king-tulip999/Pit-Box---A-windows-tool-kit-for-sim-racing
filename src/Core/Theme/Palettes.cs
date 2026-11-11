using System.Windows.Media;

namespace Core.Theme;

internal static class Palettes
{
    public static readonly IReadOnlyDictionary<string, Color> Custom = new Dictionary<string, Color>
    {
        ["Bg"] = Hex("#000000"), ["Panel"] = Hex("#000000"), ["SurfaceAlt"] = Hex("#100B0E"), ["Surface"] = Hex("#070507"),
        ["TileRaised"] = Hex("#161015"), ["Hover"] = Hex("#2A1520"), ["HoverRaised"] = Hex("#3A1C2B"),
        ["TileEdge"] = Hex("#291E24"),
        ["Line"] = Hex("#291E24"), ["LineStrong"] = Hex("#5E4654"),
        ["Heading"] = Hex("#F5ECEE"), ["Text"] = Hex("#EBDCE1"), ["TextMuted"] = Hex("#A7999F"), ["TextFaint"] = Hex("#8C7F85"),
        ["Accent"] = Hex("#DB156B"), ["AccentHover"] = Hex("#F0388A"), ["AccentPressed"] = Hex("#A80F52"), ["AccentEdge"] = Hex("#DB156B"),
        ["OnAccent"] = Hex("#FFF5F6"), ["AccentSoft"] = Hex("#240815"), ["AccentText"] = Hex("#FF6AA6"),
        ["Sage"] = Hex("#3DDC84"), ["SageSoft"] = Hex("#062114"), ["SageDeep"] = Hex("#3DDC84"),
        ["Warn"] = Hex("#FFB224"), ["WarnSoft"] = Hex("#241800"), ["WarnDeep"] = Hex("#FFB224"),
        ["Danger"] = Hex("#FF4545"), ["DangerSoft"] = Hex("#2E0A0A"), ["DangerDeep"] = Hex("#FF7A7A"),
        ["Gold"] = Hex("#FFB224"), ["GoldSoft"] = Hex("#241800"), ["GoldDeep"] = Hex("#FFC24F"), ["GoldEdge"] = Hex("#CC8E1D"), ["OnGold"] = Hex("#1A1000"),
        ["Apricot"] = Hex("#FFB224"), ["ApricotSoft"] = Hex("#241800"), ["ApricotDeep"] = Hex("#FFC24F"),
        ["Coral"] = Hex("#FF6A3D"), ["CoralSoft"] = Hex("#2A0F07"), ["CoralDeep"] = Hex("#FF8C66"),
        ["Slate"] = Hex("#2EE6C8"), ["SlateSoft"] = Hex("#04211D"), ["SlateDeep"] = Hex("#5CF2D6"),
        ["Plum"] = Hex("#FF6A3D"), ["PlumSoft"] = Hex("#2A0F07"), ["PlumDeep"] = Hex("#FF8C66"),
        ["Knob"] = Hex("#725863"),
        ["MeterTrack"] = Hex("#150A0F"), ["GridLine"] = Hex("#130C10"),
        ["Meter1"] = Hex("#DB156B"), ["Meter2"] = Hex("#DB156B"), ["Meter3"] = Hex("#DB156B"), ["Meter4"] = Hex("#FFB224"), ["Meter5"] = Hex("#FF4545"),
        ["ScrollThumbBrushHover"] = Hex("#48333E"), ["ScrollThumbBrushDrag"] = Hex("#A80F52"),
        ["TooltipBg"] = Hex("#150A0F"), ["TooltipText"] = Hex("#EBDCE1"), ["CloseHover"] = Hex("#E02E2E"),
        ["Dim"] = Hex("#CC000000"),
        ["BtnBg"] = Hex("#00000000"), ["BtnFg"] = Hex("#EBDCE1"), ["BtnHover"] = Hex("#150A0F"), ["BtnHoverFg"] = Hex("#F5ECEE"),
        ["PrimaryBg"] = Hex("#12DB156B"), ["PrimaryFg"] = Hex("#FF6AA6"), ["PrimaryEdge"] = Hex("#DB156B"),
        ["PrimaryHover"] = Hex("#DB156B"), ["PrimaryHoverFg"] = Hex("#FFF5F6"), ["PrimaryPressed"] = Hex("#A80F52"),
        ["ActiveModeBg"] = Hex("#1A00FFD0"), ["ActiveModeFg"] = Hex("#00FFD0"),
        ["ChipBorder"] = Hex("#291E24"), ["MetricText"] = Hex("#FF6AA6"),
        ["InputEdge"] = Hex("#775869"), ["InputEdgeHover"] = Hex("#9D7E8E"), ["FocusEdge"] = Hex("#00FFD0"),
        ["LogoBg"] = Hex("#000000"), ["LogoFg"] = Hex("#F5ECEE"), ["LogoAccent"] = Hex("#DB156B"),
        ["OnDanger"] = Hex("#1A0303"),
        ["EyebrowText"] = Hex("#4FC9B4"), ["SegmentBg"] = Hex("#100B0E"), ["SegmentEdge"] = Hex("#3A2C34"), ["QuietEdge"] = Hex("#4F3B46"),
    };

    public static readonly IReadOnlyDictionary<string, string> Derived = new Dictionary<string, string>
    {
        ["BtnBg"] = "TileRaised", ["BtnFg"] = "Heading", ["BtnEdge"] = "BtnOutline", ["BtnHover"] = "HoverRaised", ["BtnHoverFg"] = "BtnFg",
        ["PrimaryBg"] = "Accent", ["PrimaryFg"] = "OnAccent", ["PrimaryEdge"] = "AccentEdge", ["PrimaryHover"] = "AccentHover",
        ["PrimaryHoverFg"] = "PrimaryFg", ["PrimaryPressed"] = "AccentPressed",
        ["QuietBg"] = "SurfaceAlt", ["QuietFg"] = "Text", ["QuietEdge"] = "BtnEdge", ["QuietHover"] = "BtnHover", ["QuietEdgeHover"] = "LineStrong",
        ["ActiveModeBg"] = "Gold", ["ActiveModeFg"] = "OnGold",
        ["ChipBorder"] = "Transparent", ["MetricText"] = "Heading",
        ["Mono"] = "Text", ["Graph"] = "Accent", ["GraphSoft"] = "AccentSoft", ["GraphText"] = "AccentText",
        ["Meter2"] = "Tile8", ["Meter3"] = "Tile16", ["Meter4"] = "Tile32", ["Meter5"] = "Tile64",
        ["LogoBg"] = "Bg", ["LogoFg"] = "Heading", ["LogoAccent"] = "Accent",
        ["SidebarBg"] = "Surface",["BgGlow"] = "Bg", ["BgGlowFar"] = "Bg", ["LiftedBg"] = "Surface",
        ["InputEdge"] = "Line", ["InputEdgeHover"] = "LineStrong", ["FocusEdge"] = "Accent", ["FocusGap"] = "Transparent",
        ["SegmentBg"] = "Transparent", ["SegmentEdge"] = "BtnOutline", ["EyebrowText"] = "TextFaint", ["TooltipEdge"] = "LineStrong",
        ["Selected"] = "HoverRaised", ["SelectedHover"] = "HoverRaised", ["SelectedEdge"] = "LineStrong", ["IconWell"] = "TileRaised", ["IconInk"] = "Text",
    };

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static string ToHex(Color c) => c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
}

internal static class OverlayPresets
{
    public static Config.OverlayColors For(Config.OverlayColorPreset preset, Config.OverlayColors custom, string? set = null)
    {
        if ((preset.IsApp ? ThemeManager.Current.Id : preset.Theme) is not { } theme)
            return custom;
        if (preset.IsApp) set = null;
        lock (Cache)
        {
            var key = (theme, set ?? "");
            if (!Cache.TryGetValue(key, out var colors))
                Cache[key] = colors = FromPalette(ThemeManager.Resolve(Themes.ForSet(theme, set)));
            return Copy(colors);
        }
    }

    private static readonly Dictionary<(Config.AppTheme, string), Config.OverlayColors> Cache = new();

    public static void ClearCache()
    {
        lock (Cache) Cache.Clear();
    }

    private static Config.OverlayColors Copy(Config.OverlayColors c) => new()
    {
        Text = c.Text, Secondary = c.Secondary, Accent = c.Accent, Background = c.Background,
        Graph = c.Graph, Warning = c.Warning, Critical = c.Critical, Fps = c.Fps,
    };

    private static Config.OverlayColors FromPalette(IReadOnlyDictionary<string, Color> p) => new()
    {
        Text = Palettes.ToHex(p["Text"]),
        Secondary = Palettes.ToHex(p["TextMuted"]),
        Accent = Palettes.ToHex(p["AccentText"]),
        Background = Palettes.ToHex(p["Panel"]),
        Graph = Palettes.ToHex(p["Graph"]),
        Warning = Palettes.ToHex(p["Warn"]),
        Critical = Palettes.ToHex(p["Danger"]),
        Fps = Palettes.ToHex(p["SageDeep"]),
    };

    public static IReadOnlyList<(Config.OverlayColorPreset Preset, string Label)> All =>
        Themes.All.Select(t => (Config.OverlayLayout.For(t.Id), t.Name)).ToList();
}
