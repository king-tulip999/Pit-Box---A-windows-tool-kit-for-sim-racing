using GoodieBag.Config;
using GoodieBag.SimRacing.Hud;

namespace GoodieBag.SimRacing.RaceWindow;

internal enum RaceWindowTab { Live, After }

internal sealed record RaceWindowPanelDef(HudWidgetDef Widget, double MaxW, double MaxH, double GrowX, double GrowY, bool On)
{
    public string Kind => Widget.Kind;
    public string Name => Widget.Name;
    public IReadOnlyList<string>? Sessions { get; init; }

    public RwSize Size => new(Widget.MinW, Widget.MinH, Widget.W, Widget.H, Math.Max(MaxW, Widget.W), Math.Max(MaxH, Widget.H), GrowX, GrowY);
}

internal static class RaceWindowCatalog
{
    private static RaceWindowPanelDef P(string kind, double maxW, double maxH, double gx, double gy, bool on) =>
        new(HudCatalog.Find(kind) ?? throw new InvalidOperationException("Unknown widget " + kind), maxW, maxH, gx, gy, on);

    public static readonly RaceWindowPanelDef[] Live =
    {
        P("standings", 640, 2400, 1, 3, true),
        P("relative", 640, 520, 1, 1, true),
        P("timing", 560, 230, 0, 0, true),
        P("fuel", 480, 380, 0, 0, true),
        P("laps", 520, 900, 0, 1, true),
        P("events", 640, 900, 0, 1, true),
        P("session", 520, 170, 0, 0, false),
        P("tyres", 460, 260, 0, 0, false),
        P("weather", 500, 170, 0, 0, false),
        P("wind", 240, 240, 0, 0, false),
        P("traffic", 520, 220, 0, 0, false),
        P("alerts", 520, 220, 0, 0, false),
    };

    public static readonly List<RaceWindowPanelDef> After = new();

    private static readonly Dictionary<string, RaceWindowPanelDef> LiveByKind = Live.ToDictionary(d => d.Kind, StringComparer.OrdinalIgnoreCase);

    public static RaceWindowPanelDef? Find(RaceWindowTab tab, string? kind)
    {
        if (kind is null) return null;
        if (tab == RaceWindowTab.Live) return LiveByKind.GetValueOrDefault(kind);
        return After.FirstOrDefault(d => string.Equals(d.Kind, kind, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<RaceWindowPanelDef> Of(RaceWindowTab tab) => tab == RaceWindowTab.Live ? Live : After;

    public static List<HudWidgetConfig> Defaults(RaceWindowTab tab) =>
        Of(tab).Select(d => new HudWidgetConfig { Id = HudWidgetConfig.NewId(d.Kind), Kind = d.Kind, Enabled = d.On, W = d.Widget.W, H = d.Widget.H }).ToList();

    public static List<HudWidgetConfig> DefaultLive() => Defaults(RaceWindowTab.Live);

    public static List<HudWidgetConfig> DefaultAfter() => Defaults(RaceWindowTab.After);

    public static void Complete(RaceWindowSettings s)
    {
        foreach (var (tab, list) in new[] { (RaceWindowTab.Live, s.Live), (RaceWindowTab.After, s.After) })
        {
            list.RemoveAll(w => Find(tab, w.Kind) is null);
            foreach (var d in Of(tab))
                if (!list.Any(w => string.Equals(w.Kind, d.Kind, StringComparison.OrdinalIgnoreCase)))
                    list.Add(new HudWidgetConfig { Id = HudWidgetConfig.NewId(d.Kind), Kind = d.Kind, Enabled = d.On, W = d.Widget.W, H = d.Widget.H });
        }
    }
}
