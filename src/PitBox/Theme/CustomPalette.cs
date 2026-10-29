using System.Windows.Media;
using PitBox.Config;

namespace PitBox.Theme;

internal static class CustomPalette
{
    public static readonly string[] Families = { "Pastel", "Neon", "Glow", "Night", "Nature", "Warm", "Racing", "Mono" };

    public static readonly (string Name, string Family, ThemeColors Colors)[] Presets =
    {
        ("Pastel", "Pastel", Make("#000000", "#6E6380", "#E6DEEE", "#F2A7C6", "#9EEBD4", "#8FE3B4", "#F6D28B", "#FF8A65")),
        ("Cotton Candy", "Pastel", Make("#05030A", "#6F6585", "#F3ECFA", "#C9A7FF", "#FFB8E1", "#9EE6C4", "#FFE29A", "#FF7B8A")),
        ("Lavender", "Pastel", Make("#000000", "#6B6080", "#F3EEFC", "#B39DDB", "#F8BBD0", "#9DE5C0", "#F5D08A", "#FF7A85")),
        ("Peach", "Pastel", Make("#000000", "#7D6258", "#FFF3EE", "#FFAB91", "#FFD7BA", "#9BE3B0", "#FFCF70", "#FF6B9A")),
        ("Mint", "Pastel", Make("#000000", "#557A6E", "#ECFBF5", "#7FE3C4", "#C7B8FF", "#B9E86A", "#FFD27A", "#FF7A7A")),
        ("Cherry Blossom", "Pastel", Make("#000000", "#7A5F6B", "#FFF0F5", "#FF8FB1", "#B5EAD7", "#8EDBA6", "#FFD27F", "#FF6A3D")),
        ("Sorbet", "Pastel", Make("#04040A", "#6D6478", "#FAF2EC", "#FFB38A", "#A8D8FF", "#A6E3A1", "#FFE08A", "#FF6F91")),

        ("Synthwave", "Neon", Make("#05010A", "#6A4C8C", "#F5E9FF", "#FF2E97", "#2DE2E6", "#45F0A0", "#FFD319", "#FF5A36")),
        ("Cyberpunk", "Neon", Make("#050208", "#5E4A70", "#F2F0FF", "#FCEE0A", "#00F0FF", "#3DFF8A", "#FF9F1C", "#FF2A6D")),
        ("Neon City", "Neon", Make("#03030C", "#4B4F7A", "#EAF0FF", "#FF3CAC", "#3CF0FF", "#5CFF9D", "#FFC93C", "#FF5F3C")),
        ("Vaporwave", "Neon", Make("#0A0514", "#6B5A8E", "#F7EEFF", "#FF71CE", "#01CDFE", "#05FFA1", "#FFFB96", "#FF5E5E")),
        ("Terminal", "Neon", Make("#000300", "#3E6B45", "#D8FFE0", "#39FF6A", "#FFE14D", "#3CE0FF", "#FFB03B", "#FF4F5E")),
        ("Laser", "Neon", Make("#020208", "#4D4D7A", "#EEF0FF", "#4D7CFF", "#FF4DF0", "#3DF2A0", "#FFC23D", "#FF4D6A")),
        ("Outrun", "Neon", Make("#0B0418", "#6A4A8A", "#FDEBFF", "#FF6B35", "#00E5FF", "#7CFF6B", "#FFD23F", "#FF2E88")),
        ("Blacklight", "Neon", Make("#050010", "#5A3F86", "#F1E8FF", "#B026FF", "#39FF14", "#3CFFB0", "#FFE14D", "#FF3B6B")),

        ("Afterglow", "Glow", Make("#0A0402", "#7A4A38", "#FFEDE3", "#FF8A3D", "#FFD08A", "#8BE38B", "#FFD23F", "#FF3D6B")),
        ("Aurora", "Glow", Make("#01060A", "#3F6A72", "#E8FFF8", "#46F2C1", "#B57CFF", "#9CF26A", "#FFD166", "#FF6B8A")),
        ("Firefly", "Glow", Make("#050602", "#5E6A3A", "#FBFFE8", "#E8F23D", "#FFB347", "#5CF2B0", "#FF9F43", "#FF5A6E")),
        ("Plasma", "Glow", Make("#06020C", "#5C4380", "#F4EDFF", "#9D5CFF", "#FF5CC8", "#4CF0B0", "#FFC94C", "#FF5C5C")),
        ("Lava", "Glow", Make("#0A0200", "#7A3F2E", "#FFEDE6", "#FF4D1A", "#FFB020", "#6EE07A", "#FFC233", "#E8336B")),
        ("Deep Glow", "Glow", Make("#000608", "#2F6670", "#E3FFFD", "#00E5D0", "#6B8CFF", "#9DFF6B", "#FFD45C", "#FF5C8A")),

        ("Ocean", "Night", Make("#000000", "#4A6A78", "#E6F6FA", "#00B4D8", "#90E0EF", "#2DD4A0", "#FFC145", "#FF5A5F")),
        ("Midnight", "Night", Make("#03050C", "#4A5578", "#E8ECFA", "#5B7CFA", "#A5B8FF", "#4ADE80", "#FBBF24", "#F87171")),
        ("Deep Sea", "Night", Make("#00040A", "#355A78", "#E2F1FF", "#2D8CFF", "#5CF2E6", "#4CE08A", "#FFC145", "#FF6B6B")),
        ("Glacier", "Night", Make("#000000", "#56707F", "#EEF7FB", "#4FC3F7", "#E1F5FE", "#69F0AE", "#FFD54F", "#FF6E6E")),
        ("Arctic", "Night", Make("#02060A", "#4F6E82", "#F0FAFF", "#8FD3FF", "#C8F0FF", "#7EE8B5", "#FFD98A", "#FF7A8A")),
        ("Nebula", "Night", Make("#05030C", "#55488A", "#EFEAFF", "#7B6CFF", "#FF8AD8", "#5EE6B0", "#FFC861", "#FF5E7E")),
        ("Storm", "Night", Make("#04060A", "#5A6878", "#EAF0F6", "#6FA8DC", "#B8C7D9", "#6FD6A0", "#E8C468", "#E86A6A")),

        ("Forest", "Nature", Make("#000000", "#4F6B55", "#EAF5EC", "#3FB950", "#C3E88D", "#4DD0C8", "#E3B341", "#F85149")),
        ("Moss", "Nature", Make("#030502", "#5A6B48", "#EEF3E4", "#9CBF5A", "#D9C27A", "#5CC8D0", "#E8B04A", "#E8604C")),
        ("Jungle", "Nature", Make("#010603", "#3E6B4E", "#E6F7EC", "#19C9A0", "#F2C14E", "#A3E635", "#FFB547", "#FF5C6E")),
        ("Desert", "Nature", Make("#070503", "#7A6A52", "#FAF1E4", "#E07A4F", "#F2D49B", "#8FCB7A", "#FFD95C", "#FF5C7A")),
        ("Autumn", "Nature", Make("#080402", "#7A5A40", "#FBEFE4", "#E8762E", "#F2C46B", "#7FCB6B", "#F2C94E", "#E8435A")),
        ("Meadow", "Nature", Make("#030603", "#5C7A55", "#F0F8EC", "#F2A65A", "#A8E06B", "#5CD6A0", "#FFEA5C", "#FF5C6E")),

        ("Sunrise", "Warm", Make("#000000", "#7A5A4E", "#FFF1E6", "#FF7A45", "#FFC857", "#7BD88F", "#FFB224", "#FF4D8A")),
        ("Ember", "Warm", Make("#000000", "#7A4E42", "#FBECE6", "#FF5722", "#FFAB40", "#66D17A", "#FFC107", "#FF1F6A")),
        ("Gold Rush", "Warm", Make("#000000", "#7A6A45", "#FFF8E7", "#FFC107", "#FFE082", "#8BE38B", "#FF8A3D", "#FF4D4D")),
        ("Copper", "Warm", Make("#060403", "#7A5E4E", "#FBEEE6", "#D9895B", "#F2C29B", "#7CCB8F", "#F2C94C", "#E8506B")),
        ("Sunset", "Warm", Make("#08030A", "#7A4E6A", "#FFEDF0", "#FF6F61", "#FFB86B", "#6EDCA0", "#FFC94C", "#FF3D8A")),
        ("Terracotta", "Warm", Make("#070403", "#7A5A4C", "#F8EDE6", "#D9714E", "#E8C39E", "#8CC98A", "#E8B44C", "#E84A6A")),

        ("Carbon", "Racing", Make("#050505", "#5C5C5C", "#F2F2F2", "#FF3B30", "#C8C8C8", "#34D399", "#FBBF24", "#FF4DA6")),
        ("Papaya", "Racing", Make("#050302", "#6B5444", "#FFF1E8", "#FF8000", "#47C7FC", "#4ADE80", "#FFEA4D", "#FF4D6A")),
        ("Racing Green", "Racing", Make("#010503", "#3F6650", "#E8F5EE", "#1FA36A", "#F2D16B", "#A3E635", "#FFB547", "#FF5C5C")),
        ("Petrol", "Racing", Make("#02060A", "#4A6478", "#EAF4FB", "#5CB8E6", "#FF9A3D", "#4ADE80", "#FFC93C", "#FF5C5C")),
        ("Pit Lane", "Racing", Make("#050505", "#5E6168", "#F4F4F6", "#FFD400", "#5CC8FF", "#4ADE80", "#FF7A1A", "#FF3B5C")),

        ("Graphite", "Mono", Make("#000000", "#5A5A5A", "#EDEDED", "#9AA7B8", "#D0D0D0", "#6EE7A0", "#FFD166", "#FF6B6B")),
        ("Noir", "Mono", Make("#000000", "#555555", "#F5F5F5", "#FFFFFF", "#BDBDBD", "#6EE7A0", "#FFD166", "#FF6B6B")),
        ("Slate", "Mono", Make("#05070A", "#56606E", "#E9EDF2", "#7C93B0", "#C2CCD8", "#6FCF97", "#E8C468", "#E87070")),
        ("Steel", "Mono", Make("#040506", "#5F6B73", "#EEF2F4", "#8FB3C7", "#D0DCE3", "#72D6A0", "#F2C46B", "#F27A7A")),
        ("Sepia", "Mono", Make("#060504", "#6B6155", "#F5EEDF", "#D49A6A", "#E6D3B3", "#8FC98F", "#F2DA6B", "#E85F6F")),
    };

    public static ThemeColors? Preset(string? name) =>
        name is null ? null : Presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Colors;

    public static string? NameOf(ThemeColors colors) => Presets.FirstOrDefault(p => p.Colors.SameAs(colors)).Name;

    private static ThemeColors Make(string bg, string edges, string text, string accent, string highlight, string success, string warning, string error) => new()
    {
        Background = bg, Edges = edges, Text = text, Accent = accent, Highlight = highlight, Success = success, Warning = warning, Error = error,
    };

    public static Color? Parse(string? hex)
    {
        string t = (hex ?? "").Trim();
        if (!t.StartsWith('#')) t = "#" + t;
        if (t.Length != 7 || !t.Skip(1).All(Uri.IsHexDigit)) return null;
        return Color.FromRgb(Convert.ToByte(t[1..3], 16), Convert.ToByte(t[3..5], 16), Convert.ToByte(t[5..7], 16));
    }

    public static bool IsDark(ThemeColors seeds) => ThemeManager.Contrast(Pick(seeds.Background, ThemeColors.Default.Background), Colors.Black) < 4.5;

    private static Color Pick(string hex, string fallback) => Parse(hex) ?? Parse(fallback)!.Value;

    public static readonly IReadOnlyDictionary<string, Color> Default = Derive(ThemeColors.Default);

    public static IReadOnlyDictionary<string, Color> Build(ThemeColors seeds) => seeds.IsDefault ? Default : Derive(seeds);

    private static IReadOnlyDictionary<string, Color> Derive(ThemeColors seeds)
    {
        var d = ThemeColors.Default;
        Color bg = Pick(seeds.Background, d.Background), edge = Pick(seeds.Edges, d.Edges), text = Pick(seeds.Text, d.Text),
            accent = Pick(seeds.Accent, d.Accent), hi = Pick(seeds.Highlight, d.Highlight), ok = Pick(seeds.Success, d.Success),
            warn = Pick(seeds.Warning, d.Warning), bad = Pick(seeds.Error, d.Error);
        static Color M(Color a, Color b, double t) => ThemeManager.Mix(a, b, t);
        static Color Light(Color c, double t) => ThemeManager.Mix(c, Colors.White, t);
        static Color Dark(Color c, double t) => ThemeManager.Mix(c, Colors.Black, t);
        static Color A(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);
        bool darkBg = ThemeManager.Contrast(bg, Colors.Black) < 4.5;
        edge = Readable(edge, bg, 3);
        var card = M(bg, edge, 0.06);
        text = Readable(text, bg, c => ThemeManager.Contrast(c, card) >= 7 && ThemeManager.Contrast(M(bg, c, 0.66), card) >= 4.5);
        var hiText = Readable(hi, bg, 4.5);

        var p = new Dictionary<string, Color>(Palettes.Custom);

        var tint = M(edge, accent, 0.5);
        p["Bg"] = p["Panel"] = p["LogoBg"] = bg;
        p["Surface"] = M(bg, edge, 0.06);
        p["SurfaceAlt"] = p["SegmentBg"] = M(bg, edge, 0.12);
        p["TileRaised"] = M(bg, edge, 0.17);
        p["MeterTrack"] = p["TooltipBg"] = p["BtnHover"] = p["SwitchTrackHover"] = M(bg, tint, 0.11);
        p["Hover"] = M(bg, tint, 0.17);
        p["HoverRaised"] = M(bg, tint, 0.26);
        p["SwitchTrack"] = p["Surface"];
        p["Line"] = p["TileHighlight"] = p["TileEdge"] = p["ChipBorder"] = p["SidebarEdge"] = p["ScrollThumbBrush"] = M(bg, edge, 0.35);
        p["ButtonEdge"] = p["InputEdge"] = p["SwitchBorder"] = edge;
        p["LineStrong"] = M(bg, edge, 0.78);
        p["InputEdgeHover"] = M(edge, text, 0.25);
        p["ScrollThumbBrushHover"] = M(bg, edge, 0.6);
        p["Knob"] = M(bg, edge, 0.95);
        p["GridLine"] = M(bg, edge, 0.16);
        p["SegmentEdge"] = M(bg, edge, 0.5);
        p["QuietEdge"] = M(bg, edge, 0.65);

        p["Heading"] = p["BtnHoverFg"] = text;
        p["Text"] = p["TooltipText"] = p["BtnFg"] = M(bg, text, 0.93);
        p["TextMuted"] = M(bg, text, 0.66);
        p["TextFaint"] = M(bg, text, 0.56);

        var onAccent = ThemeManager.Contrast(Colors.White, accent) >= 3 ? Light(accent, 0.96) : Dark(accent, 0.85);
        var accentText = ReadableOn(accent, bg);
        var pressed = Dark(accent, 0.23);
        if (ThemeManager.Contrast(onAccent, pressed) < 4.5) pressed = Dark(accent, 0.12);
        p["Accent"] = p["AccentEdge"] = p["PrimaryEdge"] = p["PrimaryHover"] = p["SwitchOnBorder"] = p["SwitchOnKnob"] = p["LogoFg"] = p["LogoEdge"] = accent;
        p["Mono"] = p["Meter1"] = p["Meter2"] = p["Meter3"] = M(bg, text, 0.8);
        p["Graph"] = hi;
        p["GraphSoft"] = M(bg, hi, 0.2);
        p["GraphText"] = Readable(darkBg ? Light(hi, 0.55) : Dark(hi, 0.5), bg, 4.5);
        p["AccentHover"] = Light(accent, 0.18);
        p["AccentPressed"] = p["PrimaryPressed"] = p["ScrollThumbBrushDrag"] = pressed;
        p["OnAccent"] = p["PrimaryHoverFg"] = onAccent;
        p["AccentSoft"] = M(bg, accent, 0.165);
        p["AccentSoftHover"] = M(bg, accent, 0.225);
        p["AccentText"] = p["PrimaryFg"] = p["MetricText"] = accentText;
        p["PrimaryBg"] = A(accent, 0x12);
        p["SwitchOn"] = M(bg, accent, 0.115);

        p["ActiveModeBg"] = A(hi, 0x1A);
        p["ActiveModeHover"] = A(hi, 0x33);
        p["ActiveModeFg"] = hiText;
        p["ActiveModeEdge"] = p["FocusEdge"] = Readable(hi, bg, 3);

        p["Slate"] = hi;
        p["SlateSoft"] = M(bg, hi, 0.12);
        p["SlateDeep"] = darkBg ? Light(hi, 0.35) : Dark(hi, 0.45);
        p["EyebrowText"] = Readable(M(bg, hi, 0.78), bg, 4.5);

        p["Sage"] = ok;
        p["SageSoft"] = M(bg, ok, 0.2);
        p["SageDeep"] = darkBg ? Light(ok, 0.45) : Dark(ok, 0.45);
        p["Warn"] = p["Gold"] = p["Apricot"] = p["Meter4"] = warn;
        p["WarnSoft"] = M(bg, warn, 0.2);
        p["WarnDeep"] = darkBg ? Light(warn, 0.45) : Dark(warn, 0.5);
        p["GoldSoft"] = p["ApricotSoft"] = M(bg, warn, 0.14);
        p["GoldDeep"] = p["ApricotDeep"] = Light(warn, 0.2);
        p["GoldEdge"] = Dark(warn, 0.2);
        p["OnGold"] = M(Colors.Black, warn, 0.1);
        p["Danger"] = p["Meter5"] = bad;
        p["DangerSoft"] = M(bg, bad, 0.24);
        p["DangerDeep"] = Light(bad, 0.55);
        p["CloseHover"] = Dark(bad, 0.12);
        p["OnDanger"] = M(Colors.Black, bad, 0.1);

        if (!darkBg)
        {
            p["AccentText"] = p["PrimaryFg"] = p["MetricText"] = ReadableOn(accent, bg);
            p["DangerDeep"] = Dark(bad, 0.5);
            p["GoldDeep"] = p["ApricotDeep"] = Dark(warn, 0.35);
            p["Dim"] = Color.FromArgb(0x80, 0, 0, 0);
        }
        return p;
    }

    private static Color Readable(Color c, Color bg, double minimum) => Readable(c, bg, x => ThemeManager.Contrast(x, bg) >= minimum);

    private static Color Readable(Color c, Color bg, Func<Color, bool> readable)
    {
        if (readable(c)) return c;
        var target = ThemeManager.Contrast(Colors.White, bg) >= ThemeManager.Contrast(Colors.Black, bg) ? Colors.White : Colors.Black;
        for (int step = 1; step <= 20; step++)
        {
            var mixed = ThemeManager.Mix(c, target, step * 0.05);
            if (readable(mixed)) return mixed;
        }
        return target;
    }

    private static Color ReadableOn(Color accent, Color bg)
    {
        bool darkBg = ThemeManager.Contrast(bg, Colors.Black) < 4.5;
        var start = darkBg ? Light(accent, 0.4) : Dark(accent, 0.2);
        for (int step = 0; step <= 20; step++)
        {
            var c = ThemeManager.Mix(start, darkBg ? Colors.White : Colors.Black, step * 0.05);
            if (ThemeManager.Contrast(c, bg) >= 4.5) return c;
        }
        return darkBg ? Colors.White : Colors.Black;

        static Color Light(Color c, double t) => ThemeManager.Mix(c, Colors.White, t);
        static Color Dark(Color c, double t) => ThemeManager.Mix(c, Colors.Black, t);
    }
}
