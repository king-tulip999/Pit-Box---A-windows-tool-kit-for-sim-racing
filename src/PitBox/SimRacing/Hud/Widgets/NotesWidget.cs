using System.Drawing;
using PitBox.Config;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Hud.Widgets;

internal sealed class NotesWidget : HudWidget
{
    public static IReadOnlyList<HudOption> Options { get; } = new[]
    {
        HudOption.Int("ahead", "Corner notes from", 300, 50, 2000, Services.CapsUnits.M, "How far before the corner its note lights up"),
        HudOption.Bool("general", "Notes for the whole track", true),
        HudOption.Bool("onlyNext", "Only the next corner's note", false),
    };

    private static readonly List<TrackNote> Samples = new()
    {
        new TrackNote { Pct = 0.08, Turn = "T1", Text = "Brake at the 100 board, trail into the apex" },
        new TrackNote { Pct = 0.42, Turn = "T5", Text = "Stay off the exit kerb: it counts as off" },
        new TrackNote { Text = "Bias 52.5 once the fuel is down" },
    };

    private readonly List<(TrackNote Note, double Metres)> _corners = new();
    private readonly List<TrackNote> _general = new();

    private void Collect(HudView v)
    {
        _corners.Clear();
        _general.Clear();
        var f = v.Frame;
        var me = f.Session.Player;
        double length = (f.Session.TrackLengthKm ?? 0) * 1000;
        double at = f.LapDistPct is { } p && double.IsFinite(p) ? p : 0;
        var notes = v.Settings.NotesFor(f.Session.TrackDisplayName, f.Session.TrackConfig, me is null ? "" : me.CarName.Length > 0 ? me.CarName : me.CarShortName).ToList();
        if (notes.Count == 0 && f.Simulated) notes = Samples;
        foreach (var n in notes)
        {
            if (n.Pct is { } pct) _corners.Add((n, ((pct - at) % 1 + 1) % 1 * length));
            else if (v.Options.Bool("general")) _general.Add(n);
        }
        _corners.Sort(static (a, b) => a.Metres.CompareTo(b.Metres));
    }

    public override bool HasContent(HudView v)
    {
        if (v.Frame.Status != SimStatus.Connected) return false;
        Collect(v);
        return _corners.Count > 0 || _general.Count > 0;
    }

    public override void Draw(HudPainter p, HudView v)
    {
        if (DrawWaiting(p, v, "Notes")) return;
        Collect(v);
        int ahead = v.Options.Int("ahead");
        bool soon = _corners.Count > 0 && _corners[0].Metres <= ahead;
        var area = p.Header("Notes", soon ? _corners[0].Note.Turn ?? v.Race.Turns.Where(_corners[0].Note.Pct!.Value, caps: true) : null, soon ? p.P.AccentText : null);
        if (_corners.Count == 0 && _general.Count == 0)
        {
            p.Text("No notes for this track yet", p.Small, p.P.Faint, area, HAlign.Center, VAlign.Center);
            return;
        }
        float rowH = p.LineHeight(p.Body) * 1.35f * p.Density, gap = 3 * p.Density, y = area.Y;
        float tagW = p.Measure("T00", p.SmallBold) + 12 * p.Density;
        int shown = v.Options.Bool("onlyNext") ? Math.Min(1, _corners.Count) : _corners.Count;
        for (int i = 0; i < shown && y + rowH <= area.Bottom + 1; i++)
        {
            var (note, metres) = _corners[i];
            bool lit = i == 0 && metres <= ahead;
            var row = new RectangleF(area.X, y, area.Width, rowH);
            if (lit) p.Soft(row, p.P.Accent, 90, false);
            var tag = row.Left(tagW, out var rest);
            p.Text(note.Turn ?? v.Race.Turns.Where(note.Pct!.Value, caps: true), p.SmallBold, lit ? p.P.Heading : p.P.Muted, tag.Inflated(-4 * p.Density, 0), HAlign.Left);
            p.Text(note.Text, lit ? p.Bold : p.Body, lit ? p.P.Heading : p.P.Text, rest.Inflated(-4 * p.Density, 0), HAlign.Left);
            y += rowH + gap;
        }
        foreach (var note in _general)
        {
            if (y + rowH > area.Bottom + 1) break;
            if (y > area.Y) p.AccentRule(area.X + 2, area.Right - 2, MathF.Round(y - gap / 2) + 0.5f);
            p.Text(note.Text, p.Small, p.P.Text, new RectangleF(area.X + 4 * p.Density, y, area.Width - 8 * p.Density, rowH), HAlign.Left);
            y += rowH + gap;
        }
    }
}
