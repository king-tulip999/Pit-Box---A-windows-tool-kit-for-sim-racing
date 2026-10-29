using System.Windows;
using PitBox.Config;

namespace PitBox.Theme;

internal sealed record ThemeSpec
{
    public required AppTheme Id { get; init; }
    public required string Name { get; init; }
    public required bool Dark { get; init; }
    public required IReadOnlyDictionary<string, System.Windows.Media.Color> Colors { get; init; }

    public string DisplayFont { get; init; } = "Bahnschrift, Segoe UI";
    public string UiFont { get; init; } = "Bahnschrift, Segoe UI";
    public string MonoFont { get; init; } = "Consolas, Segoe UI";
    public string? MetricFont { get; init; }
    public FontWeight HeadingWeight { get; init; } = FontWeights.Bold;
    public FontStyle HeadingStyle { get; init; } = FontStyles.Normal;
    public bool Caps { get; init; }
    public bool MonoLabels { get; init; } = true;

    public Uri? WindowIcon { get; init; }
    public ThemeLink? Link { get; init; }

    public double Radius { get; init; }
    public double PanelOpacity { get; init; } = 0.85;
    public double SidebarOpacity { get; init; } = 0.6;
    public double SwitchRadius { get; init; } = 11;
    public double KnobRadius { get; init; } = 8;
    public double ChipRadius { get; init; }
    public double ControlRadius { get; init; } = 5;
    public Thickness CardEdge { get; init; } = new(0, 1, 0, 2);
    public bool TileEdge => CardEdge.Bottom > CardEdge.Top;
    public Thickness ButtonEdge { get; init; } = new(1);
    public Thickness ButtonEdgePressed { get; init; } = new(1);
    public double PressDrop { get; init; } = 0;
    public string Outline { get; init; } = "LineStrong";
    public Thickness LogoEdge { get; init; }
    public double CardChamfer { get; init; }
    public double ControlChamfer { get; init; }

    public string CircuitInk { get; init; } = "Slate";
    public string CircuitLine { get; init; } = "Accent";
    public string CircuitApex { get; init; } = "AccentText";
    public string CircuitGold { get; init; } = "Gold";
    public double CircuitBoost { get; init; } = 1;
    public double CircuitGlow { get; init; }

    public bool RacingDecor { get; init; }
    public double FocusOffset { get; init; } = 1;

    public bool Glow { get; init; }
    public double GlowStrength { get; init; } = 1;
    public bool CornerGlow { get; init; }
    public bool StepsClimb { get; init; } = true;

    public required AppTheme Partner { get; init; }
}

internal static class Themes
{
    public static ThemeSpec? Variant(ThemeSpec spec)
    {
        var partner = For(spec.Partner);
        return partner.Id != spec.Id && partner.Partner == spec.Id && partner.Dark != spec.Dark ? partner : null;
    }

    public static readonly ThemeSpec Light = new()
    {
        Id = AppTheme.Light, Name = "Light", Dark = false, Colors = Palettes.Light, Partner = AppTheme.Dark,
        Outline = "ButtonEdge",
        CircuitInk = "SlateDeep", CircuitGold = "GoldDeep", CircuitBoost = 1.35,
    };

    private static readonly Thickness Hairline = new(1);

    public static readonly ThemeSpec Dark = new()
    {
        Id = AppTheme.Dark, Name = "Dark", Dark = true, Colors = Palettes.Dark, Partner = AppTheme.Light,
        CardEdge = Hairline, LogoEdge = new(0),
        StepsClimb = false, Outline = "ScrollThumbBrushDrag",
    };

    public static readonly ThemeSpec Custom = new()
    {
        Id = AppTheme.Custom, Name = DefaultCustomName, Dark = true, Colors = CustomPalette.Default, Partner = AppTheme.Light,
        CardEdge = Hairline, LogoEdge = new(1.5),
        Glow = true, GlowStrength = 0.45, StepsClimb = false,
        CircuitInk = "AccentText", CircuitLine = "Slate", CircuitApex = "SlateDeep", CircuitBoost = 1.1, CircuitGlow = 5,
    };

    public static readonly ThemeSpec[] BuiltIn = { Light, Dark, Custom };

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

    public const string OledDefaultSet = "#default";

    public static ThemeSpec ForSet(AppTheme id, string? set)
    {
        if (id != AppTheme.Custom || string.IsNullOrEmpty(set)) return For(id);
        if (set == OledDefaultSet) return Custom;
        if (Services.AppServices.Current?.Config.Ui.OledCustom.Profile(set) is not { } profile) return For(id);
        return Custom with { Name = profile.Name, Colors = CustomPalette.Build(profile.Colors), Dark = CustomPalette.IsDark(profile.Colors) };
    }

    public const string DefaultCustomName = "Custom";

    public static ThemeSpec For(AppTheme id) => _all.FirstOrDefault(t => t.Id == id) ?? Dark;

    public static bool Exists(AppTheme id) => _all.Any(t => t.Id == id);
}

internal sealed record ThemeLink(Uri Logo, string Url, string Title, string Tip)
{
    public required string Name { get; init; }
}
