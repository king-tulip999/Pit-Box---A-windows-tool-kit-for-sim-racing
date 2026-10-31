using System.Drawing;
using System.Globalization;
using PitBox.SessionStats;
using PitBox.SimRacing.Race;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Hud.Widgets;

internal sealed class ResultWidget : HudWidget
{
    public const string AllParts = "checker,gain,class,car,ribbon,name,carname,track,rating,incidents,awards,best,average,consistency,laps,time,strip";

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Int("seconds", "Show for", 90, 0, 600, Services.CapsUnits.S, "0 = until the session ends"),
        HudOption.Choice("position", "Position", "class",
            new HudChoice("class", "In my class"), new HudChoice("overall", "Overall")),
        HudOption.Choice("colors", "Colors", "finish",
            new HudChoice("finish", "By finish", "Gold, silver and bronze for a podium, the accent color otherwise"), new HudChoice("accent", "Accent color")),
        HudOption.Choice("nameFormat", "Name", "full",
            new HudChoice("full", "Full name"), new HudChoice("initial", "First initial and last name"), new HudChoice("last", "Last name only"), new HudChoice("first", "First name and last initial")),
        HudOption.Int("awardChips", "Award chips", 3, 0, 6, null, "Awards after the first, which is the ribbon"),
        HudOption.Columns("show", "Shows", AllParts,
            new HudChoice("checker", "Checkered strip"), new HudChoice("gain", "Places gained"), new HudChoice("class", "Class"),
            new HudChoice("car", "Car picture"), new HudChoice("ribbon", "Award ribbon"),
            new HudChoice("name", "Driver name"), new HudChoice("carname", "Car"), new HudChoice("track", "Track"),
            new HudChoice("rating", "iRating"), new HudChoice("incidents", "Incidents"), new HudChoice("awards", "Award chips"),
            new HudChoice("best", "Best lap"), new HudChoice("average", "Average clean lap"), new HudChoice("consistency", "Consistency"),
            new HudChoice("laps", "Laps and stops"), new HudChoice("time", "Race time"), new HudChoice("strip", "Lap times strip")),
    };

    private readonly RaceFinish _finish = new();

    private void Track(HudView v)
    {
        if (v.Frame.Status == SimStatus.Connected && !v.Frame.Simulated && !v.Frame.IsReplayPlaying) _finish.Update(v.Frame, v.Race, v.Now, DateTime.UtcNow);
    }

    private bool Showing(HudView v)
    {
        int seconds = v.Options.Int("seconds");
        return _finish.Card is not null && (seconds == 0 || v.Now - _finish.FinishedAt < seconds);
    }

    public override bool HasContent(HudView v)
    {
        Track(v);
        return v.Frame.Simulated || Showing(v);
    }

    private static Color Tier(HudPalette p, int finish, bool byFinish) => !byFinish ? p.Accent : finish switch { 1 => p.Gold, 2 => p.Silver, 3 => p.Bronze, _ => p.Accent };

    private static string Ordinal(int n) => (n % 100) is >= 11 and <= 13 ? "TH" : (n % 10) switch { 1 => "ST", 2 => "ND", 3 => "RD", _ => "TH" };

    public override void Draw(HudPainter p, HudView v)
    {
        Track(v);
        bool sample = p.IsPreview || v.Frame.Simulated || v.Frame.Status != SimStatus.Connected;
        var card = Showing(v) ? _finish.Card : sample ? FinishCard.Sample : null;
        p.Panel();
        if (card is null)
        {
            p.Text("Shows when you take the checkered flag", p.Small, p.P.Faint, p.Bounds.Inflated(-8 * p.Density, 0), HAlign.Center, VAlign.Center);
            return;
        }
        var show = v.Options.Columns("show");
        bool Has(string part) => show.Contains(part);
        bool overall = v.Options.Choice("position") == "overall" && card.Overall is > 0;
        int finish = overall ? card.Overall!.Value : card.Finish;
        float d = p.Density, w = p.Bounds.Width, bottom = p.Bounds.Bottom, pad = 12 * d;
        var tier = Tier(p.P, finish, v.Options.Choice("colors") == "finish");
        float y = 0;

        if (Has("checker"))
        {
            float check = 5 * d;
            p.Checker(new RectangleF(0, 0, w, check), tier, check);
            y += check;
        }

        float heroH = Math.Clamp(w * 0.4f, 84 * d, 128 * d);
        var hero = new RectangleF(0, y, w, heroH);
        p.Slant(hero, HudPalette.Alpha(tier, 36), 0, 0, 1, 0, 1, 0.55f, 0, 1);
        p.Slant(hero, HudPalette.Alpha(tier, 18), 0, 0, 0.62f, 0, 0.3f, 1, 0, 1);
        string number = finish.ToString(CultureInfo.InvariantCulture);
        float numberPx = heroH * 0.98f;
        var numberSize = p.DisplaySize(number, numberPx);
        p.Display(number, numberPx, tier, new RectangleF(pad, y + 13 * d, numberSize.Width, numberSize.Height));
        float side = pad + numberSize.Width + 7 * d;
        float labelH = p.LineHeight(p.Label);
        p.Text(p.LabelText(overall ? "Overall" : "Finished"), p.Label, p.P.Muted, new RectangleF(side, y + 10 * d, w / 3, labelH), HAlign.Left, VAlign.Top);
        p.Display(Ordinal(finish), numberPx * 0.3f, tier, new RectangleF(side, y + 12 * d + labelH, w / 3, numberPx * 0.3f));
        if (Has("gain") && !overall && card.Gained is { } gained)
        {
            var gain = new RectangleF(w / 2, y + 11 * d, w / 2 - pad, 30 * d);
            p.Display(gained > 0 ? "+" + gained : gained < 0 ? "−" + -gained : "0", 30 * d, gained > 0 ? p.P.GoodText : gained < 0 ? p.P.DangerText : p.P.Muted, gain, HAlign.Right);
            p.Text($"{RaceFormat.Pos(card.Start ?? 0)} to {RaceFormat.Pos(card.Finish)}", p.Small, p.P.Muted, new RectangleF(w / 2, gain.Bottom + 3 * d, w / 2 - pad, p.LineHeight(p.Small)), HAlign.Right, VAlign.Top);
        }
        if (Has("class"))
            p.Text(overall ? $"{card.CarClass} · {RaceFormat.Pos(card.Finish)} in class" : card.CarClass, p.Small, p.P.Muted,
                new RectangleF(w / 2, y + heroH - p.LineHeight(p.Small) - 10 * d, w / 2 - pad, p.LineHeight(p.Small)), HAlign.Right, VAlign.Top);
        y += heroH;

        var ribbonAward = Has("ribbon") && card.Awards.Count > 0 ? card.Awards[0] : (Award?)null;
        float carH = Math.Clamp(w * 0.3f, 60 * d, 100 * d);
        if ((Has("car") || ribbonAward is not null) && y + carH < bottom)
        {
            var zone = new RectangleF(0, y - 14 * d, w, carH);
            p.Slant(zone, HudPalette.Alpha(tier, 58), 0, 0.55f, 1, 0.23f, 1, 0.59f, 0, 0.91f);
            p.Slant(zone, HudPalette.Alpha(tier, 190), 0, 0.94f, 1, 0.62f, 1, 0.66f, 0, 0.98f);
            if (Has("car") && HudArt.Car(card.Car) is { } picture) p.Picture(picture, new RectangleF(w * 0.12f, zone.Y + 2 * d, w * 0.76f, carH * 0.92f));
            if (ribbonAward is { } award)
            {
                string name = p.LabelText(RaceResult.Name(award));
                float ribbonW = p.Measure(name, p.SmallBold) + 26 * d, ribbonH = p.LineHeight(p.SmallBold) + 8 * d;
                var ribbon = new RectangleF(w - ribbonW, zone.Bottom - ribbonH - 2 * d, ribbonW, ribbonH);
                p.Slant(ribbon, p.P.Accent, 0.13f, 0, 1, 0, 1, 1, 0, 1);
                p.Text(name, p.SmallBold, p.P.OnAccent, new RectangleF(ribbon.X + 16 * d, ribbon.Y, ribbonW - 22 * d, ribbonH), HAlign.Left, VAlign.Center, false);
            }
            y = zone.Bottom;
        }

        float bigH = p.LineHeight(p.Big), smallH = p.LineHeight(p.Small);
        y += 6 * d;
        if (Has("name") && y + bigH <= bottom)
        {
            p.Text(RaceFormat.Name(card.Driver, v.Options.Choice("nameFormat")), p.Big, p.P.Heading, new RectangleF(pad, y, w - pad * 2, bigH), HAlign.Left, VAlign.Top);
            y += bigH;
        }
        if (Has("carname") && y + smallH <= bottom)
        {
            p.Text(card.Car, p.Small, p.P.Muted, new RectangleF(pad, y, w - pad * 2, smallH), HAlign.Left, VAlign.Top);
            y += smallH;
        }
        if (Has("track") && y + smallH <= bottom)
        {
            p.Text(card.Place, p.Small, p.P.Muted, new RectangleF(pad, y, w - pad * 2, smallH), HAlign.Left, VAlign.Top);
            y += smallH;
        }
        y += 8 * d;

        float chipH = p.LineHeight(p.SmallBold) + 6 * d;
        int awardChips = Has("awards") ? v.Options.Int("awardChips") : 0;
        var chipAwards = card.Awards.Skip(ribbonAward is null ? 0 : 1).Take(awardChips).ToList();
        if ((Has("rating") || Has("incidents") || chipAwards.Count > 0) && y + chipH <= bottom)
        {
            float x = pad;
            bool Chip(string label, string value, Color valueColor, Color? edge = null)
            {
                float lw = label.Length > 0 ? p.Measure(label, p.SmallBold) : 0, vw = p.Measure(value, p.Small), cw = lw + vw + (label.Length > 0 ? 28 : 14) * d;
                if (x + cw > w - pad) return false;
                var r = new RectangleF(x, y, cw, chipH);
                p.Fill(r, edge is { } e ? HudPalette.Alpha(e, 40) : p.Sunken(0.25));
                p.Outline(r, edge ?? p.P.Line);
                if (label.Length > 0)
                {
                    p.Text(label, p.SmallBold, p.P.Muted, new RectangleF(x + 7 * d, y, lw + 2, chipH), HAlign.Left, VAlign.Center, false);
                    float divider = MathF.Round(x + 14 * d + lw);
                    p.Line(divider, y + 5 * d, divider, y + chipH - 5 * d, p.P.Line);
                }
                p.Text(value, p.Small, valueColor, new RectangleF(x + (label.Length > 0 ? 21 * d + lw : 7 * d), y, vw + 2, chipH), HAlign.Left, VAlign.Center, false);
                x += cw + 5 * d;
                return true;
            }
            if (Has("rating"))
            {
                if (!card.Official) Chip("Hosted", "Ratings unchanged", p.P.Text);
                else if (card.IRating is { } ir)
                {
                    double? rounded = card.IRatingChange is { } c ? Math.Round(c) : null;
                    string change = rounded is { } r ? $" {RaceFormat.Signed(r, "0")} est." : "";
                    Chip("iR", ir.ToString("N0", CultureInfo.InvariantCulture) + change, rounded is > 0 ? p.P.GoodText : rounded is < 0 ? p.P.DangerText : p.P.Text);
                }
            }
            if (Has("incidents") && card.Incidents is { } inc) Chip("Inc", inc + "x", p.P.Text);
            foreach (var award in chipAwards)
                if (!Chip("", RaceResult.Name(award), p.P.Heading, tier)) break;
            y += chipH + 6 * d;
        }

        float rowH = p.LineHeight(p.Body) + 9 * d;
        void Row(string label, string value, Action<RectangleF>? extra = null)
        {
            if (y + rowH > bottom - 4 * d) { y = bottom; return; }
            p.Line(pad, y, w - pad, y, p.P.Line);
            var r = new RectangleF(pad, y, w - pad * 2, rowH);
            p.Text(label, p.Small, p.P.Muted, r, HAlign.Left, VAlign.Center);
            float vw = p.Measure(value, p.Bold);
            p.Text(value, p.Bold, p.P.Heading, r, HAlign.Right, VAlign.Center, false);
            extra?.Invoke(new RectangleF(r.Right - vw - 8 * d, r.Y, 0, r.Height));
            y += rowH;
        }
        if (Has("best")) Row("Best lap", RaceFormat.LapTime(card.Best));
        if (Has("average")) Row("Average clean lap", RaceFormat.LapTime(card.AverageClean));
        if (Has("consistency") && card.Consistency is { } grade)
            Row("Consistency", grade.ToString("0.0", CultureInfo.InvariantCulture) + "%", at =>
            {
                const int segments = 12;
                float cellW = 5 * d, gap = 2 * d, cellH = 8 * d;
                int on = (int)Math.Round(Math.Clamp((grade - 70) / 30, 0, 1) * segments);
                float x0 = at.X - segments * (cellW + gap);
                for (int i = 0; i < segments; i++)
                    p.Slant(new RectangleF(x0 + i * (cellW + gap), at.Y + (at.Height - cellH) / 2, cellW + 2 * d, cellH), i < on ? tier : p.Sunken(0.4), 0.3f, 0, 1, 0, 0.7f, 1, 0, 1);
            });
        if (Has("laps")) Row("Laps", card.PitStops > 0 ? $"{card.Laps.Count} · {RaceFormat.Count(card.PitStops, "stop")}" : card.Laps.Count.ToString(CultureInfo.InvariantCulture));
        if (Has("time")) Row("Race time", RaceFormat.Clock(card.RaceTime));

        float stripH = 34 * d + smallH + 4 * d;
        if (Has("strip") && card.Laps.Count > 1 && y + stripH <= bottom - 4 * d)
        {
            y += 4 * d;
            p.Text(p.LabelText("Lap times"), p.Label, p.P.Muted, new RectangleF(pad, y, w - pad * 2, smallH), HAlign.Left, VAlign.Top);
            int clean = card.Laps.Count(l => l.Clean);
            p.Text($"{100.0 * clean / card.Laps.Count:0}% clean", p.Small, p.P.Muted, new RectangleF(pad, y, w - pad * 2, smallH), HAlign.Right, VAlign.Top);
            y += smallH + 4 * d;
            var area = new RectangleF(pad, y, w - pad * 2, 30 * d);
            double lo = card.Laps.Min(l => l.Time), hi = Math.Min(card.Laps.Max(l => l.Time), lo * 1.05), range = Math.Max(0.001, hi - lo);
            float step = area.Width / card.Laps.Count;
            for (int i = 0; i < card.Laps.Count; i++)
            {
                var (time, isClean) = card.Laps[i];
                float share = time >= hi ? 0.08f : (float)(0.08 + 0.92 * (hi - time) / range);
                var color = time == lo ? p.P.Heading : isClean ? HudPalette.Alpha(tier, 210) : HudPalette.Alpha(p.P.Danger, 150);
                p.Fill(new RectangleF(area.X + i * step, area.Bottom - share * area.Height, Math.Max(1, step - (step > 3 ? 1 : 0)), share * area.Height), color);
            }
            p.Line(area.X, area.Bottom, area.Right, area.Bottom, p.P.Line);
        }
    }
}
