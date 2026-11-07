using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using PitBox.Config;
using PitBox.Overlay;
using Units = PitBox.Services.CapsUnits;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Hud;

internal enum HAlign { Left, Center, Right }

internal enum VAlign { Top, Center, Bottom }

internal sealed class HudPalette
{
    public Color Bg, Surface, SurfaceAlt, Text, Heading, Muted, Faint, Line, LineStrong;
    public Color Accent, AccentText, AccentSoft, OnAccent;
    public Color Good, GoodText, GoodSoft, Warn, WarnText, WarnSoft, Danger, DangerText, DangerSoft, Gold, Slate, Plum, Silver, Bronze;
    public bool Dark;
    public Color Player, PlayerText;
    public Color? Highlight, HighlightEdge;
    public bool SoftBands;
    public Color RowTint => Highlight ?? Player;
    public Color PickTint => Highlight ?? Accent;
    public Color Lapping = Color.FromArgb(0xF8, 0x71, 0x71), Lapped = Color.FromArgb(0x60, 0xA5, 0xFA);
    public Color Fastest = Color.FromArgb(0xB0, 0x6B, 0xFF);
    public Color ThrottleInput = Hex(0x3DDC84), BrakeInput = Hex(0xFF3B4E), ClutchInput = Hex(0x4C9AFF), SteerInput = Hex(0xC9D1DE), AbsInput = Hex(0xFFB020);
    public Color InputTrack = Hex(0x1A2130), TraceBack = Hex(0x111725), InputTicks = Hex(0x3A4456), RedZone = Hex(0x4A1622);
    public Color? MapTrack, MapEdge, MapPit, MapCars;

    public static readonly Color FlagGreen = Color.FromArgb(0x1E, 0xA8, 0x4A), FlagYellow = Color.FromArgb(0xFF, 0xD0, 0x1A),
        FlagBlue = Color.FromArgb(0x1F, 0x6F, 0xEB), FlagRed = Color.FromArgb(0xE0, 0x24, 0x24), FlagWhite = Color.FromArgb(0xF2, 0xF2, 0xF2),
        FlagBlack = Color.FromArgb(0x10, 0x10, 0x10), FlagOrange = Color.FromArgb(0xF2, 0x7A, 0x1A);

    private static int _themeAccent;

    public static Color? ThemeAccent
    {
        get => Volatile.Read(ref _themeAccent) is var argb && argb != 0 ? Color.FromArgb(argb) : null;
        set => Volatile.Write(ref _themeAccent, value?.ToArgb() ?? 0);
    }

    public static HudPalette Of(HudAppearance look) => HudThemes.For(look);

    public static HudPalette Default { get; } = new HudPalette
    {
        Dark = true,
        Bg = Hex(0x1E1E1E), Surface = Hex(0x161616), SurfaceAlt = Hex(0x303030), Text = Hex(0xE2E2E2), Heading = Hex(0xF5F5F5),
        Muted = Hex(0xB8B8B8), Faint = Hex(0x969696), Line = Hex(0x333333), LineStrong = Hex(0x3C3C3C),
        Accent = Hex(0xDB156B), AccentText = Hex(0xF5559A), AccentSoft = Hex(0x3A3A3A), OnAccent = Color.White,
        Good = Hex(0x7FC48E), GoodText = Hex(0x9ED4A8), GoodSoft = Hex(0x24352A), Warn = Hex(0xE8B04A), WarnText = Hex(0xF2CC80), WarnSoft = Hex(0x3A3020),
        Danger = Hex(0xE5484D), DangerText = Hex(0xF07C7F), DangerSoft = Hex(0x42201F), Gold = Hex(0xFACC15), Slate = Hex(0x7A7A7A), Plum = Hex(0xA855F7), Silver = Hex(0xC9CED6), Bronze = Hex(0xD99A63),
        Player = Hex(0xDB156B), PlayerText = Hex(0xFFFFFF), Highlight = Hex(0x6A6A6A), HighlightEdge = Hex(0xDB156B), SoftBands = true,
        InputTrack = Hex(0x2A2A2A), TraceBack = Hex(0x171717), InputTicks = Hex(0x444444), RedZone = Hex(0x4A1E22),
    };

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (int)Math.Round(a.A + (b.A - a.A) * t), (int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    public static Color Alpha(Color c, int alpha) => Color.FromArgb(Math.Clamp(alpha, 0, 255), c.R, c.G, c.B);

    public static Color On(Color bg) => 0.2126 * Lin(bg.R) + 0.7152 * Lin(bg.G) + 0.0722 * Lin(bg.B) > 0.22 ? Color.FromArgb(0x12, 0x12, 0x12) : Color.White;

    private static double Lin(int v) { double c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
}

internal sealed class HudPainter : IDisposable
{
    private const int MaxCached = 256;
    private readonly Dictionary<int, SolidBrush> _brushes = new();
    private readonly Dictionary<(int, float), Pen> _pens = new();
    private readonly Dictionary<(float, bool), Font> _fonts = new();
    private readonly Dictionary<(Font, string), float> _widths = new();
    private readonly Dictionary<(Font, string[]), float> _widest = new();
    private readonly StringFormat[] _formats = new StringFormat[18];
    private readonly Dictionary<(int, float), Pen> _flatPens = new();
    private Pen? _dashed;
    private readonly Bitmap _measureBitmap = new(1, 1);
    private readonly Graphics _measure;
    private float _textScale = 1;

    public HudPainter()
    {
        _measure = Graphics.FromImage(_measureBitmap);
        _measure.TextRenderingHint = TextRenderingHint.AntiAlias;
        _measure.TextContrast = 0;
    }

    public const int ClearTypeAlpha = 190;

    public Graphics G { get; private set; } = null!;
    public HudPalette P { get; private set; } = HudPalette.Default;
    public HudAppearance Look { get; private set; } = new();
    public RectangleF Bounds { get; private set; }
    public float Density { get; private set; } = 1;
    public bool Simulated { get; set; }
    public bool ReducedMotion => Look.ReducedMotion;

    public List<(string Key, string Id, RectangleF Box)>? Items { get; set; }

    public void Item(string key, string id, RectangleF box) => Items?.Add((key, id, new RectangleF(box.X + _inset.X, box.Y + _inset.Y, box.Width, box.Height)));

    public List<(string Id, RectangleF Box)>? Regions { get; set; }

    public void Region(string id, RectangleF box) => Regions?.Add((id, new RectangleF(box.X + _inset.X, box.Y + _inset.Y, box.Width, box.Height)));

    private PointF _inset, _far;
    private SizeF _layout;
    private readonly Stack<(PointF Inset, PointF Far, RectangleF Outer, PointF Step)> _insets = new();

    public void Inset(float left, float top, float right, float bottom)
    {
        _insets.Push((_inset, _far, Bounds, new PointF(left, top)));
        if (left == 0 && top == 0 && right == 0 && bottom == 0) return;
        G.TranslateTransform(left, top);
        _inset = new PointF(_inset.X + left, _inset.Y + top);
        _far = new PointF(_far.X + right, _far.Y + bottom);
        Bounds = new RectangleF(0, 0, Bounds.Width - left - right, Bounds.Height - top - bottom);
    }

    public RectangleF EndInset()
    {
        if (_insets.Count == 0) return Bounds;
        var (inset, far, outer, step) = _insets.Pop();
        if (step != PointF.Empty) G.TranslateTransform(-step.X, -step.Y);
        _inset = inset;
        _far = far;
        Bounds = outer;
        return Bounds;
    }

    public SizeF? Needed { get; private set; }

    public bool Spilled { get; private set; }

    public float MinWidth { get; private set; }

    public void NeedBelow(RectangleF area, float content) => Need(Bounds.Width, area.Y + content + Math.Max(0, Bounds.Bottom - area.Bottom));

    public void NeedWidth(float width) => MinWidth = Math.Max(MinWidth, MathF.Ceiling(width + _inset.X + _far.X));

    public void Need(float width, float height)
    {
        var abs = new SizeF(MathF.Ceiling(width + _inset.X + _far.X), MathF.Ceiling(height + _inset.Y + _far.Y));
        Needed = Needed is { } had ? new SizeF(Math.Max(had.Width, abs.Width), Math.Max(had.Height, abs.Height)) : abs;
    }

    public bool IsPreview { get; set; }

    public List<(string Key, string Id)>? Overflow { get; set; }

    public Font Tiny => Font(10f, false);
    public Font Label => LabelFont(10.5f);
    public Font Small => Font(11.5f, false);
    public Font SmallBold => Font(11.5f, true);
    public Font Body => Font(13.5f, false);
    public Font Bold => Font(13.5f, true);
    public Font Big => Font(20f, true);
    public Font Huge => Font(34f, true);

    public void Begin(Graphics g, float scale, SizeF layoutSize, HudPalette palette, HudAppearance look)
    {
        G = g;
        P = palette;
        Look = look;
        float textScale = look.FontScale / 100f;
        if (Math.Abs(textScale - _textScale) > 0.001f) { ClearFonts(); _textScale = textScale; }
        Density = look.Density switch { HudDensity.Compact => 0.8f, HudDensity.Expanded => 1.25f, _ => 1f };
        _scale = scale > 0 ? scale : 1;
        Background = null;
        g.ResetTransform();
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.TextContrast = 0;
        g.ScaleTransform(scale, scale);
        Bounds = new RectangleF(0, 0, layoutSize.Width, layoutSize.Height);
        _layout = layoutSize;
        _inset = PointF.Empty;
        _far = PointF.Empty;
        _insets.Clear();
        Needed = null;
        Spilled = false;
        MinWidth = 0;
        Items?.Clear();
        _panels.Clear();
        _sharp.Clear();
        Regions?.Clear();
        Overflow?.Clear();
    }

    private float _scale = 1;

    public int? Background { get; set; }

    public float Snap(float v) => MathF.Round(v * _scale) / _scale;

    public float PixelScale => _scale;

    public RectangleF Snap(RectangleF r)
    {
        float l = Snap(r.Left), t = Snap(r.Top), rt = Snap(r.Right), b = Snap(r.Bottom);
        return RectangleF.FromLTRB(l, t, Math.Max(rt, l + 1 / _scale), Math.Max(b, t + 1 / _scale));
    }

    private float SnapLine(float v, float width)
    {
        float w = MathF.Max(1, MathF.Round(width * _scale));
        float d = v * _scale;
        return (w % 2 == 1 ? MathF.Floor(d) + 0.5f : MathF.Round(d)) / _scale;
    }

    private readonly Dictionary<Font, Font> _boldOf = new(ReferenceEqualityComparer.Instance);

    public Font BoldOf(Font font)
    {
        if (_boldOf.TryGetValue(font, out var known)) return known;
        var bold = font;
        foreach (var (key, value) in _fonts)
            if (ReferenceEquals(value, font)) { bold = key.Item1 > 0 ? Font(key.Item1, true) : font; break; }
        return _boldOf[font] = bold;
    }

    public Font Font(float px, bool bold)
    {
        px = MathF.Round(px * 2) / 2;
        var key = (px, bold);
        if (_fonts.TryGetValue(key, out var f)) return f;
        f = new Font(bold ? Semibold : Regular, px * _textScale, FontStyle.Regular, GraphicsUnit.Pixel);
        _fonts[key] = f;
        _fontSpecs[f] = (px * _textScale, bold);
        return f;
    }

    private readonly Dictionary<Font, float> _lineHeights = new(), _textHeights = new();

    public float LineHeight(Font font) => _lineHeights.TryGetValue(font, out var h) ? h : _lineHeights[font] = font.Height;

    private float TextHeight(Font font) => _textHeights.TryGetValue(font, out var h) ? h : _textHeights[font] = font.GetHeight();

    private static readonly FontFamily Regular = OverlayFonts.Regular, Semibold = OverlayFonts.Semibold;

    private Font LabelFont(float px)
    {
        var key = (-px, false);
        if (_fonts.TryGetValue(key, out var f)) return f;
        f = new Font(Semibold, px * _textScale, FontStyle.Regular, GraphicsUnit.Pixel);
        _fonts[key] = f;
        _fontSpecs[f] = (px * _textScale, true);
        return f;
    }

    public string LabelText(string label) => Caps(label);

    private readonly Dictionary<string, string> _caps = new();

    private string Caps(string label)
    {
        if (_caps.TryGetValue(label, out var c)) return c;
        if (_caps.Count >= 512) _caps.Clear();
        return _caps[label] = label.ToUpperInvariant();
    }

    private readonly Dictionary<Font, float> _ascents = new();

    public float Ascent(Font font)
    {
        if (_ascents.TryGetValue(font, out var a)) return a;
        var family = font.FontFamily;
        return _ascents[font] = font.Size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
    }

    public SolidBrush Brush(Color c)
    {
        int argb = c.ToArgb();
        if (_brushes.TryGetValue(argb, out var b)) return b;
        if (_brushes.Count >= MaxCached) { foreach (var x in _brushes.Values) x.Dispose(); _brushes.Clear(); }
        return _brushes[argb] = new SolidBrush(c);
    }

    public Pen Pen(Color c, float width = 1)
    {
        var key = (c.ToArgb(), width);
        if (_pens.TryGetValue(key, out var p)) return p;
        if (_pens.Count >= MaxCached) { foreach (var x in _pens.Values) x.Dispose(); _pens.Clear(); }
        return _pens[key] = new Pen(c, width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
    }

    public Pen FlatPen(Color c, float width)
    {
        var key = (c.ToArgb(), width);
        if (_flatPens.TryGetValue(key, out var p)) return p;
        if (_flatPens.Count >= MaxCached) { foreach (var x in _flatPens.Values) x.Dispose(); _flatPens.Clear(); }
        return _flatPens[key] = new Pen(c, width) { LineJoin = LineJoin.Miter, StartCap = LineCap.Flat, EndCap = LineCap.Flat };
    }

    private StringFormat Format(HAlign h, VAlign v, bool ellipsis)
    {
        int i = (int)h * 6 + (int)v * 2 + (ellipsis ? 1 : 0);
        if (_formats[i] is { } f) return f;
        f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        f.Alignment = h switch { HAlign.Center => StringAlignment.Center, HAlign.Right => StringAlignment.Far, _ => StringAlignment.Near };
        f.LineAlignment = v switch { VAlign.Center => StringAlignment.Center, VAlign.Bottom => StringAlignment.Far, _ => StringAlignment.Near };
        f.Trimming = ellipsis ? StringTrimming.EllipsisCharacter : StringTrimming.None;
        return _formats[i] = f;
    }

    public void Text(string text, Font font, Color color, RectangleF box, HAlign h = HAlign.Left, VAlign v = VAlign.Center, bool ellipsis = true)
    {
        if (text.Length == 0 || box.Width <= 0) return;
        if (ellipsis && Measure(text, font) > box.Width + 0.5f) text = Trimmed(text, font, box.Width);
        if (text.Length == 0) return;
        float lh = TextHeight(font);
        float top = v switch { VAlign.Center => box.Y + (box.Height - lh) / 2, VAlign.Bottom => box.Bottom - lh, _ => box.Y };
        if (top + _inset.Y < -0.5f || top + lh * 0.8f + _inset.Y > _layout.Height + 0.5f) Spilled = true;
        if (SharpText.Available && _fontSpecs.TryGetValue(font, out var spec))
        {
            float x = h == HAlign.Left ? Snap(box.X) : box.X, right = h == HAlign.Right ? Snap(box.Right) : box.Right;
            var align = h switch { HAlign.Center => TextAlign.Center, HAlign.Right => TextAlign.Right, _ => TextAlign.Left };
            var at = new RectangleF((x + _inset.X) * _scale, (Snap(top) + _inset.Y) * _scale, (right - x) * _scale, (lh + 2) * _scale);
            _sharp.Add(text, spec.Size * _scale, spec.Bold ? SharpText.Semibold : SharpText.Regular, color, at, align);
            return;
        }
        var hint = PanelAlpha >= ClearTypeAlpha ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAlias;
        if (G.TextRenderingHint != hint) G.TextRenderingHint = hint;
        G.PixelOffsetMode = PixelOffsetMode.None;
        float left = h == HAlign.Left ? Snap(box.X) : box.X, width = box.Right - left;
        if (h == HAlign.Right) width = Snap(box.Right) - left;
        G.DrawString(text, font, Brush(color), new RectangleF(left, Snap(top), width, lh + 2), Format(h, VAlign.Top, false));
        G.PixelOffsetMode = PixelOffsetMode.Half;
    }

    private readonly Dictionary<(Font, string, int), string> _trimmed = new();

    private string Trimmed(string text, Font font, float width)
    {
        var key = (font, text, (int)width);
        if (_trimmed.TryGetValue(key, out var done)) return done;
        if (_trimmed.Count >= 512) _trimmed.Clear();
        int lo = 0, hi = text.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (Measure(text[..mid].TrimEnd() + "…", font) <= width + 0.5f) lo = mid; else hi = mid - 1;
        }
        string fitted = lo > 0 ? text[..lo].TrimEnd() + "…" : Measure("…", font) <= width + 0.5f ? "…" : "";
        return _trimmed[key] = fitted;
    }

    public float Widest(string[] samples, Font font)
    {
        var key = (font, samples);
        if (_widest.TryGetValue(key, out var known)) return known;
        if (_widest.Count >= 64) _widest.Clear();
        float most = 0;
        foreach (var s in samples) most = Math.Max(most, Measure(s, font));
        return _widest[key] = most;
    }

    public float Measure(string text, Font font)
    {
        if (text.Length == 0) return 0;
        var key = (font, text);
        if (_widths.TryGetValue(key, out var w)) return w;
        if (_widths.Count >= 2048) _widths.Clear();
        if (_fontSpecs.TryGetValue(font, out var spec) && SharpText.Measure(text, spec.Size, spec.Bold ? SharpText.Semibold : SharpText.Regular) is var sharp and >= 0) return _widths[key] = sharp;
        w = _measure.MeasureString(text, font, PointF.Empty, Format(HAlign.Left, VAlign.Top, false)).Width;
        return _widths[key] = w;
    }

    public void Panel() => Panel(Bounds);

    private readonly List<(RectangleF Box, Color Color)> _panels = new();

    public void Panel(RectangleF r)
    {
        int alpha = PanelAlpha;
        float radius = Math.Min(Look.Corner, 4);
        r = Snap(r);
        if (r.Width <= 1 || r.Height <= 1) return;
        if (radius <= 0.5f && !(Simulated && MarkSimulated))
        {
            if (alpha > 0) G.FillRectangle(Brush(HudPalette.Alpha(PanelColor, alpha)), r);
            if (Look.Outline) _panels.Add((new RectangleF(r.X + _inset.X, r.Y + _inset.Y, r.Width, r.Height), HudPalette.Alpha(P.LineStrong, Math.Max(60, alpha * 2 / 3))));
            return;
        }
        var path = Shape(new SizeF(r.Width - 1, r.Height - 1), radius);
        float ox = r.X + 0.5f, oy = r.Y + 0.5f;
        G.TranslateTransform(ox, oy);
        if (alpha > 0) G.FillPath(Brush(HudPalette.Alpha(PanelColor, alpha)), path);
        if (Look.Outline)
        {
            G.DrawPath(Pen(HudPalette.Alpha(P.LineStrong, Math.Max(60, alpha * 2 / 3)), 1), path);
        }
        bool sim = Simulated && MarkSimulated;
        if (sim) G.DrawPath(DashedPen(), path);
        G.TranslateTransform(-ox, -oy);
        if (sim)
        {
            var tag = new RectangleF(r.Right - 30, r.Y + 1.5f, 26, 9);
            Fill(tag, HudPalette.Alpha(P.Warn, 200), 2);
            Text("SIM", Font(7.5f, true), HudPalette.On(P.Warn), tag, HAlign.Center, VAlign.Center, false);
        }
    }

    public bool MarkSimulated { get; set; } = true;

    public void FinishPanels()
    {
        float px = 1 / _scale;
        for (int i = 0; i < _panels.Count; i++)
        {
            var (r, c) = _panels[i];
            bool top = true, bottom = true;
            for (int j = 0; j < _panels.Count; j++)
            {
                var o = _panels[j].Box;
                if (j == i || Math.Abs(o.Left - r.Left) > px || Math.Abs(o.Right - r.Right) > px) continue;
                if (Math.Abs(o.Bottom - r.Top) <= px) top = false;
                if (Math.Abs(o.Top - r.Bottom) <= px) bottom = false;
            }
            Fill(new RectangleF(r.Left, r.Top, px, r.Height), c);
            Fill(new RectangleF(r.Right - px, r.Top, px, r.Height), c);
            if (top) Fill(new RectangleF(r.Left + px, r.Top, r.Width - px * 2, px), c);
            if (bottom) Fill(new RectangleF(r.Left + px, r.Bottom - px, r.Width - px * 2, px), c);
        }
        _panels.Clear();
    }

    private Pen DashedPen()
    {
        var c = HudPalette.Alpha(P.Warn, 170);
        if (_dashed is { } d && d.Color == c) return d;
        _dashed?.Dispose();
        return _dashed = new Pen(c, 1) { DashStyle = DashStyle.Dash };
    }

    public void Fill(RectangleF r, Color c, float radius = 0)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        r = Snap(r);
        if (radius <= 0.5f) { G.FillRectangle(Brush(c), r); return; }
        var path = Shape(r.Size, radius);
        G.TranslateTransform(r.X, r.Y);
        G.FillPath(Brush(c), path);
        G.TranslateTransform(-r.X, -r.Y);
    }

    private readonly Dictionary<(int, int, float), LinearGradientBrush> _gradients = new();

    public void Gradient(RectangleF r, Color from, Color to)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        var key = (from.ToArgb(), to.ToArgb(), MathF.Round(r.Width, 1));
        if (!_gradients.TryGetValue(key, out var brush))
        {
            if (_gradients.Count >= 64) { foreach (var x in _gradients.Values) x.Dispose(); _gradients.Clear(); }
            brush = _gradients[key] = new LinearGradientBrush(new PointF(0, 0), new PointF(key.Item3, 0), from, to) { WrapMode = WrapMode.TileFlipX };
        }
        G.TranslateTransform(r.X, r.Y);
        G.FillRectangle(brush, 0, 0, r.Width, r.Height);
        G.TranslateTransform(-r.X, -r.Y);
    }

    public void CenteredCaps(string text, Font font, Color color, RectangleF box)
    {
        if (text.Length == 0 || box.Width <= 0) return;
        if (SharpText.Available && _fontSpecs.TryGetValue(font, out var spec))
        {
            float size = spec.Size * _scale;
            int weight = spec.Bold ? SharpText.Semibold : SharpText.Regular;
            if (SharpText.Ink(text, size, weight) is { } ink)
            {
                float bx = (box.X + _inset.X) * _scale, by = (box.Y + _inset.Y) * _scale, bw = box.Width * _scale, bh = box.Height * _scale;
                float left = MathF.Round(bx + (bw - (ink.Right - ink.Left)) / 2 - ink.Left);
                float baseline = MathF.Round(by + (bh - (ink.Bottom - ink.Top)) / 2 - ink.Top + ink.Baseline);
                var at = new RectangleF(left, baseline - ink.Baseline, ink.Right + 4, ink.Bottom + 2);
                _sharp.Add(text, size, weight, color, at, TextAlign.Left, crisp: true);
                return;
            }
        }
        Text(text, font, color, box, HAlign.Center, VAlign.Center, false);
    }

    public void Outline(RectangleF r, Color c, float width = 1, float radius = 0)
    {
        r = RectangleF.FromLTRB(SnapLine(r.Left, width), SnapLine(r.Top, width), SnapLine(r.Right, width), SnapLine(r.Bottom, width));
        if (radius <= 0.5f) { G.DrawRectangle(Pen(c, width), r.X, r.Y, r.Width, r.Height); return; }
        var path = Shape(r.Size, radius);
        G.TranslateTransform(r.X, r.Y);
        G.DrawPath(Pen(c, width), path);
        G.TranslateTransform(-r.X, -r.Y);
    }

    private readonly Dictionary<(float, float, float), GraphicsPath> _shapes = new();

    private GraphicsPath Shape(SizeF size, float radius)
    {
        var key = (MathF.Round(size.Width, 1), MathF.Round(size.Height, 1), MathF.Round(radius, 1));
        if (_shapes.TryGetValue(key, out var path)) return path;
        if (_shapes.Count >= MaxCached) { foreach (var s in _shapes.Values) s.Dispose(); _shapes.Clear(); }
        return _shapes[key] = Rounded(new RectangleF(0, 0, key.Item1, key.Item2), Math.Min(key.Item3, Math.Min(key.Item1, key.Item2) / 2));
    }

    public void Line(float x1, float y1, float x2, float y2, Color c, float width = 1)
    {
        if (y1 == y2) y1 = y2 = SnapLine(y1, width);
        else if (x1 == x2) x1 = x2 = SnapLine(x1, width);
        G.DrawLine(Pen(c, width), x1, y1, x2, y2);
    }

    public void Dot(float cx, float cy, float r, Color c) => G.FillEllipse(Brush(c), cx - r, cy - r, r * 2, r * 2);

    public void Bar(RectangleF r, double value, Color fill, Color? track = null)
    {
        Fill(r, track ?? HudPalette.Alpha(P.LineStrong, 110), Corner(Math.Min(3, r.Height / 2)));
        float w = (float)(Math.Clamp(value, 0, 1) * r.Width);
        if (w > 0.5f) Fill(new RectangleF(r.X, r.Y, w, r.Height), fill, Corner(Math.Min(3, r.Height / 2)));
    }

    public void Pill(RectangleF r, string text, Color bg, Font font, Color? fg = null)
    {
        Fill(r, bg, Corner(Math.Min(4, r.Height / 2)));
        Text(text, font, fg ?? HudPalette.On(bg), r, HAlign.Center, VAlign.Center);
    }

    public Color LabelColor => P.Muted;

    public void Stat(RectangleF r, string label, string value, Color? valueColor = null, Font? valueFont = null, HAlign align = HAlign.Left)
    {
        float lh = LineHeight(Label) + 1;
        Text(LabelText(label), Label, LabelColor, new RectangleF(r.X, r.Y, r.Width, lh), align, VAlign.Top);
        Text(value, valueFont ?? Bold, valueColor ?? P.Heading, new RectangleF(r.X, r.Y + lh, r.Width, r.Height - lh), align, VAlign.Center);
    }

    public Color PanelColor => P.Surface;

    public int PanelAlpha => (int)Math.Round((Background ?? Look.Background) * 2.55);

    public float RowGap => 0;

    public Color Raised(double step) => HudPalette.Mix(PanelColor, P.Dark ? Color.White : Color.Black, step);

    public Color Sunken(double step) => HudPalette.Mix(PanelColor, P.Dark ? Color.Black : P.Text, P.Dark ? step : step * 0.35);

    public Color Solid(Color c, int floor = 70) => HudPalette.Alpha(c, Math.Max(floor, PanelAlpha));

    public void Tile(RectangleF r, Color? tint = null)
    {
        var fill = tint is { } t ? HudPalette.Mix(Raised(P.Dark ? 0.05 : 0.04), t, 0.18) : Raised(P.Dark ? 0.05 : 0.04);
        Fill(r, Solid(fill), CornerRadius(4));
    }

    public void Box(RectangleF r)
    {
        Tile(r);
        Outline(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), HudPalette.Alpha(P.LineStrong, 170), 1, CornerRadius(4));
    }

    public float BoxPad => 3 * Density;

    public void BoxedStat(RectangleF r, string label, string value, Color? valueColor = null, Font? valueFont = null)
    {
        Box(r);
        var font = valueFont ?? Bold;
        var inner = r.Inflated(-6 * Density, -BoxPad);
        float h = Math.Min(inner.Height, ValueStatHeight(font));
        ValueStat(new RectangleF(inner.X, inner.Y + (inner.Height - h) / 2, inner.Width, h), label, value, valueColor, font, HAlign.Center);
    }

    public float BoxedStatHeight(Font? valueFont = null) => ValueStatHeight(valueFont ?? Bold) + BoxPad * 2;

    public float CornerRadius(float r) => Look.Corner <= 0 || r <= 2 ? 0 : 2;

    private float Corner(float r) => Look.Corner <= 0 ? 0 : r;

    public void ValueStat(RectangleF r, string label, string value, Color? valueColor = null, Font? valueFont = null, HAlign align = HAlign.Left)
    {
        var font = valueFont ?? Big;
        float lh = LineHeight(Label) + 1;
        if (r.Height - lh < LineHeight(font) * 0.85f) font = r.Height - lh >= LineHeight(Big) * 0.85f ? Big : Bold;
        if (r.Height - lh < LineHeight(font) * 0.7f)
        {
            Text(value, font, valueColor ?? P.Heading, r, align, VAlign.Center, false);
            return;
        }
        float vh = Math.Min(LineHeight(font), r.Height - lh);
        Number(value, font, valueColor ?? P.Heading, new RectangleF(r.X, r.Y, r.Width, vh), align, VAlign.Bottom);
        Text(LabelText(label), Label, LabelColor, new RectangleF(r.X, r.Y + vh, r.Width, lh), align, VAlign.Top);
    }

    private readonly Dictionary<string, (string Number, string Unit)?> _numberParts = new(StringComparer.Ordinal);

    public void Number(string value, Font font, Color color, RectangleF box, HAlign align = HAlign.Left, VAlign valign = VAlign.Center)
    {
        if (!_numberParts.TryGetValue(value, out var parts))
        {
            if (_numberParts.Count >= 512) _numberParts.Clear();
            int cut = UnitStart(value);
            _numberParts[value] = parts = cut < 0 ? null : (value[..cut].TrimEnd(), value[cut..].Trim());
        }
        if (parts is not { } split) { Text(value, font, color, box, align, valign, false); return; }
        Number(split.Number, split.Unit, font, color, box, align, valign);
    }

    public void Number(string number, string unit, Font font, Color color, RectangleF box, HAlign align = HAlign.Left, VAlign valign = VAlign.Center)
    {
        if (unit.Length == 0) { Text(number, font, color, box, align, valign, false); return; }
        var unitFont = font.Size > Bold.Size * 1.2f ? SmallBold : Small;
        float nw = Measure(number, font), uw = Measure(unit, unitFont) + 2, total = nw + uw;
        float x = align switch { HAlign.Right => box.Right - total, HAlign.Center => box.X + (box.Width - total) / 2, _ => box.X };
        if (total > box.Width) { Text(number + " " + unit, font, color, box, align, valign, true); return; }
        Text(number, font, color, new RectangleF(x, box.Y, nw + 1, box.Height), HAlign.Left, valign, false);
        float drop = LineHeight(font) - LineHeight(unitFont);
        var ub = valign switch
        {
            VAlign.Top => new RectangleF(x + nw + 2, box.Y + drop * 0.78f, uw, LineHeight(unitFont)),
            VAlign.Bottom => new RectangleF(x + nw + 2, box.Bottom - LineHeight(unitFont) - drop * 0.22f, uw, LineHeight(unitFont)),
            _ => new RectangleF(x + nw + 2, box.Y + box.Height / 2 + LineHeight(font) / 2 - LineHeight(unitFont) - drop * 0.22f, uw, LineHeight(unitFont)),
        };
        Text(unit, unitFont, HudPalette.Mix(color, P.Muted, 0.35), ub, HAlign.Left, VAlign.Top, false);
    }

    public Font Fitting(string text, float width, float height, params Font[] fonts)
    {
        foreach (var f in fonts)
            if (Measure(text, f) <= width && LineHeight(f) <= height) return f;
        return fonts[^1];
    }

    public string Shortened(string label, Font font, float width)
    {
        if (Measure(label, font) <= width) return label;
        int paren = label.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? label[..paren] : label;
    }

    private static readonly string[] ValueUnits = { " " + Units.L, " " + Units.Gal, " " + Units.Kmh, " " + Units.Mph, " " + Units.KPa, " " + Units.Psi, " " + Units.S, "°C", "°F", "%", " laps", " lap" };

    internal static int UnitStart(string value)
    {
        foreach (var u in ValueUnits)
            if (value.EndsWith(u, StringComparison.Ordinal) && value.Length > u.Length && char.IsDigit(value[value.Length - u.Length - 1]))
                return value.Length - u.Length;
        return -1;
    }

    public void Divider(float x, float top, float bottom) => Line(x, top, x, bottom, HudPalette.Alpha(P.LineStrong, 110), 1);

    public float ValueStatHeight(Font? valueFont = null) => LineHeight((valueFont ?? Big)) + LineHeight(Label) + 3;

    public void Strip(RectangleF r, IReadOnlyList<(string Label, string Value, Color? Color)> items, string? key = null, IReadOnlyList<string>? ids = null)
    {
        if (items.Count == 0) return;
        var inner = r.Inflated(-8 * Density, 0);
        var valueFont = SmallBold;
        var labelFont = LabelFont(11.5f);
        float baseline = r.Y + (r.Height - TextHeight(valueFont)) / 2 + Ascent(valueFont);
        float valueTop = baseline - Ascent(valueFont), labelTop = baseline - Ascent(labelFont);
        float[] widths = new float[items.Count];
        for (int i = 0; i < items.Count; i++)
            widths[i] = (items[i].Label.Length > 0 ? Measure(Caps(items[i].Label), labelFont) + 4 : 0) + Measure(items[i].Value, valueFont);
        var (shown, gap) = FitStrip(widths, inner.Width, 14 * Density);
        float natural = 16 * Density + 14 * Density * (items.Count - 1);
        foreach (float w in widths) natural += w + 4 * Density;
        NeedWidth(r.X + natural);
        float x = inner.X;
        for (int i = 0; i < shown; i++)
        {
            var (label, value, color) = items[i];
            float lw = label.Length > 0 ? Measure(Caps(label), labelFont) + 4 : 0;
            if (lw > 0) Text(Caps(label), labelFont, LabelColor, new RectangleF(x, labelTop, lw, TextHeight(labelFont)), HAlign.Left, VAlign.Top, false);
            float room = (i == shown - 1 ? inner.Right - x : widths[i] + gap) - lw;
            Text(value, valueFont, color ?? P.Heading, new RectangleF(x + lw, valueTop, Math.Max(0, room), TextHeight(valueFont)), HAlign.Left, VAlign.Top);
            if (key is not null && ids is not null && i < ids.Count) Item(key, ids[i], new RectangleF(x - 3, r.Y, widths[i] + 6, r.Height));
            x += widths[i] + gap;
        }
        if (key is not null && ids is not null)
            for (int i = shown; i < items.Count && i < ids.Count; i++) Overflow?.Add((key, ids[i]));
    }

    internal static (int Shown, float Gap) FitStrip(IReadOnlyList<float> widths, float room, float minGap)
    {
        float used = 0, sum = 0;
        int shown = 0;
        foreach (float w in widths)
        {
            float need = used + (shown > 0 ? minGap : 0) + w;
            if (need > room + 0.5f) break;
            used = need;
            sum += w;
            shown++;
        }
        return (shown, shown > 1 ? (room - sum) / (shown - 1) : 0);
    }

    public float StripHeight => LineHeight(SmallBold) + 7 * Density;

    public float PillHeight => StripHeight + 4 * Density;

    public float PieceGap => 0;

    public Color BandColor(int? opacity = null) => HudPalette.Alpha(P.Bg, (int)Math.Round((opacity ?? 70) * 2.55));

    public void Soft(RectangleF r, Color c, int alpha = 80, bool stripe = true)
    {
        Fill(r, Solid(Raised(P.Dark ? 0.04 : 0.03), 150));
        Fade(r, c, alpha);
        if (stripe) Fill(new RectangleF(r.X, r.Y, 3, r.Height), c);
    }

    public void Fade(RectangleF r, Color c, int alpha) => Fill(r, HudPalette.Alpha(c, alpha * 2 / 5));

    public void Chip(RectangleF r, Color c)
    {
        Fill(r, HudPalette.Alpha(c, 55));
        Fill(new RectangleF(r.X, r.Bottom - 2, r.Width, 2), c);
    }

    public Color TitleColor => HudPalette.Mix(P.Muted, P.Heading, 0.45);

    public float MarkSize => MathF.Round(6 * Density + 1);

    public void Mark(float x, float centerY, Color? color = null)
    {
        float s = MarkSize;
        Fill(new RectangleF(x, centerY - s / 2, s, s), color ?? P.Accent);
    }

    public void TitleBar(RectangleF r, string title, string? right = null, Color? rightColor = null)
    {
        Panel(r);
        TitleRow(r, title, right, rightColor);
    }

    private void TitleRow(RectangleF r, string title, string? right, Color? rightColor)
    {
        var inner = r.Inflated(-8 * Density, 0);
        var font = LabelFont(11f);
        Mark(inner.X, r.Y + r.Height / 2);
        float x = inner.X + MarkSize + 6 * Density;
        string text = Caps(title);
        float room = Math.Max(0, inner.Right - x);
        float titleW = Math.Min(Measure(text, font) + 4, room * (right is null ? 1 : 0.62f));
        Text(text, font, TitleColor, new RectangleF(x, inner.Y, titleW, inner.Height), HAlign.Left, VAlign.Center);
        if (right is not null) Text(right, Small, rightColor ?? P.Muted, new RectangleF(x + titleW + 6, inner.Y, Math.Max(0, inner.Right - x - titleW - 6), inner.Height), HAlign.Right, VAlign.Center);
    }

    public bool TitleRowShown { get; set; } = true;

    public RectangleF Header(string title, string? right = null, Color? rightColor = null)
    {
        float pillH = PillHeight, padH = 8 * Density;
        Panel(Bounds);
        if (!TitleRowShown) return Bounds.Inflated(-padH, -6 * Density);
        TitleRow(new RectangleF(0, 0, Bounds.Width, pillH), title, right, rightColor);
        Line(1, pillH, Bounds.Width - 1, pillH, HudPalette.Alpha(P.LineStrong, 160), 1);
        var body = new RectangleF(0, pillH + 1, Bounds.Width, Bounds.Height - pillH - 1);
        return body.Inflated(-padH, -6 * Density);
    }

    public void AccentRule(float x1, float x2, float y) => Line(x1, y, x2, y, HudPalette.Alpha(P.Accent, P.Dark ? 70 : 90), 1);

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        if (radius <= 0.5f) { path.AddRectangle(r); return path; }
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private SessionInfo? _classSession;
    private HudPalette? _classPalette;
    private readonly Dictionary<int, Color> _classColors = new();

    public Color ClassColor(SessionInfo s, int classId)
    {
        if (!ReferenceEquals(s, _classSession) || !ReferenceEquals(P, _classPalette))
        {
            _classColors.Clear();
            _classSession = s;
            _classPalette = P;
            var classes = s.Drivers.Where(d => d.Races).GroupBy(d => d.ClassId)
                .OrderBy(g => g.First().ClassEstLapTime ?? double.MaxValue).ToList();
            for (int i = 0; i < classes.Count; i++)
            {
                int color = classes[i].First().ClassColor;
                _classColors[classes[i].Key] = classes.Count <= 1 ? P.Accent
                    : color == 0 ? P.Slate
                    : Color.FromArgb(255, (color >> 16) & 0xFF, (color >> 8) & 0xFF, color & 0xFF);
            }
        }
        return _classColors.TryGetValue(classId, out var c) ? c : P.Faint;
    }

    public Color Readable(Color c)
    {
        var bg = PanelColor;
        for (double t = 0; t <= 1.0001; t += 0.1)
        {
            var m = HudPalette.Mix(c, P.Text, t);
            if (Contrast(m, bg) >= 3) return m;
        }
        return P.Text;
    }

    private static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double Lin(int v) { double x = v / 255.0; return x <= 0.04045 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        }
        double la = L(a), lb = L(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private void ClearFonts()
    {
        foreach (var f in _fonts.Values) f.Dispose();
        _fonts.Clear();
        _fontSpecs.Clear();
        _boldOf.Clear();
        _lineHeights.Clear();
        _textHeights.Clear();
        _ascents.Clear();
        _widths.Clear();
        _widest.Clear();
    }

    public void Slant(RectangleF r, Color c, params float[] xy)
    {
        if (r.Width <= 0 || r.Height <= 0 || xy.Length < 6) return;
        var points = new PointF[xy.Length / 2];
        for (int i = 0; i < points.Length; i++) points[i] = new PointF(r.X + xy[i * 2] * r.Width, r.Y + xy[i * 2 + 1] * r.Height);
        G.FillPolygon(Brush(c), points);
    }

    public void Checker(RectangleF r, Color c, float cell)
    {
        r = Snap(r);
        cell = Math.Max(1, cell);
        for (int row = 0; row * cell < r.Height; row++)
            for (int col = row % 2; col * cell < r.Width; col += 2)
                G.FillRectangle(Brush(c), r.X + col * cell, r.Y + row * cell, Math.Min(cell, r.Right - r.X - col * cell), Math.Min(cell, r.Bottom - r.Y - row * cell));
    }

    private readonly Dictionary<(string, float), (GraphicsPath Path, RectangleF Box)> _display = new();

    private (GraphicsPath Path, RectangleF Box) DisplayPath(string text, float px)
    {
        px = MathF.Round(px * _textScale * 2) / 2;
        var key = (text, px);
        if (_display.TryGetValue(key, out var known)) return known;
        if (_display.Count >= MaxCached) { foreach (var x in _display.Values) x.Path.Dispose(); _display.Clear(); }
        var (family, style) = OverlayFonts.Display;
        var path = new GraphicsPath();
        path.AddString(text, family, (int)style, px, PointF.Empty, StringFormat.GenericTypographic);
        var box = path.GetBounds();
        if (box.Width <= 0 || box.Height <= 0) box = new RectangleF(0, 0, Math.Max(1, text.Length * px * 0.5f), px * 0.7f);
        return _display[key] = (path, box);
    }

    public SizeF DisplaySize(string text, float px) => text.Length == 0 ? SizeF.Empty : DisplayPath(text, px).Box.Size;

    public void Display(string text, float px, Color c, RectangleF box, HAlign h = HAlign.Left, VAlign v = VAlign.Top)
    {
        if (text.Length == 0) return;
        var (path, bounds) = DisplayPath(text, px);
        float x = h switch { HAlign.Center => box.X + (box.Width - bounds.Width) / 2, HAlign.Right => box.Right - bounds.Width, _ => box.X } - bounds.X;
        float y = v switch { VAlign.Center => box.Y + (box.Height - bounds.Height) / 2, VAlign.Bottom => box.Bottom - bounds.Height, _ => box.Y } - bounds.Y;
        G.TranslateTransform(x, y);
        G.FillPath(Brush(c), path);
        G.TranslateTransform(-x, -y);
    }

    public void Picture(Image image, RectangleF box)
    {
        if (box.Width <= 1 || box.Height <= 1 || image.Width <= 0 || image.Height <= 0) return;
        float scale = Math.Min(box.Width / image.Width, box.Height / image.Height);
        var at = new RectangleF(box.X + (box.Width - image.Width * scale) / 2, box.Y + (box.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
        var quality = G.InterpolationMode;
        G.InterpolationMode = InterpolationMode.HighQualityBicubic;
        G.DrawImage(image, at);
        G.InterpolationMode = quality;
    }

    public void Dispose()
    {
        ClearFonts();
        foreach (var x in _display.Values) x.Path.Dispose();
        foreach (var b in _brushes.Values) b.Dispose();
        foreach (var p in _pens.Values) p.Dispose();
        foreach (var p in _flatPens.Values) p.Dispose();
        foreach (var f in _formats) f?.Dispose();
        _dashed?.Dispose();
        foreach (var s in _shapes.Values) s.Dispose();
        foreach (var g in _gradients.Values) g.Dispose();
        _measure.Dispose();
        _measureBitmap.Dispose();
        _sharp.Dispose();
    }

    public void ReleaseText() => _sharp.Dispose();

    private readonly SharpText _sharp = new();
    private readonly Dictionary<Font, (float Size, bool Bold)> _fontSpecs = new();

    public void End(IntPtr dc, Size size)
    {
        G.Flush(FlushIntention.Sync);
        _sharp.Render(dc, size);
    }
}

internal static class HudRect
{
    public static RectangleF Inflated(this RectangleF r, float dx, float dy) => RectangleF.FromLTRB(r.Left - dx, r.Top - dy, r.Right + dx, r.Bottom + dy);

    public static RectangleF Top(this RectangleF r, float h, out RectangleF rest)
    {
        h = Math.Min(h, r.Height);
        rest = new RectangleF(r.X, r.Y + h, r.Width, r.Height - h);
        return new RectangleF(r.X, r.Y, r.Width, h);
    }

    public static RectangleF Left(this RectangleF r, float w, out RectangleF rest)
    {
        w = Math.Min(w, r.Width);
        rest = new RectangleF(r.X + w, r.Y, r.Width - w, r.Height);
        return new RectangleF(r.X, r.Y, w, r.Height);
    }

    public static RectangleF Right(this RectangleF r, float w, out RectangleF rest)
    {
        w = Math.Min(w, r.Width);
        rest = new RectangleF(r.X, r.Y, r.Width - w, r.Height);
        return new RectangleF(r.Right - w, r.Y, w, r.Height);
    }
}
