using PitBox.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.SessionStats;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed record SessionDetail(SessionSummary Session, List<LapRow> Laps, HashSet<int> Traced, double? AllTimeBest, SessionConditions? Conditions = null,
    double? Tank = null, IReadOnlyList<TyreReading>? Tyres = null, RaceCardData? Race = null, int? TrackId = null, string TrackName = "");

internal static class StatsSessionView
{
    public const int MaxLaps = 4;

    public static FrameworkElement Build(SessionDetail d, Config.SessionStatsSettings settings, string noTraceReason, HashSet<int> ticked, Action<IReadOnlyList<int>> analyze)
    {
        bool imperial = settings.Imperial;
        var s = d.Session;
        var page = new StackPanel();

        var laps = d.Laps;
        var cleanLaps = laps.Where(l => l.Clean).ToList();
        int sectors = laps.Max(l => (int?)(l.Sectors?.Count ?? 0)) ?? 0;
        var bestSector = Enumerable.Range(0, sectors).Select(k => laps.Where(l => l.Clean && l.Sectors?.Count == sectors).Select(l => (double?)l.Sectors![k]).Min()).ToArray();
        page.Children.Add(Banner(d, cleanLaps, sectors, bestSector, imperial));

        if (laps.Count == 0)
        {
            page.Children.Add(Card(StatsSections.Empty("No complete laps in this session.")));
            return page;
        }

        var chartCard = Card(StatsSections.Chart("Lap times", "Every lap in order; hollow = not clean (an incident, the pit lane or off track), arrow = slower than the scale shows", new LapTimeChart(laps) { Height = 178 }));

        double? sessionBest = cleanLaps.Select(l => (double?)l.Time).Min();
        var bestSoFar = new double?[sectors];
        if (ticked.Count == 0 && laps.Where(l => d.Traced.Contains(l.Lap)).OrderByDescending(l => l.Clean).ThenBy(l => l.Time).FirstOrDefault() is { } first) ticked.Add(first.Lap);

        var analyzeButton = Ui.LabeledButton("I.Activity", "Analyze", () => analyze(ticked.OrderBy(x => x).ToList()), "PrimaryButton");
        void Sync() => analyzeButton.IsEnabled = ticked.Count is > 0 and <= MaxLaps;
        Sync();
        analyzeButton.ToolTip = d.Traced.Count == 0 ? noTraceReason : $"Compare up to {SimRacing.Race.RaceFormat.Count(MaxLaps, "ticked lap")}: line, inputs and time gained or lost";

        var cols = new List<(string Head, bool Right)> { ("", false), ("Lap", true), ("Time", true), ("Δ best", true) };
        for (int k = 0; k < sectors; k++) cols.Add(($"S{k + 1}", true));
        cols.Add(("Fuel", true));
        cols.Add(("Top speed", true));
        cols.Add(("", false));
        bool hasTyres = d.Tyres is { Count: > 0 };
        Grid NewGrid()
        {
            var g = new Grid();
            for (int c = 0; c < cols.Count; c++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "LapCol" + c });
            if (!hasTyres) g.ColumnDefinitions[^1] = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
            return g;
        }
        FrameworkElement Section(Grid left, FrameworkElement? right)
        {
            if (!hasTyres) return left;
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 211 });
            left.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(left);
            if (right is not null)
            {
                right.Margin = new Thickness(Ui.PanelSpacing, 8, 0, 8);
                right.MaxWidth = 502;
                right.HorizontalAlignment = HorizontalAlignment.Left;
                Grid.SetColumn(right, 1);
                g.Children.Add(right);
            }
            return g;
        }
        var lapsTable = new StackPanel();
        Grid.SetIsSharedSizeScope(lapsTable, true);
        var headRow = NewGrid();
        headRow.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int c = 0; c < cols.Count; c++) Cell(headRow, 0, c, new TextBlock { Text = cols[c].Head, Style = Ui.Res<Style>("Eyebrow") }, cols[c].Right);
        lapsTable.Children.Add(Section(headRow, null));
        var (pbSoft, pbDeep) = StatsUi.Hue("Gold");
        var (sageSoft, sageDeep) = StatsUi.Hue("Sage");
        var (coralSoft, coralDeep) = StatsUi.Hue("Coral");
        bool runs = laps.Select(l => l.Run ?? 1).Distinct().Count() > 1 || laps.Any(l => l.Setup is not null);
        var groups = new List<List<LapRow>>();
        foreach (var lap in laps)
        {
            if (groups.Count == 0 || (runs && (lap.Run ?? 1) != (groups[^1][0].Run ?? 1))) groups.Add(new List<LapRow>());
            groups[^1].Add(lap);
        }
        var tyresOf = new Dictionary<int, List<TyreReading>>();
        foreach (var reading in d.Tyres ?? Array.Empty<TyreReading>())
        {
            int gi = runs ? groups.FindLastIndex(g => (g[0].Run ?? 1) == reading.Run) : 0;
            if (gi < 0) gi = groups.Count - 1;
            if (!tyresOf.TryGetValue(gi, out var list)) tyresOf[gi] = list = new List<TyreReading>();
            list.Add(reading);
        }
        for (int gi = 0; gi < groups.Count; gi++)
        {
            var inRun = groups[gi];
            if (runs)
            {
                int run = inRun[0].Run ?? 1;
                var parts = new List<string> { $"Run {run}", $"{SimRacing.Race.RaceFormat.Count(inRun.Count, "lap")}" };
                if (inRun.Where(l => l.Clean).ToList() is { Count: > 0 } clean) parts.Add($"average clean {StatFormat.LapTime(clean.Average(l => l.Time))}");
                var header = new WrapPanel { Margin = new Thickness(8, 6, 8, 6) };
                header.Children.Add(new TextBlock { Text = string.Join(" · ", parts), Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center });
                var setup = inRun.Select(l => l.Setup).LastOrDefault(x => x is not null);
                if (setup is not null)
                {
                    var file = new TextBlock { Text = setup, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) };
                    TextCase.Verbatim(file);
                    var setupRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(15, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "The setup loaded" };
                    var glyph = new Icon { Data = Ui.Res<Geometry>("I.Sliders"), Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center };
                    glyph.SetResourceReference(Control.ForegroundProperty, "TextMuted");
                    setupRow.Children.Add(glyph);
                    setupRow.Children.Add(file);
                    header.Children.Add(setupRow);
                }
                var runBand = new Tile { Child = header, CornerRadius = ThemeManager.Round(4) };
                runBand.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
                runBand.Margin = new Thickness(0, gi == 0 ? 5 : 11, 0, 2);
                lapsTable.Children.Add(runBand);
            }
            var table = NewGrid();
            int row = -1;
            foreach (var lap in inRun)
            {
                row++;
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool isBest = lap.Clean && lap.Time == sessionBest;
                bool isPb = lap.Clean && d.AllTimeBest is { } pb && Math.Abs(lap.Time - pb) < 0.0005;
                var band = new Border();
                if (isPb) band.SetResourceReference(Border.BackgroundProperty, pbSoft);
                else if (isBest) band.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
                var rule = Hairline.Frame(new Border { Child = band }, Sides.Top);
                rule.SetResourceReference(Border.BorderBrushProperty, "Line");
                Grid.SetRow(rule, row);
                Grid.SetColumnSpan(rule, cols.Count);
                table.Children.Add(rule);

                int c = 0;
                bool traced = d.Traced.Contains(lap.Lap);
                var tick = new CheckBox { IsChecked = ticked.Contains(lap.Lap), IsEnabled = traced, VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Automation.AutomationProperties.SetName(tick, $"Lap {lap.Lap}");
                var tickHost = new Border { Child = tick, Background = Brushes.Transparent, ToolTip = traced ? "Tick to analyze (up to 4)" : "No trace for this lap (not in a telemetry file, or not kept)" };
                int number = lap.Lap;
                tick.Click += (_, _) =>
                {
                    if (tick.IsChecked == true)
                    {
                        if (ticked.Count >= MaxLaps) { tick.IsChecked = false; return; }
                        ticked.Add(number);
                    }
                    else ticked.Remove(number);
                    Sync();
                };
                Cell(table, row, c++, tickHost);
                Cell(table, row, c++, Caption(lap.Lap.ToString(CultureInfo.CurrentCulture)), true);
                var time = StatsSessions.Time(lap.Time, isPb ? pbDeep : isBest ? "AccentText" : lap.Clean ? "Heading" : "TextMuted");
                if (isPb) time.ToolTip = "Your all-time best in this car at this track";
                Cell(table, row, c++, time, true);
                Cell(table, row, c++, Caption(sessionBest is { } sb && !isBest ? StatFormat.Value(StatKind.LapDelta, lap.Time - sb, false) : "–"), true);
                for (int k = 0; k < sectors; k++)
                {
                    double? sector = lap.Sectors is { } list && list.Count == sectors ? list[k] : null;
                    var text = Caption(sector is { } v ? v.ToString("0.000", CultureInfo.CurrentCulture) : "–");
                    System.Windows.Documents.Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
                    if (sector is { } sv && lap.Clean)
                    {
                        if (bestSector[k] is { } bk && Math.Abs(sv - bk) < 0.0005)
                        {
                            text.SetResourceReference(TextBlock.ForegroundProperty, "TimingBest");
                            text.FontWeight = FontWeights.SemiBold;
                            text.ToolTip = "Best sector of the session";
                        }
                        else if (bestSoFar[k] is not { } before || sv < before)
                        {
                            text.SetResourceReference(TextBlock.ForegroundProperty, sageDeep);
                            text.ToolTip = "Faster than this sector on every clean lap before it";
                        }
                        if (bestSoFar[k] is not { } prev || sv < prev) bestSoFar[k] = sv;
                    }
                    Cell(table, row, c++, text, true);
                }
                Cell(table, row, c++, Caption(lap.FuelUsed is { } fuel ? StatFormat.Value(StatKind.Fuel, fuel, imperial) : "–"), true);
                Cell(table, row, c++, Caption(lap.TopSpeed is { } top ? StatFormat.Value(StatKind.Speed, top, imperial) : "–"), true);
                var flag = new WrapPanel();
                void Tag(string text, string soft, string deep, string? tip = null)
                {
                    var chip = Ui.Chip(text, soft, deep);
                    chip.Margin = new Thickness(0, 0, 3, 0);
                    chip.ToolTip = tip;
                    flag.Children.Add(chip);
                }
                if (isPb) Tag("All-time best", pbSoft, pbDeep, "Your all-time best in this car at this track");
                else if (isBest) Tag("Fastest", "AccentSoft", "AccentText", "The session's fastest clean lap");
                if (lap.Flags.HasFlag(LapFlags.Incident)) Tag(lap.Incidents is > 0 and var n ? $"Incident {n}x" : "Incident", coralSoft, coralDeep);
                if (lap.Flags.HasFlag(LapFlags.OffTrack)) Tag("Off track", coralSoft, coralDeep);
                if (lap.Flags.HasFlag(LapFlags.PitLane)) Tag(lap.Flags.HasFlag(LapFlags.Refuel) ? "Pit stop" : "Pit lane", "SurfaceAlt", "TextMuted", "Out lap, in lap or a stop: never a clean lap");
                if (lap.Flags.HasFlag(LapFlags.Untimed)) Tag("Not timed", "SurfaceAlt", "TextMuted", "iRacing gave this lap no time: the time shown is measured line to line, and it's never a clean lap");
                if (lap.Flags.HasFlag(LapFlags.TimeDiffers)) Tag("Timed from the pits", "SurfaceAlt", "TextMuted", "iRacing's time for this lap started before you crossed the line, so it's never a clean lap");
                if (!lap.Clean && lap.Flags == LapFlags.None) Tag("Not clean", coralSoft, coralDeep, "An incident, the pit lane or off track on this lap");
                Cell(table, row, c, flag);
            }
            lapsTable.Children.Add(Section(table, tyresOf.TryGetValue(gi, out var readings) ? StatsSessionTools.TyreRun(readings) : null));
        }
        var tableScroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = lapsTable };
        FrameworkElement lapsBody = tableScroll;
        if (d.Traced.Count == 0)
        {
            var why = Ui.Text($"This session can't be analyzed. {noTraceReason} Its lap times, sectors and stats are all still here.", "Caption");
            why.TextWrapping = TextWrapping.Wrap;
            var note = new Tile { Child = Ui.IconText("I.Info", why, "TextMuted"), CornerRadius = ThemeManager.Round(4), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 0, 6) };
            note.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            lapsBody = new StackPanel { Children = { new NoWidth { Child = note }, tableScroll } };
        }
        if (sectors > 0)
        {
            var legend = Ui.Text("Sector times: purple = best of the session, green = faster than every clean lap before it", "Caption");
            legend.TextWrapping = TextWrapping.Wrap;
            legend.Margin = new Thickness(6, 8, 0, 0);
            legend.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            lapsBody = new StackPanel { Children = { lapsBody, new NoWidth { Child = legend } } };
        }
        var lapsCard = StatsSections.Section("Laps", "I.Flag", d.Traced.Count > 0 ? $"{d.Traced.Count} with a trace" : "No lap traces", analyzeButton, lapsBody);
        page.Children.Add(chartCard);
        if (StatsSessionTools.Sectors(laps) is { } sectorCard)
        {
            sectorCard.VerticalAlignment = VerticalAlignment.Top;
            page.Children.Add(new BesidePanel { Gap = Ui.PanelSpacing, SecondMin = 420, Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), Children = { lapsCard, sectorCard } });
        }
        else
        {
            lapsCard.Margin = new Thickness(0, 0, 0, Ui.PanelSpacing);
            page.Children.Add(lapsCard);
        }
        return page;
    }

    private const double BannerHeight = 196;
    private const double CarArea = 420;
    private const double CarRoom = 300;
    private const double MapWidth = 170, MapHeight = 120;

    private static FrameworkElement Banner(SessionDetail d, List<LapRow> cleanLaps, int sectors, double?[] bestSector, bool imperial)
    {
        var s = d.Session;
        var laps = d.Laps;
        bool raced = RaceParts.Raced(s);
        string place = RaceParts.Place(s);
        var awards = raced && d.Race is { } race ? race.Awards : Array.Empty<Award>();
        var times = laps.Select(l => (l.Time, l.Clean)).ToList();
        double? grade = d.Race?.Consistency ?? RaceResult.Consistency(times);
        var local = s.StartUtc.ToLocalTime();

        var top = new Grid { MinHeight = BannerHeight, ClipToBounds = true };
        top.Children.Add(new Slant(place + "Wash", 0, 0, 0.7, 0, 0.52, 1, 0, 1));
        top.Children.Add(new Slant(place + "Faint", 0.7, 0, 0.76, 0, 0.58, 1, 0.52, 1));
        var carZone = RaceParts.CarOnStripe(s.Car, place, BannerHeight, 150, 380, carArea: CarArea);
        top.Children.Add(carZone);
        var outline = new Border
        {
            Width = MapWidth, Height = MapHeight, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, CarArea, 0), IsHitTestVisible = false,
            Child = new TrackSketch(s.Track, s.Config, place + "Text", 4, lap: true) { PlayOnLoad = true, TrackId = d.TrackId, TrackName = d.TrackName },
        };
        top.Children.Add(outline);
        if (awards.Count > 0)
        {
            var ribbon = RaceParts.Ribbon(awards[0]);
            ribbon.Margin = new Thickness(0, 0, 0, 10);
            top.Children.Add(ribbon);
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(24, 18, 24, 18), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        if (raced) row.Children.Add(RaceParts.Position(s.FinishPos!.Value, place, 150, "Finished"));
        else
        {
            var lead = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            lead.Children.Add(new TextBlock { Text = "Fastest clean lap", Style = Ui.Res<Style>("Caption") });
            lead.Children.Add(RaceParts.Big(s.Best is { } fastest ? StatFormat.LapTime(fastest) : SimRacing.Race.RaceFormat.Missing, 58, place + "Text", italic: true).Also(t => t.HorizontalAlignment = HorizontalAlignment.Left));
            row.Children.Add(lead);
        }
        var meta = new StackPanel { Margin = new Thickness(raced ? 22 : 30, 10, 0, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 520 };
        meta.Children.Add(RaceParts.Big(s.Car, 30, "Heading", italic: false, heavy: false).Also(t => { t.TextWrapping = TextWrapping.Wrap; t.HorizontalAlignment = HorizontalAlignment.Left; }));
        meta.Children.Add(new TextBlock { Text = $"{s.Place} · {Dates.Weekday(local)} {Dates.ShortTime(local)}", Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        chips.Children.Add(RaceParts.Chip(StatsSessions.TypeMark(s.Type)));
        if (s.CarClass.Length > 0) chips.Children.Add(RaceParts.Labeled("Class", s.CarClass));
        if (s.Type == "Race") foreach (var chip in RaceParts.Ratings(s)) chips.Children.Add(chip);
        foreach (var award in awards.Skip(1)) chips.Children.Add(RaceParts.AwardChip(award, place));
        meta.Children.Add(chips);
        row.Children.Add(meta);
        top.Children.Add(row);
        carZone.KeepClearOf(row);
        top.SizeChanged += (_, e) =>
        {
            var shown = e.NewSize.Width - row.ActualWidth >= CarRoom ? Visibility.Visible : Visibility.Collapsed;
            if (carZone.Car is { } shownCar && shownCar.Visibility != shown) shownCar.Visibility = shown;
            var map = e.NewSize.Width - row.Margin.Left - row.ActualWidth - CarArea >= MapWidth + 8 ? Visibility.Visible : Visibility.Collapsed;
            if (outline.Visibility != map) outline.Visibility = map;
        };

        var tiles = new List<FrameworkElement>();
        void Tile(string value, string label, string? tip = null, string brush = "Heading", double size = 26) => tiles.Add(RaceParts.Tile(value, label, place, brush, size, tip));
        if (raced && s.StartPos is { } start)
        {
            int gained = start - s.FinishPos!.Value;
            Tile(RaceParts.Signed(gained), $"Positions · P{start} to P{s.FinishPos}", null, RaceParts.GainBrush(gained));
        }
        if (raced) Tile(s.Best is { } best ? StatFormat.LapTime(best) : SimRacing.Race.RaceFormat.Missing, "Fastest clean lap");
        if (sectors > 0 && bestSector.All(b => b is not null))
        {
            double sum = bestSector.Sum(b => b!.Value);
            if (s.Best is { } fastest && fastest < sum) sum = fastest;
            Tile(StatFormat.LapTime(sum), "Optimal lap", "Your best clean sectors of the session put together");
        }
        if (cleanLaps.Count > 0) Tile(StatFormat.LapTime(cleanLaps.Average(l => l.Time)), "Average clean lap");
        if (grade is { } g) Tile(g.ToString("0.0", CultureInfo.CurrentCulture) + "%", "Consistency", RaceParts.ConsistencyTip);
        Tile(laps.Count == 0 ? s.Laps.ToString(CultureInfo.CurrentCulture) : laps.Count.ToString(CultureInfo.CurrentCulture),
            laps.Count == 0 ? "Laps" : $"Laps · {cleanLaps.Count} clean ({100.0 * cleanLaps.Count / laps.Count:0}%)");
        Tile(StatFormat.Duration(s.DriveS), raced ? $"Drive time · {SimRacing.Race.RaceFormat.Count(s.PitStops, "stop")}" : "Driving");
        if (!raced && cleanLaps.Where(l => l.FuelUsed is not null).Select(l => l.FuelUsed!.Value).ToList() is { Count: > 0 } burned)
            Tile(StatFormat.Value(StatKind.Fuel, burned.Average(), imperial), "Fuel per clean lap");
        if (s.Incidents is { } inc) Tile(inc + "x", "Incidents");

        var stack = new StackPanel();
        stack.Children.Add(top);
        stack.Children.Add(RaceParts.Tiles(tiles.Count, tiles.ToArray<FrameworkElement?>()));
        if (ConditionsStrip(s, d.Conditions, imperial) is { } strip)
            stack.Children.Add(new Border { Padding = new Thickness(19, 0, 19, 8), Child = strip });
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Child = stack, ClipToBounds = true, Margin = new Thickness(0, 0, 0, Ui.PanelSpacing) };
    }

    private sealed class NoWidth : Decorator
    {
        private double _shownWidth = double.PositiveInfinity;

        protected override Size MeasureOverride(Size constraint)
        {
            if (Child is null) return default;
            Child.Measure(new Size(double.IsInfinity(constraint.Width) ? _shownWidth : constraint.Width, constraint.Height));
            return new Size(0, Child.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            Child?.Arrange(new Rect(final));
            if (Math.Abs(final.Width - _shownWidth) > 0.5)
            {
                _shownWidth = final.Width;
                InvalidateMeasure();
            }
            return final;
        }
    }

    private sealed class BesidePanel : Panel
    {
        public double Gap { get; init; } = 19;
        public double SecondMin { get; init; } = 400;
        private bool _side;
        private double _firstWidth;

        protected override Size MeasureOverride(Size available)
        {
            var (first, second) = (InternalChildren[0], InternalChildren[1]);
            double width = double.IsInfinity(available.Width) ? 4000 : available.Width;
            first.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double want = first.DesiredSize.Width;
            _side = want + Gap + SecondMin <= width;
            if (_side)
            {
                _firstWidth = want;
                second.Measure(new Size(width - want - Gap, double.PositiveInfinity));
                return new Size(width, Math.Max(first.DesiredSize.Height, second.DesiredSize.Height));
            }
            first.Measure(new Size(width, double.PositiveInfinity));
            second.Measure(new Size(width, double.PositiveInfinity));
            return new Size(width, first.DesiredSize.Height + Gap + second.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var (first, second) = (InternalChildren[0], InternalChildren[1]);
            if (_side)
            {
                first.Arrange(new Rect(0, 0, _firstWidth, first.DesiredSize.Height));
                second.Arrange(new Rect(_firstWidth + Gap, 0, Math.Max(0, final.Width - _firstWidth - Gap), second.DesiredSize.Height));
            }
            else
            {
                first.Arrange(new Rect(0, 0, final.Width, first.DesiredSize.Height));
                second.Arrange(new Rect(0, first.DesiredSize.Height + Gap, final.Width, second.DesiredSize.Height));
            }
            return final;
        }
    }

    private static string? SimTime(string text) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? Dates.ShortTime(t) : null;

    private static readonly string[] SkyNames = { "Clear", "Partly cloudy", "Mostly cloudy", "Overcast" };

    private static readonly string[] WetnessNames = { "", "Dry", "Mostly dry", "Very lightly wet", "Lightly wet", "Moderately wet", "Very wet", "Extremely wet" };

    private static readonly string[] Compass = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

    private static FrameworkElement? ConditionsStrip(SessionSummary s, SessionConditions? c, bool imperial)
    {
        var strip = new WrapPanel();
        void Add(string icon, string label, string value, string tip)
        {
            var cell = new DockPanel { Margin = new Thickness(0, 0, 23, 10), MinWidth = 97, ToolTip = tip };
            var glyph = new Icon { Data = Ui.Res<Geometry>(icon), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            glyph.SetResourceReference(Control.ForegroundProperty, "AccentText");
            cell.Children.Add(glyph);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var reading = new TextBlock { Text = value, Style = Ui.Res<Style>("Body"), FontSize = 12, FontWeight = FontWeights.SemiBold };
            reading.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            text.Children.Add(reading);
            text.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption") });
            cell.Children.Add(text);
            strip.Children.Add(cell);
        }
        string type = s.Weather.Split(" · ")[0];
        if (c?.Skies is { } sky) Add(sky == 0 ? "I.Sun" : "I.Cloud", type.Length > 0 ? $"Skies · {type}" : "Skies", SkyNames[sky], "Skies when you first drove · weather type");
        else if (s.Weather.Length > 0) Add("I.Cloud", "Weather", s.Weather, "Weather");
        if (c is { SimStart.Length: > 0 } && SimTime(c.SimStart) is { } simTime) Add("I.Clock", "In-sim time", simTime, "The date and time of day in the sim when the session started");
        if (c is not null)
        {
            if (c.TrackTempMin is { } lo && c.TrackTempMax is { } hi)
                Add("I.Thermometer", "Track", Math.Round(hi - lo) >= 1 ? $"{StatFormat.Number(StatKind.Temp, lo, imperial)}–{StatFormat.Value(StatKind.Temp, hi, imperial)}" : StatFormat.Value(StatKind.Temp, hi, imperial),
                    "Track temperature while you drove (lowest–highest)");
            if (c.AirTemp is { } air) Add("I.Thermometer", "Air", StatFormat.Value(StatKind.Temp, air, imperial), "Air temperature when you first drove");
            if (c.WindKph is { } wind)
                Add("I.Wind", c.WindDir is { } dir ? "Wind from " + Compass[(int)Math.Round((dir % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI) / (Math.PI / 4)) % 8] : "Wind",
                    StatFormat.Value(StatKind.Speed, wind, imperial), "Wind speed and where it comes from");
            if (c.Humidity is { } hum) Add("I.Droplet", "Humidity", $"{hum * 100:0}%", "Relative humidity");
            if (c.Pressure is { } pa) Add("I.Gauge", "Pressure", imperial ? $"{pa / 3386.389:0.00} inHg" : $"{pa / 100:0} hPa", "Air pressure");
            if (c.AirDensity is { } rho) Add("I.Layers", "Air density", imperial ? $"{rho * 0.062428:0.000} lb/ft³" : $"{rho:0.00} kg/m³", "Air density: less air, less grip and power");
            if (c.RainMax is > 0 and var rain) Add("I.CloudRain", "Most rain", $"{rain * 100:0}%", "The most rain while you drove");
            if (c.WetnessMax is > 1 and < 8 and var wet) Add("I.Droplet", "Wettest", WetnessNames[wet], "The wettest the track got while you drove");
            if (c.Rubber.Length > 0) Add("I.Wheel", "Rubber", char.ToUpperInvariant(c.Rubber[0]) + c.Rubber[1..], "Rubber on the racing line at the start");
        }
        if (strip.Children.Count == 0) return null;
        var block = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        var rule = Hairline.Rule(new Border { Margin = new Thickness(0, 0, 0, 11) });
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        block.Children.Add(rule);
        block.Children.Add(strip);
        return block;
    }

    private static Tile Card(FrameworkElement content) => new()
    {
        Style = Ui.Res<Style>("Card"), Padding = new Thickness(19, 16, 19, 19), Child = content, Margin = new Thickness(0, 0, 0, Ui.PanelSpacing),
    };

    private static TextBlock Caption(string text) => new() { Text = text, Style = Ui.Res<Style>("Caption") };

    private static T Also<T>(this T item, Action<T> act)
    {
        act(item);
        return item;
    }

    private static void Cell(Grid table, int row, int col, FrameworkElement element, bool right = false)
    {
        element.Margin = new Thickness(col == 0 ? 6 : 15, 7, 0, 7);
        element.VerticalAlignment = VerticalAlignment.Center;
        if (right) element.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        table.Children.Add(element);
    }

    private sealed class LapTimeChart : StatsCharts.HoverChart
    {
        private readonly List<LapRow> _laps;
        private readonly double _lo, _hi, _step;

        public LapTimeChart(List<LapRow> laps)
        {
            _laps = laps;
            var times = laps.Select(l => l.Time).OrderBy(t => t).ToList();
            double median = StatsCharts.Quantile(times, 0.5);
            (_lo, _hi, _step) = StatsCharts.LapScale(times[0], Math.Min(times[^1], median * 1.04));
        }

        private const double Inset = 11;

        private double X(int i) => Left + Inset + (ActualWidth - Left - Right - 2 * Inset) * (_laps.Count == 1 ? 0.5 : (double)i / (_laps.Count - 1));

        protected override int HitAt(Point p)
        {
            int best = -1;
            double near = 14;
            for (int i = 0; i < _laps.Count; i++)
                if (Math.Abs(X(i) - p.X) < near) { near = Math.Abs(X(i) - p.X); best = i; }
            return best;
        }

        protected override string TipOf(int i)
        {
            var l = _laps[i];
            return $"Lap {l.Lap} · {StatFormat.LapTime(l.Time)}{(l.Clean ? "" : " · not clean")}";
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (ActualWidth <= Left + Right || ActualHeight <= Top + Bottom) return;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            var y = LapAxis(dc, _lo, _hi, _step);
            double bottom = ActualHeight - Bottom;
            double px = Hairline.ScaleOf(this);
            var runPen = new Pen(Ui.Res<Brush>("LineStrong"), Hairline.Pixels(1, px)) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) };
            runPen.Freeze();
            var band = Ui.Res<Brush>("Hover");
            int runStart = 0;
            for (int i = 1; i <= _laps.Count; i++)
            {
                if (i < _laps.Count && (_laps[i].Run ?? 1) == (_laps[runStart].Run ?? 1)) continue;
                if (runStart > 0 || i < _laps.Count)
                {
                    double from = runStart == 0 ? Left : (X(runStart - 1) + X(runStart)) / 2, to = i == _laps.Count ? ActualWidth - Right : (X(i - 1) + X(i)) / 2;
                    if ((_laps[runStart].Run ?? 1) % 2 == 0) dc.DrawRectangle(band, null, new Rect(from, Top, Math.Max(0, to - from), bottom - Top));
                    if (runStart > 0) dc.DrawLine(runPen, new Point(Hairline.Snap(from, px), Top), new Point(Hairline.Snap(from, px), bottom));
                    var name = Text($"Run {_laps[runStart].Run ?? 1}", bold: true, size: 10.5);
                    dc.DrawText(name, new Point(from + 6, bottom - name.Height - 3));
                }
                runStart = i;
            }
            var accent = Ui.Res<Brush>("GraphText");
            var line = new Pen(Ui.Res<Brush>("Graph"), 1.5) { LineJoin = PenLineJoin.Round };
            var hollow = new Pen(Ui.Res<Brush>("TextMuted"), 1.5);
            Point P(int i) => new(X(i), Math.Max(Top, y(Math.Min(_hi, _laps[i].Time))));
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                bool started = false;
                for (int i = 0; i < _laps.Count; i++)
                {
                    if (!_laps[i].Clean || _laps[i].Time > _hi) { started = false; continue; }
                    if (!started) { g.BeginFigure(P(i), false, false); started = true; }
                    else g.LineTo(P(i), true, true);
                }
            }
            geo.Freeze();
            dc.DrawGeometry(null, line, geo);
            int step = Math.Max(1, (int)Math.Ceiling(_laps.Count / ((ActualWidth - Left - Right) / 34)));
            for (int i = 0; i < _laps.Count; i++)
            {
                var p = P(i);
                double r = i == Hover ? 5 : 3.5;
                if (_laps[i].Clean) dc.DrawEllipse(accent, null, p, r, r);
                else dc.DrawEllipse(Ui.Res<Brush>("Surface"), hollow, p, r, r);
                if (_laps[i].Time > _hi)
                {
                    var up = new StreamGeometry();
                    using (var g = up.Open())
                    {
                        g.BeginFigure(new Point(p.X, p.Y + 7), true, true);
                        g.LineTo(new Point(p.X - 4, p.Y + 13), true, false);
                        g.LineTo(new Point(p.X + 4, p.Y + 13), true, false);
                    }
                    up.Freeze();
                    dc.DrawGeometry(Ui.Res<Brush>("TextMuted"), null, up);
                    var actual = Text(StatFormat.LapTime(_laps[i].Time), size: 10.5);
                    dc.DrawText(actual, new Point(Math.Min(p.X + 8, ActualWidth - actual.Width), p.Y + 6));
                }
                if (i % step == 0 || i == _laps.Count - 1)
                {
                    var label = Text(_laps[i].Lap.ToString(CultureInfo.CurrentCulture));
                    dc.DrawText(label, new Point(p.X - label.Width / 2, ActualHeight - Bottom + 6));
                }
            }
        }
    }
}
