using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Services;
using PitBox.SessionStats;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed record GoalsData(RaceStreaks Streaks, List<AchievementState> Achievements, List<GoalState> Goals, List<string> Cars, List<string> Tracks,
    List<string> Categories, Dictionary<(GoalMetric, string), double> Ratings)
{
    public static GoalsData Read(StatsStore store, SessionStatsSettings settings, DateTime nowUtc)
    {
        var streaks = StatGoals.ReadStreaks(store);
        var categories = StatGoals.RatedCategories(store);
        var ratings = new Dictionary<(GoalMetric, string), double>();
        foreach (var category in categories)
            foreach (var kind in new[] { GoalMetric.IRating, GoalMetric.SafetyRating })
                if (StatGoals.LatestRating(store, kind, category).Value is { } v) ratings[(kind, category)] = v;
        return new(
            streaks,
            SessionStats.Achievements.Evaluate(SessionStats.Achievements.Read(store, streaks)),
            settings.Goals.Select(g => StatGoals.Read(store, g, nowUtc)).ToList(),
            store.Db.Query("SELECT DISTINCT car FROM sessions ORDER BY car").Select(r => (string)r[0]!).ToList(),
            store.Db.Query("SELECT DISTINCT track FROM sessions ORDER BY track").Select(r => (string)r[0]!).ToList(),
            categories,
            ratings);
    }

    public double? Rating(GoalMetric kind, string? category) => category is not null && Ratings.TryGetValue((kind, category), out var v) ? v : null;
}

internal static class StatsGoals
{
    public static FrameworkElement Card(GoalsData data, SessionStatsService service, Action changed, Action save)
    {
        AdoptRatingStarts(data, save);
        var s = data.Streaks;
        string Run(Streak k) => SimRacing.Race.RaceFormat.Count(k.Current, "race");
        (StatDef, StatResult) M(string id, string label, Streak k) =>
            (new StatDef("streak." + id, StatRegistry.Racing, label, StatKind.Text) { Help = "Races in a row, up to your latest" },
                s.Races == 0 ? StatResult.None : new StatResult(Run(k), "best " + Run(new Streak(k.Best, k.Best))));
        var streaks = StatsSections.Metrics(new[] { M("clean", "Clean races in a row", s.Clean), M("finish", "Finishes in a row", s.Finished), M("top5", "Top fives in a row", s.TopFive) })!;

        int earned = data.Achievements.Sum(a => a.Earned);
        var badges = new StatGrid { MinTileWidth = 150, Gap = 0, RowGap = 13 };
        foreach (var state in data.Achievements) badges.Children.Add(Badge(state));

        var settings = service.Settings;
        int count = settings.Goals.Count, room = SessionStatsSettings.MaxGoals - count;
        var open = settings.Reached.Where(g => g.Open).Reverse().ToList();
        var goals = new StackPanel();
        if (data.Goals.Count == 0 && open.Count == 0) goals.Children.Add(StatsSections.Empty("No goals yet: add one for an iRating, a number of races or wins, or time at a track."));
        else
        {
            var grid = new EvenGrid { MinItemWidth = GoalMin, ColumnSteps = new[] { 1, 2, 3 }, ColumnGap = 8, RowGap = 8 };
            foreach (var g in open) grid.Children.Add(ReachedTile(g, data, service, room, changed, save));
            foreach (var state in data.Goals) grid.Children.Add(GoalTile(state, data, service, changed));
            goals.Children.Add(grid);
        }
        if (room > 0)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var add = Ui.LabeledButton("I.Plus", "Add goal", () => { }, "SoftButton");
            add.Click += (_, _) => _ = Edit(add, data, service, null, changed);
            actions.Children.Add(add);
            if (SessionStats.Achievements.Suggest(data.Achievements, settings.Goals, room).Count > 0)
            {
                var suggest = Ui.LabeledButton("I.Sparkles", "Suggest goals", () => { });
                suggest.Click += (_, _) => _ = Suggest(suggest, data, service, room, changed);
                suggest.Margin = new Thickness(8, 0, 0, 0);
                actions.Children.Add(suggest);
            }
            goals.Children.Add(actions);
        }
        string limit = room >= 0
            ? $"{count} of {SessionStatsSettings.MaxGoals} · click a goal to change or delete it"
            : $"{count} goals, {SessionStatsSettings.MaxGoals} at most: delete {count - SessionStatsSettings.MaxGoals} to add a new one";
        var card = StatsSections.Section("Goals and achievements", "I.Trophy", "Targets you set, and milestones earned from your sessions", null,
            StatsSections.Stack(
                StatsSections.Chart("Goals", limit, goals),
                StatsSections.Chart("Reached goals", Rewards(settings.Reached.Count), History(settings.Reached)),
                StatsSections.Chart("Streaks", "Official and unofficial races, all time", streaks),
                StatsSections.Chart("Achievements", $"All time · {earned} of {SessionStats.Achievements.TierCount} earned", badges)));
        Ui.Anchor(card, "stats.goals");
        return card;
    }

    private const double GoalMin = 260;

    public static IReadOnlyList<string> ReachedIn(SessionStatsSettings settings, long? session) =>
        session is null ? Array.Empty<string>() : settings.Reached.Where(g => g.ReachedSession == session).Select(StatGoals.Label).ToList();
    private const int HistoryShown = 8;

    private static string Rewards(int reached)
    {
        int unlocked = HudColorSets.UnlockedCount(reached);
        string sets = $"{unlocked} of {HudColorSets.All.Length} iRacing Overlay color sets unlocked";
        return HudColorSets.Next(reached) is { } next ? $"{sets} · “{next.Name}” at {SimRacing.Race.RaceFormat.Count(next.Goals, "goal")}" : sets;
    }

    private static FrameworkElement History(List<StatGoal> reached)
    {
        if (reached.Count == 0) return StatsSections.Empty("None yet. Each goal you reach counts toward new color sets for the iRacing Overlay (Sim Racing Tools → iRacing Overlay → Settings).");
        var list = new StackPanel();
        foreach (var g in reached.AsEnumerable().Reverse().Take(HistoryShown))
        {
            var line = new DockPanel { Height = 30 };
            string when = Dates.Day(g.ReachedUtc!.Value.ToLocalTime());
            if (GoalRewards.Took(g) is { } took) when += " · " + Took(took);
            var date = new TextBlock { Text = when, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(date, Dock.Right);
            line.Children.Add(date);
            var name = new TextBlock { Text = StatGoals.Label(g), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, StatsUi.Hue("Gold").Deep);
            var scope = TextFit.TipWhenTrimmed(new TextBlock { Text = Scope(g, g.Category), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 0, 0) });
            DockPanel.SetDock(name, Dock.Left);
            line.Children.Add(name);
            line.Children.Add(scope);
            var rule = Hairline.Frame(new Border { Child = line }, Sides.Top);
            rule.SetResourceReference(Border.BorderBrushProperty, "Line");
            list.Children.Add(rule);
        }
        if (reached.Count > HistoryShown)
            list.Children.Add(StatsSections.Empty($"Latest {HistoryShown} of {reached.Count} shown."));
        return list;
    }

    private static string Took(TimeSpan took) => took.TotalDays < 1 ? "within a day" : "took " + SimRacing.Race.RaceFormat.Count((int)Math.Round(took.TotalDays), "day");

    private static Border ReachedTile(StatGoal g, GoalsData data, SessionStatsService service, int room, Action changed, Action save)
    {
        var gold = StatsUi.Hue("Gold");
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = StatGoals.Name(g.Kind), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        name.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        var scope = TextFit.TipWhenTrimmed(new TextBlock { Text = Scope(g, g.Category), Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });
        var value = new TextBlock { Text = StatGoals.Format(g.Kind, g.Target), Style = Ui.Res<Style>("BigValue"), FontSize = 20, FontWeight = FontWeights.ExtraBold, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 6, 0, 0) };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        value.SetResourceReference(TextBlock.ForegroundProperty, gold.Deep);
        string when = "Reached " + Dates.Day(g.ReachedUtc!.Value.ToLocalTime()) + (GoalRewards.Took(g) is { } took ? " · " + Took(took) : "");
        var note = new TextBlock { Text = when, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 4, 0, 0) };
        note.SetResourceReference(TextBlock.ForegroundProperty, gold.Deep);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9, 0, 0) };
        if (GoalRewards.NextTarget(g) is { } next)
        {
            var follow = Ui.LabeledButton("I.Plus", "Next: " + StatGoals.Format(g.Kind, next), () =>
            {
                service.Settings.Goals.Add(GoalRewards.FollowUp(g, data.Rating(g.Kind, g.Category), DateTime.UtcNow));
                g.Open = false;
                changed();
            }, "SoftButton");
            follow.IsEnabled = room > 0;
            follow.ToolTip = room > 0 ? "Adds the same goal with the next target" : $"Already {SessionStatsSettings.MaxGoals} goals";
            ToolTipService.SetShowOnDisabled(follow, true);
            follow.Margin = new Thickness(0, 0, 6, 0);
            actions.Children.Add(follow);
        }
        actions.Children.Add(Ui.LabeledButton("I.Check", "Done", () => { g.Open = false; changed(); }));

        var body = new StackPanel { Children = { name, scope, value, note, actions } };
        var tile = Hairline.Frame(new Border { Padding = new Thickness(12, 9, 12, 10), Child = body });
        tile.SetResourceReference(Border.BackgroundProperty, gold.Soft);
        tile.SetResourceReference(Border.BorderBrushProperty, gold.Deep);
        if (!g.Seen)
        {
            g.Seen = true;
            save();
            tile.Loaded += (_, _) => Celebrate(tile);
        }
        return tile;
    }

    private static void Celebrate(Border tile)
    {
        var glow = new System.Windows.Media.Animation.DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(CelebrateMs))
        {
            AutoReverse = true, RepeatBehavior = new System.Windows.Media.Animation.RepeatBehavior(3), FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop,
            EasingFunction = Motion.Ease,
        };
        tile.BeginAnimation(UIElement.OpacityProperty, glow);
    }

    private const double CelebrateMs = 420;

    private static async Task Suggest(FrameworkElement from, GoalsData data, SessionStatsService service, int room, Action changed)
    {
        if (Window.GetWindow(from) is not MainWindow window) return;
        var shown = new List<StatGoal>();
        var picks = new List<(StatGoal Goal, bool On)>();
        var list = new StackPanel();
        var others = Ui.LabeledButton("I.Refresh", "Suggest others", () => { });
        void Fill()
        {
            var next = SessionStats.Achievements.Suggest(data.Achievements, service.Settings.Goals.Concat(shown), room);
            if (next.Count == 0)
            {
                shown.Clear();
                next = SessionStats.Achievements.Suggest(data.Achievements, service.Settings.Goals, room);
            }
            shown.AddRange(next);
            picks = next.Select(g => (g, true)).ToList();
            list.Children.Clear();
            for (int i = 0; i < picks.Count; i++)
            {
                int at = i;
                var g = picks[i].Goal;
                var state = data.Achievements.FirstOrDefault(a => a.Family.Goal == g.Kind);
                string now = state?.Value is { } v ? "now " + StatGoals.Format(g.Kind, v) : "no data yet";
                var row = Ui.SettingRow(StatGoals.Label(g), $"The next achievement · {now}", Ui.Switch(true, on => picks[at] = (picks[at].Goal, on)));
                list.Children.Add(row);
            }
        }
        Fill();
        others.Click += (_, _) => Fill();
        var own = Ui.LabeledButton("I.Pencil", "Make my own", () => { });
        own.Margin = new Thickness(6, 0, 0, 0);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { others, own } };
        var buttons = Ui.DialogButtons(window, "Add", () =>
        {
            var now = DateTime.UtcNow;
            foreach (var (goal, on) in picks.Where(p => p.On).Take(room))
            {
                goal.CreatedUtc = now;
                service.Settings.Goals.Add(goal);
            }
            return true;
        });
        bool makeOwn = false;
        own.Click += (_, _) => { makeOwn = true; window.CloseModal(false); };
        var content = new StackPanel { Children = { list } };
        bool added = await window.ShowModal(Ui.Dialog(window, "Suggested goals", "The achievements you are closest to. Switch off any you don't want.", content, buttons, null, 518, left));
        if (added) changed();
        else if (makeOwn) await Edit(from, data, service, null, changed);
    }

    private static void AdoptRatingStarts(GoalsData data, Action save)
    {
        bool adopted = false;
        foreach (var state in data.Goals)
        {
            var g = state.Goal;
            if (!StatGoal.IsRating(g.Kind) || g.From is not null || state.Current is not { } now) continue;
            g.Category ??= state.Category;
            g.From = now;
            g.CreatedUtc ??= DateTime.UtcNow;
            adopted = true;
        }
        if (adopted) save();
    }

    private static Button GoalTile(GoalState state, GoalsData data, SessionStatsService service, Action changed)
    {
        var g = state.Goal;
        var gold = StatsUi.Hue("Gold").Deep;
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = StatGoals.Name(g.Kind), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        name.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        var scope = TextFit.TipWhenTrimmed(new TextBlock { Text = Scope(g, state.Category), Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) });

        var now = new TextBlock { Text = state.Current is { } c ? StatGoals.Format(g.Kind, c) : SimRacing.Race.RaceFormat.Missing, Style = Ui.Res<Style>("BigValue"),
            FontSize = 20, FontWeight = FontWeights.ExtraBold, FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Bottom };
        now.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        now.SetResourceReference(TextBlock.ForegroundProperty, state.Done ? gold : "Heading");
        var target = new TextBlock { Text = " / " + StatGoals.Format(g.Kind, g.Target), Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 0, 3) };
        var figures = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { now, target } };

        var meter = new Meter { Value = state.Fraction, Fill = Ui.Res<Brush>(state.Done ? gold : "Accent"), Margin = new Thickness(0, 6, 0, 0) };
        var left = new TextBlock { Text = Left(state), Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 4, 0, 0) };
        if (state.Done) left.SetResourceReference(TextBlock.ForegroundProperty, gold);

        var body = new StackPanel { Children = { name, scope, figures, meter, left } };
        var tile = new Button { Style = Ui.Res<Style>("GhostButton"), Content = body, Height = double.NaN, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Top, VerticalAlignment = VerticalAlignment.Stretch, Padding = new Thickness(12, 9, 12, 10) };
        System.Windows.Automation.AutomationProperties.SetName(tile, Describe(g, state.Category));
        tile.Click += (_, _) => _ = Edit(tile, data, service, g, changed);
        return tile;
    }

    private static string Left(GoalState state)
    {
        var g = state.Goal;
        if (state.Current is not { } c) return "No data yet";
        if (state.Done) return "Reached";
        string togo = StatGoals.Format(g.Kind, g.Target - c) + " to go";
        return g.From is { } from && StatGoal.IsRating(g.Kind) ? $"{togo} · from {StatGoals.Format(g.Kind, from)}" : togo;
    }

    private static string Scope(StatGoal g, string? category)
    {
        if (StatGoal.IsRating(g.Kind)) return category is { Length: > 0 } c ? StatGoals.CategoryName(c) + " license" : "Latest license";
        var scope = new List<string>();
        if (g.Car is { } car) scope.Add(car);
        if (g.Track is { } track) scope.Add("at " + track);
        if (g.ThisSeason) scope.Add("this season");
        return scope.Count == 0 ? "Any car and track, all time" : string.Join(", ", scope);
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

    private static string Describe(StatGoal g, string? category) => $"{StatGoals.Name(g.Kind)} · {Scope(g, category)}";

    private static async Task Edit(FrameworkElement from, GoalsData data, SessionStatsService service, StatGoal? goal, Action changed)
    {
        if (Window.GetWindow(from) is not MainWindow window) return;
        var kind = goal?.Kind ?? GoalMetric.IRating;
        double target = goal?.Target ?? DefaultTarget(kind, data);
        string car = goal?.Car ?? "", track = goal?.Track ?? "";
        bool season = goal?.ThisSeason ?? false;
        string? category = goal?.Category ?? data.Goals.FirstOrDefault(g => g.Goal == goal)?.Category ?? data.Categories.FirstOrDefault();
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
        var license = new StackPanel();
        if (data.Categories.Count > 0)
            license.Children.Add(Ui.FieldRow("License", Ui.Combo(data.Categories.Select(c => new Choice(c, StatGoals.CategoryName(c))), category, c => category = c.Id, 259)));
        void ShowScope()
        {
            bool rating = StatGoal.IsRating(kind);
            scope.Visibility = rating ? Visibility.Collapsed : Visibility.Visible;
            license.Visibility = rating ? Visibility.Visible : Visibility.Collapsed;
        }
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
        var content = new StackPanel { Children = { Ui.FieldRow("Goal", kindPick), license, Ui.FieldRow("Target", numberHost), scope } };
        var buttons = Ui.DialogButtons(window, goal is null ? "Add" : "Save", () =>
        {
            var g = goal ?? new StatGoal();
            bool restart = goal is null || g.Kind != kind || g.Category != category || g.From is null;
            g.Kind = kind;
            g.Category = category;
            if (restart) g.From = data.Rating(kind, category);
            g.Target = number?.Value ?? target;
            g.Car = car;
            g.Track = track;
            g.ThisSeason = season;
            g.Clean();
            if (goal is null) { g.CreatedUtc = DateTime.UtcNow; service.Settings.Goals.Add(g); }
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
        var current = data.Rating(kind, data.Categories.FirstOrDefault()) ?? data.Goals.FirstOrDefault(g => g.Goal.Kind == kind)?.Current;
        return kind switch
        {
            GoalMetric.IRating => Math.Round(((current ?? 1350) + 250) / 50) * 50,
            GoalMetric.SafetyRating => Math.Min(4.99, Math.Round((current ?? 2.5) + 0.5, 2)),
            _ when next is { } n => n,
            GoalMetric.DrivingHours => 50,
            GoalMetric.Laps => 1000,
            GoalMetric.Races => 25,
            _ => 5,
        };
    }
}
