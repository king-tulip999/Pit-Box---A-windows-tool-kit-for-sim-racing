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
        public readonly PresentGuard Drawn = new();
        public long LastSeq = -1;
        public long LastDraw;
        public byte[]? Last;

        public void Dispose()
        {
            Widget?.Dispose();
            Painter.Dispose();
            Surface.Dispose();
        }
    }

    private static readonly byte[] Empty = Array.Empty<byte>();
    private readonly Dictionary<(string Kind, int W, int H, bool Sample), Target> _targets = new();
    private readonly Dictionary<(string Kind, int W, int H, bool Sample), List<HudWebClient>> _groups = new();
    private readonly List<(string Kind, int W, int H, bool Sample)> _stale = new();
    private readonly Dictionary<string, HudWidgetConfig> _fallbacks = new(StringComparer.OrdinalIgnoreCase);
    private readonly MemoryStream _png = new();
    private readonly TelemetryFrame _sampleFrame = new();
    private DemoTelemetry? _sampleFeed;
    private RaceState? _sampleRace;
    private RaceHudSettings? _sampleSettings;
    private long _sampleSequence;

    public static IReadOnlyList<HudWebOverlay> Overlays { get; } = HudCatalog.All.Select(d => new HudWebOverlay(d.Kind, d.Name, d.W, d.H)).ToList();

    public int Render(IReadOnlyList<HudWebClient> clients, TelemetryFrame frame, RaceState race, RaceHudSettings settings, HudLayout? layout, HudPalette palette, bool dirty)
    {
        int fastest = 0;
        bool live = frame.Status == SimStatus.Connected;
        bool sampled = !live && AdvanceSample(clients, settings);
        foreach (var group in _groups.Values) group.Clear();
        foreach (var client in clients)
        {
            var key = (client.Kind, client.Width, client.Height, sampled && client.Sample);
            if (!_groups.TryGetValue(key, out var group)) _groups[key] = group = new List<HudWebClient>();
            group.Add(client);
        }
        _stale.Clear();
        foreach (var (key, group) in _groups)
            if (group.Count == 0) _stale.Add(key);
        foreach (var key in _stale)
        {
            _groups.Remove(key);
            if (_targets.Remove(key, out var gone)) gone.Dispose();
        }
        long now = Stopwatch.GetTimestamp();
        foreach (var (key, group) in _groups)
        {
            var (shown, shownRace) = key.Sample ? (_sampleFrame, _sampleRace!) : (frame, race);
            if (HudCatalog.Find(key.Kind) is not { } def) continue;
            if (!_targets.TryGetValue(key, out var t)) _targets[key] = t = new Target { Def = def };
            bool redraw = dirty || t.Last is null;
            var config = ConfigFor(def, layout);
            if (!ReferenceEquals(config, t.Config))
            {
                t.Config = config;
                t.Options = new HudOptions(def, config);
                t.Widget?.Dispose();
                t.Widget = def.Create();
                t.Last = null;
                redraw = true;
            }
            int hz = config.UpdateHz > 0 ? config.UpdateHz : def.Hz(t.Options!);
            fastest = Math.Max(fastest, hz);
            string? look = null;
            foreach (var client in group)
            {
                if (Math.Abs(config.Opacity - client.ToldOpacity) <= 0.01) continue;
                client.ToldOpacity = config.Opacity;
                look ??= string.Create(CultureInfo.InvariantCulture, $"{{\"type\":\"look\",\"opacity\":{Math.Clamp(config.Opacity / 100, 0, 1):0.###}}}");
                client.Say(look);
            }
            bool due = Stopwatch.GetElapsedTime(t.LastDraw, now).TotalMilliseconds >= 1000.0 / hz - 4;
            bool waited = !redraw && group.TrueForAll(c => c.Holding);
            if (redraw || due && t.LastSeq != shown.Sequence && !waited)
            {
                t.LastSeq = shown.Sequence;
                t.LastDraw = now;
                var bytes = Draw(t, shown, shownRace, settings, palette, (key.W, key.H));
                if (!ReferenceEquals(bytes, t.Last) && (t.Last is null || !bytes.AsSpan().SequenceEqual(t.Last))) t.Last = bytes;
            }
            if (t.Last is not { } last) continue;
            foreach (var client in group)
            {
                if (ReferenceEquals(client.Offered, last)) continue;
                client.Offered = last;
                client.Offer(last);
            }
        }
        return fastest;
    }

    private bool AdvanceSample(IReadOnlyList<HudWebClient> clients, RaceHudSettings settings)
    {
        bool wanted = false;
        foreach (var c in clients) wanted |= c.Sample;
        if (!wanted) return false;
        _sampleSettings = settings;
        if (_sampleFeed is null || _sampleRace is null)
        {
            _sampleFeed = new DemoTelemetry();
            _sampleRace = new RaceState(() => _sampleSettings!) { MapLoader = TrackMaps.TrackMapLibrary.ForRace };
            DemoTelemetry.WarmUp(_sampleRace, _sampleFrame, ref _sampleSequence);
        }
        _sampleFeed.Fill(_sampleFrame);
        _sampleFrame.Sequence = ++_sampleSequence;
        _sampleRace.Update(_sampleFrame);
        return true;
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
            _fallbacks[def.Kind] = fallback = new HudWidgetConfig { Id = "web-" + def.Kind, Kind = def.Kind, W = def.W, H = def.H, Background = def.Background };
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
        t.Widget!.Paint(p, view);
        p.FinishPanels();
        p.End(t.Surface.Dc, size);
        if (t.Drawn.Same(t.Surface.Bits, size.Width * size.Height * 4, new Point(size.Width, size.Height), 255) && t.Last is { Length: > 0 } unchanged) return unchanged;
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
            ("app-heading", App("Heading")), ("app-text", App("Text")), ("app-muted", App("TextMuted")), ("app-accent", App("Accent")), ("app-accent-soft", App("AccentSoft")), ("app-danger", App("Danger")),
            ("hud-bg", Hex(hud.Bg)), ("hud-surface", Hex(hud.Surface)), ("hud-text", Hex(hud.Text)), ("hud-heading", Hex(hud.Heading)), ("hud-muted", Hex(hud.Muted)),
            ("hud-line", Hex(hud.LineStrong)), ("hud-accent", Hex(hud.Accent)), ("hud-good", Hex(hud.Good)), ("hud-warn", Hex(hud.Warn)), ("hud-danger", Hex(hud.Danger)),
        };
        return ":root {\n" + string.Concat(lines.Where(l => l.Item2 != "transparent").Select(l => $"  --{l.Item1}: {l.Item2};\n")) + "}\n";
    }
}
