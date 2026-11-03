using System.Drawing;
using System.Globalization;
using Units = PitBox.Services.CapsUnits;
using PitBox.SimRacing.Race;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Hud.Widgets;

internal sealed class GoalsWidget : HudWidget
{
    public const string Ledger = "ledger", Counters = "counters";
    private const float LedgerW = 300, LedgerH = 240, CounterW = 336, CounterH = 106;

    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        TitleRow,
        HudOption.Choice("look", "Look", Ledger,
            new HudChoice(Ledger, "Lap list", "The goals as tiles over your last laps, each lap marked with the goals it counted toward"),
            new HudChoice(Counters, "Counters", "One big number per goal")),
        HudOption.Set("goals", "Goals", "clean,consistent,near", null,
            new HudChoice("clean", "Clean laps in a row"), new HudChoice("consistent", "Consistent laps in a row", "Every lap in the run within the spread of each other"),
            new HudChoice("near", "Laps close to your best", "Clean laps this session within the margin of your best"),
            new HudChoice("under", "Laps under a time", "Clean laps this session at or under the lap time you set"),
            new HudChoice("total", "Clean laps"), new HudChoice("minutes", "Clean minutes", "Time on track in clean laps")),
        HudOption.Int("cleanLaps", "Clean laps", 5, 1, 50),
        HudOption.Int("consistentLaps", "Consistent laps", 5, 2, 50),
        HudOption.Number("consistentWithin", "Spread", 0.5, 0.1, 5, 0.1, Units.S),
        HudOption.Int("nearLaps", "Laps close to the best", 10, 1, 100),
        HudOption.Number("nearWithin", "Margin", 0.5, 0.1, 5, 0.1, Units.S),
        HudOption.Int("underLaps", "Laps under the time", 5, 1, 100),
        HudOption.Number("underTime", "Lap time", 95, 20, 900, 0.1, Units.S, "In seconds: 95 is 1:35.0"),
        HudOption.Int("totalLaps", "Clean laps in total", 30, 1, 500),
        HudOption.Int("minutes", "Clean minutes", 45, 5, 600),
        HudOption.Bool("races", "Show in races", false),
    };

    public static SizeF Natural(HudOptions o) => o.Choice("look") == Counters
        ? new SizeF(CounterW, CounterH * Rows(Math.Max(1, o.Columns("goals").Count)))
        : new SizeF(LedgerW, LedgerH);

    internal static int PerRow(int count) => count <= 3 ? Math.Max(1, count) : (count + 1) / 2;

    internal static int Rows(int count) => count <= 3 ? 1 : 2;

    private readonly record struct Goal(string Id, string Title, string Mark, GoalProgress Progress);

    private readonly List<Goal> _goals = new(6);
    private double _within, _under;

    public override bool HasContent(HudView v) =>
        v.Frame.Status == SimStatus.Connected && (v.Frame.SessionKind != SessionKind.Race || v.Options.Bool("races")) && v.Options.Columns("goals").Count > 0;

    private void Collect(HudOptions o, IReadOnlyList<LapRecord> laps)
    {
        _goals.Clear();
        _within = o.Number("consistentWithin");
        _under = o.Number("underTime");
        foreach (var id in o.Columns("goals"))
            switch (id)
            {
                case "clean":
                    _goals.Add(new(id, "Clean run", "C", PracticeGoals.CleanStreak(laps, o.Int("cleanLaps"))));
                    break;
                case "consistent":
                    _goals.Add(new(id, $"Within {_within.ToString("0.0#", CultureInfo.InvariantCulture)} {Units.S}", "S", PracticeGoals.Consistent(laps, o.Int("consistentLaps"), _within)));
                    break;
                case "near":
                    _goals.Add(new(id, "Near best", "N", PracticeGoals.NearBest(laps, o.Int("nearLaps"), o.Number("nearWithin"))));
                    break;
                case "under":
                    _goals.Add(new(id, "Under " + RaceFormat.LapTime(_under, 1), "U", PracticeGoals.UnderTime(laps, o.Int("underLaps"), _under)));
                    break;
                case "total":
                    _goals.Add(new(id, "Clean laps", "T", PracticeGoals.CleanLaps(laps, o.Int("totalLaps"))));
                    break;
                case "minutes":
                    _goals.Add(new(id, "Clean min", "M", PracticeGoals.CleanMinutes(laps, o.Int("minutes"))));
                    break;
            }
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Practice goals")) return;
        var laps = v.Race.Laps.Laps;
        Collect(v.Options, laps);
        if (v.Options.Choice("look") == Counters) DrawCounters(p);
        else DrawLedger(p, v, laps);
    }

    private int DoneCount()
    {
        int done = 0;
        foreach (var g in _goals) if (g.Progress.Done) done++;
        return done;
    }

    private static string Shown(GoalProgress g) => Math.Min(g.Current, g.Target).ToString(CultureInfo.InvariantCulture);

    private static void Count(HudPainter p, GoalProgress g, float x, float baseline, float px, Color color, Font slash, string separator)
    {
        string number = Shown(g);
        var size = p.DisplaySize(number, px, false);
        p.Display(number, px, color, new RectangleF(x, baseline - size.Height, size.Width + 2, size.Height), HAlign.Left, VAlign.Top, false);
        float sx = x + size.Width + 3 * p.Density;
        p.Text(separator + g.Target.ToString(CultureInfo.InvariantCulture), slash, p.P.Muted,
            new RectangleF(sx, baseline - p.Ascent(slash), p.Measure(separator + g.Target, slash) + 4, p.LineHeight(slash)), HAlign.Left, VAlign.Top, false);
    }

    private void DrawLedger(HudPainter p, HudView v, IReadOnlyList<LapRecord> laps)
    {
        int done = DoneCount(), n = _goals.Count;
        var area = p.Header("Practice goals", n == 0 ? null : $"{done} / {n} done", done == n && n > 0 ? p.P.GoodText : null);
        if (n == 0) return;
        float d = p.Density, gap = 4 * d, chipH = 42 * d;
        int perRow = PerRow(n), rows = (n + perRow - 1) / perRow;
        float chipW = (area.Width - gap * (perRow - 1)) / perRow;
        var label = p.Label;
        var slash = p.Font(12, false);
        for (int i = 0; i < n; i++)
        {
            var g = _goals[i];
            var chip = new RectangleF(area.X + (i % perRow) * (chipW + gap), area.Y + (i / perRow) * (chipH + gap), chipW, chipH);
            p.Item("goals", g.Id, chip);
            p.Fill(chip, g.Progress.Done ? HudPalette.Alpha(p.P.Good, 46) : p.Solid(p.Raised(0.06)));
            var inner = chip.Inflated(-8 * d, 0);
            var color = g.Progress.Done ? p.P.GoodText : p.P.Muted;
            p.Text(p.LabelText(g.Title), label, color, new RectangleF(inner.X, chip.Y + 5 * d, inner.Width, p.LineHeight(label)), HAlign.Left, VAlign.Top);
            Count(p, g.Progress, inner.X, chip.Bottom - 7 * d, 20 * d, g.Progress.Done ? p.P.GoodText : p.P.Heading, slash, "/ ");
        }
        float y = area.Y + rows * (chipH + gap) + 4 * d;
        float rowH = 20 * d, markSize = 9 * d, markStep = 17 * d;
        float lapX = area.X, timeX = area.X + 30 * d, deltaX = area.X + 102 * d, offX = area.X + 158 * d;
        float MarkX(int j) => area.Right - (n - 1 - j) * markStep - markSize;
        float labelH = p.LineHeight(label);
        if (y + labelH + rowH > area.Bottom + 1) return;
        p.Text(p.LabelText("Lap"), label, p.P.Faint, new RectangleF(lapX, y, 28 * d, labelH), HAlign.Left, VAlign.Top, false);
        p.Text(p.LabelText("Time"), label, p.P.Faint, new RectangleF(timeX, y, 60 * d, labelH), HAlign.Left, VAlign.Top, false);
        p.Text(p.LabelText("Best"), label, p.P.Faint, new RectangleF(deltaX, y, 50 * d, labelH), HAlign.Left, VAlign.Top, false);
        for (int j = 0; j < n; j++)
            p.Text(_goals[j].Mark, label, p.P.Faint, new RectangleF(MarkX(j) - 4 * d, y, markSize + 8 * d, labelH), HAlign.Center, VAlign.Top, false);
        y += labelH + 2 * d;
        int shown = Math.Max(0, (int)((area.Bottom - y + 1) / rowH));
        double? best = null;
        foreach (var l in laps) if (l.Clean && !l.PitLap && (best is null || l.Time < best)) best = l.Time;
        var body = p.Small;
        var bold = p.SmallBold;
        for (int r = 0; r < shown && r < laps.Count; r++)
        {
            int index = laps.Count - 1 - r;
            var lap = laps[index];
            var row = new RectangleF(area.X - 4 * d, y + r * rowH, area.Width + 8 * d, rowH);
            if (r == 0) p.Fill(row, p.Solid(p.Raised(0.05)));
            bool counts = lap.Clean && !lap.PitLap;
            p.Text(lap.Lap.ToString(CultureInfo.InvariantCulture), body, p.P.Muted, new RectangleF(lapX, row.Y, 28 * d, rowH), HAlign.Left, VAlign.Center, false);
            p.Text(RaceFormat.LapTime(lap.Time), bold, counts ? p.P.Text : p.P.DangerText, new RectangleF(timeX, row.Y, 72 * d, rowH), HAlign.Left, VAlign.Center, false);
            string delta = best is { } b ? RaceFormat.Signed(lap.Time - b, "0.00") : RaceFormat.Missing;
            p.Text(delta, body, best is { } bb && lap.Time == bb ? p.P.GoodText : p.P.Muted, new RectangleF(deltaX, row.Y, 56 * d, rowH), HAlign.Left, VAlign.Center, false);
            if (!counts) p.Text(p.LabelText(lap.PitLap ? "Pit" : "Off"), label, p.P.DangerText, new RectangleF(offX, row.Y, 34 * d, rowH), HAlign.Left, VAlign.Center, false);
            for (int j = 0; j < n; j++)
            {
                var g = _goals[j];
                bool on = PracticeGoals.Counted(g.Progress.Kind, laps, index, g.Progress, _within, _under);
                p.Fill(new RectangleF(MarkX(j), row.Y + (rowH - markSize) / 2, markSize, markSize), on ? p.P.Good : HudPalette.Alpha(p.P.Muted, 38));
            }
        }
    }

    private void DrawCounters(HudPainter p)
    {
        p.Panel();
        int n = _goals.Count;
        if (n == 0) return;
        var b = p.Bounds;
        int perRow = PerRow(n), rows = Rows(n);
        float tileW = (b.Width - 8) / perRow, tileH = b.Height / rows;
        float k = Math.Clamp(Math.Min(tileH / CounterH, tileW / 100), 0.5f, 3f);
        var label = p.Font(10.5f * k, true);
        var slash = p.Font(16f * k, true);
        for (int i = 0; i < n; i++)
        {
            var g = _goals[i];
            int col = i % perRow, row = i / perRow;
            float x = 4 + col * tileW, top = row * tileH;
            var tile = new RectangleF(x, top, tileW, tileH);
            p.Item("goals", g.Id, tile);
            if (col > 0) p.Line(x, top + 12 * k, x, top + tileH - 12 * k, p.P.Line);
            if (row > 0 && col == 0) p.Line(16 * k, top, b.Width - 16 * k, top, p.P.Line);
            float inX = x + 12 * k, inW = tileW - 24 * k;
            var done = g.Progress.Done;
            float labelH = p.LineHeight(label);
            p.Text(p.LabelText(g.Title), label, done ? p.P.GoodText : p.P.Muted, new RectangleF(inX, top + 18 * k - labelH / 2, inW, labelH), HAlign.Left, VAlign.Top);
            Count(p, g.Progress, inX, top + 66 * k, 40 * k, done ? p.P.GoodText : p.P.Heading, slash, "/");
            p.Fill(new RectangleF(inX, top + 74 * k, inW, 3 * k), HudPalette.Alpha(p.P.Muted, 38));
            float share = (float)Math.Min(1, (double)(done ? g.Progress.Target : g.Progress.Current) / g.Progress.Target);
            if (share > 0) p.Fill(new RectangleF(inX, top + 74 * k, inW * share, 3 * k), done ? p.P.Good : p.P.Accent);
            string under = done ? "Done" : g.Progress.InARow && g.Progress.Best > g.Progress.Current ? $"Best {g.Progress.Best}" : $"{g.Progress.ToGo} to go";
            p.Text(p.LabelText(under), label, done ? p.P.GoodText : p.P.Faint, new RectangleF(inX, top + tileH - 14 * k - labelH / 2, inW, labelH), HAlign.Left, VAlign.Top);
        }
    }
}
