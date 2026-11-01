using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Config;
using PitBox.Services;
using PitBox.SessionStats;
using PitBox.SimRacing.TrackMaps;

namespace PitBox.Views.Pages;

internal static class StatsReport
{
    private const int HotspotsShown = 6;

    public static FrameworkElement Card(ComboReport r, SessionStatsService service, Action changed)
    {
        bool imperial = service.Settings.Imperial;
        var names = r.Map is { } map ? TurnNames.From(map, null) : TurnNames.None;
        string place = r.Config.Length > 0 ? $"{r.Track} · {r.Config}" : r.Track;
        (StatDef, StatResult) M(string id, string label, StatKind kind, double? value) =>
            (new StatDef("combo." + id, StatRegistry.CarsTracks, label, kind), value is { } v ? new StatResult(StatFormat.Value(kind, v, imperial)) : StatResult.None);
        var metrics = StatsSections.Metrics(new[]
        {
            M("best", "Best lap", StatKind.LapTime, r.Best),
            M("sessions", "Sessions", StatKind.Count, r.Sessions),
            M("laps", "Laps", StatKind.Count, r.Laps),
            M("time", "Driving time", StatKind.Duration, r.DriveS),
            M("races", "Races", StatKind.Count, r.Races),
            M("inc", "Incidents per race", StatKind.Decimal, r.IncidentsPerRace),
            M("clean", "Clean races", StatKind.Count, r.Races > 0 ? r.CleanRaces : null),
            M("finish", "Average finish", StatKind.Position, r.AverageFinish),
        })!;
        string last = r.Last is { } when ? "Last driven " + Dates.Day(when.ToLocalTime()) : "";
        var summary = StatsSections.Chart("Report card", $"{r.Car} · {place}" + (last.Length > 0 ? " · " + last : ""), metrics);
        return StatsSections.Stack(summary, OffTracks(r, names), Notes(r, names, service, changed));
    }

    private static FrameworkElement OffTracks(ComboReport r, TurnNames names)
    {
        string caption = r.TracedLaps == 0 ? "Needs recorded laps (lap telemetry) for this car and track"
            : $"Where all four wheels left the track, from your {SimRacing.Race.RaceFormat.Count(r.TracedLaps, "most recent recorded lap")}";
        if (r.OffTracks.Count == 0)
            return StatsSections.Chart("Off-tracks", caption, StatsSections.Empty(r.TracedLaps == 0 ? "No recorded laps here yet." : "None on these laps."));
        var hotspots = SimRacing.Race.TrackLimits.Hotspots(r.OffTracks, p => names.Where(p)).Take(HotspotsShown).ToList();
        var list = new Grid { VerticalAlignment = VerticalAlignment.Top };
        list.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        list.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int i = 0; i < hotspots.Count; i++)
        {
            list.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var where = new TextBlock { Text = hotspots[i].Where, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 18, 6) };
            var count = new TextBlock { Text = SimRacing.Race.RaceFormat.Count(hotspots[i].Count, "time"), Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 0, 0, 6) };
            Grid.SetRow(where, i);
            Grid.SetRow(count, i);
            Grid.SetColumn(count, 1);
            list.Children.Add(where);
            list.Children.Add(count);
        }
        FrameworkElement body = list;
        if (r.Map is { } map)
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var drawn = new OffTrackMap(map, r.OffTracks) { Height = 200, ToolTip = "Each dot is one off-track; darker spots are where it happens most" };
            list.Margin = new Thickness(24, 4, 0, 0);
            Grid.SetColumn(list, 1);
            row.Children.Add(drawn);
            row.Children.Add(list);
            body = row;
        }
        return StatsSections.Chart("Off-tracks", caption, body);
    }

    private static FrameworkElement Notes(ComboReport r, TurnNames names, SessionStatsService service, Action changed)
    {
        var hud = service.Hud;
        var notes = hud.Notes.Where(n => string.Equals(n.Track, r.Track, StringComparison.OrdinalIgnoreCase) && string.Equals(n.Config, r.Config, StringComparison.OrdinalIgnoreCase)
            && (n.Car is null || string.Equals(n.Car, r.Car, StringComparison.OrdinalIgnoreCase))).OrderBy(n => n.Pct ?? -1).ToList();
        var stack = new StackPanel();
        if (notes.Count == 0) stack.Children.Add(StatsSections.Empty("No notes for this track yet."));
        foreach (var note in notes)
        {
            var n = note;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(97) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var where = new TextBlock { Text = n.Pct is { } p ? n.Turn ?? names.Where(p) : "Whole track", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var text = TextFit.TipWhenTrimmed(new TextBlock { Text = n.Text, Style = Ui.Res<Style>("Body"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            var car = new TextBlock { Text = n.Car is null ? "Any car" : "This car", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(car, 2);
            grid.Children.Add(where);
            grid.Children.Add(text);
            grid.Children.Add(car);
            var row = new Button { Style = Ui.Res<Style>("GhostButton"), Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, 0, 4), ToolTip = "Edit this note" };
            row.Click += (_, _) => _ = Edit(row, r, names, hud, n, changed);
            stack.Children.Add(row);
        }
        var add = Ui.LabeledButton("I.Plus", "Add note", () => { }, "SoftButton");
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 6, 0, 0);
        add.Click += (_, _) => _ = Edit(add, r, names, hud, null, changed);
        stack.Children.Add(add);
        return StatsSections.Chart("Notes", "Braking points, lines, setup thoughts: the Notes overlay widget shows them here, a corner's note as you get close to it", stack);
    }

    private static async Task Edit(FrameworkElement from, ComboReport r, TurnNames names, RaceHudSettings hud, TrackNote? note, Action changed)
    {
        if (Window.GetWindow(from) is not MainWindow window) return;
        var places = new List<Choice> { new("", "Whole track") };
        places.AddRange(names.All.Select(t => new Choice(t.Pct.ToString("R", CultureInfo.InvariantCulture), t.Name)));
        string? place = note?.Pct is { } p ? places.Skip(1).MinBy(c => Math.Abs(double.Parse(c.Id!, CultureInfo.InvariantCulture) - p))?.Id ?? "" : "";
        var wherePick = Ui.Combo(places, place, c => place = c.Id ?? "", 178);
        string carId = note?.Car is null && note is not null ? "any" : "this";
        var carPick = Ui.Combo(new[] { new Choice("this", r.Car), new Choice("any", "Any car") }, carId, c => carId = c.Id ?? "this", 178);
        var box = new TextBox { Text = note?.Text ?? "", MaxLength = RaceHudSettings.NoteLength, Width = 300, TextWrapping = TextWrapping.Wrap, MinHeight = 56, AcceptsReturn = false, HorizontalAlignment = HorizontalAlignment.Left };
        var content = new StackPanel();
        if (names.HasTurns) content.Children.Add(Ui.FieldRow("Where", wherePick));
        content.Children.Add(Ui.FieldRow("Car", carPick));
        content.Children.Add(Ui.FieldRow("Note", box));
        var error = new TextBlock();
        var buttons = Ui.DialogButtons(window, note is null ? "Add" : "Save", () =>
        {
            if (string.IsNullOrWhiteSpace(box.Text)) { error.Text = "Write the note first."; return false; }
            var target = note ?? new TrackNote();
            target.Track = r.Track;
            target.Config = r.Config;
            target.Car = carId == "any" ? null : r.Car;
            target.Pct = place.Length > 0 ? double.Parse(place, CultureInfo.InvariantCulture) : null;
            target.Turn = target.Pct is { } at ? names.Where(at) : null;
            target.Text = box.Text;
            target.Clean();
            if (note is null && hud.Notes.Count < RaceHudSettings.MaxNotes) hud.Notes.Add(target);
            return true;
        });
        Button? delete = null;
        if (note is not null)
        {
            delete = new Button { Content = "Delete", Style = Ui.Res<Style>("DangerButton") };
            delete.Click += (_, _) => { hud.Notes.Remove(note); window.CloseModal(true); };
        }
        string subtitle = r.Config.Length > 0 ? $"{r.Track} · {r.Config}" : r.Track;
        var shown = window.ShowModal(Ui.Dialog(window, note is null ? "Add note" : "Edit note", subtitle, content, buttons, error, 518, delete));
        box.Focus();
        if (await shown) changed();
    }
}

internal sealed class OffTrackMap : FrameworkElement
{
    private const double Pad = 10, Dot = 4.5;
    private readonly TrackMap _map;
    private readonly IReadOnlyList<double> _pcts;

    public OffTrackMap(TrackMap map, IReadOnlyList<double> pcts)
    {
        _map = map;
        _pcts = pcts;
        ClipToBounds = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var line = _map.Centerline;
        if (line.Length < 2 || ActualWidth < 2 * Pad || ActualHeight < 2 * Pad) return;
        float minX = line.Min(p => p.X), maxX = line.Max(p => p.X), minY = line.Min(p => p.Y), maxY = line.Max(p => p.Y);
        double scale = Math.Min((ActualWidth - 2 * Pad) / Math.Max(1, maxX - minX), (ActualHeight - 2 * Pad) / Math.Max(1, maxY - minY));
        double ox = (ActualWidth - (maxX - minX) * scale) / 2, oy = (ActualHeight - (maxY - minY) * scale) / 2;
        Point At(System.Drawing.PointF p) => new(ox + (p.X - minX) * scale, oy + (p.Y - minY) * scale);
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(At(line[0]), false, true);
            for (int i = 1; i < line.Length; i++) g.LineTo(At(line[i]), true, false);
        }
        geo.Freeze();
        var track = new Pen(Ui.Res<Brush>("LineStrong"), 5) { LineJoin = PenLineJoin.Round };
        track.Freeze();
        dc.DrawGeometry(null, track, geo);
        var dot = Ui.Res<Brush>("Danger").Clone();
        dot.Opacity = 0.45;
        dot.Freeze();
        foreach (double pct in _pcts) dc.DrawEllipse(dot, null, At(_map.At(pct)), Dot, Dot);
    }
}
