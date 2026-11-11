using Core.Services;
using Core.SimRacing;
using Core.SimRacing.Hud;
using WinForms = System.Windows.Forms;
using Core.Views.Pages;

namespace Core.Plugins.BuiltIn;

internal sealed class SimRacingPlugin : Plugin
{
    public override string Id => PluginIds.SimRacing;
    public override AppKind App => AppKind.Velocita;
    public override string Name => "Sim Racing Tools";
    public override string Description => "iRacing overlay, setups, fuel, FOV and iRacing settings.";
    public override string Icon => "I.FormulaWheel";
    public override (string Soft, string Deep) Colors => ("CoralSoft", "CoralDeep");
    public override int Order => 10;
    public override string Reads =>
        "With the iRacing Overlay on: iRacing's live telemetry (its shared memory, read only) while iRacing runs, and whether iRacing is running. With a keybind set to a wheel or button box button: that controller's button presses. iRacing's settings files and setups folder, and recent setups in Downloads, while its page is open. With “Car pictures in your paint” on: your paint files in iRacing's paint folder and the driver details in your newest telemetry files. With “Launch when iRacing starts” on: whether iRacing is running.";
    public override string BackgroundWork =>
        "With the iRacing Overlay switched on: waits for iRacing to start (process events), then reads its telemetry about 60 times a second and draws the widgets at their own rates; while iRacing is closed it only waits. Only with “Launch when iRacing starts” on for Trading Paints or Garage 61: watches for iRacing starting (process events, no polling). With “Car pictures in your paint” on: after iRacing opens or a session ends, asks iRacing to draw your car in each paint that changed (its own local service) and keeps the pictures in the Velocita folder. With a Bass shakers output picked: reads the same telemetry and, only while iRacing is connected or a test plays, writes a sound to the output you picked. While the Setup Manager is open on screen: file events for the setups folder and Downloads. Otherwise never.";
    public override string Changes =>
        "The iRacing Overlay draws its own windows on top of the game; nothing is injected into iRacing or written to it. Only when you ask: iRacing's settings files when you save in iRacing Controller or write the FOV (backed up first), setup files and folders you import, rename, move, delete (to the Recycle Bin) or export in the Setup Manager, and starting or closing Trading Paints / Garage 61.";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage(PluginIds.SimRacingPage, "Sim Racing Tools", "I.FormulaWheel", (app, window) => new SimRacingPage(app, window), Sections: SimRacingPage.SectionsFor),
        new PluginPage(PluginIds.RaceOverlayPage, "iRacing Overlay", "I.RaceOverlay", (app, window) => new RaceHudPage(app, window)),
    };

    private const string OverlayPage = PluginIds.RaceOverlayPage;

    public HudHost? Hud { get; private set; }

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        var h = app.Config.SimRacing.Hud;
        yield return new PluginHotkey("hud.toggle", "Show / hide the iRacing Overlay", () => h.ToggleHotkey, k => h.ToggleHotkey = k, () => Hud?.ToggleHidden(), "iRacing Overlay", "Ctrl+Alt+H", OverlayPage);
        yield return new PluginHotkey("hud.edit", "Arrange the iRacing Overlay", () => h.EditHotkey, k => h.EditHotkey = k, () =>
        {
            if (Hud is null) return;
            if (Hud.Editing) Hud.EndEdit(keep: true); else Hud.BeginEdit();
        }, "iRacing Overlay", Page: OverlayPage);
        yield return new PluginHotkey("hud.quickedit", "iRacing Overlay in-game editor", () => h.QuickEditHotkey, k => h.QuickEditHotkey = k, () =>
        {
            if (Hud is { } hud) Core.Views.HudQuickEditor.Toggle(app, hud);
        }, "iRacing Overlay", Config.RaceHudSettings.QuickEditDefault, OverlayPage);
        yield return new PluginHotkey("hud.layout", "Next iRacing Overlay layout", () => h.NextLayoutHotkey, k => h.NextLayoutHotkey = k, () => Hud?.NextLayout(), "iRacing Overlay", Page: OverlayPage);
        yield return new PluginHotkey("hud.layout.previous", "Previous iRacing Overlay layout", () => h.PreviousLayoutHotkey, k => h.PreviousLayoutHotkey = k, () => Hud?.PreviousLayout(), "iRacing Overlay", Page: OverlayPage);
        yield return new PluginHotkey("hud.dismiss", "Dismiss iRacing Overlay alerts", () => h.DismissHotkey, k => h.DismissHotkey = k, () => Hud?.DismissAlerts(), "iRacing Overlay", Page: OverlayPage);
        foreach (var layout in h.Layouts)
        {
            string group = $"Show / hide widgets in “{layout.Name}”";
            foreach (var widget in layout.Widgets)
            {
                var w = widget;
                if (HudCatalog.Find(w.Kind) is null) continue;
                yield return new PluginHotkey(WidgetHotkeyId(layout, w), WidgetName(layout, w), () => w.Hotkey, k => w.Hotkey = k, () => Hud?.ToggleWidget(w), group, Page: OverlayPage);
            }
        }
    }

    public static string WidgetHotkeyId(Config.HudLayout layout, Config.HudWidgetConfig widget) => $"hud.widget:{layout.Name}:{widget.Id}";

    public static string WidgetName(Config.HudLayout layout, Config.HudWidgetConfig widget)
    {
        string name = HudCatalog.Find(widget.Kind)?.Name ?? widget.Kind;
        int index = 0;
        foreach (var other in layout.Widgets)
        {
            if (ReferenceEquals(other, widget)) break;
            if (other.Kind == widget.Kind) index++;
        }
        return index > 0 ? $"{name} {index + 1}" : name;
    }

    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app)
    {
        var h = app.Config.SimRacing.Hud;
        var item = new WinForms.ToolStripMenuItem("Show iRacing Overlay") { Checked = h.Enabled, ShortcutKeyDisplayString = Core.Hotkeys.Hotkey.Display(h.ToggleHotkey) };
        item.Click += (_, _) => Hud?.SetEnabled(!h.Enabled);
        items.Add(item);
    }

    public override string? Status(AppServices app) =>
        Hud is null ? null
        : !app.Config.SimRacing.Hud.Enabled ? "iRacing Overlay off"
        : Hud.OnScreen ? $"iRacing Overlay on screen · {Hud.LayoutName}"
        : app.Telemetry.Status switch
        {
            SimRacing.Telemetry.SimStatus.Connected => "iRacing Overlay on · iRacing connected",
            SimRacing.Telemetry.SimStatus.Waiting => "iRacing Overlay on · iRacing loading",
            _ => "iRacing Overlay on · waiting for iRacing",
        };

    public SimRacing.Shakers.ShakerHost? Shakers { get; private set; }

    public CompanionApp? TradingPaints { get; private set; }

    public CompanionApp? Garage61 { get; private set; }

    public LiveryPictures? Liveries { get; private set; }

    protected override void OnStart(AppServices app)
    {
        Liveries = new LiveryPictures(app);
        Liveries.SetActive(LiveryPictures.On(app));
        Hud = new HudHost(app);
        Hud.SetActive(true);
        Shakers = new SimRacing.Shakers.ShakerHost(app);
        Shakers.SetActive(true);
        TradingPaints = new CompanionApp(app, CompanionApp.TradingPaints, () => app.Config.SimRacing.TradingPaints);
        Garage61 = new CompanionApp(app, CompanionApp.Garage61, () => app.Config.SimRacing.Garage61);
        TradingPaints.Start();
        Garage61.Start();
    }

    protected override void OnStop(AppServices app)
    {
        Hud?.Dispose();
        Hud = null;
        Shakers?.Dispose();
        Shakers = null;
        Liveries?.SetActive(false);
        Liveries = null;
        TradingPaints?.Stop();
        Garage61?.Stop();
        TradingPaints = Garage61 = null;
    }
}
