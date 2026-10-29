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
        if (_app.Config.Ui.Theme != AppTheme.Custom) return;

        Children.Add(new TextBlock { Text = "Colors", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        foreach (var family in CustomPalette.Families)
        {
            Children.Add(new TextBlock { Text = family, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 8, 0, 4) });
            var row = new WrapPanel();
            foreach (var (name, _, colors) in CustomPalette.Presets.Where(p => p.Family == family))
                row.Children.Add(Swatch(name, colors));
            Children.Add(row);
        }
        Children.Add(new Border { Height = 8 });
    }

    private bool Picked(string name) => C.Enabled ? string.Equals(C.Name, name, StringComparison.OrdinalIgnoreCase) : name == CustomPalette.NameOf(ThemeColors.Default);

    private Button Swatch(string name, ThemeColors colors)
    {
        var c = CustomPalette.Build(colors);
        Brush Of(string key) => new SolidColorBrush(c[key]);
        bool selected = Picked(name);

        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var key in new[] { "Heading", "ActiveModeFg" })
            dots.Children.Add(new Rectangle { Width = 6, Height = 6, Fill = Of(key), Margin = new Thickness(0, 0, 3, 0) });
        var bar = new Rectangle { Height = 6, Fill = Of("Accent"), VerticalAlignment = VerticalAlignment.Bottom };

        var button = new Button
        {
            Style = Ui.Res<Style>("Btn"), Width = 32, Height = 28, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(3),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
            Background = Of("Bg"), BorderBrush = selected ? Ui.Res<Brush>("Accent") : Ui.Res<Brush>("LineStrong"),
            BorderThickness = new Thickness(selected ? 2 : 1), Content = new Grid { Children = { dots, bar } }, ToolTip = name,
        };
        button.MouseEnter += (_, _) => button.BorderBrush = Ui.Res<Brush>("Accent");
        button.MouseLeave += (_, _) => button.BorderBrush = selected ? Ui.Res<Brush>("Accent") : Ui.Res<Brush>("LineStrong");
        System.Windows.Automation.AutomationProperties.SetName(button, name + (selected ? " (selected)" : ""));
        button.Click += (_, _) =>
        {
            if (!selected) _app.UseOledColors(name);
        };
        return button;
    }
}
