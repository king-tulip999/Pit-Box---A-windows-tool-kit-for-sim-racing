using System.Windows.Media;

namespace PitBox.Theme;

internal static class Palettes
{
    public static readonly IReadOnlyDictionary<string, Color> Dark = new Dictionary<string, Color>
    {
        ["Bg"] = Hex("#1E1E1E"), ["Panel"] = Hex("#232323"), ["Surface"] = Hex("#232323"), ["SurfaceAlt"] = Hex("#1B1B1B"),
        ["TileRaised"] = Hex("#2C2C2C"), ["SidebarBg"] = Hex("#191919"), ["Hover"] = Hex("#2A2A2A"), ["HoverRaised"] = Hex("#343434"),
        ["TileHighlight"] = Hex("#313131"), ["TileEdge"] = Hex("#313131"),
        ["Line"] = Hex("#2E2E2E"), ["LineStrong"] = Hex("#3C3C3C"),
        ["Heading"] = Hex("#EDEDED"), ["Text"] = Hex("#CFCFCF"), ["TextMuted"] = Hex("#9A9A9A"), ["TextFaint"] = Hex("#8E8E8E"),
        ["Accent"] = Hex("#DB156B"), ["AccentHover"] = Hex("#B8115A"), ["AccentPressed"] = Hex("#9A0E4B"), ["AccentEdge"] = Hex("#DB156B"),
        ["OnAccent"] = Hex("#FFFFFF"), ["AccentSoft"] = Hex("#3D0E24"), ["ToggleOff"] = Hex("#5E1131"), ["AccentSoftHover"] = Hex("#4B112C"), ["AccentText"] = Hex("#F5559A"),
        ["Selected"] = Hex("#353535"), ["SelectedHover"] = Hex("#3D3D3D"), ["SelectedEdge"] = Hex("#4A4A4A"),
        ["IconWell"] = Hex("#2C2C2C"), ["IconInk"] = Hex("#CFCFCF"),
        ["ButtonEdge"] = Hex("#3A3A3A"),
        ["Sage"] = Hex("#7FA35E"), ["SageSoft"] = Hex("#24321A"), ["SageDeep"] = Hex("#BFE09A"),
        ["Warn"] = Hex("#A66F10"), ["WarnSoft"] = Hex("#3A2A0C"), ["WarnDeep"] = Hex("#F2CC80"),
        ["Danger"] = Hex("#D92B1F"), ["DangerSoft"] = Hex("#451210"), ["DangerDeep"] = Hex("#FFC9C2"),
        ["Gold"] = Hex("#FFC08A"), ["GoldSoft"] = Hex("#33291F"), ["GoldDeep"] = Hex("#FFD2AD"), ["GoldEdge"] = Hex("#D99A63"), ["OnGold"] = Hex("#1A120A"),
        ["Apricot"] = Hex("#FFC08A"), ["ApricotSoft"] = Hex("#33291F"), ["ApricotDeep"] = Hex("#FFD2AD"),
        ["Coral"] = Hex("#F07A35"), ["CoralSoft"] = Hex("#36231C"), ["CoralDeep"] = Hex("#FF9A5C"),
        ["Slate"] = Hex("#49C8F5"), ["SlateSoft"] = Hex("#173540"), ["SlateDeep"] = Hex("#8ADCFA"),
        ["Plum"] = Hex("#7E7E7E"), ["PlumSoft"] = Hex("#2A2A2A"), ["PlumDeep"] = Hex("#B8B8B8"),
        ["Knob"] = Hex("#F0F0F0"), ["SwitchTrack"] = Hex("#363636"), ["SwitchTrackHover"] = Hex("#404040"), ["SwitchBorder"] = Hex("#5E5E5E"),
        ["MeterTrack"] = Hex("#2E2E2E"), ["GridLine"] = Hex("#2A2A2A"),
        ["Meter1"] = Hex("#D6CEC2"), ["Meter2"] = Hex("#D6CEC2"), ["Meter3"] = Hex("#D6CEC2"), ["Meter4"] = Hex("#A66F10"), ["Meter5"] = Hex("#D92B1F"),
        ["Mono"] = Hex("#D6CEC2"), ["Graph"] = Hex("#C8135F"), ["GraphSoft"] = Hex("#4A0F27"), ["GraphText"] = Hex("#FF9CC4"),
        ["ScrollThumbBrush"] = Hex("#3A3A3A"), ["ScrollThumbBrushHover"] = Hex("#4A4A4A"), ["ScrollThumbBrushDrag"] = Hex("#5A5A5A"),
        ["TooltipBg"] = Hex("#333333"), ["TooltipText"] = Hex("#F0F0F0"), ["CloseHover"] = Hex("#D93636"),
        ["Dim"] = Hex("#B3030307"), ["Shadow"] = Hex("#000000"),
        ["BtnBg"] = Hex("#2A2A2A"), ["BtnFg"] = Hex("#F0F0F0"), ["BtnHover"] = Hex("#343434"),
        ["PrimaryEdge"] = Hex("#DB156B"),
        ["ActiveModeBg"] = Hex("#DB156B"), ["ActiveModeFg"] = Hex("#FFFFFF"), ["ActiveModeEdge"] = Hex("#DB156B"), ["ActiveModeHover"] = Hex("#B8115A"),
        ["LogoBg"] = Hex("#191919"), ["LogoFg"] = Hex("#FFFFFF"), ["LogoAccent"] = Hex("#DB156B"), ["LogoEdge"] = Hex("#191919"),
        ["SidebarEdge"] = Hex("#2A2A2A"),
        ["InputEdge"] = Hex("#777777"), ["InputEdgeHover"] = Hex("#8C8C8C"), ["FocusEdge"] = Hex("#F5559A"), ["OnDanger"] = Hex("#FFFFFF"),
        ["SegmentBg"] = Hex("#1B1B1B"), ["SegmentEdge"] = Hex("#383838"), ["QuietEdge"] = Hex("#3A3A3A"), ["EyebrowText"] = Hex("#7FBFD6"),
    };

    public static readonly IReadOnlyDictionary<string, Color> Custom = new Dictionary<string, Color>
    {
        ["Bg"] = Hex("#000000"), ["Panel"] = Hex("#000000"), ["SurfaceAlt"] = Hex("#100B0E"), ["Surface"] = Hex("#070507"),
        ["TileRaised"] = Hex("#161015"), ["Hover"] = Hex("#2A1520"), ["HoverRaised"] = Hex("#3A1C2B"),
        ["TileHighlight"] = Hex("#291E24"), ["TileEdge"] = Hex("#291E24"),
        ["Line"] = Hex("#291E24"), ["LineStrong"] = Hex("#5E4654"),
        ["Heading"] = Hex("#F5ECEE"), ["Text"] = Hex("#EBDCE1"), ["TextMuted"] = Hex("#A7999F"), ["TextFaint"] = Hex("#8C7F85"),
        ["Accent"] = Hex("#DB156B"), ["AccentHover"] = Hex("#F0388A"), ["AccentPressed"] = Hex("#A80F52"), ["AccentEdge"] = Hex("#DB156B"),
        ["OnAccent"] = Hex("#FFF5F6"), ["AccentSoft"] = Hex("#240815"), ["ToggleOff"] = Hex("#4F0A28"), ["AccentSoftHover"] = Hex("#310B1C"), ["AccentText"] = Hex("#FF6AA6"),
        ["ButtonEdge"] = Hex("#775869"),
        ["Sage"] = Hex("#3DDC84"), ["SageSoft"] = Hex("#062114"), ["SageDeep"] = Hex("#3DDC84"),
        ["Warn"] = Hex("#FFB224"), ["WarnSoft"] = Hex("#241800"), ["WarnDeep"] = Hex("#FFB224"),
        ["Danger"] = Hex("#FF4545"), ["DangerSoft"] = Hex("#2E0A0A"), ["DangerDeep"] = Hex("#FF7A7A"),
        ["Gold"] = Hex("#FFB224"), ["GoldSoft"] = Hex("#241800"), ["GoldDeep"] = Hex("#FFC24F"), ["GoldEdge"] = Hex("#CC8E1D"), ["OnGold"] = Hex("#1A1000"),
        ["Apricot"] = Hex("#FFB224"), ["ApricotSoft"] = Hex("#241800"), ["ApricotDeep"] = Hex("#FFC24F"),
        ["Coral"] = Hex("#FF6A3D"), ["CoralSoft"] = Hex("#2A0F07"), ["CoralDeep"] = Hex("#FF8C66"),
        ["Slate"] = Hex("#2EE6C8"), ["SlateSoft"] = Hex("#04211D"), ["SlateDeep"] = Hex("#5CF2D6"),
        ["Plum"] = Hex("#FF6A3D"), ["PlumSoft"] = Hex("#2A0F07"), ["PlumDeep"] = Hex("#FF8C66"),
        ["Knob"] = Hex("#725863"), ["SwitchTrack"] = Hex("#070507"), ["SwitchTrackHover"] = Hex("#150A0F"),
        ["MeterTrack"] = Hex("#150A0F"), ["GridLine"] = Hex("#130C10"),
        ["Meter1"] = Hex("#DB156B"), ["Meter2"] = Hex("#DB156B"), ["Meter3"] = Hex("#DB156B"), ["Meter4"] = Hex("#FFB224"), ["Meter5"] = Hex("#FF4545"),
        ["ScrollThumbBrush"] = Hex("#291E24"), ["ScrollThumbBrushHover"] = Hex("#48333E"), ["ScrollThumbBrushDrag"] = Hex("#A80F52"),
        ["TooltipBg"] = Hex("#150A0F"), ["TooltipText"] = Hex("#EBDCE1"), ["CloseHover"] = Hex("#E02E2E"),
        ["Dim"] = Hex("#CC000000"), ["Shadow"] = Hex("#000000"),
        ["BtnBg"] = Hex("#00000000"), ["BtnFg"] = Hex("#EBDCE1"), ["BtnHover"] = Hex("#150A0F"), ["BtnHoverFg"] = Hex("#F5ECEE"),
        ["PrimaryBg"] = Hex("#12DB156B"), ["PrimaryFg"] = Hex("#FF6AA6"), ["PrimaryEdge"] = Hex("#DB156B"),
        ["PrimaryHover"] = Hex("#DB156B"), ["PrimaryHoverFg"] = Hex("#FFF5F6"), ["PrimaryPressed"] = Hex("#A80F52"),
        ["ActiveModeBg"] = Hex("#1A00FFD0"), ["ActiveModeFg"] = Hex("#00FFD0"), ["ActiveModeEdge"] = Hex("#00FFD0"), ["ActiveModeHover"] = Hex("#3300FFD0"),
        ["SwitchBorder"] = Hex("#775869"), ["SwitchOn"] = Hex("#19040E"), ["SwitchOnBorder"] = Hex("#DB156B"), ["SwitchOnKnob"] = Hex("#DB156B"),
        ["ChipBorder"] = Hex("#291E24"), ["MetricText"] = Hex("#FF6AA6"),
        ["InputEdge"] = Hex("#775869"), ["InputEdgeHover"] = Hex("#9D7E8E"), ["FocusEdge"] = Hex("#00FFD0"),
        ["LogoBg"] = Hex("#000000"), ["LogoFg"] = Hex("#F5ECEE"), ["LogoAccent"] = Hex("#DB156B"), ["LogoEdge"] = Hex("#000000"),
        ["SidebarEdge"] = Hex("#291E24"),
        ["OnDanger"] = Hex("#1A0303"),
        ["EyebrowText"] = Hex("#4FC9B4"), ["SegmentBg"] = Hex("#100B0E"), ["SegmentEdge"] = Hex("#3A2C34"), ["QuietEdge"] = Hex("#4F3B46"),
    };

    public static readonly IReadOnlyDictionary<string, string> Derived = new Dictionary<string, string>
    {
        ["BtnBg"] = "TileRaised", ["BtnFg"] = "Heading", ["BtnEdge"] = "BtnOutline", ["BtnHover"] = "HoverRaised", ["BtnHoverFg"] = "BtnFg",
        ["PrimaryBg"] = "Accent", ["PrimaryFg"] = "OnAccent", ["PrimaryEdge"] = "AccentEdge", ["PrimaryHover"] = "AccentHover",
        ["PrimaryHoverFg"] = "PrimaryFg", ["PrimaryPressed"] = "AccentPressed",
        ["QuietBg"] = "SurfaceAlt", ["QuietFg"] = "Text", ["QuietEdge"] = "BtnEdge", ["QuietHover"] = "BtnHover", ["QuietEdgeHover"] = "LineStrong",
        ["ActiveModeBg"] = "Gold", ["ActiveModeFg"] = "OnGold", ["ActiveModeEdge"] = "GoldEdge", ["ActiveModeHover"] = "Tile64",
        ["SwitchBorder"] = "Transparent", ["SwitchOn"] = "Accent", ["SwitchOnBorder"] = "Transparent", ["SwitchOnKnob"] = "Knob",
        ["ChipBorder"] = "Transparent", ["MetricText"] = "Heading",
        ["Mono"] = "Text", ["Graph"] = "Accent", ["GraphSoft"] = "AccentSoft", ["GraphText"] = "AccentText",
        ["Meter2"] = "Tile8", ["Meter3"] = "Tile16", ["Meter4"] = "Tile32", ["Meter5"] = "Tile64",
        ["LogoBg"] = "Bg", ["LogoFg"] = "Heading", ["LogoAccent"] = "Accent", ["LogoEdge"] = "LogoBg",
        ["SidebarEdge"] = "Transparent", ["SidebarBg"] = "Surface",["BgGlow"] = "Bg", ["LiftedBg"] = "Surface",
        ["InputEdge"] = "Line", ["InputEdgeHover"] = "LineStrong", ["FocusEdge"] = "Accent", ["FocusGap"] = "Transparent",
        ["SegmentBg"] = "Transparent", ["SegmentEdge"] = "BtnOutline", ["EyebrowText"] = "TextFaint", ["TooltipEdge"] = "LineStrong",
        ["Selected"] = "HoverRaised", ["SelectedHover"] = "HoverRaised", ["SelectedEdge"] = "LineStrong", ["IconWell"] = "TileRaised", ["IconInk"] = "Text",
        ["ToggleOff"] = "AccentSoft",
    };

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static bool TryHex(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        try { color = Hex(hex.Trim()); return true; }
        catch (FormatException) { return false; }
    }

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
