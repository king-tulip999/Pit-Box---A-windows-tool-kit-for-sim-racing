using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Core.Controls;
using Core.SessionStats;
using Core.Theme;

namespace Core.Views.Pages;

internal static class StatsSessionTools
{
    public static Tile? Sectors(IReadOnlyList<LapRow> laps)
    {
        var lines = SectorReport.Make(laps);
        if (lines.Count == 0 || SectorReport.Total(laps) is not { } total) return null;
        int? worst = SectorReport.MostToFind(lines);
        double maxLost = Math.Max(0.001, lines.Max(l => l.Lost));
        const int columns = 7;
        var table = new Grid();
        for (int c = 0; c < columns; c++) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions[^1].Width = new GridLength(1, GridUnitType.Star);
        table.ColumnDefinitions[^1].MinWidth = 80;
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell(table, 0, 1, Head("Best"), true);
        Cell(table, 0, 2, Head("On lap"), true);
        Cell(table, 0, 3, Head("Average"), true);
        Cell(table, 0, 4, Head("Spread", "Standard deviation over the clean laps"), true);
        var leftHead = Head("Left on the table", "Your average less your best");
        Grid.SetColumnSpan(leftHead, 2);
        Cell(table, 0, 5, leftHead);
        foreach (var line in lines)
        {
            int row = line.Index + 1;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            bool focus = worst == line.Index;
            var rule = Hairline.Frame(new Border(), Sides.Top);
            rule.SetResourceReference(Border.BorderBrushProperty, "Line");
            if (focus) rule.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            Span(table, rule, row);
            if (focus)
            {
                var stripe = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
                stripe.SetResourceReference(Border.BackgroundProperty, "Accent");
                Span(table, stripe, row);
            }
            Cell(table, row, 0, Label($"S{line.Index + 1}"));
            Cell(table, row, 1, Best(line.Best.ToString("0.000", CultureInfo.CurrentCulture)), true);
            Cell(table, row, 2, Number(line.BestLap.ToString(CultureInfo.CurrentCulture), "Caption"), true);
            Cell(table, row, 3, Number(line.Average.ToString("0.000", CultureInfo.CurrentCulture)), true);
            Cell(table, row, 4, Number("±" + line.Spread.ToString("0.000", CultureInfo.CurrentCulture), "Caption"), true);
            var lost = Number(LapReplayView.Signed(line.Lost, "0.000"));
            if (focus)
            {
                lost.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
                lost.FontWeight = FontWeights.SemiBold;
            }
            Cell(table, row, 5, lost, true);
            Cell(table, row, 6, Bar(Math.Max(0, line.Lost) / maxLost, focus ? "Accent" : "LineStrong"));
        }
        int idealRow = lines.Count + 1, bestRow = lines.Count + 2;
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var totalRule = new Border { BorderThickness = new Thickness(0, 2, 0, 0) };
        totalRule.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        Span(table, totalRule, idealRow);
        Cell(table, idealRow, 0, Label("Ideal"));
        Cell(table, idealRow, 1, Best(StatFormat.LapTime(total.Ideal)), true);
        Cell(table, idealRow, 2, Number(SimRacing.Race.RaceFormat.Missing, "Caption"), true);
        Cell(table, idealRow, 3, Number(StatFormat.LapTime(total.Average)), true);
        Cell(table, idealRow, 4, Number("±" + total.Spread.ToString("0.000", CultureInfo.CurrentCulture), "Caption"), true);
        Cell(table, idealRow, 5, Number(LapReplayView.Signed(total.Lost, "0.000")), true);
        Cell(table, idealRow, 6, Faint("Sum of best sectors"));
        var bestRule = Hairline.Frame(new Border(), Sides.Top);
        bestRule.SetResourceReference(Border.BorderBrushProperty, "Line");
        Span(table, bestRule, bestRow);
        Cell(table, bestRow, 0, Label("Best lap"));
        Cell(table, bestRow, 1, Number(StatFormat.LapTime(total.BestTime)), true);
        Cell(table, bestRow, 2, Number(total.BestLap.ToString(CultureInfo.CurrentCulture), "Caption"), true);
        Cell(table, bestRow, 3, Number(SimRacing.Race.RaceFormat.Missing), true);
        Cell(table, bestRow, 4, Number(SimRacing.Race.RaceFormat.Missing, "Caption"), true);
        Cell(table, bestRow, 5, Number(LapReplayView.Signed(total.BestToIdeal, "0.000")), true);
        Cell(table, bestRow, 6, Faint("To the ideal lap"));
        return StatsSections.Section("Sectors", SimRacing.Race.RaceFormat.Count(lines[0].Laps, "clean lap"), null, table);
    }

    private static TextBlock Head(string text, string? tip = null)
    {
        return Ui.TableHead(text, tip);
    }

    private static TextBlock Label(string text) =>
        new() { Text = text, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold };

    private static TextBlock Best(string text)
    {
        var t = Number(text);
        t.SetResourceReference(TextBlock.ForegroundProperty, "TimingBest");
        return t;
    }

    private static TextBlock Faint(string text)
    {
        var t = new TextBlock { Text = text, Style = Ui.Res<Style>("Caption") };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        return t;
    }

    private static Grid Bar(double share, string brush)
    {
        var grid = new Grid { Height = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(share, 0.005), GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - share, 0), GridUnitType.Star) });
        var fill = new Border();
        fill.SetResourceReference(Border.BackgroundProperty, brush);
        grid.Children.Add(fill);
        grid.HorizontalAlignment = HorizontalAlignment.Stretch;
        return grid;
    }

    private static void Span(Grid table, Border element, int row)
    {
        Grid.SetRow(element, row);
        Grid.SetColumnSpan(element, table.ColumnDefinitions.Count);
        table.Children.Add(element);
    }

    public static FrameworkElement TyreRun(IReadOnlyList<TyreReading> readings)
    {
        var body = new StackPanel { ToolTip = "Measured by iRacing when the car stops in its pit stall: tread left on each tire (left / middle / right)" };
        var (sageSoft, sageDeep) = StatsUi.Hue("Sage");
        var (goldSoft, goldDeep) = StatsUi.Hue("Gold");
        var (coralSoft, coralDeep) = StatsUi.Hue("Coral");
        foreach (var r in readings)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, body.Children.Count > 0 ? 8 : 0, 0, 8) };
            var glyph = new Icon { Data = Ui.Res<Geometry>("I.Wheel"), Width = 13, Height = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            glyph.SetResourceReference(Control.ForegroundProperty, "AccentText");
            head.Children.Add(glyph);
            head.Children.Add(new TextBlock
            {
                Text = $"Tires after lap {r.Lap}" + (r.Laps > 0 ? $" · {SimRacing.Race.RaceFormat.Count(r.Laps, "lap")} on this set" : ""),
                Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center,
            });
            body.Children.Add(head);
            var grid = new UniformGrid { Columns = 2 };
            for (int c = 0; c < 4; c++)
            {
                double low = r.Lowest(c);
                var (soft, deep) = low >= 0.9 ? (sageSoft, sageDeep) : low >= 0.75 ? (goldSoft, goldDeep) : (coralSoft, coralDeep);
                var tile = Hairline.Frame(new Tile { CornerRadius = ThemeManager.Round(4), Padding = new Thickness(11, 10, 11, 10) });
                tile.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
                tile.SetResourceReference(Border.BorderBrushProperty, "Line");
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = TyreReading.Corners[c], Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                var big = new TextBlock { Text = $"{low * 100:0.0}%", Style = Ui.Res<Style>("Body"), FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0) };
                big.SetResourceReference(TextBlock.ForegroundProperty, deep);
                big.ToolTip = "Tread left at its most worn point";
                stack.Children.Add(big);
                var zones = new UniformGrid { Columns = 3, Margin = new Thickness(0, 6, 0, 0) };
                string[] names = { "Left", "Mid", "Right" };
                for (int z = 0; z < 3; z++)
                {
                    double left = r.Left(c, z);
                    var fill = new Border { Height = 4, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
                    fill.SetResourceReference(Border.BackgroundProperty, deep);
                    var track = new Border { Height = 4, Child = fill, Margin = new Thickness(0, 3, 0, 0) };
                    track.SetResourceReference(Border.BackgroundProperty, soft);
                    track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Clamp(left, 0, 1);
                    var cell = new StackPanel { ToolTip = $"{names[z]}: {left * 100:0.0}% left", Margin = new Thickness(0, 0, z < 2 ? 8 : 0, 0) };
                    var zone = new TextBlock { Text = names[z], Style = Ui.Res<Style>("Caption"), FontSize = 12 };
                    zone.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
                    cell.Children.Add(zone);
                    cell.Children.Add(Number($"{left * 100:0.0}", "Caption"));
                    cell.Children.Add(track);
                    zones.Children.Add(cell);
                }
                stack.Children.Add(zones);
                if (r.UsedPerLap(c) is { } perLap)
                    stack.Children.Add(new TextBlock { Text = $"{perLap * 100:0.00}% a lap", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
                tile.Child = stack;
                grid.Children.Add(tile);
            }
            for (int i = 0; i < grid.Children.Count; i++)
                ((FrameworkElement)grid.Children[i]).Margin = new Thickness(0, 0, i % 2 == 0 ? 6 : 0, i < 2 ? 6 : 0);
            body.Children.Add(grid);
        }
        var legend = new TextBlock { Text = "Green = 90% or more left, amber = 75% or more, red = less", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        legend.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        body.Children.Add(legend);
        return body;
    }

    private static TextBlock Number(string text, string style = "Body")
    {
        var t = new TextBlock { Text = text, Style = Ui.Res<Style>(style) };
        Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    private static void Cell(Grid table, int row, int col, FrameworkElement element, bool right = false)
    {
        element.Margin = new Thickness(col == 0 ? 6 : 13, 7, 0, 7);
        element.VerticalAlignment = VerticalAlignment.Center;
        if (right) element.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetRow(element, row);
        Grid.SetColumn(element, col);
        table.Children.Add(element);
    }
}
