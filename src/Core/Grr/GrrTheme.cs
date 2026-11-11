using System.Windows;
using System.Windows.Media;
using Core.Config;
using Core.Theme;
using static Core.Theme.Palettes;

namespace Core.Grr;

internal static class GrrResources
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        ThemeManager.ExtraTokens.Add((spec, colors, res) =>
        {
            foreach (var (key, color) in GrrTheme.Podium(spec.Id, colors))
                res[key] = Frozen(color);
            res[GrrPlugin.LogoIcon] = Logo;
        });
    }

    private static readonly Geometry Logo = Frozen(Geometry.Parse("F0 M1.24,8.90 L9.12,8.90 8.72,10.14 8.48,10.38 2.88,10.38 2.80,10.46 2.80,10.54 3.32,10.98 3.32,11.10 2.96,12.18 2.88,12.26 2.56,13.34 2.48,13.42 2.48,13.58 5.48,13.58 5.64,13.10 5.72,13.02 5.84,12.50 5.08,12.46 4.88,12.26 4.88,12.18 4.48,11.82 4.52,11.62 8.24,11.62 8.24,11.78 8.12,11.98 7.56,13.70 7.16,14.46 6.72,14.86 6.00,15.10 0.28,15.10 0.12,15.02 0.00,14.86 0.00,14.58 1.72,9.54 1.72,9.42 1.64,9.34 1.56,9.34 1.48,9.22 1.20,9.06 Z M9.28,8.90 L15.64,8.90 16.16,9.06 16.52,9.38 16.68,9.78 16.68,10.06 16.60,10.38 9.52,10.38 9.60,10.02 9.80,9.58 9.80,9.42 9.48,9.18 9.40,9.18 9.36,9.10 9.28,9.10 Z M16.64,8.90 L22.96,8.90 23.40,9.02 23.80,9.30 23.96,9.58 24.00,10.10 23.52,11.58 23.20,12.18 22.92,12.50 22.84,12.50 22.72,12.66 22.64,12.66 22.40,12.86 21.92,13.02 21.20,13.06 22.16,14.58 22.16,14.82 22.04,15.02 21.88,15.10 20.08,15.10 19.00,13.06 18.00,13.06 17.40,14.86 17.24,15.06 15.20,15.10 15.48,14.14 15.56,14.06 15.76,13.34 15.84,13.26 16.00,12.66 16.08,12.58 16.40,11.62 20.92,11.62 20.96,11.70 20.88,12.06 20.96,12.06 21.16,11.90 22.76,11.10 22.72,10.98 22.56,10.94 22.52,10.86 22.44,10.86 22.40,10.78 22.12,10.66 22.08,10.58 22.00,10.58 21.96,10.50 21.68,10.38 21.64,10.30 21.56,10.30 21.24,10.06 21.16,10.18 21.16,10.38 16.84,10.38 17.16,9.42 17.08,9.42 16.88,9.22 16.68,9.14 16.60,9.06 Z M9.08,11.62 L16.12,11.62 16.12,11.74 15.88,12.18 15.28,12.74 14.88,12.94 14.56,13.02 13.88,13.06 13.88,13.14 13.96,13.18 14.16,13.58 14.24,13.62 14.24,13.70 14.32,13.74 14.44,14.02 14.52,14.06 14.52,14.14 14.60,14.18 14.60,14.26 14.84,14.58 14.80,14.90 14.72,14.94 14.72,15.02 14.56,15.10 12.76,15.10 11.68,13.06 10.64,13.10 10.16,14.62 10.08,14.70 10.04,14.94 9.92,15.06 7.84,15.10 Z"));

    private static Geometry Frozen(Geometry g)
    {
        g.Freeze();
        return g;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

internal static class GrrTheme
{
    public static readonly AppTheme Id = new("Grr");

    private static ThemeSpec? _spec;

    public static ThemeSpec Spec => _spec ??= new()
    {
        Id = Id, Name = "GRR", Dark = true, Colors = Palette,
        HeadingWeight = FontWeights.ExtraBold,
        Radius = 0, ChipRadius = 0,
        CardEdge = new Thickness(1),
        RacingDecor = true, FocusOffset = 2,
        Glow = true, CornerGlow = true,
        WindowIcon = new Uri("pack://application:,,,/Core;component/Assets/Themes/Velocita-Grr.ico"),
        Link = new ThemeLink(new Uri("pack://application:,,,/Core;component/Assets/GrrLogo.png"), GrrLeagues.Site, "GRR Website", "Take me to the GRR website") { Name = "GRR theme" },
    };

    public static readonly IReadOnlyDictionary<string, Color> Palette = new Dictionary<string, Color>
    {
        ["Bg"] = Hex("#1E2620"), ["Panel"] = Hex("#222B23"), ["SurfaceAlt"] = Hex("#19201A"), ["Surface"] = Hex("#222B23"), ["SidebarBg"] = Hex("#1B221C"),
        ["TileRaised"] = Hex("#28322A"), ["Hover"] = Hex("#28332A"), ["HoverRaised"] = Hex("#2E3A30"),
        ["Selected"] = Hex("#323E34"), ["SelectedHover"] = Hex("#384539"), ["SelectedEdge"] = Hex("#4A5A4D"),
        ["IconWell"] = Hex("#28322A"), ["IconInk"] = Hex("#D6DCD6"),
        ["TileEdge"] = Hex("#2A352C"),
        ["Line"] = Hex("#2A352C"), ["LineStrong"] = Hex("#56685A"),
        ["Heading"] = Hex("#F4F6F4"), ["Text"] = Hex("#E8ECE8"), ["TextMuted"] = Hex("#98A399"), ["TextFaint"] = Hex("#839085"),
        ["Accent"] = Hex("#75FF00"), ["AccentHover"] = Hex("#67DE0C"), ["AccentPressed"] = Hex("#44BD00"), ["AccentEdge"] = Hex("#75FF00"),
        ["OnAccent"] = Hex("#071000"), ["AccentSoft"] = Hex("#2C4A1B"), ["AccentText"] = Hex("#8CFF33"),
        ["Sage"] = Hex("#5ED600"), ["SageSoft"] = Hex("#2A461A"), ["SageDeep"] = Hex("#B8F28A"),
        ["Warn"] = Hex("#E8B931"), ["WarnSoft"] = Hex("#3A3212"), ["WarnDeep"] = Hex("#F7DA82"),
        ["Danger"] = Hex("#FF3B3B"), ["DangerSoft"] = Hex("#5A1512"), ["DangerDeep"] = Hex("#FFC9C2"),
        ["Gold"] = Hex("#E8B931"), ["GoldSoft"] = Hex("#3A3B22"), ["GoldDeep"] = Hex("#F2D06B"), ["GoldEdge"] = Hex("#E8B931"), ["OnGold"] = Hex("#071000"),
        ["Apricot"] = Hex("#E8B931"), ["ApricotSoft"] = Hex("#3A3B22"), ["ApricotDeep"] = Hex("#F2D06B"),
        ["Coral"] = Hex("#F0524F"), ["CoralSoft"] = Hex("#3B2C27"), ["CoralDeep"] = Hex("#F79A98"),
        ["Slate"] = Hex("#B7C0B8"), ["SlateSoft"] = Hex("#2C352E"), ["SlateDeep"] = Hex("#E8ECE8"),
        ["Plum"] = Hex("#C6F24A"), ["PlumSoft"] = Hex("#353F1C"), ["PlumDeep"] = Hex("#E2F7A6"),
        ["Knob"] = Hex("#8E988F"),
        ["MeterTrack"] = Hex("#2A352C"), ["GridLine"] = Hex("#252F27"),
        ["Meter1"] = Hex("#E8ECE8"), ["Meter2"] = Hex("#E8ECE8"), ["Meter3"] = Hex("#E8ECE8"), ["Meter4"] = Hex("#E8B931"), ["Meter5"] = Hex("#FF3B3B"),
        ["Mono"] = Hex("#E8ECE8"), ["Graph"] = Hex("#75FF00"), ["GraphSoft"] = Hex("#2C4A1B"), ["GraphText"] = Hex("#B8F28A"),
        ["ScrollThumbBrushHover"] = Hex("#3D4B40"), ["ScrollThumbBrushDrag"] = Hex("#4A5C4E"),
        ["TooltipBg"] = Hex("#2E3930"), ["TooltipText"] = Hex("#F4F6F4"), ["CloseHover"] = Hex("#F0524F"),
        ["Dim"] = Hex("#B3000000"),
        ["BtnBg"] = Hex("#28322A"), ["BtnFg"] = Hex("#F4F6F4"), ["BtnHover"] = Hex("#2E3930"),
        ["PrimaryEdge"] = Hex("#75FF00"),
        ["ActiveModeBg"] = Hex("#75FF00"), ["ActiveModeFg"] = Hex("#071000"),
        ["ChipBorder"] = Hex("#00000000"),
        ["InputEdge"] = Hex("#5C6E60"), ["InputEdgeHover"] = Hex("#7D8F80"),
        ["FocusEdge"] = Hex("#8CFF33"), ["FocusGap"] = Hex("#1E2620"),
        ["LogoBg"] = Hex("#1E2620"), ["LogoFg"] = Hex("#F4F6F4"), ["LogoAccent"] = Hex("#75FF00"),
        ["BgGlow"] = Hex("#22301F"),
        ["OnDanger"] = Hex("#1A0606"),
        ["EyebrowText"] = Hex("#8CFF33"), ["SegmentBg"] = Hex("#19201A"), ["SegmentEdge"] = Hex("#303B32"), ["QuietEdge"] = Hex("#37443A"),
    };

    private static readonly Dictionary<string, Dictionary<string, Color>> PodiumByTheme = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CustomTheme"] = new Dictionary<string, Color> { ["Podium1Bg"] = Hex("#241C00"), ["Podium1Edge"] = Hex("#E0B020"), ["Podium1Fg"] = Hex("#FFD45A"), ["Podium2Bg"] = Hex("#18191B"), ["Podium2Edge"] = Hex("#B8BEC6"), ["Podium2Fg"] = Hex("#E8ECF0"), ["Podium3Bg"] = Hex("#26140A"), ["Podium3Edge"] = Hex("#D0773A"), ["Podium3Fg"] = Hex("#F5AA74") },
        ["Grr"] = new Dictionary<string, Color> { ["Podium1Bg"] = Hex("#2E2A0E"), ["Podium1Edge"] = Hex("#E8B931"), ["Podium1Fg"] = Hex("#F2D06B"), ["Podium2Bg"] = Hex("#20251F"), ["Podium2Edge"] = Hex("#B7C0B8"), ["Podium2Fg"] = Hex("#E8ECE8"), ["Podium3Bg"] = Hex("#2E1E12"), ["Podium3Edge"] = Hex("#C98450"), ["Podium3Fg"] = Hex("#F0B88E") },
    };

    public static IReadOnlyDictionary<string, Color> Podium(AppTheme theme, IReadOnlyDictionary<string, Color> colors)
    {
        if (PodiumByTheme.TryGetValue((ThemeSets.For(theme) is null ? theme : AppTheme.Custom).Id, out var known)) return known;
        var made = new Dictionary<string, Color>();
        foreach (var (place, family) in new[] { (1, "Gold"), (2, "Slate"), (3, "Apricot") })
        {
            made[$"Podium{place}Bg"] = colors[family + "Soft"];
            made[$"Podium{place}Edge"] = colors[family];
            made[$"Podium{place}Fg"] = colors[family + "Deep"];
        }
        return made;
    }
}
