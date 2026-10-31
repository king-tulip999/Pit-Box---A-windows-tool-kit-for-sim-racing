using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using PitBox.SimRacing.Race;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Hud.Widgets;

internal sealed class InputsWidget : HudWidget
{
    public const string Trace = "trace", Tach = "tach", Strip = "strip";
    private const double TraceSeconds = 6, StripSeconds = 4, LimiterHz = 10, AbsHz = 4;
    private const int Capacity = 640;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("style", "Style", Trace,
            new HudChoice(Trace, "Trace", "A pedal and steering trace beside the pedal bars, gear, speed and RPM"),
            new HudChoice(Tach, "Tach", "A round RPM dial inside a steering ring, pedal bars under it"),
            new HudChoice(Strip, "Strip", "One low row to sit under the mirror")),
        HudOption.Set("show", "Shows", "throttle,brake,clutch,steering,speed,rpm,trace,abs", null,
            new HudChoice("throttle", "Throttle"), new HudChoice("brake", "Brake"), new HudChoice("clutch", "Clutch", "Pedal travel: pressed shows full"),
            new HudChoice("steering", "Steering"), new HudChoice("speed", "Speed"), new HudChoice("rpm", "RPM readout"),
            new HudChoice("trace", "Input trace", "Trace and Strip only"),
            new HudChoice("abs", "ABS flash", "While ABS works the gear flashes amber and the brake turns amber")),
    };

    internal readonly record struct Shows(bool Throttle, bool Brake, bool Clutch, bool Steering, bool Speed, bool Rpm, bool Trace, bool Abs)
    {
        public bool AnyPedal => Throttle || Brake || Clutch;
        public int Pedals => (Clutch ? 1 : 0) + (Brake ? 1 : 0) + (Throttle ? 1 : 0);
        public bool TraceGraph => Trace && (AnyPedal || Steering);
        public bool StripGraph => Trace && AnyPedal;
        public bool StripMiddle => Rpm || Steering;
        public float StripFixed => 50 + 12 + (Speed ? 44 + 12 : 0) + (StripGraph ? 90 + 12 : 0) + (AnyPedal ? Pedals * 12 - 4 + 12 : 0);
    }

    internal static Shows Parse(HudOptions o)
    {
        bool t = false, b = false, c = false, st = false, sp = false, r = false, tr = false, a = false;
        var on = o.Columns("show");
        for (int i = 0; i < on.Count; i++)
            switch (on[i])
            {
                case "throttle": t = true; break;
                case "brake": b = true; break;
                case "clutch": c = true; break;
                case "steering": st = true; break;
                case "speed": sp = true; break;
                case "rpm": r = true; break;
                case "trace": tr = true; break;
                case "abs": a = true; break;
            }
        return new Shows(t, b, c, st, sp, r, tr, a);
    }

    public static SizeF Natural(HudOptions o)
    {
        var on = Parse(o);
        return o.Choice("style") switch
        {
            Tach => new SizeF(188, 176 + (on.AnyPedal ? 30 : 0)),
            Strip => new SizeF(on.StripFixed + (on.StripMiddle ? 254 + 12 : 0), 50),
            _ => new SizeF(10 + (on.TraceGraph ? 250 : 0) + (on.AnyPedal || on.Steering ? 90 : 0) + 90 + 10, (on.Rpm ? 4 : 0) + 100),
        };
    }

    private struct Sample
    {
        public double At;
        public float Throttle, Brake, Clutch, Steer;
        public bool Abs;
    }

    private readonly Sample[] _ring = new Sample[Capacity];
    private int _head, _count;
    private long _seq = long.MinValue;
    private double _peakRpm;
    private Shows _on;
    private bool _throttle, _brake, _clutch, _steering, _speed, _rpm, _trace, _abs;
    private readonly GraphicsPath _path = new(), _absPath = new();
    private float _k = 1;

    private readonly record struct RpmScale(double Min, double Max, double Shift, double Blink, double ZoneFrom)
    {
        public double Frac(double rpm) => Max > Min ? Math.Clamp((rpm - Min) / (Max - Min), 0, 1) : 0;
    }

    private readonly record struct Live(double? Throttle, double? Brake, double? Clutch, double? SteerDeg, double SteerFrac,
        double? Rpm, RpmScale Scale, bool Limiter, bool Abs, double Now);

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v)) return;
        ReadShows(v.Options);
        var live = Read(v);
        Record(v, live);
        p.Panel();
        switch (v.Options.Choice("style"))
        {
            case Tach: DrawTach(p, v, live); break;
            case Strip: DrawStrip(p, v, live); break;
            default: DrawTrace(p, v, live); break;
        }
    }

    private void ReadShows(HudOptions o)
    {
        _on = Parse(o);
        (_throttle, _brake, _clutch, _steering, _speed, _rpm, _trace, _abs) = (_on.Throttle, _on.Brake, _on.Clutch, _on.Steering, _on.Speed, _on.Rpm, _on.Trace, _on.Abs);
    }

    private bool AnyPedal => _on.AnyPedal;

    private int PedalCount => _on.Pedals;

    internal static double? Pedal(double? raw, double? cooked) => (raw ?? cooked) is { } x ? Math.Clamp(x, 0, 1) : null;

    internal static double? ClutchTravel(double? raw, double? cooked) => Pedal(raw, cooked) is { } engaged ? 1 - engaged : null;

    internal static double? SteerDegrees(double? radians) => radians is { } r ? -r * 180 / Math.PI : null;

    internal static double HalfLockDegrees(double? lockToLockRadians) => lockToLockRadians is > 0 and var m ? m * 90 / Math.PI : 450;

    internal static string Angle(double? degrees) => degrees is not { } d ? RaceFormat.Missing
        : Math.Abs(d) < 2 ? "0°"
        : string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(d):0}° {(d > 0 ? "R" : "L")}");

    internal static string GearText(int? gear) => gear switch { null => RaceFormat.Missing, < 0 => "R", 0 => "N", _ => gear.Value.ToString(CultureInfo.InvariantCulture) };

    private Live Read(HudView v)
    {
        var f = v.Frame;
        double? steer = SteerDegrees(f.Steering);
        double half = HalfLockDegrees(f.SteeringMax);
        var scale = Scale(f);
        bool limiter = f.Rpm is { } rpm && rpm >= scale.Blink;
        double? brake = Pedal(f.BrakeRaw, f.Brake);
        bool abs = _abs && f.AbsActive == true && brake is > 0.01;
        return new Live(Pedal(f.ThrottleRaw, f.Throttle), brake, ClutchTravel(f.ClutchRaw, f.Clutch), steer,
            steer is { } d ? Math.Clamp(d / half, -1, 1) : 0, f.Rpm, scale, limiter, abs, v.Now);
    }

    private RpmScale Scale(TelemetryFrame f)
    {
        var s = f.Session;
        if (f.Rpm is { } now && now > _peakRpm) _peakRpm = now;
        double shift = s.ShiftRpm ?? s.ShiftLightFirstRpm ?? 0, blink = s.BlinkRpm ?? 0, redline = s.RedlineRpm ?? 0;
        double max = Math.Max(redline, Math.Max(blink, shift));
        if (max <= 0) max = Math.Max(6000, Math.Ceiling(_peakRpm / 1000) * 1000);
        double min = s.IdleRpm is { } idle && idle < max * 0.6 ? idle : 0;
        double b = blink > 0 ? blink : redline > 0 ? redline : max;
        double sh = shift > 0 ? Math.Min(shift, max) : b;
        double zone = redline > 0 ? Math.Min(b, redline) : b;
        return new RpmScale(min, max, sh, Math.Min(b, max), Math.Min(zone, max));
    }

    private void Record(HudView v, in Live live)
    {
        var f = v.Frame;
        if (f.Sequence == _seq) return;
        _seq = f.Sequence;
        if (f.Simulated && _count == 0) Backfill(v);
        Push(live.Now, live.Throttle ?? 0, live.Brake ?? 0, live.Clutch ?? 0, live.SteerFrac, live.Abs);
    }

    private void Push(double at, double throttle, double brake, double clutch, double steer, bool abs)
    {
        _ring[_head] = new Sample { At = at, Throttle = (float)throttle, Brake = (float)brake, Clutch = (float)clutch, Steer = (float)steer, Abs = abs };
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    private void Backfill(HudView v)
    {
        if (v.Frame.SessionTime is not { } now) return;
        var demo = new DemoTelemetry();
        var past = new TelemetryFrame();
        double step = 1.0 / 30;
        for (double back = TraceSeconds; back > step / 2; back -= step)
        {
            demo.FillAt(past, now - back);
            double? brake = Pedal(past.BrakeRaw, past.Brake);
            Push(v.Now - back, Pedal(past.ThrottleRaw, past.Throttle) ?? 0, brake ?? 0, ClutchTravel(past.ClutchRaw, past.Clutch) ?? 0,
                SteerDegrees(past.Steering) is { } d ? Math.Clamp(d / HalfLockDegrees(past.SteeringMax), -1, 1) : 0,
                _abs && past.AbsActive == true && brake is > 0.01);
        }
    }

    private float S(float px) => px * _k;

    private static bool FlashOn(HudPainter p, double now, double hz) => p.ReducedMotion || ((long)Math.Floor(now * hz * 2) & 1) == 0;

    private Color GearColor(HudPainter p, in Live live)
    {
        if (live.Abs) return FlashOn(p, live.Now, AbsHz) ? p.P.AbsInput : p.P.Heading;
        return live.Limiter ? p.P.Danger : p.P.Heading;
    }

    private Color BrakeColor(HudPainter p, in Live live) => live.Abs ? p.P.AbsInput : p.P.BrakeInput;

    private void DrawTrace(HudPainter p, HudView v, in Live live)
    {
        var b = p.Bounds;
        bool graph = _on.TraceGraph, pedals = AnyPedal || _steering;
        float lineH = _rpm ? 4 : 0;
        float designH = lineH + 100, minW = 10 + (graph ? 250 : 0) + (pedals ? 90 : 0) + 70 + 10;
        _k = Math.Max(0.1f, Math.Min(b.Height / designH, b.Width / minW));
        float top = (b.Height - designH * _k) / 2;
        if (_rpm) RpmLine(p, live, new RectangleF(b.X, b.Y, b.Width, S(4)), p.P.Surface, false);
        float y = top + S(lineH + 10), h = S(80), x = S(10);
        if (graph)
        {
            Graph(p, new RectangleF(x, y, S(240), h), live.Now, TraceSeconds, true);
            x += S(250);
        }
        if (pedals)
        {
            PedalBlock(p, new RectangleF(x, y, S(80), h), live);
            x += S(90);
        }
        GearBlock(p, v, new RectangleF(x, y, Math.Max(S(40), b.Right - S(10) - x), h), live, 44, 15, 10);
    }

    private void RpmLine(HudPainter p, in Live live, RectangleF r, Color notchColor, bool tallNotch)
    {
        var sc = live.Scale;
        p.Fill(r, p.P.InputTrack);
        float zone = r.X + (float)sc.Frac(sc.ZoneFrom) * r.Width;
        if (zone < r.Right) p.Fill(RectangleF.FromLTRB(zone, r.Top, r.Right, r.Bottom), p.P.RedZone);
        if (live.Rpm is { } rpm && (!live.Limiter || FlashOn(p, live.Now, LimiterHz)))
            p.Fill(new RectangleF(r.X, r.Y, (float)sc.Frac(rpm) * r.Width, r.Height), live.Limiter ? p.P.Danger : p.P.Accent);
        float sx = r.X + (float)sc.Frac(sc.Shift) * r.Width;
        float over = tallNotch ? S(2) : 0;
        p.Fill(new RectangleF(sx - S(1), r.Y - over, S(2), r.Height + over * 2), notchColor);
    }

    private void Graph(HudPainter p, RectangleF r, double now, double window, bool steering)
    {
        p.Fill(r, p.P.TraceBack);
        if (steering) p.Fill(new RectangleF(r.X, r.Y + r.Height / 2 - 0.5f, r.Width, 1), p.P.InputTicks);
        float thick = steering ? 2 : 1.5f;
        if (steering && _steering) Lines(p, r, now, window, 3, p.P.SteerInput, null, S(1.5f));
        if (_clutch) Lines(p, r, now, window, 2, p.P.ClutchInput, null, S(1.5f));
        if (_brake) Lines(p, r, now, window, 1, p.P.BrakeInput, _abs ? p.P.AbsInput : null, S(thick));
        if (_throttle) Lines(p, r, now, window, 0, p.P.ThrottleInput, null, S(thick));
    }

    private static float Value(in Sample s, int channel) => channel switch { 0 => s.Throttle, 1 => s.Brake, 2 => s.Clutch, _ => s.Steer };

    private void Lines(HudPainter p, RectangleF r, double now, double window, int channel, Color color, Color? absColor, float width)
    {
        _path.Reset();
        _absPath.Reset();
        float inset = Math.Max(1, width), from = r.Bottom - inset, span = r.Height - inset * 2, mid = r.Y + r.Height / 2;
        int start = (_head - _count + Capacity) % Capacity;
        PointF last = default;
        bool have = false, lastAbs = false, normalOpen = false, absOpen = false;
        for (int n = 0; n < _count; n++)
        {
            ref readonly var s = ref _ring[(start + n) % Capacity];
            double age = now - s.At;
            if (age > window || age < -0.5) { have = false; continue; }
            float x = r.Right - (float)(age / window) * r.Width;
            float val = Value(s, channel);
            float y = channel == 3 ? mid - val * (r.Height / 2 - inset) : from - val * span;
            var pt = new PointF(x, y);
            if (have)
            {
                bool abs = absColor is not null && (s.Abs || lastAbs);
                var path = abs ? _absPath : _path;
                ref bool open = ref abs ? ref absOpen : ref normalOpen;
                ref bool other = ref abs ? ref normalOpen : ref absOpen;
                if (!open) path.StartFigure();
                path.AddLine(last, pt);
                open = true;
                other = false;
            }
            else
            {
                normalOpen = absOpen = false;
            }
            last = pt;
            lastAbs = s.Abs;
            have = true;
        }
        if (_path.PointCount > 1) p.G.DrawPath(p.Pen(color, width), _path);
        if (absColor is { } ac && _absPath.PointCount > 1) p.G.DrawPath(p.Pen(ac, width), _absPath);
    }

    private void VerticalBar(HudPainter p, RectangleF r, double? value, Color fill)
    {
        p.Fill(r, p.P.InputTrack);
        if (value is { } x && x > 0) p.Fill(RectangleF.FromLTRB(r.Left, r.Bottom - (float)x * r.Height, r.Right, r.Bottom), fill);
    }

    private void HorizontalBar(HudPainter p, RectangleF r, double? value, Color fill)
    {
        p.Fill(r, p.P.InputTrack);
        if (value is { } x && x > 0) p.Fill(new RectangleF(r.X, r.Y, (float)x * r.Width, r.Height), fill);
    }

    private void SteerBar(HudPainter p, RectangleF r, in Live live)
    {
        p.Fill(r, p.P.InputTrack);
        float cx = r.X + r.Width / 2, end = cx + (float)live.SteerFrac * r.Width / 2;
        if (live.SteerDeg is not null && Math.Abs(end - cx) > 0.25f) p.Fill(RectangleF.FromLTRB(Math.Min(cx, end), r.Top, Math.Max(cx, end), r.Bottom), p.P.SteerInput);
        p.Fill(new RectangleF(cx - S(1), r.Y - S(2), S(2), r.Height + S(4)), p.P.Accent);
    }

    private static string Percent(double? v) => v is { } x ? string.Create(CultureInfo.InvariantCulture, $"{x * 100:0}%") : RaceFormat.Missing;

    private void PedalBlock(HudPainter p, RectangleF r, in Live live)
    {
        int n = PedalCount;
        var small = p.Font(S(10), false);
        float textH = p.LineHeight(small), steerH = _steering ? S(5) + S(3) + textH : 0;
        if (n > 0)
        {
            float barH = r.Height - steerH - S(3) - textH - (_steering ? S(4) : 0);
            if (!_steering) barH = Math.Min(barH, r.Height - S(3) - textH);
            float slot = r.Width / n;
            int i = 0;
            if (_clutch) One(i++, live.Clutch, p.P.ClutchInput);
            if (_brake) One(i++, live.Brake, BrakeColor(p, live));
            if (_throttle) One(i, live.Throttle, p.P.ThrottleInput);

            void One(int at, double? value, Color color)
            {
                float cx = r.X + slot * at + slot / 2;
                VerticalBar(p, new RectangleF(cx - S(5), r.Y, S(10), barH), value, color);
                p.Text(Percent(value), small, p.P.Muted, new RectangleF(cx - slot / 2, r.Y + barH + S(3), slot, textH), HAlign.Center, VAlign.Top, false);
            }
        }
        if (_steering)
        {
            float sy = n > 0 ? r.Bottom - steerH : r.Y + (r.Height - steerH) / 2;
            SteerBar(p, new RectangleF(r.X, sy, r.Width, S(5)), live);
            p.Text(Angle(live.SteerDeg), small, p.P.Muted, new RectangleF(r.X, sy + S(5) + S(3), r.Width, textH), HAlign.Center, VAlign.Top, false);
        }
    }

    private void GearBlock(HudPainter p, HudView v, RectangleF r, in Live live, float gearPx, float speedPx, float rpmPx)
    {
        var gearFont = p.Font(S(gearPx), true);
        var speedFont = p.Font(S(speedPx), true);
        var small = p.Font(S(rpmPx), false);
        float gearH = p.LineHeight(gearFont) * 0.92f, speedH = _speed ? p.LineHeight(speedFont) : 0, rpmH = _rpm ? p.LineHeight(small) : 0;
        float y = r.Y + (r.Height - gearH - speedH - rpmH) / 2;
        p.Text(GearText(v.Frame.Gear), gearFont, GearColor(p, live), new RectangleF(r.X, y, r.Width, gearH), HAlign.Center, VAlign.Center, false);
        y += gearH;
        if (_speed)
        {
            SpeedLine(p, v, new RectangleF(r.X, y, r.Width, speedH), speedFont, small);
            y += speedH;
        }
        if (_rpm) p.Text(RpmText(live.Rpm), small, p.P.Muted, new RectangleF(r.X, y, r.Width, rpmH), HAlign.Center, VAlign.Center, false);
    }

    private static string RpmText(double? rpm) => rpm is { } x ? string.Create(CultureInfo.InvariantCulture, $"{x:0} RPM") : RaceFormat.Missing + " RPM";

    private static readonly string[] SpeedSamples = { "888" };

    private void SpeedLine(HudPainter p, HudView v, RectangleF r, Font number, Font unitFont)
    {
        string value = v.Frame.Speed is { } ms ? RaceFormat.SpeedValue(ms, v.Units).ToString("0", CultureInfo.InvariantCulture) : RaceFormat.Missing;
        string unit = RaceFormat.SpeedUnit(v.Units);
        float nw = p.Widest(SpeedSamples, number), uw = p.Measure(unit, unitFont), gap = S(3);
        float x = r.X + (r.Width - nw - gap - uw) / 2;
        p.Text(value, number, p.P.Text, new RectangleF(x, r.Y, nw, r.Height), HAlign.Right, VAlign.Center, false);
        float drop = (p.LineHeight(number) - p.LineHeight(unitFont)) * 0.3f;
        p.Text(unit, unitFont, p.P.Muted, new RectangleF(x + nw + gap, r.Y + drop, uw + 2, r.Height), HAlign.Left, VAlign.Center, false);
    }

    private void DrawTach(HudPainter p, HudView v, in Live live)
    {
        var b = p.Bounds;
        bool pedals = AnyPedal;
        float designH = 10 + 156 + (pedals ? 30 : 0) + 10;
        _k = Math.Max(0.1f, Math.Min(b.Width / 188, b.Height / designH));
        float ox = (b.Width - S(188)) / 2, oy = (b.Height - S(designH)) / 2;
        var dial = new RectangleF(ox + S(10), oy + S(10), S(168), S(156));
        float cx = dial.X + dial.Width / 2, cy = dial.Y + dial.Height / 2;
        if (_steering) SteerRing(p, cx, cy, S(73), live);
        RpmArc(p, cx, cy, S(62), live);
        var gearFont = p.Font(S(58), true);
        var speedFont = p.Font(S(16), true);
        var small = p.Font(S(10), false);
        float gearH = p.LineHeight(gearFont), speedH = p.LineHeight(speedFont), rpmH = p.LineHeight(small);
        float lift = (_speed ? S(8) : 0) + (_rpm ? S(6) : 0);
        float left = cx - S(55), width = S(110);
        p.Text(GearText(v.Frame.Gear), gearFont, GearColor(p, live), new RectangleF(left, cy - lift - gearH / 2, width, gearH), HAlign.Center, VAlign.Center, false);
        float below = cy - lift + S(30);
        if (_speed)
        {
            SpeedLine(p, v, new RectangleF(left, below - speedH / 2, width, speedH), speedFont, small);
            below += S(15);
        }
        if (_rpm) p.Text(RpmText(live.Rpm), small, p.P.Muted, new RectangleF(left, below - rpmH / 2, width, rpmH), HAlign.Center, VAlign.Center, false);
        if (_steering)
        {
            var angleFont = p.Font(S(11), true);
            float ah = p.LineHeight(angleFont);
            p.Text(Angle(live.SteerDeg), angleFont, p.P.SteerInput, new RectangleF(cx - S(40), cy + S(57) - ah / 2, S(80), ah), HAlign.Center, VAlign.Center, false);
        }
        if (pedals) PedalRow(p, new RectangleF(dial.X, dial.Bottom + S(8), dial.Width, S(22)), live);
    }

    private void SteerRing(HudPainter p, float cx, float cy, float radius, in Live live)
    {
        var box = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
        p.G.DrawEllipse(p.FlatPen(p.P.InputTrack, S(3)), box);
        p.Fill(new RectangleF(cx - S(1), cy - radius - S(4), S(2), S(8)), p.P.InputTicks);
        if (live.SteerDeg is not { } deg) return;
        float sweep = (float)Math.Clamp(deg, -359.5, 359.5);
        if (Math.Abs(sweep) >= 0.5f) p.G.DrawArc(p.FlatPen(p.P.SteerInput, S(3)), box, 270, sweep);
        double a = (270 + sweep) * Math.PI / 180;
        float ex = cx + radius * (float)Math.Cos(a), ey = cy + radius * (float)Math.Sin(a);
        p.Fill(new RectangleF(ex - S(4), ey - S(4), S(8), S(8)), p.P.Accent);
    }

    private void RpmArc(HudPainter p, float cx, float cy, float radius, in Live live)
    {
        var sc = live.Scale;
        var box = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
        float stroke = S(10);
        p.G.DrawArc(p.FlatPen(p.P.InputTrack, stroke), box, 135, 270);
        float zone = (float)sc.Frac(sc.ZoneFrom) * 270;
        if (zone < 270) p.G.DrawArc(p.FlatPen(p.P.RedZone, stroke), box, 135 + zone, 270 - zone);
        if (live.Rpm is { } rpm && (!live.Limiter || FlashOn(p, live.Now, LimiterHz)) && sc.Frac(rpm) * 270 is var fill and > 0.3)
            p.G.DrawArc(p.FlatPen(live.Limiter ? p.P.Danger : p.P.Accent, stroke), box, 135, (float)fill);
        float inner = radius - stroke / 2;
        double step = sc.Max - sc.Min > 12000 ? 2000 : 1000;
        var tick = p.FlatPen(p.P.InputTicks, S(1.5f));
        for (double at = Math.Ceiling(sc.Min / step) * step; at <= sc.Max + 0.5; at += step)
            Spoke(p, tick, cx, cy, 135 + sc.Frac(at) * 270, inner - S(2), inner - S(6));
        Spoke(p, p.FlatPen(p.P.Accent, S(2.5f)), cx, cy, 135 + sc.Frac(sc.Shift) * 270, inner - S(1), inner - S(9));
    }

    private static void Spoke(HudPainter p, Pen pen, float cx, float cy, double degrees, float from, float to)
    {
        double a = degrees * Math.PI / 180;
        float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
        p.G.DrawLine(pen, cx + c * from, cy + s * from, cx + c * to, cy + s * to);
    }

    private void PedalRow(HudPainter p, RectangleF r, in Live live)
    {
        int n = PedalCount;
        float gap = S(8), w = (r.Width - gap * (n - 1)) / n;
        var small = p.Font(S(10), false);
        float x = r.X;
        if (_clutch) One("CLU", live.Clutch, p.P.ClutchInput);
        if (_brake) One("BRK", live.Brake, BrakeColor(p, live));
        if (_throttle) One("THR", live.Throttle, p.P.ThrottleInput);

        void One(string label, double? value, Color color)
        {
            HorizontalBar(p, new RectangleF(x, r.Y, w, S(8)), value, color);
            var text = new RectangleF(x, r.Y + S(10), w, r.Height - S(10));
            p.Text(label, small, p.P.Muted, text, HAlign.Left, VAlign.Top, false);
            p.Text(Percent(value), small, p.P.Muted, text, HAlign.Right, VAlign.Top, false);
            x += w + gap;
        }
    }

    private void DrawStrip(HudPainter p, HudView v, in Live live)
    {
        var b = p.Bounds;
        bool middle = _on.StripMiddle, graph = _on.StripGraph, pedals = AnyPedal;
        float fixedW = _on.StripFixed;
        float minW = fixedW + (middle ? 120 + 12 : 0);
        _k = Math.Max(0.1f, Math.Min(b.Height / 50, b.Width / minW));
        float oy = (b.Height - S(50)) / 2;
        float spare = b.Width / _k - fixedW - (middle ? 12 : 0);
        float middleW = middle ? Math.Max(120, spare) : 0, graphW = graph ? 90 + (middle ? 0 : Math.Max(0, spare)) : 0;
        var tile = new RectangleF(0, oy, S(50), S(50));
        var tileColor = live.Limiter ? FlashOn(p, live.Now, LimiterHz) ? p.P.Danger : p.P.InputTrack
            : live.Rpm is { } rpm && rpm >= live.Scale.Shift ? p.P.Accent : p.P.InputTrack;
        p.Fill(tile, tileColor);
        p.Text(GearText(v.Frame.Gear), p.Font(S(36), true), live.Abs ? GearColor(p, live) : p.P.Heading, tile, HAlign.Center, VAlign.Center, false);
        float x = tile.Right + S(12);
        float cy = oy + S(25);
        if (_speed)
        {
            var number = p.Font(S(20), true);
            var unitFont = p.Font(S(9), false);
            float nh = p.LineHeight(number), uh = p.LineHeight(unitFont), top = cy - (nh + uh) / 2;
            var box = new RectangleF(x, top, S(44), nh);
            string value = v.Frame.Speed is { } ms ? RaceFormat.SpeedValue(ms, v.Units).ToString("0", CultureInfo.InvariantCulture) : RaceFormat.Missing;
            p.Text(value, number, p.P.Text, box, HAlign.Right, VAlign.Center, false);
            p.Text(RaceFormat.SpeedUnit(v.Units), unitFont, p.P.Muted, new RectangleF(x, top + nh, S(44), uh), HAlign.Right, VAlign.Top, false);
            x += S(44 + 12);
        }
        if (middle)
        {
            Middle(p, new RectangleF(x, oy, S(middleW), S(50)), live);
            x += S(middleW + 12);
        }
        if (graph)
        {
            Graph(p, new RectangleF(x, cy - S(16), S(graphW), S(32)), live.Now, StripSeconds, false);
            x += S(graphW + 12);
        }
        if (pedals)
        {
            int i = 0;
            if (_clutch) Bar(live.Clutch, p.P.ClutchInput);
            if (_brake) Bar(live.Brake, BrakeColor(p, live));
            if (_throttle) Bar(live.Throttle, p.P.ThrottleInput);

            void Bar(double? value, Color color) => VerticalBar(p, new RectangleF(x + S(12) * i++, cy - S(16), S(8), S(32)), value, color);
        }
    }

    private void Middle(HudPainter p, RectangleF r, in Live live)
    {
        var small = p.Font(S(9), false);
        float textH = p.LineHeight(small);
        float total = (_rpm ? S(10) + S(5) : 0) + (_steering ? S(5) + S(5) : 0) + textH;
        float y = r.Y + (r.Height - total) / 2;
        if (_rpm)
        {
            RpmLine(p, live, new RectangleF(r.X, y, r.Width, S(10)), p.P.Muted, true);
            y += S(15);
        }
        if (_steering)
        {
            SteerBar(p, new RectangleF(r.X, y, r.Width, S(5)), live);
            y += S(10);
        }
        var row = new RectangleF(r.X, y, r.Width, textH);
        if (_rpm) p.Text(RpmText(live.Rpm), small, p.P.Muted, row, HAlign.Left, VAlign.Center, false);
        if (_steering) p.Text(Angle(live.SteerDeg), small, p.P.Muted, row, HAlign.Right, VAlign.Center, false);
    }
}
