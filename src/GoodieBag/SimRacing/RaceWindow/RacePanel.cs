using System.Diagnostics;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GoodieBag.Config;
using GoodieBag.Overlay;
using GoodieBag.SimRacing.Hud;
using GoodieBag.SimRacing.Race;

namespace GoodieBag.SimRacing.RaceWindow;

internal sealed class RacePanel : IDisposable
{
    public const float TitleHeight = 24;

    private readonly HudPainter _painter = new();
    private readonly DibSurface _surface = new();
    private readonly Dictionary<string, (HudWidgetConfig Config, HudWidget Widget, HudOptions Options)> _widgets = new(StringComparer.OrdinalIgnoreCase);
    private long _lastDraw;
    private long _lastSeq = -1;

    public WriteableBitmap? Bitmap { get; private set; }
    public bool Dirty { get; set; } = true;
    public List<(string Id, RectangleF Box)> TabBoxes { get; } = new();
    public string? Shown { get; private set; }
    public double DrawMs { get; private set; }

    public bool Due(long sequence, int hz)
    {
        if (Dirty) return true;
        if (sequence == _lastSeq) return false;
        return Stopwatch.GetElapsedTime(_lastDraw).TotalMilliseconds >= 1000.0 / Math.Max(1, hz) - 4;
    }

    private (HudWidget Widget, HudOptions Options) WidgetFor(HudWidgetConfig config, HudWidgetDef def)
    {
        if (_widgets.TryGetValue(config.Id, out var w) && ReferenceEquals(w.Config, config)) return (w.Widget, w.Options);
        w = (config, def.Create(), new HudOptions(def, config));
        _widgets[config.Id] = w;
        return (w.Widget, w.Options);
    }

    public void Draw(IReadOnlyList<(HudWidgetConfig Config, HudWidgetDef Def)> tabs, string active, RaceState race, RaceHudSettings settings, HudPalette palette, int pixelW, int pixelH, double pixelsPerDip, double textScale, bool simulated)
    {
        long start = Stopwatch.GetTimestamp();
        var (config, def) = tabs.FirstOrDefault(t => string.Equals(t.Config.Id, active, StringComparison.OrdinalIgnoreCase));
        if (config is null) (config, def) = tabs[0];
        Shown = config.Id;
        var size = new System.Drawing.Size(Math.Max(1, pixelW), Math.Max(1, pixelH));
        var g = _surface.Ensure(size);
        float scale = (float)(pixelsPerDip * textScale);
        var layout = new SizeF(size.Width / scale, size.Height / scale);
        var p = _painter;
        p.Simulated = simulated;
        p.MarkSimulated = false;
        p.Headless = true;
        p.Begin(g, scale, layout, palette, settings.Appearance);
        p.Background = 100;
        p.Fill(new RectangleF(0, 0, layout.Width, layout.Height), p.P.Surface);
        var (widget, options) = WidgetFor(config, def);
        var view = new HudView { Frame = race.Frame, Race = race, Settings = settings, Config = config, Options = options, Def = def };
        p.Inset(0, TitleHeight, 0, 0);
        try { widget.Draw(p, view); }
        catch (Exception ex) { LogOnce(def.Kind, ex); }
        p.EndInset();
        p.FinishPanels();
        DrawTitle(p, tabs, config, def, layout.Width);
        p.End(_surface.Dc, size);
        if (Bitmap is null || Bitmap.PixelWidth != size.Width || Bitmap.PixelHeight != size.Height || Math.Abs(Bitmap.DpiX - 96 * pixelsPerDip) > 0.01)
            Bitmap = new WriteableBitmap(size.Width, size.Height, 96 * pixelsPerDip, 96 * pixelsPerDip, PixelFormats.Pbgra32, null);
        Bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, size.Width, size.Height), _surface.Bits, size.Width * 4 * size.Height, size.Width * 4);
        float k = (float)textScale;
        for (int i = 0; i < TabBoxes.Count; i++)
        {
            var (id, b) = TabBoxes[i];
            TabBoxes[i] = (id, new RectangleF(b.X * k, b.Y * k, b.Width * k, b.Height * k));
        }
        Dirty = false;
        _lastSeq = race.Frame.Sequence;
        _lastDraw = Stopwatch.GetTimestamp();
        DrawMs = DrawMs * 0.9 + Stopwatch.GetElapsedTime(start).TotalMilliseconds * 0.1;
    }

    private void DrawTitle(HudPainter p, IReadOnlyList<(HudWidgetConfig Config, HudWidgetDef Def)> tabs, HudWidgetConfig shown, HudWidgetDef def, float width)
    {
        TabBoxes.Clear();
        var bar = new RectangleF(0, 0, width, TitleHeight);
        p.Fill(bar, p.Raised(0.035));
        p.Line(0, TitleHeight - 0.5f, width, TitleHeight - 0.5f, p.P.Line, 1);
        var font = p.Label;
        if (tabs.Count > 1)
        {
            float x = 0;
            foreach (var (c, d) in tabs)
            {
                string label = p.LabelText(d.Name);
                float w = Math.Min(p.Measure(label, font) + 20, Math.Max(24, width - x));
                if (x >= width) break;
                var box = new RectangleF(x, 0, w, TitleHeight);
                bool on = ReferenceEquals(c, shown);
                p.Text(label, font, on ? p.P.Heading : p.P.Faint, new RectangleF(x + 10, 0, w - 20, TitleHeight), HAlign.Left, VAlign.Center);
                if (on) p.Fill(new RectangleF(x, TitleHeight - 2, w, 2), p.P.Accent);
                p.Line(x + w - 0.5f, 0, x + w - 0.5f, TitleHeight - 1, p.P.Line, 1);
                TabBoxes.Add((c.Id, box));
                x += w;
            }
            return;
        }
        var heading = p.Heading;
        string title = def.Name;
        p.Mark(8, TitleHeight / 2);
        float tx = 8 + p.MarkSize + 6;
        string text = p.LabelText(title);
        float room = Math.Max(0, width - tx - 8);
        string? right = heading?.Right;
        float titleW = Math.Min(p.Measure(text, font) + 4, right is null || width < 220 ? room : room * 0.62f);
        p.Text(text, font, p.P.Muted, new RectangleF(tx, 0, titleW, TitleHeight), HAlign.Left, VAlign.Center);
        if (right is not null && width >= 220)
            p.Text(right, p.Small, heading?.RightColor ?? p.P.Faint, new RectangleF(tx + titleW + 6, 0, Math.Max(0, width - 8 - tx - titleW - 6), TitleHeight), HAlign.Right, VAlign.Center);
    }

    private static readonly HashSet<string> Logged = new();

    private static void LogOnce(string kind, Exception ex)
    {
        if (Logged.Add(kind)) App.LogError(ex);
    }

    public void Forget(IEnumerable<string> keep)
    {
        var ids = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        foreach (var id in _widgets.Keys.Where(k => !ids.Contains(k)).ToList()) _widgets.Remove(id);
    }

    public void Dispose()
    {
        _painter.Dispose();
        _surface.Dispose();
        Bitmap = null;
    }
}
