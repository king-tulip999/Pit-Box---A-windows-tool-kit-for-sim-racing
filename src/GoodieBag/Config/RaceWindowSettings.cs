namespace GoodieBag.Config;

public sealed class RaceWindowSettings
{
    public const int MinTextSize = 100, MaxTextSize = 200, TextStep = 25;
    public const int MinWidth = 480, MinHeight = 360, MaxSide = 7680;

    public int TextSize { get; set; } = 100;

    public bool KeepOnTop { get; set; } = true;

    public string? Hotkey { get; set; }

    public double? X { get; set; }
    public double? Y { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 720;
    public bool Maximized { get; set; }

    public List<HudWidgetConfig> Live { get; set; } = new();

    public List<HudWidgetConfig> After { get; set; } = new();

    public RaceWindowArrangement? LiveArrangement { get; set; }

    public RaceWindowArrangement? AfterArrangement { get; set; }

    public Dictionary<string, string> ActiveTabs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize(Func<List<HudWidgetConfig>> defaultLive, Func<List<HudWidgetConfig>> defaultAfter)
    {
        TextSize = Math.Clamp((int)Math.Round(TextSize / (double)TextStep) * TextStep, MinTextSize, MaxTextSize);
        Width = double.IsFinite(Width) ? Math.Clamp(Width, MinWidth, MaxSide) : 1280;
        Height = double.IsFinite(Height) ? Math.Clamp(Height, MinHeight, MaxSide) : 720;
        if (X is { } x && !double.IsFinite(x)) X = null;
        if (Y is { } y && !double.IsFinite(y)) Y = null;
        Hotkey = string.IsNullOrWhiteSpace(Hotkey) ? null : Hotkey.Trim();
        Live = Clean(Live, defaultLive);
        After = Clean(After, defaultAfter);
        LiveArrangement = LiveArrangement?.Cleaned(Live.Select(w => w.Id));
        AfterArrangement = AfterArrangement?.Cleaned(After.Select(w => w.Id));
        var ids = new HashSet<string>(Live.Concat(After).Select(w => w.Id), StringComparer.OrdinalIgnoreCase);
        var tabs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in ActiveTabs ?? new())
            if (k is not null && v is not null && ids.Contains(k) && ids.Contains(v)) tabs[k] = v;
        ActiveTabs = tabs;
    }

    private static List<HudWidgetConfig> Clean(List<HudWidgetConfig>? list, Func<List<HudWidgetConfig>> defaults)
    {
        if (list is null || list.Count == 0) list = defaults();
        list.RemoveAll(w => w is null || string.IsNullOrWhiteSpace(w.Kind) || RaceHudSettings.RemovedKinds.Contains(w.Kind, StringComparer.OrdinalIgnoreCase));
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var w in list)
        {
            if (string.IsNullOrWhiteSpace(w.Id) || !ids.Add(w.Id)) { w.Id = HudWidgetConfig.NewId(w.Kind); ids.Add(w.Id); }
            w.Clamp();
        }
        return list;
    }
}

public sealed class RaceWindowArrangement
{
    public bool RowsFirst { get; set; }

    public List<List<string>> Groups { get; set; } = new();

    public Dictionary<string, double> ShareA { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, double> ShareB { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public RaceWindowArrangement Copy() => new()
    {
        RowsFirst = RowsFirst,
        Groups = Groups.Select(g => g.ToList()).ToList(),
        ShareA = new Dictionary<string, double>(ShareA, StringComparer.OrdinalIgnoreCase),
        ShareB = new Dictionary<string, double>(ShareB, StringComparer.OrdinalIgnoreCase),
    };

    public RaceWindowArrangement? Cleaned(IEnumerable<string> known)
    {
        var ids = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = (Groups ?? new())
            .Where(g => g is not null)
            .Select(g => g.Where(id => id is not null && ids.Contains(id) && seen.Add(id)).ToList())
            .Where(g => g.Count > 0).ToList();
        if (groups.Count == 0) return null;
        static Dictionary<string, double> Shares(Dictionary<string, double>? d, HashSet<string> keep)
        {
            var o = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in d ?? new())
                if (k is not null && keep.Contains(k) && double.IsFinite(v) && v > 0 && v < 1) o[k] = v;
            return o;
        }
        return new RaceWindowArrangement { RowsFirst = RowsFirst, Groups = groups, ShareA = Shares(ShareA, seen), ShareB = Shares(ShareB, seen) };
    }
}
