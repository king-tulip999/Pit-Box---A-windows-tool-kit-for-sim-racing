using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Plugins;
using PitBox.Services;
using PitBox.Theme;
using PitBox.Views.Pages;

namespace PitBox.Views;

internal static class ToolChooser
{
    private const string OverlayColor = "Coral";

    internal static TextBlock Compact(TextBlock text)
    {
        text.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        text.FontWeight = FontWeights.Bold;
        TextCase.SetTight(text, true);
        TextCase.SetUpper(text, true);
        return text;
    }

    private static readonly string[] PcTools = { PluginIds.Overlay, PluginIds.Displays, PluginIds.WindowProfiles, PluginIds.FanControl };

    internal static FrameworkElement Build(AppServices app, Action changed)
    {
        var ordered = PluginManager.InNavOrder(app.Plugins.All, app.Config.Ui.NavOrder).ToList();
        var pc = ordered.Where(p => PcTools.Contains(p.Id)).OrderBy(p => Array.IndexOf(PcTools, p.Id)).ToList();
        var race = ordered.Except(pc).ToList();

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        Place(grid, GroupTitle("Sim racing"), 0, 0);
        Place(grid, GroupTitle("PC"), 0, 2);

        var left = new Grid();
        foreach (var plugin in race.OrderBy(p => p.Id == PluginIds.SimRacing ? 0 : 1))
        {
            var row = ToolRow(app, plugin, changed);
            Grid.SetRow(row, left.RowDefinitions.Count);
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(plugin.Id == PluginIds.SimRacing ? 2 : 1, GridUnitType.Star) });
            left.Children.Add(row);
        }
        Place(grid, left, 1, 0);

        var right = new System.Windows.Controls.Primitives.UniformGrid { Columns = 1 };
        foreach (var plugin in pc)
            right.Children.Add(ToolRow(app, plugin, changed));
        Place(grid, right, 1, 2);
        return grid;

        static void Place(Grid grid, UIElement element, int row, int column)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            grid.Children.Add(element);
        }
    }

    private static TextBlock GroupTitle(string text)
    {
        var title = Compact(new TextBlock { Text = text, Style = Ui.Res<Style>("Eyebrow"), FontSize = 14, Margin = new Thickness(2, 0, 0, 6) });
        title.Foreground = Ui.Res<Brush>("EyebrowText");
        return title;
    }

    private static FrameworkElement ToolRow(AppServices app, Plugin plugin, Action changed)
    {
        bool on = app.Plugins.IsEnabled(plugin);
        FrameworkElement? chips = null;
        if (plugin.Id == PluginIds.SimRacing)
        {
            var grid = new Grid { Margin = new Thickness(-3, 8, -3, -3) };
            for (int c = 0; c < ChipColumns * 2; c++)
                grid.ColumnDefinitions.Add(new ColumnDefinition());
            var all = SimRacingPage.ToolsInOrder(app);
            var tools = all.Where(t => !CompanionTools.Contains(t.Id)).Select(t => (Ids: new[] { t.Id }, Name: ChipNames.GetValueOrDefault(t.Id, t.Title),
                Note: t.Title + ": " + SettingsPage.PartNotes.GetValueOrDefault(t.Id, ""))).ToList();
            var companions = all.Where(t => CompanionTools.Contains(t.Id)).Select(t => t.Id).ToArray();
            if (companions.Length > 0)
                tools.Add((companions, "Companions","Companion apps: starts Trading Paints and Garage 61 by hand or with iRacing."));
            for (int i = 0; i < tools.Count; i++)
            {
                int row = i / ChipColumns, column = i % ChipColumns;
                int inRow = Math.Min(ChipColumns, tools.Count - row * ChipColumns);
                if (column == 0) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var ids = tools[i].Ids;
                var chip = Chip(tools[i].Name, ids.Any(id => SimRacingPage.IsToolOn(app, id)), tools[i].Note, ids.Contains("hud") ? OverlayColor : "Slate",
                    value =>
                    {
                        foreach (var id in ids)
                            SimRacingPage.SetToolOn(app, id, value);
                        changed();
                    });
                Grid.SetRow(chip, row);
                Grid.SetColumn(chip, ChipColumns - inRow + column * 2);
                Grid.SetColumnSpan(chip, 2);
                grid.Children.Add(chip);
            }
            if (!on) grid.IsHitTestVisible = false;
            chips = grid;
        }
        string color = PcTools.Contains(plugin.Id) ? "Sage" : "Slate";
        var tile = Choice(plugin.Name, plugin.Description, on, true, color, value => { app.Plugins.SetEnabled(plugin, value); changed(); }, chips,
            $"Reads: {plugin.Reads}\n\nRuns in the background: {plugin.BackgroundWork}\n\nChanges on your PC: {plugin.Changes}");
        tile.Margin = new Thickness(0, 0, 0, 6);
        return tile;
    }

    private const double OffOpacity = 0.5, LockedOpacity = 0.35;

    internal static Border Choice(string title, string caption, bool on, bool enabled, string color, Action<bool> changed, FrameworkElement? below = null, string? tip = null)
    {
        var face = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(TextFit.TipWhenTrimmed(Compact(new TextBlock
        {
            Text = title, Style = Ui.Res<Style>("H3"), FontSize = 15.5, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center,
            Margin = new Thickness(8, 0, 8, 0),
        })));
        var line = new Border { Width = 26, Height = 2, Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Center };
        face.Children.Add(line);
        face.Children.Add(new TextBlock
        {
            Text = caption, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center, LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 36, ToolTip = caption,
        });
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(face);
        if (below is not null) body.Children.Add(below);
        var tile = new Border
        {
            Child = body, Focusable = enabled, Cursor = enabled ? System.Windows.Input.Cursors.Hand : null,
            BorderThickness = Hairline.Edges(), Padding = new Thickness(10, 9, 10, 9), Background = Ui.Res<Brush>("SurfaceAlt"),
        };
        if (tip is not null) tile.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(tile, title);
        void Apply()
        {
            bool lit = on && enabled;
            tile.BorderBrush = Ui.Res<Brush>(enabled && tile.IsMouseOver ? "LineStrong" : "Line");
            line.Background = Ui.Res<Brush>(lit ? color : "LineStrong");
            face.Opacity = lit ? 1 : OffOpacity;
            if (below is not null) below.Opacity = lit ? 1 : LockedOpacity;
            System.Windows.Automation.AutomationProperties.SetItemStatus(tile, on ? "On" : "Off");
        }
        void Flip()
        {
            on = !on;
            Apply();
            changed(on);
        }
        Apply();
        tile.MouseEnter += (_, _) => Apply();
        tile.MouseLeave += (_, _) => Apply();
        tile.MouseLeftButtonUp += (_, e) =>
        {
            if (!enabled || e.Handled || (below is not null && e.OriginalSource is DependencyObject source && below.IsAncestorOf(source))) return;
            e.Handled = true;
            Flip();
        };
        tile.KeyDown += (_, e) =>
        {
            if (!enabled || e.Key is not (System.Windows.Input.Key.Space or System.Windows.Input.Key.Enter)) return;
            e.Handled = true;
            Flip();
        };
        return tile;
    }

    private const int ChipColumns = 3;

    private static readonly string[] CompanionTools = { "tradingpaints", "garage61" };

    private static readonly Dictionary<string, string> ChipNames = new()
    {
        ["setups"] = "Setup Manager", ["hud"] = "Overlay", ["shakers"] = "Bass shakers", ["fov"] = "FOV",
        ["iracing"] = "Controller", ["tradingpaints"] = "Trading Paints", ["garage61"] = "Garage 61",
    };

    private static Border Chip(string name, bool on, string? note, string color, Action<bool> changed)
    {
        var text = TextFit.TipWhenTrimmed(Compact(new TextBlock { Text = name, FontSize = 14.5, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center }));
        var mark = new Border { Width = 18, Height = 2, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        var chip = new Border
        {
            Margin = new Thickness(3), Padding = new Thickness(6, 6, 6, 5), BorderThickness = Hairline.Edges(), Child = new StackPanel { Children = { text, mark } },
            Focusable = true, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = note,
        };
        System.Windows.Automation.AutomationProperties.SetName(chip, name);
        void Apply()
        {
            chip.Background = Ui.Res<Brush>("Panel");
            chip.BorderBrush = Ui.Res<Brush>(chip.IsMouseOver ? "LineStrong" : "Panel");
            mark.Background = Ui.Res<Brush>(on ? color : "LineStrong");
            text.Foreground = Ui.Res<Brush>(on ? "Heading" : "TextMuted");
            text.Opacity = on ? 1 : OffOpacity;
            System.Windows.Automation.AutomationProperties.SetItemStatus(chip, on ? "On" : "Off");
        }
        void Flip()
        {
            on = !on;
            Apply();
            changed(on);
        }
        Apply();
        chip.MouseEnter += (_, _) => Apply();
        chip.MouseLeave += (_, _) => Apply();
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Flip();
        };
        chip.KeyDown += (_, e) =>
        {
            if (e.Key is not (System.Windows.Input.Key.Space or System.Windows.Input.Key.Enter)) return;
            e.Handled = true;
            Flip();
        };
        return chip;
    }

    internal static FrameworkElement BulkButtons(AppServices app, Action changed)
    {
        var bulk = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        void All(bool on)
        {
            app.Plugins.SetEnabled(_ => on);
            changed();
        }
        var allOn = new Button { Content = "Turn all on", Style = Ui.Res<Style>("SoftButton"), Margin = new Thickness(0, 0, 6, 0) };
        allOn.Click += (_, _) => All(true);
        var allOff = new Button { Content = "Turn all off", Style = Ui.Res<Style>("GhostButton") };
        allOff.Click += (_, _) => All(false);
        bulk.Children.Add(allOn);
        bulk.Children.Add(allOff);
        return bulk;
    }
}
