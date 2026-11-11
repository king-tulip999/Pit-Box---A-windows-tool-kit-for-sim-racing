using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core.Config;
using Core.Controls;
using Core.SimRacing.Hud;
using Core.SimRacing.Hud.Editors;
using Core.SimRacing.Race;
using Core.Theme;
using Core.Views.HudSettings;

namespace Core.Views.Pages;

internal sealed partial class RaceHudWidgets
{
    private const double PanelNarrow = 400, PanelWide = 860, PanelWideMin = 560, WellPad = 13, PanelGap = 12;
    private const double TwoColumnMin = PanelNarrow * 2 + 18;
    private const double WellMinHeight = 120, CategoryWidth = 96;

    internal readonly record struct PanelTarget(string? SettingId, string? Key, string? Id)
    {
        public bool All => SettingId is null;
    }

    private Grid? _well;
    private Canvas? _panelLayer;
    private Border? _panel;
    private ScrollViewer? _panelScroll;
    private PanelTarget? _openPanel;
    private bool _panelWide;
    private double _panelWidth = PanelNarrow;
    private (string? Setting, (string Key, string Id)? Item) _pin;
    private readonly List<(FrameworkElement Head, List<FrameworkElement> Rows)> _groupHeads = new();
    private readonly HashSet<string> _addsOpen = new(StringComparer.OrdinalIgnoreCase);
    private double _laidWidth, _laidHeight;

    private FrameworkElement PreviewArea(HudWidgetConfig w, HudWidgetDef def)
    {
        _context = new HudSettingContext
        {
            Def = def, W = w, Frame = _section.Preview.Race.Frame, KnownClasses = S.KnownClasses,
            Set = values => SetValues(w, def, values), Rebuild = Fill, Pictures = (id, choices) => Pictures(w, def, id, choices),
        };
        _preview = new Image { Stretch = Stretch.None, RenderTransform = _previewNudge };
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);
        _previewMarks = new Canvas { IsHitTestVisible = false };
        _previewHighlight = new Canvas { IsHitTestVisible = false };
        var previewHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(WellPad),
            Background = Brushes.Transparent, Children = { _preview, _previewHighlight, _previewMarks },
        };
        HookPreviewDrag(previewHost, w);
        _panelLayer = new Canvas();
        _well = new Grid { ClipToBounds = true, MinHeight = WellMinHeight, Background = Brushes.Transparent, Children = { previewHost, _panelLayer } };
        _well.MouseLeftButtonDown += (_, e) => { if (!e.Handled) ClosePanel(); };
        _well.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1) DrawPreview(w);
            PlacePanel();
        };
        _previewWell = Hairline.Frame(new Tile { Child = _well });
        _previewWell.SetResourceReference(Border.BackgroundProperty, "Bg");
        _previewWell.SetResourceReference(Border.BorderBrushProperty, "Line");

        var hint = new TextBlock { Text = "Click a part of the widget to change it; drag items to reorder", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var all = Ui.LabeledButton("I.Sliders", "All settings", () => OpenPanel(new PanelTarget(null, null, null)));
        all.ToolTip = "Every setting of this widget in one panel";
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(all, Dock.Right);
        all.Margin = new Thickness(Ui.GroupGap, 0, 0, 0);
        head.Children.Add(all);
        head.Children.Add(hint);

        _previewNote = new TextBlock { Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 6, 0, 0) };
        _previewHidden = Ui.IconText("I.EyeOff", new TextBlock { Text = "Hidden in game", Style = Ui.Res<Style>("Caption") }, "TextMuted");
        _previewHidden.Margin = new Thickness(2, 8, 0, 0);
        _previewHidden.Visibility = w.Enabled ? Visibility.Collapsed : Visibility.Visible;
        return new StackPanel { Children = { head, _previewWell, _previewHidden, _previewNote } };
    }

    private double WellHeight() => Math.Max(WellMinHeight, Math.Ceiling(_laidHeight * _previewScale + WellPad * 2));

    private double RoomRight() => _well is null ? 0 : _well.ActualWidth - (WellPad + _laidWidth * _previewScale + PanelGap) - 6;

    private void ClosePanel()
    {
        _openPanel = null;
        _panel = null;
        _panelScroll = null;
        _panelLayer?.Children.Clear();
        if (_well is not null) _well.Height = WellHeight();
        _pin = (null, null);
        Highlight(null);
    }

    private void RebuildPanel()
    {
        if (_openPanel is { } target) OpenPanel(target);
    }

    private void OpenPanel(PanelTarget target)
    {
        if (Selected() is not { } w || HudCatalog.Find(w.Kind) is not { } def || _panelLayer is null || _well is null || _context is null) return;
        double keep = _openPanel == target && _panelScroll is not null ? _panelScroll.VerticalOffset : 0;
        _openPanel = target;
        _rows.Clear();
        _groupHeads.Clear();
        double room = RoomRight();
        _panelWide = room >= PanelWideMin;
        _panelWidth = _panelWide ? Math.Floor(Math.Min(room, PanelWide)) : Math.Min(PanelNarrow, Math.Max(280, _well.ActualWidth - WellPad * 2));
        var editor = def.EditorDef;
        var setting = target.SettingId is { } sid ? editor.Setting(sid) : null;
        FrameworkElement content;
        string title;
        if (setting is null)
        {
            title = "All settings";
            content = AllSettings(def, w, editor);
        }
        else
        {
            title = SettingTitle(def, setting);
            content = IsList(def, setting) ? PartPanel(def, w, setting, target, _panelWide) : OptionPanel(editor, setting);
        }
        RefreshVisibility();

        var close = Ui.IconButton("I.X", "Close", ClosePanel, "QuietIconButton");
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = title, Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var header = Hairline.Frame(new Border { Padding = new Thickness(12, 8, 6, 8), Child = new DockPanel { Children = { close, name } } }, Sides.Bottom);
        DockPanel.SetDock(close, Dock.Right);
        header.SetResourceReference(Border.BorderBrushProperty, "Line");
        _panelScroll = new ScrollViewer
        {
            Content = new Border { Padding = new Thickness(12, 4, 12, 12), Child = content },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
        };
        var dock = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        dock.Children.Add(_panelScroll);
        _panel = Hairline.Frame(new Border
        {
            Width = _panelWidth,
            Child = dock,
        });
        _panel.SetResourceReference(Border.BackgroundProperty, "Panel");
        _panel.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        _panel.MouseLeftButtonDown += (_, e) => e.Handled = true;
        _panel.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || Keyboard.FocusedElement is HotkeyBox) return;
            e.Handled = true;
            ClosePanel();
        };
        _panelLayer.Children.Clear();
        _panelLayer.Children.Add(_panel);
        PlacePanel();
        _panelScroll.ScrollToVerticalOffset(keep);
        _pin = (target.SettingId, target.Key is { } k && target.Id is { } id ? (k, id) : null);
        Highlight(_pin.Setting, _pin.Item);
    }

    private void PlacePanel()
    {
        if (_panel is null || _panelScroll is null || _well is null || _openPanel is null) return;
        double room = RoomRight();
        if (_well.ActualWidth <= 0) return;
        if (_panelWide != room >= PanelWideMin)
        {
            _well.Dispatcher.BeginInvoke(RebuildPanel, System.Windows.Threading.DispatcherPriority.Background);
            return;
        }
        if (_panelWide) _panel.Width = Math.Floor(Math.Min(room, PanelWide));
        double height;
        double left, top;
        _panelScroll.MaxHeight = double.PositiveInfinity;
        _panel.Measure(new Size(_panel.Width, double.PositiveInfinity));
        double natural = _panel.DesiredSize.Height;
        if (room >= _panel.Width - 1)
        {
            height = Math.Max(WellHeight(), Math.Ceiling(natural + WellPad + 6));
            left = WellPad + _laidWidth * _previewScale + PanelGap;
            top = WellPad;
        }
        else
        {
            left = Math.Max(6, Math.Min(_well.ActualWidth - _panel.Width - 6, WellPad));
            top = WellPad + _laidHeight * _previewScale + PanelGap;
            height = Math.Ceiling(top + natural + 12);
        }
        if (Math.Abs(_well.Height - height) > 0.5 || double.IsNaN(_well.Height)) _well.Height = height;
        Canvas.SetLeft(_panel, Math.Round(left));
        Canvas.SetTop(_panel, Math.Round(top));
    }

    private static bool IsList(HudWidgetDef def, HudSetting s) =>
        s is PickerSetting or LanesSetting || s is OptionSetting o && def.Option(o.Key) is { Kind: HudOptionKind.Columns };

    private static string SettingTitle(HudWidgetDef def, HudSetting s) => s switch
    {
        LanesSetting l => l.Label ?? l.Title,
        MergedSetting m => m.Label ?? m.Title,
        TogglesSetting t => t.Label ?? t.Title,
        ClassFilterSetting => "Which cars",
        _ => s.Label ?? def.Option(s.Keys[0])?.Label ?? s.Id,
    };

    private FrameworkElement Row(HudSetting setting, string? groupTitle)
    {
        var row = Linked(setting, SettingRows.Build(setting, _context!, groupTitle));
        _rows.Add((setting, row));
        return row;
    }

    private FrameworkElement OptionPanel(HudEditorDef editor, HudSetting setting)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var group = editor.GroupOf(setting.Id);
        stack.Children.Add(Row(setting, group?.Title));
        var rest = group?.Settings.Where(s => s.Id != setting.Id && !IsList(_context!.Def, s)).ToList() ?? new();
        if (rest.Count == 0) return stack;
        var head = string.Equals(group!.Title, SettingTitle(_context!.Def, setting), StringComparison.OrdinalIgnoreCase) ? new Border { Height = 8 } : (FrameworkElement)Ui.SubHeader(group.Title ?? "More");
        stack.Children.Add(head);
        var rows = new List<FrameworkElement>();
        foreach (var s in rest)
        {
            var row = Row(s, group.Title);
            rows.Add(row);
            stack.Children.Add(row);
        }
        _groupHeads.Add((head, rows));
        return stack;
    }

    private FrameworkElement AllSettings(HudWidgetDef def, HudWidgetConfig w, HudEditorDef editor)
    {
        FrameworkElement Group(HudGroup g, bool first)
        {
            var stack = new StackPanel();
            var head = Ui.SubHeader(g.Title ?? "Look", first);
            stack.Children.Add(head);
            var rows = new List<FrameworkElement>();
            foreach (var s in g.Settings)
            {
                var row = IsList(def, s) ? PartPanel(def, w, s, new PanelTarget(s.Id, null, null), false) : Row(s, g.Title);
                rows.Add(row);
                stack.Children.Add(row);
            }
            _groupHeads.Add((head, rows));
            return stack;
        }
        var groups = editor.Groups.ToList();
        if (!_panelWide || groups.Count < 2 || _panelWidth < TwoColumnMin) return Stack(groups.Select((g, i) => Group(g, i == 0)));
        var lists = groups.Where(g => g.Settings.Any(s => IsList(def, s))).ToList();
        var plain = groups.Except(lists).ToList();
        List<HudGroup> left, right;
        if (lists.Count > 0 && plain.Count > 0) (left, right) = (plain, lists);
        else
        {
            left = new();
            right = new();
            int a = 0, b = 0;
            foreach (var g in groups)
            {
                if (a <= b) { left.Add(g); a += g.Settings.Length; }
                else { right.Add(g); b += g.Settings.Length; }
            }
        }
        return TwoColumns(Stack(left.Select((g, i) => Group(g, i == 0))), Stack(right.Select((g, i) => Group(g, i == 0))), 1, 1);
    }

    private static StackPanel Stack(IEnumerable<FrameworkElement> children)
    {
        var stack = new StackPanel();
        foreach (var c in children) stack.Children.Add(c);
        return stack;
    }

    private static Grid TwoColumns(FrameworkElement a, FrameworkElement b, double left, double right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(left, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(right, GridUnitType.Star) });
        Grid.SetColumn(b, 2);
        grid.Children.Add(a);
        grid.Children.Add(b);
        return grid;
    }

    private IReadOnlyList<HudSetting> ItemSettings(HudWidgetDef def, HudWidgetConfig w, string key, string id)
    {
        var editor = def.EditorDef;
        var current = new HudOptions(def, w).Columns(key);
        var without = w.Copy();
        var with = w.Copy();
        without.Options[key] = string.Join(",", current.Where(x => x != id));
        with.Options[key] = string.Join(",", current.Where(x => x != id).Append(id));
        var a = new HudOptions(def, without);
        var b = new HudOptions(def, with);
        var found = editor.Settings.Where(s => !IsList(def, s) && !s.Visible(a) && s.Visible(b)).ToList();
        if (FieldSettings.ItemSettings.TryGetValue(key + ":" + id, out var named))
            foreach (var settingId in named)
                if (editor.Setting(settingId) is { } s && !found.Contains(s)) found.Add(s);
        return found;
    }

    private FrameworkElement PartPanel(HudWidgetDef def, HudWidgetConfig w, HudSetting setting, PanelTarget target, bool wide)
    {
        var picker = setting switch
        {
            PickerSetting p => p,
            OptionSetting o => new PickerSetting(o.Key, FieldSettings.NoCategories) { Label = o.Label },
            _ => null,
        };
        var lanes = setting is LanesSetting ls ? ls.Lanes.Select(x => (x.Key, Lane: (string?)x.Lane)).ToList() : new List<(string Key, string? Lane)> { (picker!.Key, null) };
        var option = def.Option(lanes[0].Key)!;
        var options = _context!.Options;
        Func<string, bool> offered = id => picker?.Offers?.Invoke(options, id) ?? true;
        var choices = option.Choices.Where(c => offered(c.Id)).ToList();
        var sourceCategories = setting is LanesSetting lc ? lc.Categories : picker!.Categories;
        var known = choices.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var categories = sourceCategories.Select(c => (c.Title, Ids: c.Ids.Where(known.Contains).ToArray())).Where(c => c.Ids.Length > 0).ToList();
        var listed = categories.SelectMany(c => c.Ids).ToHashSet(StringComparer.Ordinal);
        if (categories.Count > 0 && choices.Any(c => !listed.Contains(c.Id))) categories.Add(("Other", choices.Where(c => !listed.Contains(c.Id)).Select(c => c.Id).ToArray()));
        if (categories.Count == 0) categories.Add((null!, choices.Select(c => c.Id).ToArray()));
        var model = new ItemPickerModel(choices, lanes.Select(l => ItemPickerModel.Split(options.Value(l.Key))));
        int targetLane = Math.Max(0, lanes.FindIndex(l => l.Key == target.Key));
        var hidden = (_painter.Overflow ?? new()).ToHashSet();

        void Save()
        {
            var values = model.Values;
            var change = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lanes.Count; i++)
                change[lanes[i].Key] = picker?.Offers is null ? values[i] : ItemPickerModel.KeepUnoffered(values[i], options.Value(lanes[i].Key), offered);
            _context!.Set(change);
            _well?.Dispatcher.BeginInvoke(RebuildPanel, System.Windows.Threading.DispatcherPriority.Background);
        }

        var root = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        if (target.Id is { } focusId && target.Key is { } focusKey && choices.FirstOrDefault(c => c.Id == focusId) is { } focus && model.IsAdded(focusId))
            root.Children.Add(FocusBlock(def, w, focusKey, focus, () =>
            {
                for (int i = 0; i < lanes.Count; i++) model.Remove(i, focusId);
                _openPanel = target with { Id = null };
                Save();
            }));

        var laneViews = new List<FrameworkElement>();
        var laneRows = new List<StackPanel>();
        var before = lanes.Select(l => options.Value(l.Key)).ToList();
        void Apply(bool done)
        {
            var change = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lanes.Count; i++)
            {
                string value = string.Join(",", laneRows[i].Children.OfType<FrameworkElement>().Select(c => c.Tag).OfType<string>());
                change[lanes[i].Key] = picker?.Offers is null ? value : ItemPickerModel.KeepUnoffered(value, options.Value(lanes[i].Key), offered);
            }
            _context!.Set(change);
            if (done) _well?.Dispatcher.BeginInvoke(RebuildPanel, System.Windows.Threading.DispatcherPriority.Background);
        }
        void Cancel()
        {
            var change = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lanes.Count; i++) change[lanes[i].Key] = before[i];
            _context!.Set(change);
            _well?.Dispatcher.BeginInvoke(RebuildPanel, System.Windows.Threading.DispatcherPriority.Background);
        }
        for (int i = 0; i < lanes.Count; i++)
        {
            int lane = i;
            var list = model.Lanes[lane];
            var box = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
            var count = new TextBlock { Text = list.Count == 0 ? "" : RaceFormat.Count(list.Count, "item"), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(count, Dock.Right);
            head.Children.Add(count);
            head.Children.Add(new TextBlock { Text = lanes[lane].Lane is { } laneName ? laneName + (lanes.Count > 1 ? " bar" : "") : "Shown", Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center });
            box.Children.Add(head);
            var rows = new StackPanel { Background = Brushes.Transparent, MinHeight = 28 };
            laneRows.Add(rows);
            foreach (var id in list)
            {
                var choice = model.Choice(id);
                bool isPicked = target.Key == lanes[lane].Key && target.Id == id;
                var (row, x) = ShownRow(choice, isPicked, hidden.Contains((lanes[lane].Key, id)), () =>
                {
                    model.Remove(lane, id);
                    Save();
                });
                HookRowDrag(row, x, isPicked, laneRows, Apply, Cancel);
                rows.Children.Add(row);
            }
            rows.Children.Add(new TextBlock { Text = "Nothing here: add from the list.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 6, 0, 6), Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed });
            box.Children.Add(rows);
            laneViews.Add(box);
        }

        var add = AddChips(model, categories, choices, targetLane, Save);
        string addLabel = lanes.Count > 1 ? $"Add to the {(lanes[targetLane].Lane ?? "").ToLowerInvariant()} bar" : "Add";
        if (wide)
        {
            var addBox = new StackPanel { Children = { new TextBlock { Text = addLabel, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 10, 0, 4) }, add } };
            if (lanes.Count > 1)
            {
                root.Children.Add(TwoColumns(laneViews[0], laneViews[1], 1, 1));
                root.Children.Add(addBox);
            }
            else root.Children.Add(TwoColumns(laneViews[0], addBox, 1, 1.15));
            return root;
        }
        foreach (var view in laneViews) root.Children.Add(view);
        bool open = _addsOpen.Contains(setting.Id) || model.Lanes.All(l => l.Count == 0);
        add.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        add.Margin = new Thickness(0, 8, 0, 0);
        var toggle = Ui.LabeledButton("I.Plus", lanes.Count > 1 ? addLabel : "Add more", () => { });
        toggle.HorizontalAlignment = HorizontalAlignment.Stretch;
        toggle.Margin = new Thickness(0, 8, 0, 0);
        toggle.Click += (_, _) =>
        {
            bool now = add.Visibility != Visibility.Visible;
            add.Visibility = now ? Visibility.Visible : Visibility.Collapsed;
            if (now) _addsOpen.Add(setting.Id); else _addsOpen.Remove(setting.Id);
            PlacePanel();
        };
        root.Children.Add(toggle);
        root.Children.Add(add);
        return root;
    }

    private FrameworkElement FocusBlock(HudWidgetDef def, HudWidgetConfig w, string key, HudChoice choice, Action remove)
    {
        var stack = new StackPanel();
        var removeButton = Ui.LabeledButton("I.Trash", "Remove", remove);
        removeButton.ToolTip = "Take it out of the widget";
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = choice.Label, Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var head = new DockPanel { Children = { removeButton, name } };
        DockPanel.SetDock(removeButton, Dock.Right);
        stack.Children.Add(head);
        if (choice.Help is { Length: > 0 } help)
            stack.Children.Add(new TextBlock { Text = help, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        foreach (var s in ItemSettings(def, w, key, choice.Id)) stack.Children.Add(Row(s, null));
        var block = new Border { Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 4, 0, 2), BorderThickness = new Thickness(3, 0, 0, 0), Child = stack };
        block.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        block.SetResourceReference(Border.BorderBrushProperty, "Accent");
        return block;
    }

    private static (Border Row, Button Remove) ShownRow(HudChoice choice, bool picked, bool doesntFit, Action remove)
    {
        var grip = new Icon { Data = Ui.Res<Geometry>("I.Move"), Width = 11, Height = 11, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        grip.SetResourceReference(Control.ForegroundProperty, "TextFaint");
        var x = Ui.IconButton("I.X", "Remove " + choice.Label, remove, "QuietIconButton");
        x.VerticalAlignment = VerticalAlignment.Center;
        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = choice.Label, Style = Ui.Res<Style>("Body"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var dock = new DockPanel();
        DockPanel.SetDock(grip, Dock.Left);
        DockPanel.SetDock(x, Dock.Right);
        dock.Children.Add(grip);
        dock.Children.Add(x);
        if (doesntFit)
        {
            var tag = new TextBlock { Text = "Doesn't fit", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), ToolTip = "No room for it in the widget, so it isn't drawn" };
            DockPanel.SetDock(tag, Dock.Right);
            dock.Children.Add(tag);
        }
        dock.Children.Add(name);
        var row = Hairline.Frame(new Border { Height = 28, Margin = new Thickness(0, 0, 0, 2), Child = dock, Tag = choice.Id, Cursor = Cursors.SizeAll, ToolTip = choice.Help ?? "Drag to reorder" });
        Look(row, picked, false);
        return (row, x);
    }

    private static void Look(Border row, bool picked, bool lifted)
    {
        row.SetResourceReference(Border.BackgroundProperty, lifted ? "Selected" : picked ? "AccentSoft" : "Surface");
        row.SetResourceReference(Border.BorderBrushProperty, lifted || picked ? "Accent" : "Line");
        Panel.SetZIndex(row, lifted ? 1 : 0);
    }

    private static void HookRowDrag(Border row, Button remove, bool picked, List<StackPanel> lanes, Action<bool> apply, Action cancel)
    {
        Point? down = null;
        bool dragging = false, moved = false;
        void Place(MouseEventArgs e)
        {
            foreach (var lane in lanes)
            {
                var at = e.GetPosition(lane);
                if (at.X < -24 || at.X > lane.ActualWidth + 24 || at.Y < -16 || at.Y > lane.ActualHeight + 16) continue;
                int index = 0;
                foreach (var child in lane.Children.OfType<FrameworkElement>())
                    if (!ReferenceEquals(child, row) && child.Tag is string && e.GetPosition(child).Y > child.ActualHeight / 2) index++;
                var parent = (Panel)row.Parent;
                int now = ReferenceEquals(parent, lane) ? parent.Children.OfType<FrameworkElement>().TakeWhile(c => !ReferenceEquals(c, row)).Count(c => c.Tag is string) : -1;
                if (now == index) return;
                parent.Children.Remove(row);
                int slot = lane.Children.Count, seen = 0;
                for (int k = 0; k < lane.Children.Count; k++)
                {
                    if (lane.Children[k] is not FrameworkElement { Tag: string }) continue;
                    if (seen == index) { slot = k; break; }
                    seen++;
                }
                lane.Children.Insert(slot, row);
                foreach (var l in lanes)
                    foreach (var hint in l.Children.OfType<TextBlock>())
                        hint.Visibility = l.Children.OfType<FrameworkElement>().Any(c => c.Tag is string) ? Visibility.Collapsed : Visibility.Visible;
                moved = true;
                apply(false);
                return;
            }
        }
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is Visual v && remove.IsAncestorOf(v)) return;
            down = e.GetPosition(row);
        };
        row.MouseMove += (_, e) =>
        {
            if (down is not { } from || e.LeftButton != MouseButtonState.Pressed) return;
            if (!dragging)
            {
                if ((e.GetPosition(row) - from).Length < 4) return;
                dragging = true;
                moved = false;
                row.CaptureMouse();
                Look(row, picked, true);
            }
            Place(e);
        };
        row.MouseLeftButtonUp += (_, _) =>
        {
            down = null;
            if (!dragging) return;
            dragging = false;
            row.ReleaseMouseCapture();
            Look(row, picked, false);
            if (moved) apply(true);
        };
        row.LostMouseCapture += (_, _) =>
        {
            if (!dragging) return;
            dragging = false;
            down = null;
            Look(row, picked, false);
            if (moved) cancel();
        };
    }

    private static FrameworkElement AddChips(ItemPickerModel model, List<(string Title, string[] Ids)> categories, List<HudChoice> choices, int lane, Action save)
    {
        var root = new StackPanel();
        var search = Ui.Search(new TextBox { Margin = new Thickness(0, 0, 0, 6) });
        System.Windows.Automation.AutomationProperties.SetName(search, "Search items to add");
        root.Children.Add(search);
        var rows = new List<(FrameworkElement Row, List<(Button Chip, string Name)> Chips)>();
        foreach (var (title, ids) in categories)
        {
            var left = ids.Where(id => !model.IsAdded(id)).Select(id => choices.FirstOrDefault(c => c.Id == id)).OfType<HudChoice>().ToList();
            if (left.Count == 0) continue;
            var wrap = new WrapPanel();
            var chips = new List<(Button, string)>();
            foreach (var choice in left)
            {
                var chip = new Button { Style = Ui.Res<Style>("GhostButton"), Content = choice.Label, Height = 24, MinHeight = 24, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0, 0, Ui.InlineGap, Ui.InlineGap), ToolTip = choice.Help };
                System.Windows.Automation.AutomationProperties.SetName(chip, "Add " + choice.Label);
                string id = choice.Id;
                chip.Click += (_, _) =>
                {
                    if (model.Add(id, lane)) save();
                };
                wrap.Children.Add(chip);
                chips.Add((chip, choice.Label));
            }
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            if (title is not null)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CategoryWidth) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 8, 0) });
                Grid.SetColumn(wrap, 1);
            }
            grid.Children.Add(wrap);
            var line = new Border { Padding = new Thickness(0, 5, 0, 1), Child = grid };
            if (rows.Count > 0) Hairline.Frame(line, Sides.Top);
            line.SetResourceReference(Border.BorderBrushProperty, "Line");
            root.Children.Add(line);
            rows.Add((line, chips));
        }
        if (rows.Count == 0)
        {
            search.Visibility = Visibility.Collapsed;
            root.Children.Add(new TextBlock { Text = "Everything is shown.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0) });
        }
        search.TextChanged += (_, _) =>
        {
            string q = search.Text.Trim();
            foreach (var (row, chips) in rows)
            {
                bool any = false;
                foreach (var (chip, name) in chips)
                {
                    bool show = q.Length == 0 || name.Contains(q, StringComparison.OrdinalIgnoreCase);
                    chip.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    any |= show;
                }
                row.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            }
        };
        return root;
    }

    private (string Setting, string Key, string? Id)? PartAt(HudEditorDef editor, Point at)
    {
        var p = PainterPoint(at);
        (string, string, string?)? best = null;
        float bestArea = float.MaxValue;
        void Consider(string key, string? id, System.Drawing.RectangleF box)
        {
            float area = box.Width * box.Height;
            if (!box.Contains(p) || area >= bestArea || editor.SettingForRegion(key) is not { } setting) return;
            best = (setting, key, id);
            bestArea = area;
        }
        foreach (var (id, box) in _painter.Regions ?? new()) Consider(id, null, box);
        foreach (var (key, id, box) in _painter.Items ?? new()) Consider(key, id, box);
        return best;
    }

    internal (string? Selected, PanelTarget? Panel) Keep() => (_selected, _openPanel);

    internal void Restore((string? Selected, PanelTarget? Panel) keep)
    {
        if (keep.Selected is null || Layout?.Widgets.All(w => w.Id != keep.Selected) != false) return;
        _selected = keep.Selected;
        _openPanel = keep.Panel;
        Fill();
    }

    internal void SweepSelect(string kind)
    {
        if (Layout is not { } layout || HudCatalog.Find(kind) is not { } def) return;
        if (layout.Widgets.Any(w => w.Kind == kind)) Select(kind);
        else Add(def);
    }

    internal IReadOnlyList<(string Name, PanelTarget Target)> SweepParts()
    {
        var list = new List<(string, PanelTarget)>();
        if (Selected() is not { } w || HudCatalog.Find(w.Kind) is not { } def) return list;
        var editor = def.EditorDef;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, _) in _painter.Regions ?? new())
            if (editor.SettingForRegion(id) is { } setting && seen.Add(setting))
                list.Add(("part-" + id, new PanelTarget(setting, id, null)));
        foreach (var (key, id, _) in _painter.Items ?? new())
            if (editor.SettingForRegion(key) is { } setting && seen.Add(setting + "|item"))
                list.Add(("item-" + key + "-" + id, new PanelTarget(setting, key, id)));
        list.Add(("all", new PanelTarget(null, null, null)));
        return list;
    }

    internal void SweepOpen(PanelTarget target) => OpenPanel(target);

    private List<ComboBox> PanelCombos()
    {
        var found = new List<ComboBox>();
        void Walk(DependencyObject node)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is ComboBox { IsVisible: true } combo) found.Add(combo);
                else Walk(child);
            }
        }
        if (_panel is not null) Walk(_panel);
        return found;
    }

    internal int SweepDropdowns() => PanelCombos().Count;

    internal void SweepOpenDropdown(int index)
    {
        var combos = PanelCombos();
        foreach (var c in combos) c.IsDropDownOpen = false;
        if (index < combos.Count)
        {
            combos[index].BringIntoView();
            combos[index].IsDropDownOpen = true;
        }
    }

    private List<Control> PanelControls()
    {
        var found = new List<Control>();
        void Walk(DependencyObject node)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is UIElement { IsVisible: false } or UIElement { IsEnabled: false }) continue;
                if (child is ComboBox combo) { found.Add(combo); continue; }
                if (child is HotkeyBox or TextBox) continue;
                if (child is System.Windows.Controls.Primitives.ButtonBase button && System.Windows.Automation.AutomationProperties.GetName(button) != "Close") { found.Add(button); continue; }
                Walk(child);
            }
        }
        if (_panel is not null) Walk(_panel);
        return found;
    }

    internal async Task SweepExercise(string kind)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        void Report(string issue)
        {
            if (reported.Add(issue)) Services.ScreenshotRunner.RecordError(new InvalidOperationException(issue));
        }
        foreach (var (name, target) in SweepParts())
        {
            OpenPanel(target);
            await Task.Delay(40);
            if (_panel is { } shown && _well is { } well)
            {
                double right = Canvas.GetLeft(shown) + shown.ActualWidth, bottom = Canvas.GetTop(shown) + shown.ActualHeight;
                if (right > well.ActualWidth + 1 || bottom > well.ActualHeight + 1)
                    Report($"hud {kind} {name}: the panel ({right:0} x {bottom:0}) runs past the preview area ({well.ActualWidth:0} x {well.ActualHeight:0})");
            }
            for (int i = 0; i < SweepActionCap; i++)
            {
                var controls = PanelControls();
                if (i >= controls.Count) break;
                var control = controls[i];
                string label = System.Windows.Automation.AutomationProperties.GetName(control) is { Length: > 0 } n ? n : control is ContentControl { Content: string text } ? text : control.GetType().Name;
                try
                {
                    var peer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(control);
                    switch (control)
                    {
                        case ComboBox combo:
                            int before = combo.SelectedIndex;
                            for (int j = 0; j < combo.Items.Count; j++)
                            {
                                combo.SelectedIndex = j;
                                await Task.Delay(10);
                            }
                            if (before >= 0 && before < combo.Items.Count) combo.SelectedIndex = before;
                            break;
                        case RadioButton when peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.SelectionItem) is System.Windows.Automation.Provider.ISelectionItemProvider pick:
                            pick.Select();
                            break;
                        case System.Windows.Controls.Primitives.ToggleButton when peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle) is System.Windows.Automation.Provider.IToggleProvider toggle:
                            toggle.Toggle();
                            await Task.Delay(10);
                            toggle.Toggle();
                            break;
                        default:
                            if (peer?.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) is System.Windows.Automation.Provider.IInvokeProvider invoke) invoke.Invoke();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Report($"hud {kind} {name}: “{label}” threw {ex.GetType().Name}: {ex.Message}");
                }
                await Task.Delay(30);
                if (_openPanel != target) OpenPanel(target);
                await Task.Delay(10);
                if (_panel is not null)
                    foreach (var finding in Services.LayoutLint.Inspect(_panel, $"hud {kind} {name}")) Report(finding.ToString());
            }
        }
    }

    private const int SweepActionCap = 160;

    private bool HasParts(HudEditorDef editor) =>
        (_painter.Regions ?? new()).Any(r => editor.SettingForRegion(r.Id) is not null) || (_painter.Items ?? new()).Any(i => editor.SettingForRegion(i.Key) is not null);
}
