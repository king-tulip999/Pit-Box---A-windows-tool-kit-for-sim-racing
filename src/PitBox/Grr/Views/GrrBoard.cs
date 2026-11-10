using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.SimRacing.Race;
using PitBox.Theme;
using PitBox.Views;

namespace PitBox.Grr.Views;

internal static class GrrBoard
{
    public const double StepGap = 8, LeaderShare = 1.12;

    public sealed record Step(int Place, string Name, string? Sub, string Points, string? Aside, IReadOnlyList<FrameworkElement> Tags, bool Mine);

    public static Grid Podium(IReadOnlyList<Step> steps)
    {
        var grid = new Grid();
        int[] order = { 2, 1, 3 };
        for (int c = 0; c < order.Length; c++)
        {
            if (c > 0) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(StepGap) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(order[c] == 1 ? LeaderShare : 1, GridUnitType.Star) });
        }
        for (int c = 0; c < order.Length; c++)
        {
            if (steps.FirstOrDefault(s => s.Place == order[c]) is not { } step) continue;
            var tile = StepTile(step);
            Grid.SetColumn(tile, c * 2);
            grid.Children.Add(tile);
        }
        return grid;
    }

    private static Border StepTile(Step s)
    {
        bool leader = s.Place == 1;
        var body = new StackPanel();
        var top = new DockPanel { LastChildFill = false };
        if (s.Mine)
        {
            var you = Ui.Chip("You", "AccentSoft", "AccentText");
            you.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(you, Dock.Right);
            top.Children.Add(you);
        }
        var place = RaceParts.Big(s.Place.ToString(), leader ? 40 : 32, $"Podium{s.Place}Fg");
        RaceParts.FigureLines(place, leader ? 40 : 32);
        top.Children.Add(place);
        body.Children.Add(top);

        body.Children.Add(TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = s.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>(s.Mine ? "AccentText" : "Heading"),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0),
        }));
        body.Children.Add(GrrParts.Caption(s.Sub ?? "", 1));

        var points = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 6, 0, 0) };
        if (s.Aside is { } aside)
        {
            var right = GrrParts.Caption(aside);
            right.VerticalAlignment = VerticalAlignment.Bottom;
            right.Margin = new Thickness(6, 0, 0, GrrParts.BaselineBottom(20, 12, figureLines: true));
            right.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            DockPanel.SetDock(right, Dock.Right);
            points.Children.Add(right);
        }
        var figure = RaceParts.LiveFigure(s.Points, 20, "Heading");
        figure.HorizontalAlignment = HorizontalAlignment.Left;
        points.Children.Add(figure);
        body.Children.Add(points);

        var tags = new WrapPanel { MinHeight = 24, Margin = new Thickness(0, 6, 0, 0) };
        foreach (var t in s.Tags)
        {
            t.Margin = new Thickness(0, 0, 5, 4);
            tags.Children.Add(t);
        }
        body.Children.Add(tags);

        var tile = new Border
        {
            Background = Ui.Res<Brush>($"Podium{s.Place}Bg"), BorderBrush = Ui.Res<Brush>($"Podium{s.Place}Edge"), BorderThickness = new Thickness(1.5),
            CornerRadius = ThemeManager.Round(8), Padding = leader ? new Thickness(12, 18, 12, 8) : new Thickness(12, 10, 12, 8),
            VerticalAlignment = VerticalAlignment.Bottom, Child = body,
        };
        System.Windows.Automation.AutomationProperties.SetName(tile, $"P{s.Place} {s.Name}, {s.Points}");
        return tile;
    }

    public static Border Tag(string text, string? soft = null, string? deep = null)
    {
        var chip = Ui.Chip(text, soft, deep);
        chip.BorderThickness = new Thickness(0);
        if (soft is null) chip.Background = Ui.Res<Brush>("TileRaised");
        return chip;
    }

    public static Border Strip(UIElement child) => Hairline.Frame(new Border
    {
        Background = Ui.Res<Brush>("SurfaceAlt"), BorderBrush = Ui.Res<Brush>("Line"), CornerRadius = ThemeManager.Round(8),
        Padding = new Thickness(12, 9, 12, 9), Child = child,
    });

    public static Border TitleFight(GrrTitleFight fight, IReadOnlyList<GrrRound> schedule, DateTime now)
    {
        var dock = new DockPanel { LastChildFill = true };
        if (schedule.Count is > 1 and <= 40)
        {
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            int next = schedule.ToList().FindIndex(r => r.Start + GrrService.RaceLength > now);
            double size = schedule.Count > 20 ? 6 : 9;
            for (int i = 0; i < schedule.Count; i++)
                dots.Children.Add(new Border
                {
                    Width = size, Height = size, Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0), CornerRadius = ThemeManager.Round(2),
                    Background = Ui.Res<Brush>(i == next ? "Accent" : next < 0 || i < next ? "LineStrong" : "Line"),
                });
            dots.ToolTip = next < 0 ? $"All {RaceFormat.Count(schedule.Count, "round")} run" : $"Round {next + 1} of {schedule.Count} is next";
            DockPanel.SetDock(dots, Dock.Right);
            dock.Children.Add(dots);
        }
        var line = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var label = GrrParts.Eyebrow(fight.Decided ? "Champion" : "Title fight");
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Margin = new Thickness(0, 0, 10, 0);
        line.Children.Add(label);
        var text = new TextBlock { FontSize = 12, Foreground = Ui.Res<Brush>("Text"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        void Plain(string t) => text.Inlines.Add(new Run(t));
        void Strong(string t) => text.Inlines.Add(new Run(t) { FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("Heading") });
        if (fight.Lead <= 0 && fight.Second is { } second)
        {
            Strong(fight.Leader); Plain(" and "); Strong(second); Plain(" level on points");
        }
        else
        {
            Strong(fight.Leader);
            Plain(fight.Decided ? " wins the title by " : fight.Clinched ? " has clinched the title, " : " leads by ");
            Strong(GrrParts.Points(fight.Lead));
            if (fight.Clinched) Plain(" clear");
        }
        if (!fight.Decided && !fight.Clinched)
        {
            if (fight.OnOffer is { } offer) { Plain(" · "); Strong(GrrParts.Points(offer)); Plain(" still on offer"); }
            else if (fight.RoundsLeft > 0) Plain(" · " + RaceFormat.Count(fight.RoundsLeft, "round") + " left");
        }
        line.Children.Add(text);
        dock.Children.Add(line);
        return Strip(dock);
    }

    public static StackPanel Form(IReadOnlyList<GrrFormRound> recent, int slots)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        for (int i = 0; i < slots - recent.Count; i++) row.Children.Add(FormChip(null, null));
        foreach (var f in recent) row.Children.Add(FormChip(f.Finish, $"{(f.Number is { } n ? $"Round {n}" : "Round")} · {f.Track} · {(f.Finish is { } p ? $"P{p}" : "no result")}"));
        return row;
    }

    private static Border FormChip(int? finish, string? tip)
    {
        int? podium = finish is >= 1 and <= 3 ? finish : null;
        var text = new TextBlock
        {
            Text = finish?.ToString() ?? (tip is null ? "" : RaceFormat.Missing), FontSize = 10, FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Ui.Res<Brush>(podium is { } p ? $"Podium{p}Fg" : finish <= 5 ? "Text" : "TextMuted"),
        };
        text.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
        var chip = Hairline.Frame(new Border
        {
            Width = 20, Height = 20, Margin = new Thickness(0, 0, 3, 0), CornerRadius = ThemeManager.Round(4),
            Background = Ui.Res<Brush>(podium is { } b ? $"Podium{b}Bg" : tip is null ? "SurfaceAlt" : "TileRaised"),
            BorderBrush = podium is { } e ? Ui.Res<Brush>($"Podium{e}Edge") : Brushes.Transparent,
            Child = new Viewbox { Child = text, StretchDirection = StretchDirection.DownOnly, Margin = new Thickness(1, 0, 1, 0) }, ToolTip = tip,
        });
        return chip;
    }

    public static FrameworkElement Move(int? move, string? tip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, ToolTip = tip, Background = Brushes.Transparent };
        if (move is not { } m) return row;
        string brush = m > 0 ? "Sage" : m < 0 ? "Coral" : "TextFaint";
        row.Children.Add(new Icon
        {
            Data = Ui.Res<Geometry>(m > 0 ? "I.ChevronUp" : m < 0 ? "I.ChevronDown" : "I.Minus"), Width = 10, Height = 10, StrokeThickness = 2.6,
            Foreground = Ui.Res<Brush>(brush), VerticalAlignment = VerticalAlignment.Center,
        });
        if (m != 0)
            row.Children.Add(new TextBlock { Text = Math.Abs(m).ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>(brush), Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    public static string MoveTip(int move, int? sinceRound) =>
        (move > 0 ? $"Up {RaceFormat.Count(move, "place")}" : move < 0 ? $"Down {RaceFormat.Count(-move, "place")}" : "No change")
        + (sinceRound is { } r ? $" since round {r}" : " since the last round");

    public static FrameworkElement Gain(int? grid, int finish)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent };
        if (grid is not { } g)
        {
            row.Children.Add(GrrParts.Caption(RaceFormat.Missing));
            return row;
        }
        var from = GrrParts.Caption($"P{g}");
        from.VerticalAlignment = VerticalAlignment.Center;
        from.Margin = new Thickness(0, 0, 4, 0);
        row.Children.Add(from);
        row.Children.Add(Move(g - finish, null));
        row.ToolTip = $"Started P{g}, finished P{finish}";
        return row;
    }

    public static FrameworkElement Bar(double share)
    {
        share = double.IsFinite(share) ? Math.Clamp(share, 0, 1) : 0;
        var track = new Grid { Height = 3, Margin = new Thickness(0, 4, 0, 0) };
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(share, GridUnitType.Star) });
        track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - share, GridUnitType.Star) });
        var back = new Border { Background = Ui.Res<Brush>("Line"), CornerRadius = ThemeManager.Round(1.5) };
        Grid.SetColumnSpan(back, 2);
        track.Children.Add(back);
        if (share > 0) track.Children.Add(new Border { Background = Ui.Res<Brush>("LineStrong"), CornerRadius = ThemeManager.Round(1.5) });
        return track;
    }

    public static UniformGrid Awards(IReadOnlyList<GrrAward> awards, int columns)
    {
        var grid = new UniformGrid { Columns = Math.Max(1, Math.Min(columns, awards.Count)), Margin = new Thickness(-StepGap / 2) };
        foreach (var a in awards)
        {
            var (icon, name) = a.Kind switch
            {
                GrrAwardKind.HardCharger => ("I.TrendingUp", "Hard charger"),
                GrrAwardKind.MostLed => ("I.Flag", "Most laps led"),
                GrrAwardKind.Cleanest => ("I.Shield", "Cleanest"),
                _ => ("I.Clock", "Fastest lap"),
            };
            var body = new StackPanel();
            var head = new DockPanel { LastChildFill = true };
            var glyph = new Icon { Data = Ui.Res<Geometry>(icon), Width = 12, Height = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(Control.ForegroundProperty, "EyebrowText");
            DockPanel.SetDock(glyph, Dock.Left);
            head.Children.Add(glyph);
            var label = GrrParts.Eyebrow(name);
            label.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(label);
            body.Children.Add(head);
            body.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = a.Driver, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("Heading"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) }));
            body.Children.Add(GrrParts.Caption(a.Detail));
            grid.Children.Add(new Border
            {
                Background = Ui.Res<Brush>("TileRaised"), CornerRadius = ThemeManager.Round(8), Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(StepGap / 2), Child = body,
            });
        }
        return grid;
    }

    public static Border EventHeader(int? round, string track, string details, TrackOutline? outline)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (round is { } n)
        {
            var badge = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badge.Children.Add(new TextBlock { Text = "Round", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Ui.Res<Brush>("TextMuted"), HorizontalAlignment = HorizontalAlignment.Center });
            var number = RaceParts.LiveFigure(n.ToString(), 22, "Heading");
            number.HorizontalAlignment = HorizontalAlignment.Center;
            badge.Children.Add(number);
            grid.Children.Add(Hairline.Frame(new Border
            {
                MinWidth = 48, Height = 48, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(0, 0, 13, 0), CornerRadius = ThemeManager.Round(8),
                BorderBrush = Ui.Res<Brush>("LineStrong"), Child = badge, VerticalAlignment = VerticalAlignment.Center,
            }));
        }
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = track, Style = Ui.Res<Style>("H2"), TextTrimming = TextTrimming.CharacterEllipsis }));
        var sub = GrrParts.Caption(details, 2);
        text.Children.Add(sub);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (outline is not null)
        {
            outline.Width = 116;
            outline.Height = 50;
            outline.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(outline, 2);
            grid.Children.Add(outline);
        }
        return Strip(grid);
    }

    public static Border Divider(string text)
    {
        var dock = new DockPanel { LastChildFill = true, Margin = new Thickness(6, 8, 6, 2) };
        var label = GrrParts.Eyebrow(text, "TextFaint");
        label.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(label, Dock.Left);
        dock.Children.Add(label);
        dock.Children.Add(Hairline.Rule(new Border { Background = Ui.Res<Brush>("LineStrong"), VerticalAlignment = VerticalAlignment.Center }));
        return new Border { Child = dock };
    }
}
