using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Core.Controls;
using Core.Theme;

namespace Core.Views;

internal static class ThemePreview
{
    private const double PreviewWidth = 290, SideBySideFrom = 640, Gap = 14;

    public static Grid Beside(FrameworkElement picker, string themeName)
    {
        var preview = Build(themeName);
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(), new ColumnDefinition { Width = new GridLength(0) } },
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition { Height = GridLength.Auto } },
        };
        grid.Children.Add(picker);
        grid.Children.Add(preview);
        void Place(double width)
        {
            bool side = width >= SideBySideFrom;
            grid.ColumnDefinitions[1].Width = side ? new GridLength(PreviewWidth + Gap) : new GridLength(0);
            Grid.SetColumn(preview, side ? 1 : 0);
            Grid.SetRow(preview, side ? 0 : 1);
            preview.Margin = side ? new Thickness(Gap, 0, 0, 0) : new Thickness(0, 10, 0, 0);
        }
        Place(0);
        grid.SizeChanged += (_, e) => { if (e.WidthChanged) Place(e.NewSize.Width); };
        return grid;
    }

    private static Tile Build(string themeName)
    {
        var stack = new StackPanel();

        var top = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        top.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = themeName, Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis }));
        stack.Children.Add(top);

        var lap = new Grid { ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition(), new ColumnDefinition { Width = GridLength.Auto } } };
        var lapLabel = new TextBlock { Text = "Last lap", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
        var lapTime = new TextBlock { Text = "1:47.382", Style = Ui.Res<Style>("H2"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var lapDelta = new TextBlock { Text = "−0.214", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("SageDeep"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lapTime, 1);
        Grid.SetColumn(lapDelta, 2);
        lap.Children.Add(lapLabel);
        lap.Children.Add(lapTime);
        lap.Children.Add(lapDelta);
        stack.Children.Add(new Tile { Background = Ui.Res<Brush>("Surface"), BorderBrush = Ui.Res<Brush>("Line"), BorderThickness = Hairline.Edges(), CornerRadius = (CornerRadius)Application.Current.FindResource("R.Small"), Padding = new Thickness(10, 6, 10, 6), Child = lap });

        var buttons = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        foreach (var (text, style) in new[] { ("Apply", "PrimaryButton"), ("Cancel", "GhostButton"), ("Delete", "DangerButton") })
            buttons.Children.Add(new Button { Content = text, Style = Ui.Res<Style>(style), IsHitTestVisible = false, Focusable = false, Margin = Ui.AfterButton });
        stack.Children.Add(buttons);

        var chips = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        foreach (var (text, soft, deep) in new[] { ("Auto", "AccentSoft", "AccentText"), ("Connected", "SageSoft", "SageDeep"), ("Pit window", "WarnSoft", "WarnDeep"), ("Fan stopped", "DangerSoft", "DangerDeep") })
            chips.Children.Add(Ui.Chip(text, soft, deep));
        stack.Children.Add(chips);

        var bar = new UniformGrid { Rows = 1, Height = 14, Margin = new Thickness(0, 4, 0, 0) };
        foreach (var key in new[] { "Bg", "LineStrong", "Text", "Accent", "ActiveModeFg", "Sage", "Warn", "Danger" })
            bar.Children.Add(new Rectangle { Fill = Ui.Res<Brush>(key) });
        stack.Children.Add(new Tile { CornerRadius = (CornerRadius)Application.Current.FindResource("R.Tiny"), ClipToBounds = true, Child = bar });

        return new Tile
        {
            Background = Ui.Res<Brush>("Bg"), BorderBrush = Ui.Res<Brush>("LineStrong"), BorderThickness = Hairline.Edges(),
            CornerRadius = (CornerRadius)Application.Current.FindResource("R.Inset"), Padding = new Thickness(12), VerticalAlignment = VerticalAlignment.Top,
            Child = stack,
        };
    }
}
