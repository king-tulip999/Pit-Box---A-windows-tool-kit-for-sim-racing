using Core.Services;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.Controls;
using Core.SessionStats;
using Core.Theme;

namespace Core.Views.Pages;

internal sealed class SessionFilter
{
    public int Type;
    public string? Car, Track;
    public int Shown = 10;
    public int? CascadeFrom;

    private static readonly string?[] Types = { null, "Practice", "Qualify", "Race" };

    public IEnumerable<SessionSummary> Apply(IEnumerable<SessionSummary> sessions) => sessions.Where(s =>
        (Types[Type] is not { } type || s.Type == type) && (Car is null || s.Car == Car) && (Track is null || s.Place == Track));
}

internal static class StatsSessions
{
    public static Tile Card(IReadOnlyList<SessionSummary> sessions, IReadOnlySet<long> traced, SessionFilter filter, Config.SessionStatsSettings traceSettings,
        Action changed, Action<SessionSummary> open, FrameworkElement settings, RaceInfo? races = null)
    {
        races ??= RaceInfo.None;
        var tools = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        void Tool(FrameworkElement element) { element.Margin = new Thickness(6, 2, 0, 2); element.VerticalAlignment = VerticalAlignment.Center; tools.Children.Add(element); }
        Tool(Ui.Segmented(new[] { "All", "Practice", "Qualifying", "Race" }, filter.Type, i => { filter.Type = i; filter.Shown = 10; changed(); }, "statsSessionType"));
        Tool(Pick("All cars", sessions.Select(s => s.Car), filter.Car, v => { filter.Car = v; filter.Shown = 10; changed(); }));
        Tool(Pick("All tracks", sessions.Select(s => s.Place), filter.Track, v => { filter.Track = v; filter.Shown = 10; changed(); }));
        Tool(settings);

        var list = filter.Apply(sessions).ToList();
        var body = new StackPanel();
        if (list.Count == 0) body.Children.Add(StatsSections.Empty("No sessions match in this range."));
        else
        {
            var table = new Grid();
            for (int c = 0; c < Columns; c++)
            {
                table.ColumnDefinitions.Add(c == 2 ? new ColumnDefinition { Width = GridLength.Auto, MaxWidth = NamesMaxWidth } : new ColumnDefinition { Width = GridLength.Auto });
                if (Spaced(c)) table.ColumnDefinitions.Add(new ColumnDefinition());
            }
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var shown = list.Take(filter.Shown).ToList();
            bool strips = shown.Any(s => RaceParts.Raced(s) && races.LapsOf(s.Id).Count > 1);
            string[] heads = { "Time", "Session", "Track and car", "Map", strips ? "Lap times" : "", "Laps", "Best lap", "Incidents", "Awards", "Traces" };
            int span = table.ColumnDefinitions.Count;
            for (int c = 0; c < heads.Length; c++)
            {
                var head = Ui.TableHead(heads[c], c == 9 ? "Pink: lap traces kept, lap analysis available. Gray: times only." : null);
                if (c == 2 && shown.Any(s => CarPictures.For(s.Car) is not null)) head.Padding = new Thickness(21 * CarPictures.SlotAspect + 8, 0, 0, 0);
                Cell(table, 0, c, head, c is >= 5 and <= 8);
            }
            int row = 0, index = 0, cascadeRow = int.MaxValue;
            var now = DateTime.UtcNow;
            DateTime? day = null;
            foreach (var s in shown)
            {
                if (index++ == filter.CascadeFrom) cascadeRow = row + 1;
                var local = s.StartUtc.ToLocalTime();
                bool newDay = day != local.Date;
                day = local.Date;
                row++;
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = RowHeight });
                bool raced = RaceParts.Raced(s);
                string place = RaceParts.Place(s);
                if (raced)
                {
                    var band = new Border { IsHitTestVisible = false };
                    band.SetResourceReference(Border.BackgroundProperty, place + "Fade");
                    Grid.SetRow(band, row);
                    Grid.SetColumnSpan(band, span);
                    table.Children.Add(band);
                }
                var hit = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Open this session" };
                Grid.SetRow(hit, row);
                Grid.SetColumnSpan(hit, span);
                Ui.HoverHighlight(hit);
                var session = s;
                hit.MouseLeftButtonUp += (_, e) => { e.Handled = true; open(session); };
                var rule = Hairline.Rule(new Border { VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
                rule.SetResourceReference(Border.BackgroundProperty, newDay && row > 1 ? "LineStrong" : "Line");
                Grid.SetRow(rule, row);
                Grid.SetColumnSpan(rule, span);
                table.Children.Add(hit);
                table.Children.Add(rule);
                if (newDay)
                {
                    var dayText = new TextBlock { Text = Dates.Weekday(local) + " " + Dates.Short(local), Style = Ui.Res<Style>("Caption"), FontSize = StatSize, FontWeight = FontWeights.Bold };
                    dayText.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
                    Cell(table, row, 0, new StackPanel { Children = { dayText, Stat(Dates.Time(local)) } });
                }
                else Cell(table, row, 0, Stat(Dates.Time(local)));
                Cell(table, row, 1, raced ? Finish(s, place) : TypeMark(s.Type, true));
                var config = StatFormat.Config(s.Config);
                var names = new StackPanel { Children = { Body(s.Track, "Heading", 13), Body(config.Length > 0 ? $"{config} · {s.Car}" : s.Car, "TextMuted", 12) } };
                var carPicture = CarPictures.Beside(names, s.Car, 21, 8);
                if (CarPictures.For(s.Car) is null) names.Margin = new Thickness(21 * CarPictures.SlotAspect + 8, 0, 0, 0);
                Cell(table, row, 2, carPicture);
                Cell(table, row, 3, new TrackSketch(s.Track, s.Config, "TextMuted", 1.6) { Width = 56, Height = 36, TrackId = s.TrackId, TrackName = s.TrackName });
                var lapTimes = races.LapsOf(s.Id);
                if (raced && lapTimes.Count > 1)
                {
                    var strip = new LapStrip(lapTimes, place) { Width = StripWidth, Height = 22, ToolTip = "Lap times: taller = faster" };
                    Cell(table, row, 4, strip);
                }
                Cell(table, row, 5, Stat(s.Laps.ToString(CultureInfo.CurrentCulture)), true);
                var best = Time(s.Best);
                best.FontSize = StatSize;
                best.FontWeight = FontWeights.Bold;
                Cell(table, row, 6, best, true);
                Cell(table, row, 7, Stat(s.Incidents is { } inc ? inc + "x" : SimRacing.Race.RaceFormat.Missing), true);
                Cell(table, row, 8, Awards(races.AwardsOf(s.Id), place), true);
                bool hasTrace = traced.Contains(s.Id);
                var mark = new Icon
                {
                    Data = Ui.Res<Geometry>("I.Activity"), Width = 11, Height = 11,
                    ToolTip = hasTrace ? "Lap traces kept: lap analysis available" : "Times only: no lap analysis. " + NoTraceReason(s, traceSettings, now),
                };
                mark.SetResourceReference(Control.ForegroundProperty, hasTrace ? "AccentText" : "TextFaint");
                Cell(table, row, 9, mark);
            }
            body.Children.Add(table);
            filter.CascadeFrom = null;
            if (cascadeRow != int.MaxValue && Motion.Flourish)
                foreach (UIElement cell in table.Children)
                    if (Grid.GetRow(cell) >= cascadeRow) Motion.Enter(cell, Math.Min(MaxCascadeMs, (Grid.GetRow(cell) - cascadeRow) * CascadeStepMs), -8, 260);
            if (list.Count > filter.Shown)
            {
                var more = Ui.LabeledButton("I.ChevronDown", $"Show more ({list.Count - filter.Shown} left)", () => { filter.CascadeFrom = filter.Shown; filter.Shown += 20; changed(); });
                more.Margin = new Thickness(0, 10, 0, 0);
                more.HorizontalAlignment = HorizontalAlignment.Left;
                body.Children.Add(more);
            }
        }
        var card = StatsSections.Section("Recent sessions", $"{SimRacing.Race.RaceFormat.Count(list.Count, "session")}", tools, body);
        Ui.Anchor(card, "stats.sessions");
        return card;
    }

    private const int Columns = 10;
    private const double RowHeight = 44, CascadeStepMs = 22, MaxCascadeMs = 420, NamesMaxWidth = 380, StatSize = 13;

    internal static string NoTraceReason(SessionSummary s, Config.SessionStatsSettings settings, DateTime nowUtc)
    {
        if (!settings.TraceRecord)
            return "Lap traces are turned off (the gear on Recent sessions), so this session's line and inputs weren't kept.";
        if (settings.TraceKeepDays > 0 && s.StartUtc < nowUtc.AddDays(-settings.TraceKeepDays))
            return $"Older than lap traces are kept for ({TraceSettings.DaysName(settings.TraceKeepDays)}, the gear on Recent sessions), so its line and inputs were let go.";
        return "No line or inputs are stored for this session (not kept, or removed since), so there's nothing to analyze.";
    }

    internal static string TypeName(string type) => type switch { "Qualify" => "Qualifying", "TimeTrial" => "Time trial", "" => "Other", _ => type };

    internal static string TypeBrush(string type) => type switch { "Practice" => "Slate", "Qualify" => "Gold", "Race" => "Accent", _ => "LineStrong" };

    internal static FrameworkElement TypeMark(string type, bool tableSize = false)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var swatch = new Border { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        swatch.SetResourceReference(Border.BackgroundProperty, TypeBrush(type));
        row.Children.Add(swatch);
        var name = tableSize ? Stat(TypeName(type)) : new TextBlock { Text = TypeName(type), Style = Ui.Res<Style>("Caption") };
        name.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(name);
        return row;
    }

    private const double StripWidth = 84;

    private static FrameworkElement Finish(SessionSummary s, string place)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(RaceParts.Big("P" + s.ShownPos!.Value.ToString(CultureInfo.CurrentCulture), 25, place + "Text"));
        if (s.Gained is { } gained)
        {
            var gain = RaceParts.Big(RaceParts.Signed(gained), 14, RaceParts.GainBrush(gained));
            gain.Margin = new Thickness(6, 4, 0, 0);
            row.Children.Add(gain);
            row.ToolTip = s.HasTier
                ? $"Started P{s.TierStart} {s.Tier}, finished P{s.TierPos} {s.Tier} (P{s.StartPos} to P{s.FinishPos} in class)"
                : $"Started P{s.StartPos}, finished P{s.FinishPos}";
        }
        if (!s.HasTier) return row;
        return new StackPanel { Children = { row, Body($"{s.Tier} · P{s.FinishPos} in class", "TextMuted", 12) } };
    }

    private static FrameworkElement Awards(IReadOnlyList<Award> awards, string place)
    {
        if (awards.Count == 0) return Caption(SimRacing.Race.RaceFormat.Missing);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var award in awards) row.Children.Add(RaceParts.AwardSquare(award, place));
        return row;
    }

    private static ComboBox Pick(string all, IEnumerable<string> values, string? selected, Action<string?> changed)
    {
        var combo = new SearchCombo { MinWidth = 138, MaxWidth = 227 };
        combo.Items.Add(new Choice(null, all));
        var list = values.Where(v => v.Length > 0).Distinct().ToList();
        if (selected is { Length: > 0 } && !list.Contains(selected)) list.Add(selected);
        foreach (var v in list.OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase)) combo.Items.Add(new Choice(v, v));
        combo.SelectedItem = combo.Items.Cast<Choice>().FirstOrDefault(c => c.Id == selected) ?? combo.Items[0];
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is Choice c && c.Id != selected) changed(c.Id); };
        return combo;
    }

    private static void Cell(Grid table, int row, int col, FrameworkElement element, bool right = false)
    {
        element.Margin = new Thickness(col == 0 ? 6 : 13, row == 0 ? 7 : 3, col == Columns - 1 ? 6 : 0, row == 0 ? 7 : 3);
        element.VerticalAlignment = VerticalAlignment.Center;
        element.IsHitTestVisible = element.ToolTip is not null || element is StackPanel { Children.Count: > 0 } panel && panel.Children.OfType<FrameworkElement>().Any(c => c.ToolTip is not null);
        if (right) element.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(element, row);
        Grid.SetColumn(element, GridColumn(col));
        table.Children.Add(element);
    }

    private static bool Spaced(int col) => col is >= 2 and < Columns - 1;

    private static int GridColumn(int col) => col <= 2 ? col : 2 * col - 2;

    private static TextBlock Caption(string text) => new() { Text = text, Style = Ui.Res<Style>("Caption") };

    private static TextBlock Stat(string text) => new() { Text = text, Style = Ui.Res<Style>("Body"), FontSize = StatSize, TextWrapping = TextWrapping.NoWrap };

    private static TextBlock Body(string text, string? brush = null, double size = 11)
    {
        var t = TextFit.TipWhenTrimmed(new TextBlock { Text = text, Style = Ui.Res<Style>("Body"), FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis });
        if (brush is not null) t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    internal static TextBlock Time(double? seconds, string brush = "Heading")
    {
        var t = new TextBlock { Text = seconds is { } s ? StatFormat.LapTime(s) : "–", FontWeight = FontWeights.SemiBold, FontSize = 12 };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "MetricFont");
        t.SetResourceReference(TextBlock.ForegroundProperty, seconds is null ? "TextFaint" : brush);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }
}
