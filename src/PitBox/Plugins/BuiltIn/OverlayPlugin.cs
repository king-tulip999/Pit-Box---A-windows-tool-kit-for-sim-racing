using PitBox.Services;
using PitBox.Views;
using PitBox.Views.Pages;
using WinForms = System.Windows.Forms;

namespace PitBox.Plugins.BuiltIn;

internal sealed class OverlayPlugin : Plugin
{
    public override string Id => PluginIds.Overlay;
    public override string Name => "Performance Overlay";
    public override string Description => "FPS, temperatures, load and fan speeds on top of your game, toggled with a keybind.";
    public override string Icon => "I.Overlay";
    public override (string Soft, string Deep) Colors => ("ApricotSoft", "ApricotDeep");
    public override int Order => 50;

    public override string Reads => "The sensors and frame rate it shows, at its update rate, only while it's on screen or its page is open.";
    public override string BackgroundWork =>
        "Only while the overlay is on screen. While it's hidden, only its keybinds are registered.";
    public override string Changes => "Nothing. It draws its own window on top of your game; nothing is injected into the game.";

    public override IReadOnlyList<PluginPage> Pages { get; } = new[]
    {
        new PluginPage(PluginIds.OverlayPage, "Performance Overlay", "I.Overlay", (app, window) => new OverlayPage(app, window), PageSensors.Basic),
    };

    public override IEnumerable<SearchEntry> SearchEntries { get; } = new SearchEntry[]
    {
        new("Performance Overlay", "FPS and sensors drawn over your game.", "Performance Overlay", PluginIds.OverlayPage, null, "osd fps hud hardware monitor sensors"),
        new("Show overlay", "Turn the overlay on or off (the switch on its preview; also its keybind and the tray).", "Performance Overlay", PluginIds.OverlayPage, "overlay.visible", "toggle on off show hide osd hud"),
        new("Overlay keybind", "Show / hide the overlay from anywhere.", "Performance Overlay", PluginIds.OverlayPage, "overlay.hotkey", "hotkey shortcut toggle"),
        new("Reset FPS statistics keybind", "Start the overlay FPS average, 1% low and average temperatures over from anywhere.", "Performance Overlay", PluginIds.OverlayPage, "overlay.fpshotkey", "hotkey shortcut fps reset"),
        new("Overlay update rate", "How often overlay values refresh.", "Performance Overlay", PluginIds.OverlayPage, "overlay.rate", "refresh interval"),
        new("Overlay layouts", "Different overlay setups, switched with a keybind.", "Performance Overlay", PluginIds.OverlayPage, "overlay.layouts", "profile configuration"),
        new("Overlay metrics", "Switch FPS, 1% low, temps, clocks, power, network… on or off; drag them in the preview to reorder", "Performance Overlay", PluginIds.OverlayPage, "overlay.metrics", "fps frametime cpu gpu ram vram ping order arrange drag"),
        new("Overlay position", "Pick the corner and monitor the overlay sits on.", "Performance Overlay", PluginIds.OverlayPage, "overlay.position", "corner monitor move place"),
        new("Overlay style", "Compact or expanded layout.", "Performance Overlay", PluginIds.OverlayPage, "overlay.style", "compact expanded"),
        new("Overlay names", "Your own names for the groups and values the overlay shows, e.g. GPU → 7900 XTX, or the hotspot tag.", "Performance Overlay", PluginIds.OverlayPage, "overlay.names", "rename name label gpu cpu custom text tag hotspot hot spot"),
        new("Overlay text size", "Font size of the overlay.", "Performance Overlay", PluginIds.OverlayPage, "overlay.font", "font scale"),
        new("Overlay background opacity", "How see-through the overlay's background is.", "Performance Overlay", PluginIds.OverlayPage, "overlay.opacity", "transparency background opacity"),
        new("Overlay frame-time graph", "Small graph of recent frame times.", "Performance Overlay", PluginIds.OverlayPage, "overlay.graph", "frametime stutter graph"),
        new("Overlay colors", "Same as the app, one of the app's themes, or your own colors.", "Performance Overlay", PluginIds.OverlayPage, "overlay.colors", "color color theme text accent warning critical"),
    };

    public override IEnumerable<PluginHotkey> Hotkeys(AppServices app)
    {
        var o = app.Config.Overlay;
        var k = app.Config.Hotkeys;
        yield return new PluginHotkey("overlay", "Show / hide overlay", () => o.Hotkey, hk => o.Hotkey = hk, () => app.SetOverlayVisible(!o.Visible), Default: "Ctrl+Alt+O");
        yield return new PluginHotkey("overlay.fps", "Reset FPS statistics", () => o.ResetFpsHotkey, hk => o.ResetFpsHotkey = hk, app.ResetFpsStats);
        yield return new PluginHotkey("overlay.layout", "Next overlay layout", () => k.NextOverlayLayout, hk => k.NextOverlayLayout = hk, () =>
        {
            if (HotkeyCatalog.Step(o.Layouts, o.Layouts.FirstOrDefault(l => l.Name == o.ActiveLayout), 1) is { } layout && app.UseOverlayLayout(layout.Name))
                app.Notify("Overlay layout applied", layout.Name, NoticeKind.Success);
        });
    }

    public override void AddTrayItems(WinForms.ToolStripItemCollection items, AppServices app)
    {
        var o = app.Config.Overlay;
        var item = new WinForms.ToolStripMenuItem("Show Performance Overlay") { Checked = o.Visible, ShortcutKeyDisplayString = PitBox.Hotkeys.Hotkey.Display(o.Hotkey) };
        item.Click += (_, _) => app.SetOverlayVisible(!o.Visible);
        items.Add(item);
    }

    public override string? Status(AppServices app) => app.Overlay.Showing ? "On screen"
        : app.Config.Overlay.Visible && app.Demo ? "Switched on (not drawn in demo mode)"
        : "Hidden";

    protected override void OnStart(AppServices app) => app.Overlay.Enabled = true;

    protected override void OnStop(AppServices app) => app.Overlay.Enabled = false;
}
