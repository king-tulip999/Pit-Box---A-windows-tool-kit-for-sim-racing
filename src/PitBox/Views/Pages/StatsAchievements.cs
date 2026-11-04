using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.SessionStats;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed record AchievementsData(RaceStreaks Streaks, List<AchievementState> Achievements)
{
    public static AchievementsData Read(StatsStore store)
    {
        var streaks = RaceStreakReader.Read(store);
        return new(streaks, SessionStats.Achievements.Evaluate(SessionStats.Achievements.Read(store, streaks)));
    }
}

internal static class StatsAchievements
{
    public static FrameworkElement Card(AchievementsData data)
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

        var card = StatsSections.Section("Streaks and achievements", null, null,
            StatsSections.Stack(
                StatsSections.Chart("Streaks", "Official and unofficial races, all time", streaks),
                StatsSections.Chart("Achievements", $"All time · {earned} of {SessionStats.Achievements.TierCount} earned", badges)));
        Ui.Anchor(card, "stats.achievements");
        return card;
    }

    private static FrameworkElement Bar(double fraction, string brush)
    {
        double done = Math.Clamp(fraction, 0, 1);
        var bar = new Grid { Height = 3, Margin = new Thickness(0, 7, 12, 0) };
        bar.SetResourceReference(Panel.BackgroundProperty, "MeterTrack");
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(done, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - done, GridUnitType.Star) });
        var fill = new Border();
        fill.SetResourceReference(Border.BackgroundProperty, brush);
        bar.Children.Add(fill);
        return bar;
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
        var meter = Bar(state.Fraction, all ? gold : "Accent");
        var stack = new StackPanel { Children = { pips, value, label, line, meter } };
        string tiers = string.Join(", ", family.Tiers.Select(t => SessionStats.Achievements.Format(family, t)));
        var cell = Hairline.Frame(new Border { Padding = new Thickness(11, 2, 10, 2), Child = stack, ToolTip = $"{family.Name}: {tiers}" }, Sides.Left);
        cell.SetResourceReference(Border.BorderBrushProperty, "Line");
        return cell;
    }
}
