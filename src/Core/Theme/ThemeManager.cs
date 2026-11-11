using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Core.Config;

namespace Core.Theme;

internal static class ThemeManager
{
    public static ThemeSpec Current { get; private set; } = Themes.AppGlow;

    public static bool Dark => Current.Dark;

    public static event Action? Changed;
    public static event Action? Changing;
    public static event Action? Applying;

    public static readonly List<Action<ThemeSpec, IReadOnlyDictionary<string, Color>, ResourceDictionary>> ExtraTokens = new();

    public static Dictionary<string, Color> Resolve(ThemeSpec spec)
    {
        var all = new Dictionary<string, Color>(spec.Colors) { ["Transparent"] = Colors.Transparent };
        if (spec.Dark && !spec.Exact) Firm(all);
        all["BtnOutline"] = all[spec.Outline];
        for (bool progress = true; progress;)
        {
            progress = false;
            foreach (var (key, from) in Palettes.Derived)
                if (!all.ContainsKey(key) && all.TryGetValue(from, out var c))
                {
                    all[key] = c;
                    progress = true;
                }
        }
        all.Remove("Transparent");
        return all;
    }

    private static readonly string[] DeepGrounds = { "Bg", "SidebarBg", "SurfaceAlt", "SegmentBg", "LogoBg", "BgGlow", "BgGlowFar" };
    private static readonly string[] PanelGrounds = { "Panel", "Surface", "LiftedBg" };
    private static readonly string[] RaisedGrounds = { "TileRaised", "IconWell", "Hover", "HoverRaised", "Selected", "SelectedHover", "BtnBg", "BtnHover", "TooltipBg", "MeterTrack", "GridLine", "QuietBg" };
    private static readonly string[] Bright = { "SageDeep", "WarnDeep", "DangerDeep", "GoldDeep", "ApricotDeep", "CoralDeep", "SlateDeep", "PlumDeep" };

    private static void Firm(Dictionary<string, Color> all)
    {
        static Color Toward(Color c, Color to, double t) => Color.FromArgb(c.A, (byte)Math.Round(c.R + (to.R - c.R) * t), (byte)Math.Round(c.G + (to.G - c.G) * t), (byte)Math.Round(c.B + (to.B - c.B) * t));
        void Shift(IEnumerable<string> keys, Color to, double t)
        {
            foreach (var key in keys)
                if (all.TryGetValue(key, out var c)) all[key] = Toward(c, to, t);
        }
        Shift(DeepGrounds, Colors.Black, 0.42);
        Shift(PanelGrounds, Colors.Black, 0.36);
        Shift(RaisedGrounds, Colors.Black, 0.2);
        Shift(all.Keys.Where(k => k.EndsWith("Soft", StringComparison.Ordinal)).ToList(), Colors.Black, 0.15);
        if (!all.TryGetValue("Heading", out var heading) || !all.TryGetValue("Text", out var text) || !all.TryGetValue("Panel", out var panel)) return;
        heading = all["Heading"] = Toward(heading, Colors.White, 0.45);
        text = all["Text"] = Toward(text, heading, 0.55);
        foreach (var key in new[] { "BtnFg", "TooltipText", "BtnHoverFg" })
            if (all.TryGetValue(key, out var fg) && fg.A == 255) all[key] = Toward(fg, heading, 0.5);
        var grounds = new[] { "Bg", "Panel", "TileRaised", "HoverRaised", "Selected" }.Where(all.ContainsKey).Select(k => all[k]).Where(c => c.A == 255).ToArray();
        double Worst(Color c, Color[] under) => under.Min(g => Contrast(c, g));
        Color Lift(Color c, Color to, Color[] under, double target)
        {
            for (double t = 0; t <= 1.0001; t += 0.02)
            {
                var candidate = Toward(c, to, t);
                if (Worst(candidate, under) >= target) return candidate;
            }
            return to;
        }
        if (all.TryGetValue("TextFaint", out var faint)) all["TextFaint"] = faint = Lift(faint, text, grounds, 5.5);
        if (all.TryGetValue("TextMuted", out var muted)) all["TextMuted"] = Lift(muted, text, grounds, Math.Max(6.9, all.ContainsKey("TextFaint") ? Worst(faint, grounds) + 1.4 : 0));
        var soft = all.TryGetValue("AccentSoft", out var accentSoft) && accentSoft.A == 255 ? new[] { panel, accentSoft } : new[] { panel };
        if (all.TryGetValue("AccentText", out var accentText)) all["AccentText"] = Lift(accentText, Colors.White, soft, 6);
        if (all.TryGetValue("EyebrowText", out var eyebrow)) all["EyebrowText"] = Lift(eyebrow, Colors.White, new[] { panel }, 6);
        foreach (var key in Bright)
            if (all.TryGetValue(key, out var c) && c.A == 255) all[key] = Lift(c, Colors.White, new[] { panel }, 6.5);
        if (all.TryGetValue("LineStrong", out var strong)) all["LineStrong"] = Lift(Toward(strong, text, 0.1), text, new[] { panel }, 3);
        foreach (var key in new[] { "Line", "TileEdge" })
            if (all.TryGetValue(key, out var line) && line.A == 255) all[key] = Toward(line, text, 0.06);
        if (all.TryGetValue("InputEdge", out var input) && input.A == 255)
            all["InputEdge"] = Lift(input, text, all.TryGetValue("BtnBg", out var btn) && btn.A == 255 ? new[] { panel, btn } : new[] { panel }, 3.5);
    }

    private static ResourceDictionary? _applied;

    private static readonly HashSet<string> SeeThroughKeys = new() { "Panel", "Surface", "SurfaceAlt" };

    private static Color Faded(Color color, double opacity) =>
        opacity >= 1 ? color : Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B);

    public static void Apply(AppTheme theme)
    {
        var spec = Themes.For(theme);
        if (_applied is not null && spec != Current) Changing?.Invoke();
        if (_applied is not null) Applying?.Invoke();
        Current = spec;
        _ui = _uiBold = _display = null;
        var res = Build(spec);
        MeterHead = spec.RacingDecor && res["Heading"] is SolidColorBrush heading ? Freeze(new SolidColorBrush(heading.Color) { Opacity = 0.85 }) : null;
        var merged = Application.Current.Resources.MergedDictionaries;
        int at = _applied is null ? -1 : merged.IndexOf(_applied);
        if (at >= 0) merged[at] = res;
        else merged.Add(res);
        _applied = res;
        Hairline.Install();
        Changed?.Invoke();
    }

    public static ResourceDictionary Build(ThemeSpec spec)
    {
        var res = new ResourceDictionary();
        var p = Resolve(spec);
        foreach (var (key, color) in p)
            res[key] = Freeze(new SolidColorBrush(key == "SidebarBg" ? Faded(color, spec.SidebarOpacity)
                : SeeThroughKeys.Contains(key) ? Faded(color, spec.PanelOpacity) : color));

        var font = Family(ThemeSpec.Saira);
        foreach (var key in new[] { "DisplayFont", "UiFont", "MonoFont", "MetricFont", "LabelFont" })
            res[key] = font;
        res["HeadingWeight"] = spec.HeadingWeight;
        res["HeadingStyle"] = FontStyles.Normal;

        foreach (var (key, r) in new (string, double)[]
                 { ("R.Card", CardRadius), ("R.Inset", 8), ("R.Popup", 8), ("R.Tooltip", 6), ("R.Small", 6), ("R.Tiny", 4) })
            res[key] = Round(r);
        res["R.Chip"] = Round(spec.ChipRadius);
        res["R.Control"] = res["R.Input"] = Round(spec.ControlRadius);

        res["CardEdge"] = res["LineStrong"];

        res["Well"] = Freeze(new SolidColorBrush(Mix(p["SurfaceAlt"], p["Bg"], 0.45)));

        var tints = ToolTints(p, spec.Dark);
        for (int i = 0; i < tints.Count; i++)
        {
            res[$"Tool{i + 1}Soft"] = Freeze(new SolidColorBrush(tints[i].Soft));
            res[$"Tool{i + 1}Deep"] = Freeze(new SolidColorBrush(tints[i].Deep));
        }
        res["MeterFill"] = res["Mono"];
        res["TimingBest"] = Freeze(new SolidColorBrush(spec.Dark ? TimingBestDark : TimingBestLight));
        RaceLook.Install(spec, p, res);
        res["Changed"] = res["Accent"];
        res["CardEdgeThickness"] = new Thickness(1);
        res["BtnEdgeThickness"] = spec.ButtonEdge;
        res["BtnEdgePressed"] = spec.ButtonEdgePressed;
        res["PressDrop"] = new Thickness(0, spec.PressDrop, 0, 0);

        res["WindowBg"] = spec.CornerGlow ? CornerGlow(p) : res["Bg"];

        res["NavSelText"] = Freeze(new SolidColorBrush(NavSelection(p).Text));

        double glow = spec.GlowStrength;
        res["AccentGlow"] = spec.Glow ? Shadow(p["Accent"], 14, 0, 0.75 * glow) : null;

        double gap = Math.Max(0, spec.FocusOffset);
        res["FocusRingGap"] = gap;
        res["FocusRingGapThickness"] = new Thickness(gap);
        res["FocusRingGapMargin"] = new Thickness(-gap);
        res["FocusRingOutset"] = gap + 2;
        res["FocusRingMargin"] = new Thickness(-(gap + 2));

        res["RacingFade"] = Fade;
        res["RacingChecker"] = Checker(p["Accent"], 4);

        foreach (var extend in ExtraTokens)
        {
            try { extend(spec, p, res); }
            catch (Exception ex) { App.LogError(ex); }
        }
        return res;
    }

    private const double GlowRadius = 0.9;

    private static Brush CornerGlow(IReadOnlyDictionary<string, Color> p)
    {
        Color bg = p["Bg"];
        RadialGradientBrush From(Point corner, Color glow, Color edge) =>
            new(glow, edge) { Center = corner, GradientOrigin = corner, RadiusX = GlowRadius, RadiusY = GlowRadius };
        var near = From(new Point(0, 0), p["BgGlow"], bg);
        if (!p.TryGetValue("BgGlowFar", out var far) || far == bg) return Freeze(near);
        var area = new RectangleGeometry(new Rect(0, 0, 1, 1));
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(near, null, area));
        group.Children.Add(new GeometryDrawing(From(new Point(1, 1), far, Color.FromArgb(0, bg.R, bg.G, bg.B)), null, area));
        return Freeze(new DrawingBrush(group) { Stretch = Stretch.Fill });
    }

    internal static (Color Tint, Color Mid, Color Text) NavSelection(IReadOnlyDictionary<string, Color> p)
    {
        var tint = p["Selected"];
        var mid = p["Selected"];
        bool Reads(Color c) => Contrast(c, tint) >= 4.5 && Contrast(c, mid) >= 4.5;
        for (int step = 0; step <= 20; step++)
        {
            var c = Mix(p["AccentText"], p["Heading"], step * 0.05);
            if (Reads(c)) return (tint, mid, c);
        }
        return (tint, mid, p["Heading"]);
    }

    private static readonly Color[] ToolHues =
    {
        Color.FromRgb(0xE8, 0xA3, 0x3C), Color.FromRgb(0x4F, 0x8E, 0xF7), Color.FromRgb(0x9B, 0x6C, 0xF0),
        Color.FromRgb(0xE5, 0x48, 0x4D), Color.FromRgb(0x30, 0xA4, 0x6C), Color.FromRgb(0xF0, 0x74, 0x3A),
        Color.FromRgb(0x14, 0xB8, 0xC9),
    };

    internal static readonly Color TimingBestDark = Color.FromRgb(0xB9, 0x7C, 0xFF), TimingBestLight = Color.FromRgb(0x7E, 0x3F, 0xD0);

    internal static List<(Color Soft, Color Deep)> ToolTints(IReadOnlyDictionary<string, Color> p, bool dark) =>
        ToolHues.Select(hue => (Mix(p["Surface"], hue, 0.2), dark ? Mix(hue, Colors.White, 0.3) : Mix(hue, Colors.Black, 0.52))).ToList();

    internal static Color Mix(Color under, Color over, double amount)
    {
        byte M(byte a, byte b) => (byte)Math.Round(a + (b - a) * Math.Clamp(amount, 0, 1));
        return Color.FromRgb(M(under.R, over.R), M(under.G, over.G), M(under.B, over.B));
    }

    public static Brush? MeterHead { get; private set; }

    private static readonly LinearGradientBrush Fade = Freeze(new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), new Point(0, 0.5), new Point(1, 0.5)));

    private static DrawingBrush Checker(Color color, double cell)
    {
        var squares = new GeometryGroup();
        squares.Children.Add(new RectangleGeometry(new Rect(0, 0, cell, cell)));
        squares.Children.Add(new RectangleGeometry(new Rect(cell, cell, cell, cell)));
        return Freeze(Tiled(new Rect(0, 0, 2 * cell, 2 * cell), (color, squares)));
    }

    private static DrawingBrush Tiled(Rect tile, params (Color Color, Geometry Shape)[] parts)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(tile)));
        foreach (var (color, shape) in parts)
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(color), null, shape));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile, Viewport = tile, ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = tile, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill,
        };
    }

    private static DropShadowEffect Shadow(Color color, double blur, double depth, double opacity) =>
        Freeze(new DropShadowEffect { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = opacity, Color = color, RenderingBias = RenderingBias.Performance });

    private const double CardRadius = 8;

    public static CornerRadius CardCorner => Round(CardRadius);

    public static CornerRadius Round(double radius) => new(Scaled(radius));

    public static CornerRadius Round(double topLeft, double topRight, double bottomRight, double bottomLeft) =>
        new(Scaled(topLeft), Scaled(topRight), Scaled(bottomRight), Scaled(bottomLeft));

    private static double Scaled(double r) => r <= 0 || Current.Radius <= 0 ? 0 : Math.Max(1, Math.Round(r * Current.Radius * 2) / 2);

    public static Brush RampBrush(double fraction) =>
        Brush(MeterKeys[fraction switch { < 0.40 => 0, < 0.60 => 1, < 0.75 => 2, < 0.90 => 3, _ => 4 }], Brushes.Orange);

    private static readonly string[] MeterKeys = { "Meter1", "Meter2", "Meter3", "Meter4", "Meter5" };

    public static Brush StepBrush() => Brush("Mono", Brushes.Orange);

    private static Typeface? _ui, _uiBold, _display;

    public static Typeface UiFace(bool bold = false) => bold
        ? _uiBold ??= new(Family(ThemeSpec.Saira), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal)
        : _ui ??= new(Family(ThemeSpec.Saira), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    private static Typeface? _race;

    public static Typeface RaceFace => _race ??= new(Family(RaceLook.FontPath), FontStyles.Normal, FontWeights.ExtraBold, FontStretches.Normal);

    public static Typeface DisplayFace => _display ??= new(Family(ThemeSpec.Saira), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    private static Uri? _fontBase;

    public static FontFamily Family(string fonts) =>
        fonts.Contains('#') ? new FontFamily(_fontBase ??= new Uri("pack://application:,,,/Core;component/"), fonts) : Even(fonts);

    private const double ReferenceLineSpacing = 1.3301;

    private static readonly Dictionary<string, FontFamily> EvenFamilies = new();

    private static FontFamily Even(string fonts)
    {
        lock (EvenFamilies)
        {
            if (EvenFamilies.TryGetValue(fonts, out var cached)) return cached;
            double caps = new Typeface(fonts).TryGetGlyphTypeface(out var face) ? face.CapsHeight : 0.7;
            var family = new FontFamily { LineSpacing = ReferenceLineSpacing, Baseline = ReferenceLineSpacing / 2 + caps / 2 };
            family.FamilyMaps.Add(new FontFamilyMap { Target = fonts, Unicode = "0000-10ffff" });
            return EvenFamilies[fonts] = family;
        }
    }


    private static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double x = v / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    public static Brush Brush(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    public static Color ColorOf(string key, Color fallback) =>
        Application.Current?.TryFindResource(key) is SolidColorBrush s ? s.Color : fallback;
}
