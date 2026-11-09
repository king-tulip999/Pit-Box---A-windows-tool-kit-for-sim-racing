using System.Drawing;
using System.Globalization;
using PitBox.SessionStats;
using PitBox.SimRacing.Race;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Hud.Widgets;

internal sealed class ResultWidget : HudWidget
{
    public const string AllParts = "checker,gain,class,car,ribbon,name,carname,track,rating,incidents,awards,best,average,consistency,laps,time,strip";
    public const string Card = "card", Banner = "banner", Stamp = "stamp", Story = "story";

    private static readonly HashSet<string> BannerParts = new(StringComparer.OrdinalIgnoreCase) { "gain", "car", "ribbon", "name", "carname", "track", "best", "average", "consistency", "incidents", "laps", "time", "strip" };
    private static readonly HashSet<string> StampParts = new(StringComparer.OrdinalIgnoreCase) { "gain", "ribbon", "best", "average", "incidents", "consistency", "strip" };
    private static readonly HashSet<string> StoryParts = new(StringComparer.OrdinalIgnoreCase) { "gain", "name", "carname", "track", "best", "average", "consistency", "laps", "incidents", "time" };

    public static bool Offers(string look, string part) => look switch
    {
        Banner => BannerParts.Contains(part),
        Stamp => StampParts.Contains(part),
        Story => StoryParts.Contains(part),
        _ => true,
    };

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Choice("look", "Look", Card,
            new HudChoice(Card, "Card", "The full card: position, car picture, driver, ratings, stats and every lap's time"),
            new HudChoice(Banner, "Banner", "A wide low result graphic with the car beside your name"),
            new HudChoice(Stamp, "Stamp", "A small block: position, places gained and the key numbers"),
            new HudChoice(Story, "Lap story", "A chart of every lap's time with your best, average, stops and incident laps")),
        HudOption.Int("seconds", "Show for", 90, 0, 600, Services.CapsUnits.S, "0 = until the session ends"),
        HudOption.Choice("position", "Position", "class",
            new HudChoice("class", "In my class"), new HudChoice("overall", "Overall")),
        HudOption.Choice("colors", "Colors", "finish",
            new HudChoice("finish", "By finish", "Gold, silver and bronze for a podium, the accent color otherwise"), new HudChoice("accent", "Accent color")),
        HudOption.Choice("nameFormat", "Name", "full",
            new HudChoice("full", "Full name"), new HudChoice("initial", "First initial and last name"), new HudChoice("last", "Last name only"), new HudChoice("first", "First name and last initial")),
        HudOption.Int("awardChips", "Award chips", 3, 0, 6, null, "Awards after the first, which shows beside your name"),
        HudOption.Columns("show", "Shows", AllParts,
            new HudChoice("checker", "Checkered strip"), new HudChoice("gain", "Places gained"), new HudChoice("class", "Class"),
            new HudChoice("car", "Car picture"), new HudChoice("ribbon", "Award beside the name"),
            new HudChoice("name", "Driver name"), new HudChoice("carname", "Car"), new HudChoice("track", "Track"),
            new HudChoice("rating", "iRating"), new HudChoice("incidents", "Incidents"), new HudChoice("awards", "Award chips"),
            new HudChoice("best", "Best lap"), new HudChoice("average", "Average clean lap"), new HudChoice("consistency", "Consistency"),
            new HudChoice("laps", "Laps and stops"), new HudChoice("time", "Race time"), new HudChoice("strip", "Lap times strip")),
    };

    public static SizeF Natural(HudOptions o) => o.Choice("look") switch
    {
        Banner => new SizeF(580, 150),
        Stamp => new SizeF(240, 198),
        Story => new SizeF(460, 272),
        _ => new SizeF(300, 448),
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

    internal static string Gain(int? gained) => gained switch { null => RaceFormat.Missing, > 0 => "+" + gained.Value.ToString(CultureInfo.InvariantCulture), < 0 => "−" + (-gained.Value).ToString(CultureInfo.InvariantCulture), _ => "0" };

    internal static double ConsistencyShare(double grade) => Math.Clamp(grade / 100, 0, 1);

    private readonly record struct Result(FinishCard Card, int Finish, bool Overall, Color Tier, Color Ink, string Name, IReadOnlyList<string> Show)
    {
        public bool Has(string part) => Show.Contains(part);
    }

    private static string CarAndTrack(Result r) =>
        string.Join(" · ", new[] { r.Has("carname") ? r.Card.Car : null, r.Has("track") ? r.Card.Place : null }.Where(t => !string.IsNullOrEmpty(t)));

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
        bool overall = v.Options.Choice("position") == "overall" && card.Overall is > 0;
        int finish = overall ? card.Overall!.Value : card.Finish;
        var tier = Tier(p.P, finish, v.Options.Choice("colors") == "finish");
        var r = new Result(card, finish, overall, tier, HudPalette.On(tier), RaceFormat.Name(card.Driver, v.Options.Choice("nameFormat")), v.Options.Columns("show"));
        switch (v.Options.Choice("look"))
        {
            case Banner: DrawBanner(p, r); break;
            case Stamp: DrawStamp(p, r); break;
            case Story: DrawStory(p, r); break;
            default: DrawCard(p, v, r); break;
        }
    }

    private static float Scale(HudPainter p, float w, float h, out float ox, out float oy)
    {
        var b = p.Bounds;
        float k = Math.Max(0.1f, Math.Min(b.Width / w, b.Height / h));
        ox = (b.Width - w * k) / 2;
        oy = (b.Height - h * k) / 2;
        return k;
    }

    private static void Label(HudPainter p, string text, float x, float centerY, float width, Color color, Font font, HAlign align = HAlign.Left)
    {
        float lh = p.LineHeight(font);
        p.Text(p.LabelText(text), font, color, new RectangleF(align == HAlign.Right ? x - width : align == HAlign.Center ? x - width / 2 : x, centerY - lh / 2, width, lh), align, VAlign.Center);
    }

    private static void Number(HudPainter p, string text, float px, Color color, float x, float centerY, float width, HAlign align = HAlign.Left)
    {
        float h = p.DisplaySize("8", px).Height;
        float left = align == HAlign.Right ? x - width : align == HAlign.Center ? x - width / 2 : x;
        p.Display(text, px, color, new RectangleF(left, centerY - h / 2, width, h), align, VAlign.Center);
    }

    private static void LapStrip(HudPainter p, RectangleF area, FinishCard card, Color tier)
    {
        var laps = card.Laps;
        if (laps.Count < 2 || area.Width < 2 || area.Height < 2) return;
        double best = card.Best ?? laps.Min(l => l.Time);
        var times = laps.Select(l => l.Time).OrderBy(t => t).ToList();
        double median = times[times.Count / 2], hi = Math.Max(best + 0.01, median * 1.03);
        float gap = laps.Count < area.Width / 3 ? 1 : 0, step = area.Width / laps.Count;
        for (int i = 0; i < laps.Count; i++)
        {
            var (time, clean) = laps[i];
            float share = 1 - 0.75f * (float)Math.Clamp((time - best) / (hi - best), 0, 1);
            float h = Math.Max(2, area.Height * share);
            var color = !clean ? p.P.Danger : time == best ? p.P.Heading : HudPalette.Alpha(tier, 217);
            p.Fill(new RectangleF(area.X + i * step, area.Bottom - h, Math.Max(1, step - gap), h), color);
        }
    }

    private static void Meter(HudPainter p, RectangleF r, double share, Color color, int segments)
    {
        float gap = 2 * p.Density, w = (r.Width - gap * (segments - 1)) / segments;
        for (int i = 0; i < segments; i++)
        {
            var cell = new RectangleF(r.X + i * (w + gap), r.Y, w, r.Height);
            p.Fill(cell, HudPalette.Alpha(p.P.Muted, 46));
            float fill = (float)Math.Clamp(share * segments - i, 0, 1);
            if (fill > 0) p.Fill(cell with { Width = w * fill }, color);
        }
    }

    private void DrawCard(HudPainter p, HudView v, Result r)
    {
        var card = r.Card;
        var show = v.Options.Columns("show");
        bool Has(string part) => show.Contains(part);
        float d = p.Density, w = p.Bounds.Width, bottom = p.Bounds.Bottom, pad = 12 * d;
        float y = 0;
        if (Has("checker"))
        {
            float check = 5 * d;
            p.Checker(new RectangleF(0, 0, w, check), r.Tier, check);
            y += check;
        }

        float heroH = 100 * d;
        var hero = new RectangleF(0, y, w, heroH);
        p.Slant(hero, HudPalette.Alpha(r.Tier, 56), 0, 0, 0.57f, 0, 0.43f, 1, 0, 1);
        string pos = RaceFormat.Pos(r.Finish);
        float posPx = 84 * d;
        var posSize = p.DisplaySize(pos, posPx);
        p.Display(pos, posPx, r.Tier, new RectangleF(pad - 2 * d, y + 13 * d, posSize.Width + 2, posSize.Height), HAlign.Left, VAlign.Top);
        var label = p.Label;
        Label(p, r.Overall ? "Overall" : "Finished", pad + posSize.Width + 6 * d, y + 19 * d, w / 3, p.P.Muted, label);
        if (Has("gain") && !r.Overall && card.Gained is { } gained)
        {
            Number(p, Gain(gained), 30 * d, gained > 0 ? p.P.GoodText : gained < 0 ? p.P.DangerText : p.P.Muted, w - pad, y + 24 * d, w / 2 - pad, HAlign.Right);
            p.Text($"{RaceFormat.Pos(card.Start ?? 0)} to {RaceFormat.Pos(card.Finish)}", p.Font(12, false), p.P.Muted, new RectangleF(w / 2, y + 44 * d, w / 2 - pad, p.LineHeight(p.Font(12, false))), HAlign.Right, VAlign.Top);
        }
        if (Has("class"))
            Label(p, r.Overall ? $"{card.CarClass} · {RaceFormat.Pos(card.Finish)} in class" : card.CarClass, w - pad, y + 70 * d, w / 2 - pad, p.P.Muted, label, HAlign.Right);
        y += heroH;

        float carH = Math.Clamp(w * 0.36f, 60 * d, 120 * d);
        if (Has("car") && y + carH < bottom)
        {
            var zone = new RectangleF(0, y - 14 * d, w, carH);
            p.Slant(zone, HudPalette.Alpha(r.Tier, 90), 0, 0.62f, 1, 0.2f, 1, 0.5f, 0, 0.92f);
            if (HudArt.Car(card.Car) is { } picture) p.Picture(picture, new RectangleF(w * 0.1f, zone.Y - 4 * d, w * 0.8f, carH));
            y = zone.Bottom;
        }

        var nameFont = p.Font(20, true);
        float nameH = p.LineHeight(nameFont), smallH = p.LineHeight(p.Small);
        y += 8 * d;
        var ribbonAward = Has("ribbon") && card.Awards.Count > 0 ? card.Awards[0] : (Award?)null;
        if (Has("name") && y + nameH <= bottom)
        {
            float room = w - pad * 2;
            if (ribbonAward is { } award)
            {
                string tag = p.LabelText(RaceResult.Name(award));
                float tagW = p.Measure(tag, p.SmallBold) + 18 * d, tagH = p.LineHeight(p.SmallBold) + 8 * d;
                var box = new RectangleF(w - pad - tagW, y + (nameH - tagH) / 2, tagW, tagH);
                p.Fill(box, r.Tier);
                p.Text(tag, p.SmallBold, r.Ink, box, HAlign.Center, VAlign.Center, false);
                room -= tagW + 8 * d;
            }
            p.Text(r.Name, nameFont, p.P.Heading, new RectangleF(pad, y, room, nameH), HAlign.Left, VAlign.Top);
            y += nameH;
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
            bool Chip(string text, Color color, Color? edge = null)
            {
                float cw = p.Measure(text, p.SmallBold) + 14 * d;
                if (x + cw > w - pad) return false;
                var box = new RectangleF(x, y, cw, chipH);
                p.Fill(box, edge is { } e ? HudPalette.Alpha(e, 40) : p.Solid(p.Raised(0.08)));
                p.Text(text, p.SmallBold, color, box, HAlign.Center, VAlign.Center, false);
                x += cw + 4 * d;
                return true;
            }
            if (Has("rating"))
            {
                if (!card.Official) Chip("Hosted · Ratings unchanged", p.P.Muted);
                else if (card.IRating is { } ir)
                {
                    double? rounded = card.IRatingChange is { } c ? Math.Round(c) : null;
                    string change = rounded is { } rc ? $" {RaceFormat.Signed(rc, "0")} est." : "";
                    Chip("iR " + ir.ToString("N0", CultureInfo.InvariantCulture) + change, rounded is > 0 ? p.P.GoodText : rounded is < 0 ? p.P.DangerText : p.P.Text);
                }
            }
            if (Has("incidents") && card.Incidents is { } inc) Chip($"Inc {inc}x", inc > 4 ? p.P.WarnText : p.P.Muted);
            foreach (var award in chipAwards)
                if (!Chip(RaceResult.Name(award), p.P.Heading, r.Tier)) break;
            y += chipH + 10 * d;
        }

        var tiles = new List<(string Label, string Value, double? Meter)>(4);
        if (Has("best")) tiles.Add(("Best lap", RaceFormat.LapTime(card.Best), null));
        if (Has("average")) tiles.Add(("Avg clean", RaceFormat.LapTime(card.AverageClean), null));
        if (Has("consistency")) tiles.Add(("Consistency", card.Consistency is { } grade ? grade.ToString("0.0", CultureInfo.InvariantCulture) + "%" : RaceFormat.Missing, card.Consistency));
        if (Has("laps")) tiles.Add(("Laps · stops", $"{card.Laps.Count} · {card.PitStops}", null));
        float tileGap = 8 * d, tileW = (w - pad * 2 - tileGap) / 2, tileH = 44 * d, stripH = 14 * d;
        int tileRows = (tiles.Count + 1) / 2;
        float stack = tileRows * (tileH + 6 * d) + (Has("time") ? 14 * d : 0) + (Has("strip") ? stripH : 0);
        y = Math.Max(y, bottom - pad - stack);
        for (int i = 0; i < tiles.Count; i += 2)
        {
            if (y + tileH > bottom - pad) break;
            for (int j = i; j < Math.Min(i + 2, tiles.Count); j++)
            {
                var (name, value, meter) = tiles[j];
                var tile = new RectangleF(pad + (j - i) * (tileW + tileGap), y, tileW, tileH);
                p.Fill(tile, p.Solid(p.Raised(0.06)));
                Label(p, name, tile.X + 8 * d, tile.Y + 12 * d, tileW - 16 * d, p.P.Muted, label);
                Number(p, value, 21 * d, p.P.Heading, tile.X + 8 * d, tile.Y + 30 * d, tileW - 16 * d);
                if (meter is { } m) Meter(p, new RectangleF(tile.Right - 8 * d - 44 * d, tile.Y + 27 * d, 44 * d, 6 * d), ConsistencyShare(m), r.Tier, 5);
            }
            y += tileH + 6 * d;
        }

        if (Has("time") && y + p.LineHeight(label) <= bottom - pad)
        {
            Label(p, $"Race time {RaceFormat.Clock(card.RaceTime)}", pad, y + 6 * d, w - pad * 2, p.P.Muted, label);
            y += 14 * d;
        }
        if (Has("strip") && y + stripH <= bottom - pad + 1) LapStrip(p, new RectangleF(pad, y, w - pad * 2, stripH), card, r.Tier);
    }

    private static void DrawBanner(HudPainter p, Result r)
    {
        var card = r.Card;
        float k = Scale(p, 580, 150, out float ox, out float oy);
        float X(float v) => ox + v * k;
        float Y(float v) => oy + v * k;
        var label = p.Font(10.5f * k, true);
        p.Slant(new RectangleF(X(0), Y(0), 132 * k, 150 * k), r.Tier, 0, 0, 1, 0, 108f / 132, 1, 0, 1);
        Number(p, RaceFormat.Pos(r.Finish), 70 * k, r.Ink, X(56), Y(62), 112 * k, HAlign.Center);
        string from = r.Overall ? "Overall" : card.Start is > 0 && r.Has("gain") ? $"{Gain(card.Gained)} from {RaceFormat.Pos(card.Start.Value)}" : "";
        if (from.Length > 0) Label(p, from, X(54), Y(110), 104 * k, r.Ink, p.Font(11 * k, true), HAlign.Center);
        if (r.Has("car") && HudArt.Car(card.Car) is { } picture) p.Picture(picture, new RectangleF(X(150), Y(58), 196 * k, 72 * k));
        float nameW = 0;
        if (r.Has("name"))
        {
            string name = r.Name.ToUpperInvariant();
            float namePx = 26 * k;
            nameW = p.DisplaySize(name, namePx).Width;
            if (nameW > 230 * k) { namePx *= 230 * k / nameW; nameW = 230 * k; }
            Number(p, name, namePx, p.P.Heading, X(136), Y(22), 236 * k);
        }
        if (r.Has("ribbon") && card.Awards.Count > 0)
        {
            string tag = p.LabelText(RaceResult.Name(card.Awards[0]));
            float tagW = p.Measure(tag, label) + 14 * k;
            var box = new RectangleF(nameW > 0 ? X(144) + nameW : X(137), Y(13), tagW, 18 * k);
            if (box.Right < X(370))
            {
                p.Fill(box, HudPalette.Alpha(r.Tier, 77));
                p.Text(tag, label, p.P.Heading, box, HAlign.Center, VAlign.Center, false);
            }
        }
        string carTrack = CarAndTrack(r);
        if (carTrack.Length > 0) p.Text(carTrack, p.Font(11.5f * k, false), p.P.Muted, new RectangleF(X(137), Y(42) - 8 * k, 236 * k, 16 * k), HAlign.Left, VAlign.Center);
        int slot = 0;
        if (r.Has("best")) Stat(slot++, "Best lap", RaceFormat.LapTime(card.Best));
        if (r.Has("average")) Stat(slot++, "Avg clean", RaceFormat.LapTime(card.AverageClean));
        if (r.Has("consistency")) Stat(slot++, "Consistency", card.Consistency is { } g ? g.ToString("0.0", CultureInfo.InvariantCulture) + "%" : RaceFormat.Missing);
        if (r.Has("incidents")) Stat(slot, "Incidents", card.Incidents is { } inc ? inc + "x" : RaceFormat.Missing);
        var totals = new List<string>();
        if (r.Has("laps")) { totals.Add(RaceFormat.Count(card.Laps.Count, "lap")); totals.Add(RaceFormat.Count(card.PitStops, "stop")); }
        if (r.Has("time")) totals.Add(RaceFormat.Clock(card.RaceTime));
        if (totals.Count > 0) Label(p, string.Join(" · ", totals), X(380), Y(124), 188 * k, p.P.Muted, label);
        if (r.Has("strip")) LapStrip(p, new RectangleF(X(132), Y(140), 448 * k, 10 * k), card, r.Tier);

        void Stat(int i, string name, string value)
        {
            float sx = X(380 + (i % 2) * 100), sy = Y(20 + (i / 2) * 54);
            Label(p, name, sx, sy, 96 * k, p.P.Muted, label);
            Number(p, value, 22 * k, p.P.Heading, sx, sy + 22 * k, 96 * k);
        }
    }

    private static void DrawStamp(HudPainter p, Result r)
    {
        var card = r.Card;
        var b = p.Bounds;
        float k = Math.Max(0.1f, Math.Min(b.Width / 240, b.Height / 198));
        float pad = 12 * k, w = b.Width, colX = w - 90 * k;
        var label = p.Font(10.5f * k, true);
        string pos = RaceFormat.Pos(r.Finish);
        float fullH = p.DisplaySize("P8", 100 * k).Height, block = 2 * pad + fullH;
        p.Fill(new RectangleF(0, 0, w, block), r.Tier);
        float room = colX - 10 * k - 2 * pad, px = 100 * k;
        float posW = p.DisplaySize(pos, px).Width;
        if (posW > room) px *= room / posW;
        p.Display(pos, px, r.Ink, new RectangleF(pad - 2 * k, 0, room + 2 * k, block), HAlign.Left, VAlign.Center);
        p.Fill(new RectangleF(colX - 10 * k, pad, 1, block - 2 * pad), HudPalette.Alpha(r.Ink, 77));
        float lh = p.LineHeight(label);
        if (!r.Overall && r.Has("gain") && card.Gained is { } gained)
        {
            Label(p, "Gained", colX, pad + 3 * k, 80 * k, r.Ink, label);
            Number(p, Gain(gained), 26 * k, r.Ink, colX, pad + 26 * k, 80 * k);
            Label(p, $"{RaceFormat.Pos(card.Start ?? 0)} to {RaceFormat.Pos(card.Finish)}", colX, pad + 47 * k, 80 * k, r.Ink, label);
        }
        else Label(p, r.Overall ? "Overall" : "Finished", colX, pad + 3 * k, 80 * k, r.Ink, label);
        if (r.Has("ribbon") && card.Awards.Count > 0) Label(p, RaceResult.Name(card.Awards[0]), colX, block - pad - lh / 2 + 2 * k, 80 * k, r.Ink, label);
        float ly = block + 18 * k, vy = block + 40 * k;
        float lapX = pad;
        if (r.Has("best"))
        {
            Label(p, "Best lap", lapX, ly, 84 * k, p.P.Muted, label);
            Number(p, RaceFormat.LapTime(card.Best), 20 * k, p.P.Heading, lapX, vy, 84 * k);
            lapX = 98 * k;
        }
        if (r.Has("average"))
        {
            Label(p, "Avg clean", lapX, ly, 84 * k, p.P.Muted, label);
            Number(p, RaceFormat.LapTime(card.AverageClean), 20 * k, p.P.Heading, lapX, vy, 84 * k);
        }
        if (r.Has("incidents"))
        {
            Label(p, "Inc", w - pad, ly, 40 * k, p.P.Muted, label, HAlign.Right);
            Number(p, card.Incidents is { } inc ? inc + "x" : RaceFormat.Missing, 20 * k, p.P.Heading, w - pad, vy, 40 * k, HAlign.Right);
        }
        float cy = block + 62 * k;
        if (r.Has("consistency"))
        {
            string grade = card.Consistency is { } g ? $"Consistency {g.ToString("0.0", CultureInfo.InvariantCulture)}%" : "Consistency " + RaceFormat.Missing;
            float gw = p.Measure(p.LabelText(grade), label);
            Label(p, grade, pad, cy, gw + 4, p.P.Muted, label);
            if (card.Consistency is { } m) Meter(p, new RectangleF(pad + gw + 10 * k, cy - 3 * k, w - pad - (pad + gw + 10 * k), 6 * k), ConsistencyShare(m), r.Tier, 8);
        }
        if (r.Has("strip")) LapStrip(p, RectangleF.FromLTRB(pad, cy + 10 * k, w - pad, b.Height - pad), card, r.Tier);
    }

    private static void DrawStory(HudPainter p, Result r)
    {
        var card = r.Card;
        float k = Scale(p, 460, 272, out float ox, out float oy);
        float X(float v) => ox + v * k;
        float Y(float v) => oy + v * k;
        float W = 460;
        var label = p.Font(10.5f * k, true);
        string pos = RaceFormat.Pos(r.Finish);
        float blockW = Math.Max(76 * k, p.DisplaySize(pos, 44 * k).Width + 20 * k);
        var block = new RectangleF(X(12), Y(12), blockW, 50 * k);
        p.Fill(block, r.Tier);
        p.Display(pos, 44 * k, r.Ink, block, HAlign.Center, VAlign.Center);
        float textX = block.Right + 12 * k, textW = X(W - 70) - textX;
        var nameFont = p.Font(20 * k, true);
        if (r.Has("name")) p.Text(r.Name, nameFont, p.P.Heading, new RectangleF(textX, Y(26) - p.LineHeight(nameFont) / 2, textW, p.LineHeight(nameFont)), HAlign.Left, VAlign.Center);
        var small = p.Font(12 * k, false);
        string carTrack = CarAndTrack(r);
        if (carTrack.Length > 0) p.Text(carTrack, small, p.P.Muted, new RectangleF(textX, Y(48) - p.LineHeight(small) / 2, textW, p.LineHeight(small)), HAlign.Left, VAlign.Center);
        if (!r.Overall && r.Has("gain") && card.Gained is { } gained)
        {
            Number(p, Gain(gained), 38 * k, gained > 0 ? p.P.GoodText : gained < 0 ? p.P.DangerText : p.P.Muted, X(W - 12), Y(30), 70 * k, HAlign.Right);
            Label(p, $"From {RaceFormat.Pos(card.Start ?? 0)}", X(W - 12), Y(56), 80 * k, p.P.Muted, label, HAlign.Right);
        }

        var chart = new RectangleF(X(40), Y(82), (W - 52) * k, 120 * k);
        var laps = card.Laps;
        if (laps.Count >= 2 && card.Best is { } best)
        {
            double lo = best - 0.3, hi = best + 2.6;
            float YOf(double t) => chart.Bottom - chart.Height * (float)Math.Clamp((t - lo) / (hi - lo), 0, 1);
            float XOf(int i) => chart.X + chart.Width * i / (laps.Count - 1);
            var tick = p.Font(11 * k, false);
            for (double t = Math.Ceiling(lo); t <= hi; t++)
            {
                float y = YOf(t);
                p.Line(chart.X, y, chart.Right, y, p.P.Line);
                p.Text(RaceFormat.Clock(t), tick, p.P.Faint, new RectangleF(chart.X - 40 * k, y - p.LineHeight(tick) / 2, 36 * k, p.LineHeight(tick)), HAlign.Right, VAlign.Center, false);
            }
            if (r.Has("average") && card.AverageClean is { } avg)
            {
                p.Line(chart.X, YOf(avg), chart.Right, YOf(avg), HudPalette.Alpha(p.P.Muted, 204));
                Label(p, $"Avg {RaceFormat.LapTime(avg)}", chart.Right - 4 * k, YOf(avg) + 9 * k, 120 * k, p.P.Muted, label, HAlign.Right);
            }
            var pen = p.Pen(r.Tier, 2 * k);
            for (int i = 1; i < laps.Count; i++)
                if (laps[i].Clean && laps[i - 1].Clean) p.G.DrawLine(pen, XOf(i - 1), YOf(laps[i - 1].Time), XOf(i), YOf(laps[i].Time));
            for (int i = 0; i < laps.Count; i++)
            {
                if (laps[i].Clean) continue;
                float x = XOf(i);
                p.Fill(new RectangleF(x - 3 * k, chart.Y - 2 * k, 6 * k, 6 * k), p.P.Danger);
                for (float y = chart.Y + 6 * k; y < chart.Bottom; y += 5 * k) p.Line(x, y, x, Math.Min(chart.Bottom, y + 2 * k), HudPalette.Alpha(p.P.Danger, 90));
            }
            int bestIndex = 0;
            for (int i = 0; i < laps.Count; i++) if (laps[i].Clean && Math.Abs(laps[i].Time - best) < Math.Abs(laps[bestIndex].Time - best)) bestIndex = i;
            float bx = XOf(bestIndex), by = YOf(best);
            if (r.Has("best"))
            {
                p.Fill(new RectangleF(bx - 4 * k, by - 4 * k, 8 * k, 8 * k), p.P.Heading);
                Label(p, $"Best {RaceFormat.LapTime(best)}", bx - 8 * k, by + 12 * k, 130 * k, p.P.Heading, label, HAlign.Right);
            }
            Label(p, "Lap 1", chart.X, chart.Bottom + 10 * k, 60 * k, p.P.Faint, label);
            Label(p, laps.Count.ToString(CultureInfo.InvariantCulture), chart.Right, chart.Bottom + 10 * k, 60 * k, p.P.Faint, label, HAlign.Right);
        }

        float[] slots = { 12, 128, 210, 292 };
        int slot = 0;
        if (r.Has("consistency")) Stat(slots[slot++], "Consistency", card.Consistency is { } g ? g.ToString("0.0", CultureInfo.InvariantCulture) + "%" : RaceFormat.Missing, HAlign.Left);
        if (r.Has("laps"))
        {
            Stat(slots[slot++], "Laps", laps.Count.ToString(CultureInfo.InvariantCulture), HAlign.Left);
            Stat(slots[slot++], "Stops", card.PitStops.ToString(CultureInfo.InvariantCulture), HAlign.Left);
        }
        if (r.Has("incidents")) Stat(slots[slot], "Inc", card.Incidents is { } inc ? inc + "x" : RaceFormat.Missing, HAlign.Left);
        if (r.Has("time")) Stat(W - 12, "Time", RaceFormat.Clock(card.RaceTime), HAlign.Right);

        void Stat(float x, string name, string value, HAlign align)
        {
            Label(p, name, X(x), Y(230), 110 * k, p.P.Muted, label, align);
            Number(p, value, 26 * k, p.P.Heading, X(x), Y(252), 110 * k, align);
        }
    }
}
