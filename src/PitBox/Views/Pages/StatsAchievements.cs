using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.SessionStats;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed record AchievementsData(RaceStreaks Streaks, List<AchievementState> Achievements, List<AchievementState> Season, DateTime SeasonStartUtc)
{
    public static AchievementsData Read(StatsStore store)
    {
        var now = DateTime.UtcNow;
        var races = SessionStats.Achievements.ReadRaces(store);
        var all = SessionStats.Achievements.Read(store, races, now);
        var seasonStart = TimeRanges.SeasonStart(now, store.LatestOfficialWeek());
        return new(all.Streaks, SessionStats.Achievements.Evaluate(all),
            SessionStats.Achievements.Evaluate(SessionStats.Achievements.SeasonGoals, SessionStats.Achievements.ForSeason(races, seasonStart, now)), seasonStart);
    }
}

internal static class StatsAchievements
{
    public static FrameworkElement Card(AchievementsData data)
    {
        var s = data.Streaks;
        string Run(Streak k, string noun) => SimRacing.Race.RaceFormat.Count(k.Current, noun);
        (StatDef, StatResult) M(string id, string label, Streak k, string noun, string help) =>
            (new StatDef("streak." + id, StatRegistry.Racing, label, StatKind.Text) { Help = help },
                s.Races == 0 ? StatResult.None : new StatResult(Run(k, noun), "best " + Run(new Streak(k.Best, k.Best), noun)));
        const string upToLatest = "Races in a row, up to your latest";
        var streaks = StatsSections.Metrics(new[]
        {
            M("clean", "Clean races in a row", s.Clean, "race", upToLatest),
            M("finish", "Finishes in a row", s.Finished, "race", upToLatest),
            M("top5", "Top fives in a row", s.TopFive, "race", upToLatest),
            M("weeks", "Race weeks in a row", s.Weeks, "week", "Race weeks start Tuesday 00:00 UTC; ends when a week goes by without a race"),
            M("irating", "iRating gains in a row", s.IRating, "race", "Official races only"),
        })!;

        int earned = data.Achievements.Sum(a => a.Earned);
        int seasonEarned = data.Season.Sum(a => a.Earned), seasonTiers = data.Season.Sum(a => a.Family.Tiers.Length);
        var parts = new List<FrameworkElement?>
        {
            StatsSections.Chart("Streaks", "Official and league races, all time", streaks),
            StatsSections.Chart("This season", $"Since {Services.Dates.Day(data.SeasonStartUtc.ToLocalTime())} · {seasonEarned} of {seasonTiers} earned · starts over each season", Badges(data.Season)),
        };
        foreach (var group in SessionStats.Achievements.Groups)
        {
            var states = data.Achievements.Where(a => a.Family.Group == group).ToList();
            string caption = group == SessionStats.Achievements.Groups[0]
                ? $"All time · {earned} of {SessionStats.Achievements.TierCount} earned · official and league races"
                : $"{states.Sum(a => a.Earned)} of {states.Sum(a => a.Family.Tiers.Length)} earned";
            parts.Add(StatsSections.Chart(group, caption, Badges(states)));
        }

        var card = StatsSections.Section("Streaks and achievements", null, null, StatsSections.Stack(parts.ToArray()));
        Ui.Anchor(card, "stats.achievements");
        return card;
    }

    private static StatGrid Badges(IEnumerable<AchievementState> states)
    {
        var grid = new StatGrid { MinTileWidth = 150, Gap = 0, RowGap = 13 };
        foreach (var state in states) grid.Children.Add(Badge(state));
        return grid;
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
            FontSize = 20, FontWeight = FontWeights.ExtraBold };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        value.SetResourceReference(TextBlock.ForegroundProperty, all ? gold : state.Earned > 0 ? "Heading" : "TextFaint");
        var label = new TextBlock { Text = family.Name, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        string detail = state.Value is null ? "No data" : all ? $"All {family.Tiers.Length} earned" : "Next " + SessionStats.Achievements.Target(family, state.Next!.Value);
        var line = new TextBlock { Text = detail, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        line.SetResourceReference(TextBlock.ForegroundProperty, all ? gold : "TextFaint");
        var meter = Bar(state.Fraction, all ? gold : "Accent");
        var stack = new StackPanel { Children = { pips, value, label, line, meter } };
        string tiers = string.Join(", ", family.Tiers.Select(t => SessionStats.Achievements.Format(family, t)));
        var cell = Hairline.Frame(new Border { Padding = new Thickness(11, 2, 10, 2), Child = stack, ToolTip = family.Help is { } help ? $"{family.Name}: {tiers}\n{help}" : $"{family.Name}: {tiers}" }, Sides.Left);
        cell.SetResourceReference(Border.BorderBrushProperty, "Line");
        return cell;
    }
}
