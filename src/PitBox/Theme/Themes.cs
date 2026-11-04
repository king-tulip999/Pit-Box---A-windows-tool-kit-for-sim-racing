using System.Windows;
using PitBox.Config;

namespace PitBox.Theme;

internal sealed record ThemeSpec
{
    public required AppTheme Id { get; init; }
    public required string Name { get; init; }
    public string? Variant { get; init; }
    public string Label => Variant is null ? Name : $"{Name} · {Variant}";
    public required bool Dark { get; init; }
    public required IReadOnlyDictionary<string, System.Windows.Media.Color> Colors { get; init; }

    public const string Saira = "./Assets/Fonts/Saira/#Saira, Segoe UI";

    public FontWeight HeadingWeight { get; init; } = FontWeights.Bold;

    public Uri? WindowIcon { get; init; }
    public ThemeLink? Link { get; init; }

    public double Radius { get; init; }
    public double PanelOpacity { get; init; } = 0.85;
    public double SidebarOpacity { get; init; } = 0.6;
    public double ChipRadius { get; init; }
    public double ControlRadius { get; init; } = 5;
    public Thickness CardEdge { get; init; } = new(0, 1, 0, 2);
    public bool TileEdge => CardEdge.Bottom > CardEdge.Top;
    public Thickness ButtonEdge { get; init; } = new(1);
    public Thickness ButtonEdgePressed { get; init; } = new(1);
    public double PressDrop { get; init; } = 0;
    public string Outline { get; init; } = "LineStrong";

    public bool RacingDecor { get; init; }
    public double FocusOffset { get; init; } = 1;

    public bool Glow { get; init; }
    public double GlowStrength { get; init; } = 1;
    public bool CornerGlow { get; init; }
}

internal static class Themes
{
    private static readonly Thickness Hairline = new(1);

    public static readonly ThemeSpec Dark = new()
    {
        Id = AppTheme.Dark, Name = "The Pits", Dark = true, Colors = Palettes.Dark,
        CardEdge = Hairline,
        Outline = "ScrollThumbBrushDrag",
    };

    public static readonly ThemeSpec Custom = new()
    {
        Id = AppTheme.Custom, Name = DefaultCustomName, Dark = true, Colors = CustomPalette.Default,
        CardEdge = Hairline,
        Glow = true, GlowStrength = 0.45,
    };

    public static readonly ThemeSpec Teams = SetSpec(ThemeSets.Teams, null);

    public static readonly ThemeSpec[] BuiltIn = { Dark, Custom, Teams };

    private static ThemeSpec[] _all = BuiltIn;

    public static IReadOnlyList<ThemeSpec> All => _all;

    public static bool Register(ThemeSpec theme)
    {
        if (_all.Any(t => t.Id == theme.Id)) return false;
        _all = _all.Append(theme).ToArray();
        OverlayPresets.ClearCache();
        return true;
    }

    public static void Unregister(AppTheme id)
    {
        if (BuiltIn.Any(t => t.Id == id)) return;
        _all = _all.Where(t => t.Id != id).ToArray();
        OverlayPresets.ClearCache();
    }

    internal static void ResetRegistered() => _all = BuiltIn;

    public static void SetOledCustom(CustomThemeSettings custom)
    {
        string name = string.IsNullOrWhiteSpace(custom.Name) ? DefaultCustomName : custom.Name.Trim();
        var spec = custom.InUse
            ? Custom with { Name = name, Colors = CustomPalette.Build(custom.Colors), Dark = CustomPalette.IsDark(custom.Colors) }
            : Custom with { Name = name };
        _all = _all.Select(t => t.Id == AppTheme.Custom ? spec : t).ToArray();
        OverlayPresets.ClearCache();
    }

    public static void SetPick(ThemeSet set, string? name)
    {
        var spec = SetSpec(set, name);
        _all = _all.Select(t => t.Id == set.Id ? spec : t).ToArray();
        OverlayPresets.ClearCache();
    }

    private static ThemeSpec SetSpec(ThemeSet set, string? name)
    {
        string pick = set.Known(name) ?? set.First;
        var colors = set.Preset(pick)!;
        return new ThemeSpec
        {
            Id = set.Id, Name = set.Name, Variant = pick, Dark = CustomPalette.IsDark(colors), Colors = CustomPalette.BuildBrand(colors),
            CardEdge = Hairline,
        };
    }

    public const string OledDefaultSet = "#default";

    public static ThemeSpec ForSet(AppTheme id, string? set)
    {
        if (ThemeSets.For(id) is { } themeSet && themeSet.Known(set) is { } pick) return SetSpec(themeSet, pick);
        if (id != AppTheme.Custom || string.IsNullOrEmpty(set)) return For(id);
        if (set == OledDefaultSet) return Custom;
        if (CustomPalette.Preset(set) is not { } colors) return For(id);
        return Custom with { Name = CustomPalette.NameOf(colors)!, Colors = CustomPalette.Build(colors), Dark = CustomPalette.IsDark(colors) };
    }

    public const string DefaultCustomName = "Custom";

    public static ThemeSpec For(AppTheme id) => _all.FirstOrDefault(t => t.Id == id) ?? Dark;

    public static bool Exists(AppTheme id) => _all.Any(t => t.Id == id);
}

internal sealed record ThemeLink(Uri Logo, string Url, string Title, string Tip)
{
    public required string Name { get; init; }
}
