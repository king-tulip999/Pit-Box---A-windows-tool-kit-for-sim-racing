using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.Plugins;
using PitBox.Services;
using PitBox.Theme;
using PitBox.Views.Pages;

namespace PitBox.Views;

internal static class ToolChooser
{
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

        var left = new SpreadStack();
        foreach (var plugin in race.OrderBy(p => p.Id == PluginIds.SimRacing ? 0 : 1))
            left.Children.Add(ToolRow(app, plugin, changed));
        Place(grid, left, 1, 0);

        var right = new SpreadStack();
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
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = ChipColumns, Margin = new Thickness(-3, 8, -3, -3) };
            grid.SizeChanged += (_, e) =>
            {
                if (!e.WidthChanged) return;
                int columns = e.NewSize.Width >= ChipColumns * MinChipWidth ? ChipColumns : ChipColumns - 1;
                if (grid.Columns != columns) grid.Columns = columns;
            };
            var all = SimRacingPage.ToolsInOrder(app);
            var tools = all.Where(t => !CompanionTools.Contains(t.Id)).Select(t => (Ids: new[] { t.Id }, Name: ChipNames.GetValueOrDefault(t.Id, t.Title),
                Note: t.Title + ": " + SettingsPage.PartNotes.GetValueOrDefault(t.Id, ""))).ToList();
            var companions = all.Where(t => CompanionTools.Contains(t.Id)).Select(t => t.Id).ToArray();
            if (companions.Length > 0)
                tools.Add((companions, "Companions", "Companion apps: starts Trading Paints and Garage 61 by hand or with iRacing."));
            foreach (var tool in tools)
            {
                var ids = tool.Ids;
                grid.Children.Add(Chip(tool.Name, ids.Any(id => SimRacingPage.IsToolOn(app, id)), tool.Note,
                    value =>
                    {
                        foreach (var id in ids)
                            SimRacingPage.SetToolOn(app, id, value);
                        changed();
                    }));
            }
            if (!on) grid.IsHitTestVisible = false;
            chips = grid;
        }
        var tile = Choice(plugin.Name, plugin.Description, on, true, value => { app.Plugins.SetEnabled(plugin, value); changed(); }, chips,
            $"Reads: {plugin.Reads}\n\nRuns in the background: {plugin.BackgroundWork}\n\nChanges on your PC: {plugin.Changes}");
        tile.Margin = new Thickness(0, 0, 0, 6);
        return tile;
    }

    private const double OffOpacity = 0.85, LockedOpacity = 0.45, MinChipWidth = 140;

    internal static Border Choice(string title, string caption, bool on, bool enabled, Action<bool> changed, FrameworkElement? below = null, string? tip = null)
    {
        var face = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        face.Children.Add(TextFit.TipWhenTrimmed(Compact(new TextBlock
        {
            Text = title, Style = Ui.Res<Style>("H3"), FontSize = 15.5, TextTrimming = TextTrimming.CharacterEllipsis,
        })));
        face.Children.Add(new TextBlock
        {
            Text = caption, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
            LineHeight = 18, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Margin = new Thickness(0, 3, 0, 0),
        });
        var lamp = new Lamp();
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        body.Children.Add(ToggleCards.WithLamp(lamp, face));
        if (below is not null) body.Children.Add(below);
        var tile = new Border
        {
            Child = body, Focusable = enabled, FocusVisualStyle = Ui.Res<Style>("FocusRing"), Cursor = enabled ? System.Windows.Input.Cursors.Hand : null,
            BorderThickness = Hairline.Edges(), Padding = new Thickness(10, 9, 10, 9), Background = Ui.Res<Brush>("SurfaceAlt"),
        };
        if (tip is not null) tile.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(tile, title);
        void Apply()
        {
            bool lit = on && enabled;
            tile.BorderBrush = Ui.Res<Brush>(enabled && tile.IsMouseOver ? "LineStrong" : "Line");
            lamp.On = lit;
            lamp.Hot = enabled && tile.IsMouseOver;
            face.Opacity = !enabled ? LockedOpacity : on ? 1 : OffOpacity;
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

    private static Border Chip(string name, bool on, string? note, Action<bool> changed)
    {
        var text = TextFit.TipWhenTrimmed(Compact(new TextBlock { Text = name, FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }));
        var mark = new Lamp();
        var chip = new Border
        {
            Margin = new Thickness(3), Padding = new Thickness(8, 6, 6, 6), BorderThickness = Hairline.Edges(), Child = ToggleCards.WithLamp(mark, text),
            Focusable = true, FocusVisualStyle = Ui.Res<Style>("FocusRing"), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = note,
        };
        System.Windows.Automation.AutomationProperties.SetName(chip, name);
        void Apply()
        {
            chip.Background = Ui.Res<Brush>("Panel");
            chip.BorderBrush = Ui.Res<Brush>(chip.IsMouseOver ? "LineStrong" : "Panel");
            mark.On = on;
            mark.Hot = chip.IsMouseOver;
            text.Foreground = Ui.Res<Brush>(on ? "Heading" : "Text");
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
        var allOn = new Button { Content = "Turn all on", Style = Ui.Res<Style>("GhostButton"), Margin = Ui.AfterButton };
        allOn.Click += (_, _) => All(true);
        var allOff = new Button { Content = "Turn all off", Style = Ui.Res<Style>("GhostButton") };
        allOff.Click += (_, _) => All(false);
        bulk.Children.Add(allOn);
        bulk.Children.Add(allOff);
        return bulk;
    }

    private sealed class SpreadStack : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            double width = 0, height = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(available.Width, double.PositiveInfinity));
                width = Math.Max(width, child.DesiredSize.Width);
                height += child.DesiredSize.Height;
            }
            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            double used = 0;
            foreach (UIElement child in InternalChildren) used += child.DesiredSize.Height;
            double extra = InternalChildren.Count == 0 ? 0 : Math.Max(0, final.Height - used) / InternalChildren.Count;
            double y = 0;
            foreach (UIElement child in InternalChildren)
            {
                double h = child.DesiredSize.Height + extra;
                child.Arrange(new Rect(0, y, final.Width, h));
                y += h;
            }
            return final;
        }
    }
}
