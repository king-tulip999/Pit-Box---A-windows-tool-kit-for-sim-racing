using System.Drawing;
using Units = PitBox.Services.CapsUnits;
using PitBox.SimRacing.Race;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Hud.Widgets;

internal sealed class TrafficWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Number("window", "Within", 6, 1, 30, 0.5, Units.S),
        HudOption.Int("max", "Cars at most", 5, 1, 12),
        HudOption.Bool("sameClass", "Include my class", false),
    };

    private readonly List<(int Car, double Gap)> _cars = new();

    public override bool HasContent(HudView v) => Gather(v) > 0;

    private int Gather(HudView v)
    {
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        _cars.Clear();
        if ((uint)me >= TelemetryFrame.MaxCars) return 0;
        double window = v.Options.Number("window");
        bool same = v.Options.Bool("sameClass");
        int myClass = FieldOrder.ClassOf(f, me);
        for (int i = 0; i < TelemetryFrame.MaxCars; i++)
        {
            if (i == me || !f.CarActive(i) || f.CarOnPitRoad[i] || !same && FieldOrder.ClassOf(f, i) == myClass) continue;
            if (FieldOrder.GapSeconds(f, me, i, v.Race.Profile) is { } g && Math.Abs(g) <= window) _cars.Add((i, g));
        }
        _cars.Sort((a, b) => Math.Abs(a.Gap).CompareTo(Math.Abs(b.Gap)));
        return _cars.Count;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Traffic")) return;
        var f = v.Frame;
        int me = f.PlayerCarIdx;
        bool blue = f.Flags.HasFlag(RaceFlags.Blue);
        var area = p.Header("Traffic", blue ? "BLUE FLAG" : null, blue ? HudPalette.FlagBlue : null);
        if ((uint)me >= TelemetryFrame.MaxCars) return;
        bool same = v.Options.Bool("sameClass");
        double? myLap = FieldOrder.LapTimeOf(f, me);
        Gather(v);
        if (_cars.Count == 0)
        {
            p.Text(f.Session.NumCarClasses > 1 || same ? "No traffic nearby" : "Single-class session", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        float rowH = p.LineHeight(p.Body) * 1.4f * p.Density, y = area.Y;
        foreach (var (car, gap) in _cars.Take(v.Options.Int("max")))
        {
            if (y + rowH > area.Bottom + 1) break;
            var d = f.Session.ByCar.GetValueOrDefault(car);
            var row = new RectangleF(area.X, y, area.Width, rowH - 1);
            bool faster = FieldOrder.LapTimeOf(f, car) is { } lap && myLap is { } mine && lap < mine * 0.98;
            double? trend = v.Race.Gaps.Trend(car);
            bool closing = trend is < -0.01;
            if (faster && gap < 0 && closing) p.Fill(row, HudPalette.Alpha(p.P.Warn, 50), 3);
            var cls = row.Left(46, out var rest);
            p.Pill(new RectangleF(cls.X, cls.Y + rowH * 0.16f, cls.Width - 6, rowH * 0.66f), d?.ClassName ?? "", p.ClassColor(f.Session, d?.ClassId ?? 0), p.SmallBold);
            var gapBox = rest.Right(64, out rest);
            p.Text((gap > 0 ? "▲ " : "▼ ") + RaceFormat.Abs(gap), p.Bold, gap > 0 ? p.P.Text : faster ? p.P.WarnText : p.P.Text, gapBox, HAlign.Right);
            var info = rest.Right(80, out rest);
            string meet = closing && v.Race.Gaps.TimeToCatch(car, gap) is { } t && t < 120 ? RaceFormat.About($"{t:0} {Units.S}") : trend is > 0.01 ? "opening" : "";
            p.Text(meet, p.Small, p.P.Muted, info, HAlign.Right);
            p.Text(d?.Name ?? $"Car {car}", p.Body, p.P.Text, rest.Inflated(-4, 0), HAlign.Left);
            y += rowH;
        }
    }
}

internal sealed class RadarWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Number("range", "Range", 20, 8, 60, 1, Units.M, "How far ahead and behind cars show"),
        HudOption.Bool("distances", "Distances", true, "To the nearest car ahead and behind"),
    };

    private const float ClosingRate = 0.2f;

    private readonly RadarSlide _slide = new();
    private readonly List<(double Offset, double Lateral)> _placed = new();
    private readonly double[] _shift = new double[TelemetryFrame.MaxCars], _width = new double[TelemetryFrame.MaxCars];
    private readonly Dictionary<int, (double Offset, double At, double Rate)> _rates = new();
    private readonly PointF[] _marker = new PointF[3];

    public override bool HasContent(HudView v) =>
        v.Frame.Driving && (RadarModel.Alongside(v.Frame.Sides) > 0 || v.Race.Radar.Gather(v.Frame, v.Options.Number("range")).Count > 0);

    private double Rate(int car, double offset, double now)
    {
        if (!_rates.TryGetValue(car, out var last) || now < last.At || now - last.At > 1) { _rates[car] = (offset, now, 0); return 0; }
        if (now - last.At < 0.01) return last.Rate;
        double closing = (Math.Abs(last.Offset) - Math.Abs(offset)) / (now - last.At);
        double rate = last.Rate + (closing - last.Rate) * 0.25;
        _rates[car] = (offset, now, rate);
        return rate;
    }

    private static Color CarColor(HudPainter p, double offset, double lateral, double range)
    {
        double k = Math.Min(1, Math.Abs(lateral));
        double inline = Math.Clamp(1 - Math.Abs(offset) / range + 0.3, 0.3, 1);
        var hue = HudPalette.Mix(p.P.Warn, p.P.Danger, k);
        return HudPalette.Mix(p.PanelColor, hue, inline + (1 - inline) * k);
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v)) return;
        var f = v.Frame;
        double range = v.Options.Number("range"), now = v.Now;
        var cars = v.Race.Radar.Gather(f, range);
        double? length = v.Race.Radar.CarLength;
        p.Panel();
        float pad = 8 * p.Density;
        var area = p.Bounds.Inflated(-pad, -pad);
        float laneW = Math.Min(area.Width / 3, area.Height * 0.24f), carW = laneW * 0.62f;
        float cx = area.X + area.Width / 2, cy = area.Y + area.Height / 2;
        float scale = (float)(area.Height / 2 / range);
        float meH = length is { } l ? (float)(l * scale) : carW * 1.8f;

        _placed.Clear();
        double leftK = 0, rightK = 0;
        for (int i = 0; i < cars.Count; i++)
        {
            double lateral = _slide.Lateral(cars[i].CarIdx, cars[i].Lane, now, p.ReducedMotion);
            _placed.Add((cars[i].Offset, lateral));
            if (lateral < 0) leftK = Math.Max(leftK, -lateral); else rightK = Math.Max(rightK, lateral);
        }
        _slide.Prune(now);
        if (length is { } carLength) RadarModel.Split(_placed, carLength, _shift, _width);
        else for (int i = 0; i < _placed.Count; i++) { _shift[i] = 0; _width[i] = 1; }

        SideWash(p, cx - laneW, laneW, area, leftK);
        SideWash(p, cx + laneW, laneW, area, rightK);
        var guide = HudPalette.Alpha(p.P.LineStrong, 230);
        p.Line(cx - laneW / 2, area.Y, cx - laneW / 2, area.Bottom, guide);
        p.Line(cx + laneW / 2, area.Y, cx + laneW / 2, area.Bottom, guide);
        double tick = range <= 30 ? 5 : 10;
        for (double m = tick; m <= range; m += tick)
        {
            Tick(p, cx, laneW, cy - (float)(m * scale));
            Tick(p, cx, laneW, cy + (float)(m * scale));
        }

        RadarCar? ahead = null, behind = null;
        for (int i = 0; i < cars.Count; i++)
        {
            var c = cars[i];
            var (offset, lateral) = _placed[i];
            var color = CarColor(p, offset, lateral, range);
            float w = carW * (float)_width[i], x = cx + (float)lateral * laneW + (float)_shift[i] * carW * 0.27f;
            if (c.Lane == RadarLane.Inline)
            {
                if (c.Offset > 0 && ahead is null) ahead = c;
                if (c.Offset < 0 && behind is null) behind = c;
            }
            if (length is null && Math.Abs(lateral) < 0.5)
            {
                float y = cy - (float)(offset * scale);
                p.Fill(new RectangleF(x - w / 2, y - 1.5f * p.Density, w, 3 * p.Density), color);
                continue;
            }
            float top = length is null ? cy - meH / 2 : cy - (float)(offset * scale) - meH / 2;
            p.Fill(new RectangleF(x - w / 2, top + 1, w, Math.Max(2, meH - 2)), color, p.CornerRadius(3));
        }
        p.Fill(new RectangleF(cx - carW / 2, cy - meH / 2, carW, meH), p.P.Heading, p.CornerRadius(3));

        if (v.Options.Bool("distances"))
        {
            var font = p.Font(12, true);
            float lh = p.LineHeight(font), marker = 6 * p.Density, tx = cx + laneW / 2 + 5;
            var textBox = new RectangleF(tx + marker + 2, 0, area.Right - tx - marker - 2, lh);
            if (textBox.Width >= p.Measure("00 M", font))
            {
                Distance(p, v, ahead, length, now, font, textBox, tx, marker, cy, scale, area);
                Distance(p, v, behind, length, now, font, textBox, tx, marker, cy, scale, area);
            }
        }
    }

    private static void SideWash(HudPainter p, float center, float laneW, RectangleF area, double k)
    {
        if (k > 0.01) p.Fill(new RectangleF(center - laneW / 2 + 2, area.Y, laneW - 4, area.Height), HudPalette.Alpha(p.P.Danger, (int)(41 * k)));
    }

    private static void Tick(HudPainter p, float cx, float laneW, float y)
    {
        p.Line(cx - laneW / 2 - 3, y, cx - laneW / 2, y, p.P.Faint);
        p.Line(cx + laneW / 2, y, cx + laneW / 2 + 3, y, p.P.Faint);
    }

    private void Distance(HudPainter p, HudView v, RadarCar? car, double? length, double now, Font font, RectangleF textBox, float tx, float marker, float cy, float scale, RectangleF area)
    {
        if (car is not { } c) return;
        float lh = textBox.Height;
        float y = Math.Clamp(cy - (float)(c.Offset * scale), area.Y + lh / 2, area.Bottom - lh / 2);
        double gap = Math.Max(0, Math.Abs(c.Offset) - (length ?? 0));
        p.Text(RaceFormat.Distance(gap, v.Units), font, p.P.WarnText, textBox with { Y = y - lh / 2 }, HAlign.Left, VAlign.Center, false);
        if (Rate(c.CarIdx, c.Offset, now) <= ClosingRate) return;
        float d = c.Offset > 0 ? 1 : -1, h = marker / 2;
        _marker[0] = new PointF(tx, y - h * d);
        _marker[1] = new PointF(tx + marker, y - h * d);
        _marker[2] = new PointF(tx + marker / 2, y + h * d);
        p.G.FillPolygon(p.Brush(p.P.WarnText), _marker);
    }
}

