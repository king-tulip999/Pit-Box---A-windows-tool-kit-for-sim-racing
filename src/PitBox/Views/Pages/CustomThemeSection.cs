using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PitBox.Config;
using PitBox.Services;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed class CustomThemeSection : StackPanel
{
    private readonly AppServices _app;

    private CustomThemeSettings C => _app.Config.Ui.OledCustom;

    public CustomThemeSection(AppServices app)
    {
        _app = app;
        if (_app.Config.Ui.Theme == AppTheme.Custom)
            Build("Colors", CustomPalette.Families, CustomPalette.Presets, CustomPalette.Build, PickedColors, name => _app.UseOledColors(name));
        else if (ThemeSets.For(_app.Config.Ui.Theme) is { } set)
            Build(set.Title, set.Families, set.Presets, CustomPalette.BuildBrand, name => set.Current(_app.Config.Ui) == name, name => _app.UsePick(set, name));
    }

    private void Build(string title, string[] families, (string Name, string Family, ThemeColors Colors)[] presets,
        Func<ThemeColors, IReadOnlyDictionary<string, Color>> palette, Func<string, bool> picked, Action<string> use)
    {
        Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        foreach (var family in families)
        {
            Children.Add(new TextBlock { Text = family, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 8, 0, 4) });
            var row = new WrapPanel();
            foreach (var (name, _, colors) in presets.Where(p => p.Family == family))
                row.Children.Add(Swatch(name, palette(colors), picked(name), use));
            Children.Add(row);
        }
        Children.Add(new Border { Height = 8 });
    }

    private bool PickedColors(string name) => C.Enabled ? string.Equals(C.Name, name, StringComparison.OrdinalIgnoreCase) : name == CustomPalette.NameOf(ThemeColors.Default);

    private const double SwatchWidth = 108, SwatchHeight = 64, MarkHeight = 12;

    private static Button Swatch(string name, IReadOnlyDictionary<string, Color> c, bool selected, Action<string> use)
    {
        Brush Of(string key) => new SolidColorBrush(c[key]);

        var label = TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = ThemeMarks.ShortName(name), Foreground = Of("Heading"), FontSize = 11, LineHeight = 14, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 28, VerticalAlignment = VerticalAlignment.Bottom,
        });
        var bar = new Rectangle { Height = 3, Fill = Of("Accent"), Margin = new Thickness(0, 4, 0, 0) };
        var face = new Grid
        {
            RowDefinitions = { new RowDefinition { Height = new GridLength(MarkHeight) }, new RowDefinition(), new RowDefinition { Height = GridLength.Auto } },
        };
        face.Children.Add(Dots(c));
        Grid.SetRow(label, 1);
        face.Children.Add(label);
        Grid.SetRow(bar, 2);
        face.Children.Add(bar);

        var button = new Button
        {
            Style = Ui.Res<Style>("Btn"), Width = SwatchWidth, Height = SwatchHeight, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(6, 5, 6, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = Of("Bg"), BorderBrush = selected ? Ui.Res<Brush>("Accent") : Ui.Res<Brush>("LineStrong"),
            BorderThickness = new Thickness(selected ? 2 : 1), Content = face, ToolTip = name,
        };
        button.MouseEnter += (_, _) => button.BorderBrush = Ui.Res<Brush>("Accent");
        button.MouseLeave += (_, _) => button.BorderBrush = selected ? Ui.Res<Brush>("Accent") : Ui.Res<Brush>("LineStrong");
        System.Windows.Automation.AutomationProperties.SetName(button, name + (selected ? " (selected)" : ""));
        button.Click += (_, _) =>
        {
            if (!selected) use(name);
        };
        return button;
    }

    private static StackPanel Dots(IReadOnlyDictionary<string, Color> c)
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var key in new[] { "Accent", "ActiveModeFg", "Heading" })
            dots.Children.Add(new Rectangle { Width = 9, Height = 9, Fill = new SolidColorBrush(c[key]), Margin = new Thickness(0, 0, 4, 0) });
        return dots;
    }
}
