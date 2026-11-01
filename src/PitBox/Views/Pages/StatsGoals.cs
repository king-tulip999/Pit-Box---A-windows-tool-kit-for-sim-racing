using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Config;
using PitBox.Controls;
using PitBox.SessionStats;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed record GoalsData(RaceStreaks Streaks, List<AchievementState> Achievements, List<GoalState> Goals, List<string> Cars, List<string> Tracks)
{
    public static GoalsData Read(StatsStore store, SessionStatsSettings settings, DateTime nowUtc)
    {
        var streaks = StatGoals.ReadStreaks(store);
        return new(
            streaks,
            SessionStats.Achievements.Evaluate(SessionStats.Achievements.Read(store, streaks)),
            settings.Goals.Select(g => StatGoals.Read(store, g, nowUtc)).ToList(),
            store.Db.Query("SELECT DISTINCT car FROM sessions ORDER BY car").Select(r => (string)r[0]!).ToList(),
            store.Db.Query("SELECT DISTINCT track FROM sessions ORDER BY track").Select(r => (string)r[0]!).ToList());
    }
}

internal static class StatsGoals
{
    public static FrameworkElement Card(GoalsData data, SessionStatsService service, Action changed)
    {
        var s = data.Streaks;
        string Run(Streak k) => SimRacing.Race.RaceFormat.Count(k.Current, "race");
        (StatDef, StatResult) M(string id, string label, Streak k) =>
            (new StatDef("streak." + id, StatRegistry.Racing, label, StatKind.Text) { Help = "Races in a row, up to your latest" },
                s.Races == 0 ? StatResult.None : new StatResult(Run(k), "best " + Run(new Streak(k.Best, k.Best))));
        var streaks = StatsSections.Metrics(new[] { M("clean", "Clean races in a row", s.Clean), M("finish", "Finishes in a row", s.Finished), M("top5", "Top fives in a row", s.TopFive) })!;

        int earned = data.Achievements.Sum(a => a.Earned);
        var badges = new StatGrid { MinTileWidth = 150, Gap = 0, RowGap = 13 };
        foreach (var state in data.Achievements) badges.Children.Add(Badge(state));

        var goals = new StackPanel();
        if (data.Goals.Count == 0) goals.Children.Add(StatsSections.Empty("No goals yet: add one for an iRating, a number of races or wins, or time at a track."));
        foreach (var state in data.Goals)
        {
            var g = state.Goal;
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = TextFit.TipWhenTrimmed(new TextBlock { Text = Describe(g, state.Category), Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            string now = state.Current is { } c ? StatGoals.Format(g.Kind, c) : "No data";
            var value = new TextBlock { Text = $"{now} / {StatGoals.Format(g.Kind, g.Target)}" + (state.Done ? " · done" : ""), Style = Ui.Res<Style>("Muted"), Margin = new Thickness(12, 0, 0, 0) };
            if (state.Done) value.SetResourceReference(TextBlock.ForegroundProperty, StatsUi.Hue("Sage").Deep);
            Grid.SetColumn(value, 1);
            head.Children.Add(title);
            head.Children.Add(value);
            var meter = new Meter { Value = state.Fraction, Fill = Ui.Res<Brush>(state.Done ? StatsUi.Hue("Sage").Deep : "Accent"), Margin = new Thickness(0, 5, 0, 0) };
            var body = new StackPanel { Children = { head, meter } };
            var row = new Button { Style = Ui.Res<Style>("GhostButton"), Content = body, Height = double.NaN, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(8, 6, 8, 7), Margin = new Thickness(0, 0, 0, 4), ToolTip = "Edit this goal" };
            row.Click += (_, _) => _ = Edit(row, data, service, g, changed);
            goals.Children.Add(row);
        }
        int room = SessionStatsSettings.MaxGoals - service.Settings.Goals.Count;
        if (room > 0)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var add = Ui.LabeledButton("I.Plus", "Add goal", () => { }, "SoftButton");
            add.Click += (_, _) => _ = Edit(add, data, service, null, changed);
            actions.Children.Add(add);
            var suggestions = SessionStats.Achievements.Suggest(data.Achievements, service.Settings.Goals, room);
            if (suggestions.Count > 0)
            {
                var suggest = Ui.LabeledButton("I.Sparkles", "Suggest goals", () =>
                {
                    service.Settings.Goals.AddRange(suggestions);
                    changed();
                });
                suggest.Margin = new Thickness(8, 0, 0, 0);
                suggest.ToolTip = "Adds the next achievements you are closest to: " + string.Join(", ", suggestions.Select(g => $"{StatGoals.Name(g.Kind)} {StatGoals.Format(g.Kind, g.Target)}"));
                actions.Children.Add(suggest);
            }
            goals.Children.Add(actions);
        }
        var card = StatsSections.Section("Goals and achievements", "I.Trophy", null, null,
            StatsSections.Stack(
                StatsSections.Chart("Streaks", "Official and unofficial races, all time", streaks),
                StatsSections.Chart("Achievements", $"All time · {earned} of {SessionStats.Achievements.TierCount} earned", badges),
                StatsSections.Chart("Goals", null, goals)));
        Ui.Anchor(card, "stats.goals");
        return card;
    }

    private static FrameworkElement Badge(AchievementState state)
    {
        var family = state.Family;
        var gold = StatsUi.Hue("Gold").Deep;
        bool all = state.Next is null;
        var pips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 6) };
        for (int i = 0; i < family.Tiers.Length; i++)
        {
            var pip = new Border { Width = 10, Height = 3, Margin = new Thickness(0, 0, 3, 0), RenderTransform = new SkewTransform(-30, 0) };
            pip.SetResourceReference(Border.BackgroundProperty, i < state.Earned ? gold : "Line");
            pips.Children.Add(pip);
        }
        var value = new TextBlock { Text = state.Value is { } v ? SessionStats.Achievements.Format(family, v) : SimRacing.Race.RaceFormat.Missing, Style = Ui.Res<Style>("BigValue"),
            FontSize = 20, FontWeight = FontWeights.ExtraBold, FontStyle = FontStyles.Italic };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        value.SetResourceReference(TextBlock.ForegroundProperty, all ? gold : state.Earned > 0 ? "Heading" : "TextFaint");
        var label = new TextBlock { Text = family.Name, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        string detail = state.Value is null ? "No data" : all ? $"All {family.Tiers.Length} earned" : "Next " + SessionStats.Achievements.Target(family, state.Next!.Value);
        var line = new TextBlock { Text = detail, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.ForegroundProperty, all ? gold : "TextFaint");
        var meter = new Meter { Value = state.Fraction, Fill = Ui.Res<Brush>(all ? gold : "Accent"), Margin = new Thickness(0, 5, 12, 0) };
        var stack = new StackPanel { Children = { pips, value, label, line, meter } };
        string tiers = string.Join(", ", family.Tiers.Select(t => SessionStats.Achievements.Format(family, t)));
        var cell = Hairline.Frame(new Border { Padding = new Thickness(11, 2, 10, 2), Child = stack, ToolTip = $"{family.Name}: {tiers}" }, Sides.Left);
        cell.SetResourceReference(Border.BorderBrushProperty, "Line");
        return cell;
    }

    private static string Describe(StatGoal g, string? category)
    {
        string name = StatGoals.Name(g.Kind);
        if (category is { Length: > 0 } c) name += $" ({c.ToLowerInvariant()})";
        var scope = new List<string>();
        if (g.Car is { } car) scope.Add(car);
        if (g.Track is { } track) scope.Add("at " + track);
        if (g.ThisSeason) scope.Add("this season");
        return scope.Count == 0 ? name : $"{name} · {string.Join(", ", scope)}";
    }

    private static async Task Edit(FrameworkElement from, GoalsData data, SessionStatsService service, StatGoal? goal, Action changed)
    {
        if (Window.GetWindow(from) is not MainWindow window) return;
        var kind = goal?.Kind ?? GoalMetric.IRating;
        double target = goal?.Target ?? DefaultTarget(kind, data);
        string car = goal?.Car ?? "", track = goal?.Track ?? "";
        bool season = goal?.ThisSeason ?? false;
        var numberHost = new ContentControl { Focusable = false };
        NumberBox? number = null;
        void MakeNumber()
        {
            var (min, max, step) = StatGoal.Limits(kind);
            number = new NumberBox(Math.Clamp(target, min, max), kind == GoalMetric.DrivingHours ? "h" : "", 75)
                { Minimum = min, Maximum = max, Step = step, Decimals = kind == GoalMetric.SafetyRating ? 2 : 0 };
            number.ValueChanged += v => target = v;
            numberHost.Content = number;
        }
        MakeNumber();
        var scope = new StackPanel();
        var carPick = Ui.Combo(new[] { new Choice("", "Any car") }.Concat(data.Cars.Select(c => new Choice(c, c))), car, c => car = c.Id ?? "", 259);
        var trackPick = Ui.Combo(new[] { new Choice("", "Any track") }.Concat(data.Tracks.Select(t => new Choice(t, t))), track, c => track = c.Id ?? "", 259);
        var seasonSwitch = Ui.Switch(season, on => season = on);
        scope.Children.Add(Ui.FieldRow("Car", carPick));
        scope.Children.Add(Ui.FieldRow("Track", trackPick));
        scope.Children.Add(Ui.FieldRow("This season only", seasonSwitch));
        void ShowScope() => scope.Visibility = kind is GoalMetric.IRating or GoalMetric.SafetyRating ? Visibility.Collapsed : Visibility.Visible;
        ShowScope();
        var kinds = Enum.GetValues<GoalMetric>().Select(k => new Choice(k.ToString(), StatGoals.Name(k)));
        var kindPick = Ui.Combo(kinds, kind.ToString(), c =>
        {
            if (!Enum.TryParse<GoalMetric>(c.Id, out var k) || k == kind) return;
            kind = k;
            target = DefaultTarget(k, data);
            MakeNumber();
            ShowScope();
        }, 259);
        var content = new StackPanel { Children = { Ui.FieldRow("Goal", kindPick), Ui.FieldRow("Target", numberHost), scope } };
        var buttons = Ui.DialogButtons(window, goal is null ? "Add" : "Save", () =>
        {
            var g = goal ?? new StatGoal();
            g.Kind = kind;
            g.Target = number?.Value ?? target;
            g.Car = car;
            g.Track = track;
            g.ThisSeason = season;
            g.Clean();
            if (goal is null) service.Settings.Goals.Add(g);
            return true;
        });
        Button? delete = null;
        if (goal is not null)
        {
            delete = new Button { Content = "Delete", Style = Ui.Res<Style>("DangerButton") };
            delete.Click += (_, _) => { service.Settings.Goals.Remove(goal); window.CloseModal(true); };
        }
        if (await window.ShowModal(Ui.Dialog(window, goal is null ? "Add goal" : "Edit goal", "", content, buttons, null, 518, delete))) changed();
    }

    private static double DefaultTarget(GoalMetric kind, GoalsData data)
    {
        var next = data.Achievements.FirstOrDefault(a => a.Family.Goal == kind)?.Next;
        var current = data.Goals.FirstOrDefault(g => g.Goal.Kind == kind)?.Current;
        return kind switch
        {
            GoalMetric.IRating => Math.Round(((current ?? 1350) + 250) / 50) * 50,
            GoalMetric.SafetyRating => 3.0,
            _ when next is { } n => n,
            GoalMetric.DrivingHours => 50,
            GoalMetric.Laps => 1000,
            GoalMetric.Races => 25,
            _ => 5,
        };
    }
}
