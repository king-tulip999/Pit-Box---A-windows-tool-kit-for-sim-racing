using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Core.Services;
using Core.SessionStats;
using Core.SimRacing.Race;
using Core.Theme;

namespace Core.Views.Pages;

internal static class StatsRacecraft
{
    private static readonly CultureInfo C = CultureInfo.CurrentCulture;

    private static string Gold => StatsUi.Hue("Gold").Deep;
    private static string Sage => StatsUi.Hue("Sage").Deep;
    private static string Coral => StatsUi.Hue("Coral").Deep;

    public static FrameworkElement Panel(Racecraft r, Func<string, (StatDef Def, StatResult Result)?> get, StatQuery q, FrameworkElement? more)
    {
        var columns = new StatGrid { MinTileWidth = 236, Gap = 32, RowGap = 19, Columns = 3 };
        columns.Children.Add(Column("Results", Results(r, get)));
        columns.Children.Add(Column("Positions and ratings", Positions(r, get)));
        columns.Children.Add(Column("Incidents, pit lane and weather", Incidents(r, get, q)));
        var stack = new StackPanel { Children = { columns } };
        if (r.Starts > 0)
        {
            var head = new DockPanel { Margin = new Thickness(0, 24, 0, 0) };
            if (r.Races.Count > Racecraft.StripRaces)
            {
                var note = new TextBlock { Text = "Last " + RaceFormat.Count(Racecraft.StripRaces, "race"), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Bottom };
                DockPanel.SetDock(note, Dock.Right);
                head.Children.Add(note);
            }
            head.Children.Add(Ui.SubHeader("Race by race", first: true));
            stack.Children.Add(head);
            stack.Children.Add(Rule());
            stack.Children.Add(new RaceStrip(r.Strip) { Height = RaceStrip.Tall, Margin = new Thickness(0, 6, 0, 0) });
        }
        if (more is not null)
        {
            var title = Ui.SubHeader("More", first: true);
            title.Margin = new Thickness(0, 24, 0, 2);
            stack.Children.Add(title);
            stack.Children.Add(Rule());
            more.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(more);
        }
        return stack;
    }

    private static IEnumerable<FrameworkElement> Results(Racecraft r, Func<string, (StatDef Def, StatResult Result)?> get)
    {
        yield return Row("Starts / finishes", r.Starts > 0 ? $"{Count(r.Starts)} / {Count(r.Finishes)}" : null);
        yield return Row("Wins", r.Starts > 0 ? Count(r.Wins) : null, tone: r.Wins > 0 ? Gold : null, tip: "Class position in multiclass races");
        yield return Row("Top fives", r.Starts > 0 ? Count(r.TopFives) : null, Share(r.TopFives, r.Starts), r.TopFives > 0 ? Gold : null);
        yield return Row("Top tens", r.Starts > 0 ? Count(r.TopTens) : null, Share(r.TopTens, r.Starts));
        yield return Row("Finished ahead of start", r.Compared > 0 ? $"{Count(r.AheadOfStart)} of {Count(r.Compared)}" : null);
        yield return Row("Laps led", r.LapsLed is { } led ? Count(led) : null, tip: "Laps you crossed the line in the lead of your class");
        if (get("race.league") is { Result: { NoData: false } league } found) yield return Row(found.Def.Label, league.Value);
    }

    private static IEnumerable<FrameworkElement> Positions(Racecraft r, Func<string, (StatDef Def, StatResult Result)?> get)
    {
        string? path = r.AverageStart is { } start
            ? $"{StatFormat.Value(StatKind.Position, start, false)} → {(r.AverageFinish is { } finish ? StatFormat.Value(StatKind.Position, finish, false) : RaceFormat.Missing)}" : null;
        yield return Row("Average start → finish", path);
        yield return Row("Positions gained", r.Gained is { } gained ? StatFormat.Value(StatKind.Signed, gained, false) : null,
            r.GainedPerRace is { } each ? $" · {RaceFormat.Signed(each, "0.0", C)} a race" : null, Tone(r.Gained ?? 0));
        yield return r.BiggestGain is { Finish: { } to } best
            ? Row("Biggest gain", StatFormat.Value(StatKind.Signed, best.Gain!.Value, false), $" · P{best.Start} → P{to}", Sage, tip: $"{best.Car} · {Place(best)}\n{Dates.DateTime(best.StartUtc.ToLocalTime())}")
            : Row("Biggest gain", null);
        yield return Row("Average field", r.AverageField is { } field ? RaceFormat.Count((long)Math.Round(field), "car") : null, tip: "Cars in your class");
        var ir = get("race.irating")?.Def.Help;
        string? category = r.Latest?.Category is { Length: > 0 } cat ? $"Latest {cat} rating" : null;
        yield return Rating("iRating", r.IrNow?.ToString("N0", C), r.IrChange is { } irDelta ? StatFormat.Value(StatKind.Signed, irDelta, false) : null, r.IrChange ?? 0,
            string.Join("\n", new[] { category, ir }.OfType<string>()));
        yield return Rating("Safety rating", r.SrNow?.ToString("0.00", C), r.SrChange is { } srDelta ? StatFormat.Value(StatKind.Rating, srDelta, false) : null, r.SrChange ?? 0, category);
    }

    private static IEnumerable<FrameworkElement> Incidents(Racecraft r, Func<string, (StatDef Def, StatResult Result)?> get, StatQuery q)
    {
        string? rate = r.IncidentsPer100Km is { } per ? $" · {(q.Imperial ? per / 0.621371 : per).ToString("0.0", C)} per 100 {(q.Imperial ? Units.Mi : Units.Km)}" : null;
        yield return Row("Incidents", r.Incidents is { } n ? Count(n) : null, rate, tip: "Every session in the range");
        yield return Row("Incidents a race", r.IncidentsPerRace?.ToString("0.0", C));
        yield return Row("Clean races", r.Finishes > 0 ? Count(r.CleanRaces) : null, Share(r.CleanRaces, r.Finishes), r.CleanRaces > 0 ? Sage : null, tip: "Finished with no incidents");
        yield return Row("Pit stops", Value(get("style.pits")));
        yield return Row("Fuel used", Value(get("style.fuel")), tip: get("style.fuel")?.Def.Help);
        string? rain = Value(get("cond.rain")), night = Value(get("cond.night"));
        yield return Row("Rain / night", rain is null && night is null ? null : $"{rain ?? RaceFormat.Missing} / {night ?? RaceFormat.Missing}");
    }

    private static string? Value((StatDef Def, StatResult Result)? stat) => stat is { Result: { NoData: false } result } ? result.Value : null;

    private static string Count(int n) => StatFormat.Value(StatKind.Count, n, false);

    private static string? Share(int part, int whole) => whole > 0 ? $" · {Racecraft.Share(part, whole)}%" : null;

    private static string? Tone(double v) => v > 0 ? Sage : v < 0 ? Coral : null;

    internal static string Place(RaceLine race) => StatFormat.TrackAndConfig(race.Track, race.Config);

    private static FrameworkElement Column(string title, IEnumerable<FrameworkElement> rows)
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.SubHeader(title, first: true));
        stack.Children.Add(Rule());
        var list = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var row in rows) list.Children.Add(row);
        stack.Children.Add(list);
        return stack;
    }

    private static Border Rule()
    {
        var rule = Hairline.Rule(new Border { Margin = new Thickness(0, 4, 0, 0) });
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        return rule;
    }

    private static FrameworkElement Rating(string label, string? now, string? change, double sign, string? tip)
    {
        if (now is null) return Row(label, change, tone: Tone(sign), tip: tip);
        return Row(label, now, change is null ? null : " " + change, extraTone: Tone(sign) ?? "TextMuted", tip: tip);
    }

    private static FrameworkElement Row(string label, string? value, string? extra = null, string? tone = null, string? extraTone = null, string? tip = null)
    {
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = label, Style = Ui.Res<Style>("Muted"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var figure = new TextBlock { Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var main = new Run(value ?? RaceFormat.Missing);
        main.SetResourceReference(TextElement.ForegroundProperty, value is null ? "TextFaint" : tone ?? "Heading");
        figure.Inlines.Add(main);
        if (value is not null && extra is not null)
        {
            var rest = new Run(extra) { FontWeight = extraTone is null ? FontWeights.Normal : FontWeights.SemiBold };
            rest.SetResourceReference(TextElement.ForegroundProperty, extraTone ?? "TextMuted");
            figure.Inlines.Add(rest);
        }
        var line = new DockPanel();
        DockPanel.SetDock(figure, Dock.Right);
        line.Children.Add(figure);
        line.Children.Add(name);
        return new Border { Padding = new Thickness(0, 4, 0, 4), Child = line, ToolTip = string.IsNullOrEmpty(tip) ? null : tip, Background = Brushes.Transparent };
    }

    private sealed class RaceStrip : StatsCharts.HoverChart
    {
        public const double Tall = 272;
        private new const double Left = 112, Right = 8;
        private const double SlotMax = 72, DatesY = 20, PositionTop = 36, PositionBottom = 148, RatingBottom = 226, IncidentsBottom = 272;
        private readonly IReadOnlyList<RaceLine> _races;

        public RaceStrip(IReadOnlyList<RaceLine> races) => _races = races;

        private double Slot => Math.Min(SlotMax, (ActualWidth - Left - Right) / Math.Max(1, _races.Count));

        private double X(int i) => Left + Slot * (i + 0.5);

        protected override int HitAt(Point p)
        {
            if (p.X < Left || p.Y < DatesY - 4 || _races.Count == 0) return -1;
            int i = (int)((p.X - Left) / Slot);
            return i < _races.Count ? i : -1;
        }

        protected override string TipOf(int i)
        {
            var r = _races[i];
            string result = r.Finished && r.Finish is { } f ? $"P{r.Start} → P{f}" + (r.Gain is { } g && g != 0 ? $" · {StatFormat.Value(StatKind.Signed, g, false)}" : "") : $"Started P{r.Start} · did not finish";
            var facts = new List<string>();
            if (r.IrDelta is { } ir) facts.Add("iRating " + StatFormat.Value(StatKind.Signed, ir, false));
            if (r.SrDelta is { } sr) facts.Add("safety rating " + StatFormat.Value(StatKind.Rating, sr, false));
            if (r.Incidents is { } inc) facts.Add($"{inc}x");
            if (r.LapsLed is > 0 and var led) facts.Add($"{RaceFormat.Count(led, "lap")} led");
            return $"{Dates.DateTime(r.StartUtc.ToLocalTime())}\n{r.Car} · {StatsRacecraft.Place(r)}\n{result}" + (facts.Count > 0 ? "\n" + string.Join(" · ", facts) : "");
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_races.Count == 0 || w < Left + Right + 40) return;
            double slot = Slot, px = Hairline.ScaleOf(this);
            var stripe = Ui.Res<Brush>("SurfaceAlt");
            var hover = Ui.Res<Brush>("Hover");
            for (int i = 0; i < _races.Count; i++)
                if (i == Hover || i % 2 == 0)
                    dc.DrawRectangle(i == Hover ? hover : stripe, null, new Rect(Left + slot * i, DatesY - 4, slot, IncidentsBottom - DatesY + 4));

            Legend(dc, w);
            var dateSample = Text("28 Dec");
            int every = Math.Max(1, (int)Math.Ceiling((dateSample.Width + 8) / slot));
            for (int i = 0; i < _races.Count; i += every)
            {
                var label = Text(Dates.Short(_races[i].StartUtc.ToLocalTime()));
                dc.DrawText(label, new Point(X(i) - label.Width / 2, DatesY));
            }

            var line = Hairline.Pen(Ui.Res<Brush>("LineStrong"), px);
            var grid = Hairline.Pen(Ui.Res<Brush>("GridLine"), px);
            Lane(dc, line, w, PositionTop - 4, PositionBottom, "Position", "start → finish");
            Lane(dc, line, w, PositionBottom, RatingBottom, "iRating", "change");
            Lane(dc, line, w, RatingBottom, IncidentsBottom, "Incidents", "per race");

            int deepest = Math.Max(10, _races.Max(r => Math.Max(r.Start, r.Finish ?? 0)));
            int bottom = (int)Math.Ceiling(deepest / 5.0) * 5;
            double Y(int pos) => PositionTop + 6 + (PositionBottom - PositionTop - 16) * (pos - 1) / Math.Max(1.0, bottom - 1);
            foreach (int mark in new[] { 1, bottom / 2, bottom })
            {
                double y = Hairline.Snap(Y(mark), px);
                dc.DrawLine(grid, new Point(Left, y), new Point(w - Right, y));
                var label = Text("P" + mark);
                dc.DrawText(label, new Point(Left - 6 - label.Width, y - label.Height / 2));
            }

            var gold = Ui.Res<Brush>(Gold);
            var sage = Ui.Res<Brush>(Sage);
            var coral = Ui.Res<Brush>(Coral);
            var muted = Ui.Res<Brush>("TextMuted");
            var faint = Ui.Res<Brush>("TextFaint");
            var surface = Ui.Res<Brush>("Surface");
            var startRing = new Pen(muted, 1.5);
            double zero = (PositionBottom + RatingBottom) / 2 + 2;
            dc.DrawLine(line, new Point(Left, Hairline.Snap(zero, px)), new Point(w - Right, Hairline.Snap(zero, px)));
            var zeroLabel = Text("0");
            dc.DrawText(zeroLabel, new Point(Left - 6 - zeroLabel.Width, zero - zeroLabel.Height / 2));
            double swing = Math.Max(1, _races.Max(r => Math.Abs(r.IrDelta ?? 0)));
            double reach = (RatingBottom - PositionBottom) / 2 - 16;
            double bar = Math.Clamp(slot * 0.4, 4, 18);

            for (int i = 0; i < _races.Count; i++)
            {
                var r = _races[i];
                double x = X(i), from = Y(r.Start);
                if (r.Finished && r.Finish is { } finish)
                {
                    double to = Y(finish);
                    var colour = finish < r.Start ? sage : finish > r.Start ? coral : muted;
                    dc.DrawLine(new Pen(colour, 3), new Point(x, from), new Point(x, to));
                    dc.DrawEllipse(finish <= 3 ? gold : colour, null, new Point(x, to), 5, 5);
                    var place = Text("P" + finish, Ui.Res<Brush>("Text"));
                    dc.DrawText(place, new Point(x + 8, to - place.Height / 2));
                }
                else
                {
                    var dnf = Text("DNF", faint);
                    dc.DrawText(dnf, new Point(x - dnf.Width / 2, from + 7));
                }
                dc.DrawEllipse(surface, startRing, new Point(x, from), 4, 4);

                if (r.IrDelta is { } ir)
                {
                    double height = Math.Max(ir == 0 ? 0 : 2, reach * Math.Abs(ir) / swing);
                    var fill = ir >= 0 ? sage : coral;
                    if (height > 0) dc.DrawRectangle(fill, null, new Rect(x - bar / 2, ir >= 0 ? zero - height : zero, bar, height));
                    var value = Text(StatFormat.Value(StatKind.Signed, ir, false), Ui.Res<Brush>("Text"));
                    dc.DrawText(value, new Point(x - value.Width / 2, ir >= 0 ? zero - height - value.Height - 1 : zero + height + 1));
                }
                else
                {
                    var none = Text(RaceFormat.Missing, faint);
                    dc.DrawText(none, new Point(x - none.Width / 2, zero - none.Height - 2));
                }

                double incidentsY = RatingBottom + 8;
                if (r.Incidents is { } inc)
                {
                    var count = Text($"{inc}x", inc == 0 ? sage : Ui.Res<Brush>("Heading"), bold: true, size: 11.5);
                    dc.DrawText(count, new Point(x - count.Width / 2, incidentsY));
                    if (inc == 0 && r.Finished)
                    {
                        var clean = Text("clean", sage);
                        dc.DrawText(clean, new Point(x - clean.Width / 2, incidentsY + count.Height));
                    }
                }
                else
                {
                    var none = Text(RaceFormat.Missing, faint);
                    dc.DrawText(none, new Point(x - none.Width / 2, incidentsY));
                }
            }
        }

        private void Lane(DrawingContext dc, Pen line, double w, double top, double bottom, string title, string caption)
        {
            double y = Hairline.Snap(bottom, Hairline.ScaleOf(this));
            if (bottom < Tall) dc.DrawLine(line, new Point(0, y), new Point(w, y));
            var name = Text(title, Ui.Res<Brush>("Heading"), bold: true, size: 11.5);
            var sub = Text(caption);
            double mid = (top + bottom) / 2;
            dc.DrawText(name, new Point(0, mid - name.Height));
            dc.DrawText(sub, new Point(0, mid));
        }

        private void Legend(DrawingContext dc, double w)
        {
            var items = new (string Label, string? Fill)[] { ("Start", null), ("Gained", Sage), ("Lost", Coral), ("Podium", Gold) };
            double x = w - Right;
            for (int i = items.Length - 1; i >= 0; i--)
            {
                var label = Text(items[i].Label);
                x -= label.Width;
                dc.DrawText(label, new Point(x, 0));
                x -= 12;
                var centre = new Point(x + 4, label.Height / 2);
                if (items[i].Fill is { } fill) dc.DrawRectangle(Ui.Res<Brush>(fill), null, new Rect(x, centre.Y - 4, 8, 8));
                else dc.DrawEllipse(Ui.Res<Brush>("Surface"), new Pen(Ui.Res<Brush>("TextMuted"), 1.5), centre, 3.5, 3.5);
                x -= 14;
            }
        }
    }
}
