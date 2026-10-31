using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using PitBox.Config;
using PitBox.Overlay;
using PitBox.SimRacing.Race;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Hud.Web;

internal sealed class HudWebRenderer : IDisposable
{
    private sealed class Target : IDisposable
    {
        public required HudWidgetDef Def;
        public HudWidgetConfig? Config;
        public HudOptions? Options;
        public HudWidget? Widget;
        public readonly HudPainter Painter = new();
        public readonly DibSurface Surface = new();
        public long LastSeq = -1;
        public long LastDraw;
        public byte[]? Last;
        public double Opacity = -1;
        public (int W, int H) Size;

        public void Dispose()
        {
            Painter.Dispose();
            Surface.Dispose();
        }
    }

    private static readonly byte[] Empty = Array.Empty<byte>();
    private readonly Dictionary<HudWebClient, Target> _targets = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, HudWidgetConfig> _fallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly MemoryStream _png = new();

    public static IReadOnlyList<HudWebOverlay> Overlays { get; } = HudCatalog.All.Select(d => new HudWebOverlay(d.Kind, d.Name, d.W, d.H)).ToList();

    public int Render(IReadOnlyList<HudWebClient> clients, TelemetryFrame frame, RaceState race, RaceHudSettings settings, HudLayout? layout, HudPalette palette, bool dirty)
    {
        int fastest = 0;
        foreach (var stale in _targets.Keys.Where(k => !clients.Contains(k)).ToList())
        {
            _targets[stale].Dispose();
            _targets.Remove(stale);
        }
        long now = Stopwatch.GetTimestamp();
        foreach (var client in clients)
        {
            if (HudCatalog.Find(client.Kind) is not { } def) continue;
            if (!_targets.TryGetValue(client, out var t)) _targets[client] = t = new Target { Def = def };
            var config = ConfigFor(def, layout);
            if (!ReferenceEquals(config, t.Config))
            {
                t.Config = config;
                t.Options = new HudOptions(def, config);
                t.Widget = def.Create();
                t.Last = null;
                dirty = true;
            }
            int hz = config.UpdateHz > 0 ? config.UpdateHz : def.Hz(t.Options!);
            fastest = Math.Max(fastest, hz);
            var size = (client.Width, client.Height);
            bool due = Stopwatch.GetElapsedTime(t.LastDraw, now).TotalMilliseconds >= 1000.0 / hz - 4;
            if (!dirty && size == t.Size && !(due && t.LastSeq != frame.Sequence)) continue;
            t.Size = size;
            t.LastSeq = frame.Sequence;
            t.LastDraw = now;
            if (Math.Abs(config.Opacity - t.Opacity) > 0.01)
            {
                t.Opacity = config.Opacity;
                client.Say(string.Create(CultureInfo.InvariantCulture, $"{{\"type\":\"look\",\"opacity\":{Math.Clamp(config.Opacity / 100, 0, 1):0.###}}}"));
            }
            var bytes = Draw(t, frame, race, settings, palette, size);
            if (t.Last is not null && bytes.AsSpan().SequenceEqual(t.Last)) continue;
            t.Last = bytes;
            client.Offer(bytes);
        }
        return fastest;
    }

    private HudWidgetConfig ConfigFor(HudWidgetDef def, HudLayout? layout)
    {
        if (layout is not null)
        {
            HudWidgetConfig? any = null;
            foreach (var w in layout.Widgets)
            {
                if (!string.Equals(w.Kind, def.Kind, StringComparison.OrdinalIgnoreCase)) continue;
                if (w.Enabled) return w;
                any ??= w;
            }
            if (any is not null) return any;
        }
        if (!_fallbacks.TryGetValue(def.Kind, out var fallback))
            _fallbacks[def.Kind] = fallback = new HudWidgetConfig { Id = "web-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H };
        return fallback;
    }

    private byte[] Draw(Target t, TelemetryFrame frame, RaceState race, RaceHudSettings settings, HudPalette palette, (int W, int H) pixels)
    {
        var config = t.Config!;
        if (frame.Status != SimStatus.Connected || pixels.W < 8 || pixels.H < 8 || config.W <= 0 || config.H <= 0) return Empty;
        var view = new HudView { Frame = frame, Race = race, Settings = settings, Config = config, Options = t.Options!, Def = t.Def };
        if ((config.OnlyWhenNeeded ?? t.Def.HideWhenEmpty) == true && !t.Widget!.HasContent(view)) return Empty;
        float scale = (float)Math.Min(pixels.W / config.W, pixels.H / config.H);
        var size = new Size(Math.Clamp((int)Math.Round(config.W * scale), 1, HudWebClient.MaxPixels), Math.Clamp((int)Math.Round(config.H * scale), 1, HudWebClient.MaxPixels));
        var g = t.Surface.Ensure(size);
        var p = t.Painter;
        p.Simulated = frame.Simulated;
        p.MarkSimulated = false;
        p.Begin(g, scale, new SizeF((float)config.W, (float)config.H), palette, settings.Appearance);
        p.Background = config.Background;
        t.Widget!.Draw(p, view);
        p.FinishPanels();
        p.End(t.Surface.Dc, size);
        _png.SetLength(0);
        t.Surface.Bitmap!.Save(_png, ImageFormat.Png);
        return _png.ToArray();
    }

    public void Dispose()
    {
        foreach (var t in _targets.Values) t.Dispose();
        _targets.Clear();
        _png.Dispose();
    }

    public static string ThemeCss(HudPalette hud)
    {
        static string Hex(Color c) => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");
        static string App(string key) => Theme.ThemeManager.ColorOf(key, System.Windows.Media.Color.FromArgb(0, 0, 0, 0)) is { A: > 0 } c
            ? string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}") : "transparent";
        var lines = new[]
        {
            ("app-bg", App("Bg")), ("app-panel", App("Panel")), ("app-surface", App("Surface")), ("app-line", App("Line")), ("app-line-strong", App("LineStrong")),
            ("app-heading", App("Heading")), ("app-text", App("Text")), ("app-muted", App("TextMuted")), ("app-accent", App("Accent")),
            ("hud-bg", Hex(hud.Bg)), ("hud-surface", Hex(hud.Surface)), ("hud-text", Hex(hud.Text)), ("hud-heading", Hex(hud.Heading)), ("hud-muted", Hex(hud.Muted)),
            ("hud-line", Hex(hud.LineStrong)), ("hud-accent", Hex(hud.Accent)), ("hud-good", Hex(hud.Good)), ("hud-warn", Hex(hud.Warn)), ("hud-danger", Hex(hud.Danger)),
        };
        return ":root {\n" + string.Concat(lines.Where(l => l.Item2 != "transparent").Select(l => $"  --{l.Item1}: {l.Item2};\n")) + "}\n";
    }
}
