using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.SimRacing.Hud;
using PitBox.SimRacing.Hud.Editors;
using PitBox.SimRacing.Race;

namespace PitBox.Views.HudSettings;

internal sealed record PickerLane(string Key, string? Name, string Value, string Default);

internal sealed class ItemPicker : StackPanel
{
    private readonly IReadOnlyList<HudCategory> _categories;
    private readonly IReadOnlyList<PickerLane> _lanes;
    private readonly Action<IReadOnlyList<string>> _save;
    private readonly ItemPickerModel _model;
    private readonly Dictionary<string, (IReadOnlySet<string> Hidden, IReadOnlySet<string> Blocked)> _marks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<TextBlock> _summaries = new();
    private readonly List<Button> _adds = new();
    private readonly List<Button> _removes = new();
    private readonly string _title;

    public ItemPicker(string title, string? help, IReadOnlyList<HudChoice> choices, IReadOnlyList<HudCategory> categories, IReadOnlyList<PickerLane> lanes, Action<IReadOnlyList<string>> save, bool showTitle = true)
    {
        _title = title;
        _categories = categories;
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

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SettingRows.LabelWidth) });
        for (int i = 0; i < 2; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (int lane = 0; lane < lanes.Count; lane++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            double top = lane == 0 ? 0 : 6;
            void Place(FrameworkElement e, int column)
            {
                Grid.SetRow(e, lane);
                Grid.SetColumn(e, column);
                if (e.Margin.Top == 0) e.Margin = new Thickness(e.Margin.Left, top, e.Margin.Right, e.Margin.Bottom);
                e.VerticalAlignment = VerticalAlignment.Center;
                grid.Children.Add(e);
            }
            string where = lanes.Count > 1 ? $"{title} ({lanes[lane].Name})" : title;
            var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            label.Children.Add(new TextBlock { Text = lanes.Count > 1 ? lanes[lane].Name : "Shows", Style = Ui.Res<Style>("Body"), VerticalAlignment = VerticalAlignment.Center });
            if (lane == 0 && help is not null && !showTitle) label.Children.Add(HelpTip.For(help, title));
            Place(label, 0);
            int at = lane;
            var add = Ui.LabeledButton("I.Plus", "Add", () => { }, "SoftButton");
            add.Click += (_, _) => OpenAdd(at);
            System.Windows.Automation.AutomationProperties.SetName(add, "Add to " + where);
            _adds.Add(add);
            Place(add, 1);
            var remove = Ui.LabeledButton("I.Minus", "Remove", () => { }, "GhostButton");
            remove.Click += (_, _) => OpenRemove(at);
            remove.Margin = new Thickness(5, 0, 0, 0);
            System.Windows.Automation.AutomationProperties.SetName(remove, "Remove from " + where);
            _removes.Add(remove);
            Place(remove, 2);
            var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 0, 0, 0) };
            summary.Style = Ui.Res<Style>("Caption");
            _summaries.Add(summary);
            Place(summary, 3);
        }
        Children.Add(grid);
        Rebuild();
    }

    public void ShowAdd(string? key = null) => OpenAdd(Math.Max(0, _lanes.ToList().FindIndex(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase))));

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

    private bool Blocked(int lane, string id) => _marks.TryGetValue(_lanes[lane].Key, out var m) && m.Blocked.Contains(id);

    private bool Hidden(int lane, string id) => _marks.TryGetValue(_lanes[lane].Key, out var m) && m.Hidden.Contains(id);

    private void Save()
    {
        _save(_model.Values);
        Rebuild();
    }

    private void Rebuild()
    {
        for (int lane = 0; lane < _lanes.Count; lane++)
        {
            int count = _model.Lanes[lane].Count;
            string counted = count == 0 ? "Nothing shown: Add some" : RaceFormat.Count(count, "item");
            string text = counted;
            var hidden = _model.Lanes[lane].Where(id => Hidden(lane, id)).Select(id => _model.Choice(id).Label).ToList();
            if (hidden.Count is 1 or 2) text += $" · {string.Join(" and ", hidden)} {(hidden.Count == 1 ? "doesn't" : "don't")} fit";
            else if (hidden.Count > 2) text += $" · {hidden.Count} don't fit";
            _summaries[lane].Text = text;
            _summaries[lane].ToolTip = hidden.Count > 0 ? "Doesn't fit at this width, so not drawn: " + string.Join(", ", hidden) + ". Widen the widget or remove some." : null;
            _removes[lane].IsEnabled = count > 0;
        }
    }

    private static Popup PopupBelow(Button anchor, FrameworkElement card)
    {
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Custom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            CustomPopupPlacementCallback = (size, target, _) => new[] { new CustomPopupPlacement(new Point(target.Width - size.Width, target.Height), PopupPrimaryAxis.Horizontal) },
            Child = new Tile { Style = Ui.Res<Style>("LiftedCard"), Padding = new Thickness(8), Margin = new Thickness(0, 3, 0, 0), Child = card, MaxWidth = 308 },
        };
        RenderOptions.SetClearTypeHint(popup.Child, ClearTypeHint.Enabled);
        Ui.FollowTarget(popup);
        popup.Child.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            popup.IsOpen = false;
            anchor.Focus();
            e.Handled = true;
        };
        return popup;
    }

    private void OpenRemove(int lane)
    {
        var list = new StackPanel();
        KeyboardNavigation.SetDirectionalNavigation(list, KeyboardNavigationMode.Contained);
        var card = new ScrollViewer { MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Focusable = false, MinWidth = 227 };
        var popup = PopupBelow(_removes[lane], card);

        void Fill()
        {
            list.Children.Clear();
            foreach (var id in _model.Lanes[lane]) list.Children.Add(Option(id));
            if (list.Children.Count == 0) popup.IsOpen = false;
        }

        Button Option(string id)
        {
            var choice = _model.Choice(id);
            var line = new DockPanel { LastChildFill = true };
            var x = new Icon { Data = Ui.Res<Geometry>("I.X"), Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            x.SetResourceReference(Control.ForegroundProperty, "TextMuted");
            DockPanel.SetDock(x, Dock.Right);
            line.Children.Add(x);
            if (Hidden(lane, id))
            {
                var tag = new TextBlock { Text = "doesn't fit", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(tag, Dock.Right);
                line.Children.Add(tag);
            }
            var label = new TextBlock { Text = choice.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            line.Children.Add(label);
            var b = new Button { Style = Ui.Res<Style>("QuietButton"), Content = line, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 5, 6, 5), ToolTip = choice.Help };
            System.Windows.Automation.AutomationProperties.SetName(b, "Take out " + choice.Label);
            b.Click += (_, _) =>
            {
                if (_model.Remove(lane, id)) Save();
                Fill();
                list.Children.OfType<Button>().FirstOrDefault()?.Focus();
            };
            return b;
        }

        Fill();
        popup.IsOpen = true;
        Dispatcher.BeginInvoke(() => list.Children.OfType<Button>().FirstOrDefault()?.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OpenAdd(int lane)
    {
        var search = new TextBox { Tag = "Search…", Padding = new Thickness(23, 5, 6, 5), MinWidth = 227 };
        System.Windows.Automation.AutomationProperties.SetName(search, "Search " + _title);
        var searchIcon = new Icon { Data = Ui.Res<Geometry>("I.Search"), Width = 11, Height = 11, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 0) };
        searchIcon.SetResourceReference(Control.ForegroundProperty, "TextFaint");
        var card = new StackPanel();
        card.Children.Add(new Grid { Children = { search, searchIcon } });
        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        KeyboardNavigation.SetDirectionalNavigation(list, KeyboardNavigationMode.Contained);
        card.Children.Add(new ScrollViewer { MaxHeight = 275, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list, Focusable = false });
        var popup = PopupBelow(_adds[lane], card);

        void Fill()
        {
            list.Children.Clear();
            var groups = _model.Grouped(_categories, search.Text.Trim());
            foreach (var (title, items) in groups)
            {
                if (title is not null)
                    list.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(6, list.Children.Count == 0 ? 2 : 8, 0, 4) });
                foreach (var c in items) list.Children.Add(Option(c));
            }
            if (groups.Count == 0) list.Children.Add(new TextBlock { Text = "Nothing matches", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(5, 3, 0, 3) });
        }

        Button Option(HudChoice c)
        {
            bool added = _model.IsAdded(c.Id), blocked = !added && Blocked(lane, c.Id);
            var line = new DockPanel { LastChildFill = true };
            if (added || blocked)
            {
                var tag = new TextBlock { Text = added ? "Added" : "Doesn't fit", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                DockPanel.SetDock(tag, Dock.Right);
                line.Children.Add(tag);
            }
            var label = new TextBlock { Text = c.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            if (!added && !blocked) label.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            line.Children.Add(label);
            var b = new Button
            {
                Style = Ui.Res<Style>("QuietButton"), Content = line, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 5, 6, 5),
                IsEnabled = !added && !blocked, Opacity = added || blocked ? 0.5 : 1,
                ToolTip = blocked ? "No room: take one out first, or widen the widget" : c.Help,
            };
            ToolTipService.SetShowOnDisabled(b, true);
            b.Click += (_, _) =>
            {
                if (_model.Add(c.Id, lane)) Save();
                Fill();
                search.Focus();
            };
            return b;
        }

        search.TextChanged += (_, _) => Fill();
        search.PreviewKeyDown += (_, e) =>
        {
            var first = list.Children.OfType<Button>().FirstOrDefault(b => b.IsEnabled);
            if (e.Key == Key.Down && first is not null) { first.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter && first is not null && search.Text.Trim().Length > 0) { first.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); e.Handled = true; }
        };
        Fill();
        popup.IsOpen = true;
        Dispatcher.BeginInvoke(() => { search.Focus(); Keyboard.Focus(search); }, System.Windows.Threading.DispatcherPriority.Input);
    }
}
