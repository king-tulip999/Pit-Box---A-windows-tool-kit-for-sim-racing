using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using PitBox.Config;
using PitBox.Theme;

namespace PitBox.Views;

internal static class ThemeMenu
{
    private static CustomThemeSettings? Oled => Services.AppServices.Current?.Config.Ui.OledCustom;

    public static void OpenOledMenu(FrameworkElement anchor, Action<string?> use)
    {
        var c = Oled;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MinWidth = Math.Max(220, anchor.ActualWidth) };
        Ui.DropBelow(menu);
        bool on = ThemeManager.Current.Id == AppTheme.Custom;
        var own = new MenuItem { Header = Header("Default", Themes.Custom), IsChecked = on && c?.Enabled != true };
        own.Click += (_, _) => use(null);
        menu.Items.Add(own);
        var item = new MenuItem();
        AddSets(item, name => on && c?.Enabled == true && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase), name => use(name));
        foreach (var family in item.Items.Cast<object>().ToList())
        {
            item.Items.Remove(family);
            menu.Items.Add(family);
        }
        menu.IsOpen = true;
    }

    public static void OpenSetMenu(FrameworkElement anchor, ThemeSet set, Action<string?> use)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MinWidth = Math.Max(220, anchor.ActualWidth) };
        Ui.DropBelow(menu);
        bool on = ThemeManager.Current.Id == set.Id;
        string? current = Services.AppServices.Current is { } app ? set.Current(app.Config.Ui) : null;
        var item = new MenuItem();
        AddPicks(item, set, name => on && string.Equals(current, name, StringComparison.OrdinalIgnoreCase), name => use(name));
        foreach (var family in item.Items.Cast<object>().ToList())
        {
            item.Items.Remove(family);
            menu.Items.Add(family);
        }
        menu.IsOpen = true;
    }

    private static void AddPicks(MenuItem into, ThemeSet set, Func<string, bool> isChecked, Action<string> pick)
    {
        if (into.Items.Count > 0) into.Items.Add(new Separator());
        foreach (var family in set.Families)
        {
            var group = new MenuItem { Header = family };
            foreach (var (name, _, _) in set.Presets.Where(p => p.Family == family))
            {
                var item = new MenuItem { Header = Header(name, Themes.ForSet(set.Id, name)), IsChecked = isChecked(name) };
                item.Click += (_, _) => pick(name);
                group.IsChecked |= item.IsChecked;
                group.Items.Add(item);
            }
            into.Items.Add(group);
        }
    }

    private static void AddSets(MenuItem into, Func<string, bool> isChecked, Action<string> pick)
    {
        into.Items.Add(new Separator());
        foreach (var family in CustomPalette.Families)
        {
            var group = new MenuItem { Header = family };
            foreach (var (name, _, _) in CustomPalette.Presets.Where(p => p.Family == family))
            {
                var item = new MenuItem { Header = Header(name, Themes.ForSet(AppTheme.Custom, name)), IsChecked = isChecked(name) };
                item.Click += (_, _) => pick(name);
                group.IsChecked |= item.IsChecked;
                group.Items.Add(item);
            }
            into.Items.Add(group);
        }
    }

    private static object Header(string text, ThemeSpec? spec)
    {
        if (spec is null) return text;
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Dots(spec));
        row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    public static FrameworkElement Dots(ThemeSpec spec)
    {
        var c = ThemeManager.Resolve(spec);
        Brush B(string key) => new SolidColorBrush(c.TryGetValue(key, out var v) ? v : Colors.Gray);
        var dots = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var key in new[] { "Accent", "ActiveModeFg", "Heading" })
            dots.Children.Add(new Ellipse { Width = 6, Height = 6, Margin = new Thickness(1, 0, 1, 0), Fill = B(key) });
        var chip = Hairline.Frame(new Border { Child = dots, Padding = new Thickness(2), CornerRadius = ThemeManager.Round(3), Background = B("Bg"), VerticalAlignment = VerticalAlignment.Center });
        chip.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        return chip;
    }
}
