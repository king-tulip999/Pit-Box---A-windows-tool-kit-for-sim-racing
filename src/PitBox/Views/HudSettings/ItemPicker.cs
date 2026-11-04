using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.SimRacing.Hud;
using PitBox.SimRacing.Hud.Editors;
using PitBox.SimRacing.Race;
using PitBox.Theme;

namespace PitBox.Views.HudSettings;

internal sealed record PickerLane(string Key, string? Name, string Value, string Default);

internal sealed class ItemPicker : StackPanel
{
    private const double SingleColumn = 128, LaneColumn = 196, LaneTick = 46, FlatItem = 150;
    private const string DoesntFit = "Doesn't fit at this width, so not drawn. Widen the widget or take one out.";
    private const string NoRoom = "No room: take one out first, or widen the widget";

    private sealed class ItemRow
    {
        public required string Id { get; init; }
        public required HudChoice Choice { get; init; }
        public required Border Box { get; init; }
        public required TextBlock Label { get; init; }
        public required TextBlock Tag { get; init; }
        public required CheckBox[] Ticks { get; init; }
    }

    private readonly IReadOnlyList<PickerLane> _lanes;
    private readonly Action<IReadOnlyList<string>> _save;
    private readonly ItemPickerModel _model;
    private readonly Dictionary<string, (IReadOnlySet<string> Hidden, IReadOnlySet<string> Blocked)> _marks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TextBlock> _summaries = new();
    private readonly Dictionary<string, ItemRow> _items = new(StringComparer.OrdinalIgnoreCase);
    private ItemRow? _linked;

    public ItemPicker(string title, string? help, IReadOnlyList<HudChoice> choices, IReadOnlyList<HudCategory> categories, IReadOnlyList<PickerLane> lanes, Action<IReadOnlyList<string>> save, bool showTitle = true)
    {
        _lanes = lanes;
        _save = save;
        _model = new ItemPickerModel(choices, lanes.Select(l => ItemPickerModel.Split(l.Value)));
        Margin = new Thickness(0, 3, 0, 8);

        if (showTitle)
        {
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            titleRow.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            if (help is not null) titleRow.Children.Add(HelpTip.For(help, title));
            Children.Add(titleRow);
        }

        var summary = new Grid();
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SettingRows.LabelWidth) });
        summary.ColumnDefinitions.Add(new ColumnDefinition());
        for (int lane = 0; lane < lanes.Count; lane++)
        {
            summary.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 26 });
            var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = lanes.Count > 1 ? lanes[lane].Name : "Shows", Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center });
            if (lane == 0 && help is not null && !showTitle) label.Children.Add(HelpTip.For(help, title));
            Grid.SetRow(label, lane);
            summary.Children.Add(label);
            var text = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(text, lane);
            Grid.SetColumn(text, 1);
            summary.Children.Add(text);
            _summaries.Add(text);
        }
        Children.Add(summary);

        var groups = _model.Grouped(categories, "");
        bool laned = lanes.Count > 1;
        if (!laned && groups.Count == 1 && groups[0].Title is null)
        {
            var flat = new WrapPanel { ItemWidth = FlatItem, Margin = new Thickness(-4, 6, 0, 0) };
            foreach (var c in groups[0].Items) flat.Children.Add(Row(c));
            Children.Add(flat);
        }
        else
        {
            var panel = new MasonryPanel { MinColumnWidth = laned ? LaneColumn : SingleColumn, MaxColumns = 3, Gap = 14, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var (groupTitle, items) in groups)
            {
                var stack = new StackPanel();
                stack.Children.Add(Header(groupTitle));
                foreach (var c in items) stack.Children.Add(Row(c));
                panel.Children.Add(stack);
            }
            Children.Add(panel);
        }
        Rebuild();
    }

    public Action<string, string?>? HoverItem { get; set; }

    public void Reload(IReadOnlyList<string> values)
    {
        _model.Reset(values);
        Rebuild();
    }

    public void Mark(string key, IReadOnlySet<string> hidden, IReadOnlySet<string> blocked)
    {
        _marks[key] = (hidden, blocked);
        Rebuild();
    }

    public bool Owns(string key) => _lanes.Any(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase));

    public Border? RowFor(string key, string id) => Owns(key) && _items.TryGetValue(id, out var row) ? row.Box : null;

    public void ShowLinked(string? key, string? id)
    {
        var row = key is not null && id is not null && Owns(key) && _items.TryGetValue(id, out var found) ? found : null;
        if (ReferenceEquals(row, _linked)) return;
        var before = _linked;
        _linked = row;
        if (before is not null) Paint(before);
        if (row is not null) Paint(row);
    }

    private FrameworkElement Header(string? title)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var name = new TextBlock { Text = title ?? "", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 0, 3) };
        grid.Children.Add(name);
        if (_lanes.Count > 1)
            for (int lane = 0; lane < _lanes.Count; lane++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LaneTick) });
                var head = new TextBlock { Text = _lanes[lane].Name, Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3) };
                Grid.SetColumn(head, lane + 1);
                grid.Children.Add(head);
            }
        var edge = Hairline.Frame(new Border { Child = grid }, Sides.Bottom);
        edge.SetResourceReference(Border.BorderBrushProperty, "Line");
        edge.Margin = new Thickness(0, 0, 0, 2);
        return edge;
    }

    private Border Row(HudChoice c)
    {
        var label = TextFit.TipWhenTrimmed(new TextBlock { Text = c.Label, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
        var tag = new TextBlock { Style = Ui.Res<Style>("Caption"), Text = "doesn't fit", Visibility = Visibility.Collapsed };
        tag.SetResourceReference(TextBlock.ForegroundProperty, "WarnDeep");
        var line = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { label, tag } };
        CheckBox[] ticks;
        FrameworkElement content;
        if (_lanes.Count > 1)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(line);
            ticks = new CheckBox[_lanes.Count];
            for (int lane = 0; lane < _lanes.Count; lane++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LaneTick) });
                int at = lane;
                var tick = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                System.Windows.Automation.AutomationProperties.SetName(tick, $"{c.Label} in {_lanes[lane].Name}");
                ToolTipService.SetShowOnDisabled(tick, true);
                tick.Click += (_, _) => Toggle(c.Id, at);
                Grid.SetColumn(tick, lane + 1);
                grid.Children.Add(tick);
                ticks[lane] = tick;
            }
            content = grid;
        }
        else
        {
            var tick = new CheckBox { Content = line, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(tick, c.Label);
            ToolTipService.SetShowOnDisabled(tick, true);
            tick.Click += (_, _) => Toggle(c.Id, 0);
            ticks = new[] { tick };
            content = tick;
        }
        var box = new Border { Child = content, MinHeight = 28, Padding = new Thickness(4, 3, 2, 3), Background = Brushes.Transparent };
        var row = new ItemRow { Id = c.Id, Choice = c, Box = box, Label = label, Tag = tag, Ticks = ticks };
        box.MouseEnter += (_, _) => { Paint(row); HoverItem?.Invoke(LaneKey(c.Id), c.Id); };
        box.MouseLeave += (_, _) => { Paint(row); HoverItem?.Invoke(LaneKey(c.Id), null); };
        _items[c.Id] = row;
        return box;
    }

    private void Paint(ItemRow row)
    {
        if (ReferenceEquals(row, _linked)) row.Box.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        else if (row.Box.IsMouseOver) row.Box.SetResourceReference(Border.BackgroundProperty, "Hover");
        else row.Box.Background = Brushes.Transparent;
    }

    private int LaneOf(string id) => _model.Lanes.FindIndex(l => l.Contains(id));

    private string LaneKey(string id) => _lanes[Math.Max(0, LaneOf(id))].Key;

    private void Toggle(string id, int lane)
    {
        int at = LaneOf(id);
        if (at == lane) _model.Remove(lane, id);
        else
        {
            if (at >= 0) _model.Remove(at, id);
            _model.Add(id, lane);
        }
        _save(_model.Values);
        Rebuild();
    }

    private bool Blocked(int lane, string id) => _marks.TryGetValue(_lanes[lane].Key, out var m) && m.Blocked.Contains(id);

    private bool Hidden(int lane, string id) => _marks.TryGetValue(_lanes[lane].Key, out var m) && m.Hidden.Contains(id);

    private void Rebuild()
    {
        for (int lane = 0; lane < _lanes.Count; lane++)
        {
            int count = _model.Lanes[lane].Count;
            string text = count == 0 ? "Nothing shown" : RaceFormat.Count(count, "item");
            var hidden = _model.Lanes[lane].Where(id => Hidden(lane, id)).Select(id => _model.Choice(id).Label).ToList();
            if (hidden.Count is 1 or 2) text += $" · {string.Join(" and ", hidden)} {(hidden.Count == 1 ? "doesn't" : "don't")} fit";
            else if (hidden.Count > 2) text += $" · {hidden.Count} don't fit";
            _summaries[lane].Text = text;
            _summaries[lane].ToolTip = hidden.Count > 0 ? "Doesn't fit at this width, so not drawn: " + string.Join(", ", hidden) + ". Widen the widget or take some out." : null;
        }
        foreach (var row in _items.Values)
        {
            int lane = LaneOf(row.Id);
            bool hidden = lane >= 0 && Hidden(lane, row.Id);
            for (int l = 0; l < row.Ticks.Length; l++)
            {
                var tick = row.Ticks[l];
                bool on = lane == l, blocked = !on && Blocked(l, row.Id);
                if (tick.IsChecked != on) tick.IsChecked = on;
                if (tick.IsEnabled == blocked) tick.IsEnabled = !blocked;
                tick.ToolTip = blocked ? NoRoom : on && hidden ? DoesntFit : row.Ticks.Length > 1 ? null : row.Choice.Help;
            }
            row.Label.SetResourceReference(TextBlock.ForegroundProperty, lane >= 0 ? "Heading" : "Text");
            row.Tag.Visibility = hidden ? Visibility.Visible : Visibility.Collapsed;
            if (row.Ticks.Length > 1) row.Box.ToolTip = hidden ? DoesntFit : row.Choice.Help;
        }
    }
}
