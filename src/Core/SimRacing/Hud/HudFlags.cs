using System.Drawing;
using System.Drawing.Drawing2D;
using Core.SimRacing.Race;
using Core.SimRacing.Telemetry;

namespace Core.SimRacing.Hud;

internal static class HudFlags
{
    public static bool HasPicture(FlagKind k) => k is FlagKind.Disqualified or FlagKind.Black or FlagKind.Meatball or FlagKind.Red
        or FlagKind.Caution or FlagKind.Yellow or FlagKind.Debris or FlagKind.Blue or FlagKind.White or FlagKind.Checkered or FlagKind.Green;

    private static readonly Color Stripe = Color.FromArgb(0xFF, 0xE0, 0x00), Meatball = Color.FromArgb(0xF7, 0x8A, 0x1D),
        Frame = Color.FromArgb(235, 0x0A, 0x0A, 0x0C);

    private const float TileRatio = 1.5f;

    [ThreadStatic] private static PointF[]? _quad;

    private static Color Base(FlagKind k) => k switch
    {
        FlagKind.Green => HudPalette.FlagGreen,
        FlagKind.Yellow or FlagKind.Caution or FlagKind.Debris => HudPalette.FlagYellow,
        FlagKind.Blue => HudPalette.FlagBlue,
        FlagKind.Red => HudPalette.FlagRed,
        FlagKind.White or FlagKind.Checkered => HudPalette.FlagWhite,
        _ => HudPalette.FlagBlack,
    };

    public static void Draw(HudPainter p, RectangleF area, FlagKind kind)
    {
        if (!HasPicture(kind)) return;
        float s = p.PixelScale, px = 1 / s;
        int wPx = (int)MathF.Floor(Math.Min(area.Width, area.Height * TileRatio) * s) - 2;
        int hPx = (int)MathF.Round(wPx / TileRatio);
        if (wPx < 4 || hPx < 3) return;
        int cell = 0;
        if (kind == FlagKind.Checkered)
        {
            int rows = Math.Max(3, (int)MathF.Round(hPx / 4f));
            cell = Math.Max(1, hPx / rows);
            hPx = cell * rows;
            wPx = cell * Math.Max(2, (int)MathF.Round(wPx / (float)cell));
        }
        float x = p.Snap(area.X + (area.Width - wPx / s) / 2), y = p.Snap(area.Y + (area.Height - hPx / s) / 2);
        var r = new RectangleF(x, y, wPx / s, hPx / s);
        var g = p.G;
        g.FillRectangle(p.Brush(kind == FlagKind.Black ? HudPalette.Alpha(HudPalette.FlagWhite, 150) : Frame), r.X - px, r.Y - px, r.Width + 2 * px, r.Height + 2 * px);
        var state = g.Save();
        g.SetClip(r, CombineMode.Intersect);
        g.FillRectangle(p.Brush(Base(kind)), r);
        float w = r.Width, h = r.Height;
        switch (kind)
        {
            case FlagKind.Meatball:
                p.Dot(r.X + w / 2, r.Y + h / 2, Math.Min(w, h) * 0.32f, Meatball);
                break;
            case FlagKind.Blue:
                g.DrawLine(p.FlatPen(Stripe, Math.Max(2 * px, h * 0.26f)), r.Left - 2 * px, r.Bottom + 2 * px * h / w, r.Right + 2 * px, r.Top - 2 * px * h / w);
                break;
            case FlagKind.Disqualified:
            {
                var pen = p.FlatPen(HudPalette.FlagWhite, Math.Max(1.6f * px, h * 0.17f));
                g.DrawLine(pen, r.Left, r.Top, r.Right, r.Bottom);
                g.DrawLine(pen, r.Left, r.Bottom, r.Right, r.Top);
                break;
            }
            case FlagKind.Debris:
            {
                int n = wPx >= 18 ? 6 : 4;
                for (int i = 1; i < n; i += 2)
                {
                    float x0 = p.Snap(r.X + i * w / n), x1 = p.Snap(r.X + (i + 1) * w / n);
                    g.FillRectangle(p.Brush(HudPalette.FlagRed), x0, r.Y, x1 - x0, h);
                }
                break;
            }
            case FlagKind.Caution:
            {
                float sw = Math.Max(2 * px, p.Snap(h * 0.2f));
                for (float xx = -h; xx < w + h; xx += sw * 3) Slanted(g, p.Brush(HudPalette.Alpha(HudPalette.FlagBlack, 204)), r.X + xx, r.Top, r.Bottom, h, sw);
                break;
            }
            case FlagKind.Checkered:
            {
                float c = cell / s;
                int rows = hPx / cell, cols = wPx / cell;
                for (int j = 0; j < rows; j++)
                    for (int i = j % 2; i < cols; i += 2)
                        g.FillRectangle(p.Brush(HudPalette.FlagBlack), r.X + i * c, r.Y + j * c, c, c);
                break;
            }
        }
        g.Restore(state);
    }

    private static void Slanted(Graphics g, Brush brush, float x, float top, float bottom, float lean, float width)
    {
        var q = _quad ??= new PointF[4];
        q[0] = new PointF(x, bottom);
        q[1] = new PointF(x + lean, top);
        q[2] = new PointF(x + lean + width, top);
        q[3] = new PointF(x + width, bottom);
        g.FillPolygon(brush, q);
    }

    public static FlagKind BannerKind(RaceFlags flags)
    {
        var k = RaceControl.Top(flags);
        if (k == FlagKind.Green && !flags.HasFlag(RaceFlags.StartGo)) return FlagKind.None;
        return HasPicture(k) ? k : FlagKind.None;
    }

    public static readonly HudOption BannerOption = HudOption.Choice("flagBanner", "Flag banner", "off",
        new HudChoice("off", "Off"), new HudChoice("top", "On")) with
    {
        Help = "A bar above the widget with the flag and its name while a flag is out. Room for it is kept, so the table never moves.",
    };

    private const float Band = 24, BandGap = 5, Rule = 2, LabelRoom = 16;

    public const string OldSide = "side";

    public static readonly (float L, float T, float R, float B) OldSideRoom = (3, 3, Band, 3);

    public static (float L, float T, float R, float B) Room(string where) => where switch
    {
        "top" => (0, Band + BandGap, 0, 0),
        _ => (0, 0, 0, 0),
    };

    private static (Color Fill, Color Text, string Label) Look(FlagKind k) => k switch
    {
        FlagKind.Disqualified => (HudPalette.FlagBlack, HudPalette.FlagWhite, "DISQUALIFIED"),
        FlagKind.Black => (HudPalette.FlagBlack, HudPalette.FlagWhite, "BLACK FLAG"),
        FlagKind.Meatball => (HudPalette.FlagBlack, Meatball, "REPAIR FLAG"),
        FlagKind.Red => (HudPalette.FlagRed, Color.White, "RED FLAG"),
        FlagKind.Caution => (HudPalette.FlagYellow, HudPalette.FlagBlack, "FULL COURSE YELLOW"),
        FlagKind.Yellow => (HudPalette.FlagYellow, HudPalette.FlagBlack, "YELLOW FLAG"),
        FlagKind.Debris => (HudPalette.FlagYellow, HudPalette.FlagBlack, "DEBRIS FLAG"),
        FlagKind.Blue => (HudPalette.FlagBlue, Color.White, "BLUE FLAG"),
        FlagKind.White => (HudPalette.FlagWhite, HudPalette.FlagBlack, "WHITE FLAG"),
        FlagKind.Checkered => (HudPalette.FlagWhite, HudPalette.FlagBlack, "CHECKERED FLAG"),
        _ => (HudPalette.FlagGreen, Color.White, "GREEN FLAG"),
    };

    private static Color RuleColor(FlagKind k, Color fill) => k switch
    {
        FlagKind.Meatball => Meatball,
        FlagKind.Black or FlagKind.Disqualified => HudPalette.Alpha(HudPalette.FlagWhite, 90),
        _ => HudPalette.Mix(fill, Color.Black, 0.28),
    };

    private static bool HasEnds(FlagKind k) => k is not (FlagKind.Green or FlagKind.Red or FlagKind.White or FlagKind.Black);

    public static void DrawBanner(HudPainter p, RectangleF outer, FlagKind kind, string where, float rightSpare = 0)
    {
        if (kind == FlagKind.None || where != "top") return;
        var (fill, text, label) = Look(kind);
        outer = p.Snap(outer);
        var band = p.Snap(RectangleF.FromLTRB(outer.Left, outer.Top, outer.Right - rightSpare, outer.Top + Band));
        var g = p.G;
        float s = p.PixelScale, rule = Math.Max(1 / s, p.Snap(Rule));
        var face = RectangleF.FromLTRB(band.Left, band.Top, band.Right, band.Bottom - rule);
        g.FillRectangle(p.Brush(fill), face);
        g.FillRectangle(p.Brush(RuleColor(kind, fill)), band.Left, face.Bottom, band.Width, rule);
        var font = p.Font(12.5f, true);
        if (HasEnds(kind))
        {
            int cellPx = Math.Max(2, (int)MathF.Round(face.Height * s / 4));
            float unit = 2 * cellPx / s;
            float room = (band.Width - p.Measure(label, font)) / 2 - LabelRoom;
            if (room >= unit)
            {
                var end = new RectangleF(face.Left, face.Top, MathF.Floor(room / unit) * unit, face.Height);
                var state = g.Save();
                End(p, end, kind, fill, cellPx / s);
                g.TranslateTransform(face.Left + face.Right, 0);
                g.ScaleTransform(-1, 1);
                End(p, end, kind, fill, cellPx / s);
                g.Restore(state);
            }
        }
        p.CenteredCaps(label, font, text, band);
    }

    private static void End(HudPainter p, RectangleF r, FlagKind kind, Color fill, float cell)
    {
        var g = p.G;
        var state = g.Save();
        g.SetClip(r, CombineMode.Intersect);
        float w = r.Width, h = r.Height, solid = 0.35f;
        switch (kind)
        {
            case FlagKind.Checkered:
                for (int j = 0; j * cell < h; j++)
                    for (int i = j % 2; i * cell < w; i += 2)
                        g.FillRectangle(p.Brush(HudPalette.FlagBlack), r.X + i * cell, r.Y + j * cell, cell, cell);
                break;
            case FlagKind.Yellow or FlagKind.Caution:
            {
                var brush = p.Brush(HudPalette.Alpha(HudPalette.FlagBlack, kind == FlagKind.Caution ? 209 : 46));
                for (float xx = -h; xx < w + h; xx += 10) Slanted(g, brush, r.X + xx, r.Top, r.Bottom, h, 4);
                break;
            }
            case FlagKind.Debris:
            {
                var brush = p.Brush(HudPalette.FlagRed);
                for (float xx = -h + 5; xx < w + h; xx += 10) Slanted(g, brush, r.X + xx, r.Top, r.Bottom, h * 0.5f, 5);
                break;
            }
            case FlagKind.Blue:
            {
                var pen = p.FlatPen(Stripe, 3);
                for (float xx = -h; xx < w + h; xx += 10) g.DrawLine(pen, r.X + xx, r.Bottom, r.X + xx + h, r.Top);
                break;
            }
            case FlagKind.Meatball:
            {
                float step = MathF.Round(h * 0.8f), dot = h * 0.24f;
                for (float cx = r.X + step / 2; cx < r.Right; cx += step) p.Dot(cx, r.Y + h / 2, dot, Meatball);
                break;
            }
            case FlagKind.Disqualified:
            {
                solid = 0.7f;
                float step = h * 0.7f, arm = step * 0.5f;
                for (float cx = r.X; cx < r.Right; cx += step)
                {
                    double t = Math.Clamp(((cx + arm / 2 - r.X) / w - 0.15) / 0.65, 0, 1);
                    var pen = p.FlatPen(HudPalette.Mix(HudPalette.FlagWhite, HudPalette.FlagRed, Math.Round(t * 12) / 12), 1.5f);
                    g.DrawLine(pen, cx, r.Y + h * 0.3f, cx + arm, r.Y + h * 0.7f);
                    g.DrawLine(pen, cx, r.Y + h * 0.7f, cx + arm, r.Y + h * 0.3f);
                }
                break;
            }
        }
        p.Gradient(new RectangleF(r.X + w * solid, r.Y, w * (1 - solid), h), HudPalette.Alpha(fill, 0), fill);
        g.Restore(state);
    }
}
