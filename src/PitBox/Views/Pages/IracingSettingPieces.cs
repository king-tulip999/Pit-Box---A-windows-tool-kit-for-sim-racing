using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.Services;
using PitBox.SimRacing.IracingFiles;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed partial class IracingControllerSection
{
    private const double LevelCell = 66, LevelGap = 3, LevelLabelMax = 480, TileMin = 200, ItemMin = 300, ItemGap = 26, TrayChips = 6, TrayChipText = 260;

    private IEnumerable<FrameworkElement> Pieces(List<Setting> settings, bool span)
    {
        var levels = settings.Where(s => SettingShapes.Of(s.Def) == SettingShape.Level).ToList();
        if (levels.Count > 0) yield return LevelGrid(levels);
        var switches = settings.Where(s => SettingShapes.Of(s.Def) == SettingShape.Switch).ToList();
        if (switches.Count > 0)
        {
            var grid = new EvenGrid { MinItemWidth = TileMin, Margin = new Thickness(0, 2, 0, 8) };
            foreach (var s in switches) grid.Children.Add(SwitchTile(s));
            yield return grid;
        }
        var items = settings.Where(s => SettingShapes.Of(s.Def) is SettingShape.Slider or SettingShape.Choice).ToList();
        if (items.Count > 0)
        {
            var grid = new EvenGrid { MinItemWidth = ItemMin, ColumnGap = ItemGap, RowGap = 2, Margin = new Thickness(0, 2, 0, 8) };
            foreach (var s in items) grid.Children.Add(SettingShapes.Of(s.Def) == SettingShape.Slider ? SliderItem(s) : ChoiceItem(s));
            yield return grid;
        }
        var rows = settings.Where(s => SettingShapes.Of(s.Def) == SettingShape.Row).ToList();
        if (rows.Count > 0)
        {
            Panel list = span && rows.Count > 1 ? new SplitRows { MinColumnWidth = 389, Gap = ItemGap } : new StackPanel();
            foreach (var s in rows) list.Children.Add(SettingRow(s));
            yield return list;
        }
    }

    private void Apply(Setting s, string value, Action refreshed)
    {
        if (Session!.SetDraft(s, value) is { } problem)
            _app.Notify(s.Def.Label, problem, NoticeKind.Warning);
        refreshed();
        UpdatePending();
    }

    private Border UnsavedChip(Setting s)
    {
        var chip = Ui.Chip("Unsaved", "AccentSoft", "AccentText");
        chip.Margin = new Thickness(6, 0, 0, 0);
        chip.Padding = new Thickness(5, 0, 5, 0);
        chip.VerticalAlignment = VerticalAlignment.Center;
        chip.ToolTip = "Not written to the file yet: " + s.Show(s.Applied) + " there now";
        return chip;
    }

    private string Details(Setting s)
    {
        var std = Session!.DefaultOf(s);
        var details = $"{s.FileName}  [{s.Section}]  {s.Key}";
        if (std is not null) details += "\nDefault: " + s.Show(std);
        if (s.IsChanged) details += "\nIn the file now: " + s.Show(s.Applied);
        return details;
    }

    private static void Notes(Setting s, Panel text)
    {
        void Note(string? note, string brush, string? icon = null)
        {
            if (string.IsNullOrWhiteSpace(note)) return;
            var line = new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>(brush), TextWrapping = TextWrapping.Wrap };
            FrameworkElement shown = icon is null ? line : Ui.IconText(icon, line, brush);
            shown.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(shown);
        }
        Note(s.Def.Danger, "Danger", "I.Alert");
        Note(s.IsChanged ? s.Def.Caution : null, "WarnDeep");
        Note(s.IsInert ? s.Def.DependsOn?.Message : null, "TextFaint");
    }

    private StackPanel ItemHead(Setting s, Func<bool>? popChip = null)
    {
        var words = _words;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new WrapPanel();
        var label = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        Ui.Highlight(label, s.Def.Label, words);
        title.Children.Add(label);
        if (s.IsChanged)
        {
            var chip = UnsavedChip(s);
            title.Children.Add(chip);
            if (popChip?.Invoke() == true) Reveal.PopIn(chip);
        }
        var std = Session!.DefaultOf(s);
        text.Children.Add(Ui.Marked(title, out var dot));
        dot.Visibility = s.IsModified ? Visibility.Visible : Visibility.Collapsed;
        if (dot is FrameworkElement d) d.ToolTip = std is null ? null : "Changed from iRacing's default (" + s.Show(std) + ")";
        if (s.Def.Help.Length > 0)
        {
            var help = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
            Ui.Highlight(help, s.Def.Help, words);
            text.Children.Add(help);
        }
        Notes(s, text);
        return text;
    }

    private StackPanel ItemActions(Setting s, Action rebuilt)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (s.IsModified && Session!.DefaultOf(s) is { } std)
            actions.Children.Add(Ui.IconButton("I.Undo", "Back to iRacing's default (" + s.Show(std) + ")", () => { Session.SetDraft(s, std); rebuilt(); UpdatePending(); }));
        if (s.IsChanged)
            actions.Children.Add(Ui.IconButton("I.X", "Drop this change (keep " + s.Show(s.Applied) + ")", () => { Session!.Discard(s); rebuilt(); UpdatePending(); }));
        foreach (FrameworkElement b in actions.Children) b.Margin = new Thickness(4, 0, 0, 0);
        return actions;
    }

    private Border CompactItem(Setting s, Func<Action, FrameworkElement> control, bool stacked, Border host)
    {
        var grid = new Grid();
        var head = new ContentControl { Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center };
        var actions = new ContentControl { Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center };
        var slot = new ContentControl { Focusable = false, IsTabStop = false, VerticalAlignment = VerticalAlignment.Center };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(actions, 1);
        if (stacked)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(slot, 1);
            Grid.SetColumnSpan(slot, 3);
            slot.Margin = new Thickness(0, 5, 0, 0);
            slot.HorizontalAlignment = HorizontalAlignment.Stretch;
            slot.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }
        else
        {
            Grid.SetColumn(slot, 2);
            slot.Margin = new Thickness(8, 0, 0, 0);
        }
        grid.Children.Add(head);
        grid.Children.Add(actions);
        grid.Children.Add(slot);
        bool wasChanged = s.IsChanged;
        var edge = AddUnsavedEdge(grid, host, s, false);
        Grid.SetRowSpan(edge, 2);
        void Head()
        {
            head.Content = ItemHead(s, () => !wasChanged);
            ShowEdge(edge, s, !wasChanged);
            wasChanged = s.IsChanged;
            actions.Content = ItemActions(s, Rebuild);
            host.Opacity = s.IsInert ? 0.6 : 1;
        }
        void Rebuild()
        {
            slot.Content = control(Head);
            Head();
        }
        head.ToolTip = Details(s);
        ToolTipService.SetInitialShowDelay(head, 700);
        host.Child = grid;
        Rebuild();
        return host;
    }

    private Border SwitchTile(Setting s)
    {
        var host = new Border();
        var head = new ContentControl { Focusable = false, IsTabStop = false };
        var actions = new ContentControl { Focusable = false, IsTabStop = false };
        var notes = new StackPanel();
        Action refill = () => { };
        var toggle = Ui.Switch(s.IsOn(s.Draft), on => Apply(s, on != s.Def.Invert ? "1" : "0", refill));
        AutomationProperties.SetName(toggle, s.Def.Label);
        CardTop.Align(ToggleCards.Dress(toggle, head, s.Def.Help.Length > 0 ? s.Def.Help : null, notes, help => Ui.Highlight(help, s.Def.Help, _words), actions));
        var grid = new Grid();
        grid.Children.Add(toggle);
        host.Child = grid;
        bool wasChanged = s.IsChanged;
        var edge = AddUnsavedEdge(grid, host, s, false);
        void Fill()
        {
            bool grow = !wasChanged;
            wasChanged = s.IsChanged;
            toggle.IsChecked = s.IsOn(s.Draft);
            var title = ToggleCards.Title(s.Def.Label, false);
            Ui.Highlight(title, s.Def.Label, _words);
            var line = new WrapPanel();
            line.Children.Add(title);
            if (s.IsChanged)
            {
                var chip = UnsavedChip(s);
                line.Children.Add(chip);
                if (grow) Reveal.PopIn(chip);
            }
            var marked = Ui.Marked(line, out var dot);
            dot.Visibility = s.IsModified ? Visibility.Visible : Visibility.Collapsed;
            if (dot is FrameworkElement d && Session!.DefaultOf(s) is { } std) d.ToolTip = "Changed from iRacing's default (" + s.Show(std) + ")";
            head.Content = marked;
            notes.Children.Clear();
            Notes(s, notes);
            notes.Margin = new Thickness(0, notes.Children.Count > 0 ? 4 : 0, 0, 0);
            var buttons = ItemActions(s, Fill);
            actions.Content = buttons.Children.Count > 0 ? buttons : null;
            toggle.ToolTip = Details(s);
            ToolTipService.SetInitialShowDelay(toggle, 700);
            grid.Opacity = s.IsInert ? 0.6 : 1;
            ShowEdge(edge, s, grow);
        }
        refill = Fill;
        Fill();
        return host;
    }

    private Border ChoiceItem(Setting s)
    {
        var host = new Border { Padding = new Thickness(8, 6, 8, 6) };
        Ui.HoverHighlight(host);
        return CompactItem(s, refreshed =>
        {
            var options = s.Def.Options!;
            int selected = options.FindIndex(o => Setting.SameValue(Setting.Format(o.Value), s.Draft));
            if (selected < 0) return Editor(s, refreshed);
            var seg = Ui.Segmented(options.Select(o => o.Label).ToList(), selected, i => Apply(s, Setting.Format(options[i].Value), refreshed), "ir-choice", options.Select(o => o.Tip).ToList());
            seg.HorizontalAlignment = HorizontalAlignment.Left;
            AutomationProperties.SetName(seg, s.Def.Label);
            return seg;
        }, stacked: true, host);
    }

    private Border SliderItem(Setting s)
    {
        var host = new Border { Padding = new Thickness(8, 6, 8, 6) };
        Ui.HoverHighlight(host);
        return CompactItem(s, refreshed => SliderControl(s, refreshed), stacked: true, host);
    }

    private Grid SliderControl(Setting s, Action refreshed)
    {
        double min = s.Def.Min!.Value, max = s.Def.Max!.Value, step = s.Def.Step is > 0 and var st ? st : 1;
        CommentInference.TryNumber(s.Draft, out var n);
        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = Math.Clamp(n, min, max), IsSnapToTickEnabled = true, TickFrequency = step,
            SmallChange = step, LargeChange = step * Math.Max(1, Math.Round((max - min) / step / 20)), VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(slider, s.Def.Label);
        SliderFace.SetText(slider, s.Show(s.Draft));
        bool dragging = false;
        slider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => dragging = true));
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { dragging = false; refreshed(); }));
        slider.ValueChanged += (_, e) =>
        {
            Session!.SetDraft(s, Setting.Format(e.NewValue));
            SliderFace.SetText(slider, s.Show(s.Draft));
            UpdatePending();
            if (!dragging) refreshed();
        };
        var track = new Grid();
        track.Children.Add(slider);
        if (Session!.DefaultOf(s) is { } std && CommentInference.TryNumber(std, out var d) && d >= min && d <= max)
        {
            var tick = new Border { Width = 2, Height = 6, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            tick.SetResourceReference(Border.BackgroundProperty, "Gold");
            slider.ToolTip = "iRacing default: " + s.Show(std);
            void Place()
            {
                tick.Margin = new Thickness(Math.Clamp(slider.ActualWidth * (d - min) / (max - min) - 1, 0, Math.Max(0, slider.ActualWidth - 2)), 0, 0, 0);
            }
            slider.SizeChanged += (_, _) => Place();
            slider.Loaded += (_, _) => Place();
            track.Children.Add(tick);
        }
        var grid = new Grid();
        grid.Children.Add(track);
        return grid;
    }

    private static Grid LevelColumns()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1000, GridUnitType.Star), MinWidth = 140, MaxWidth = LevelLabelMax });
        foreach (var _ in SettingShapes.Levels) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(LevelCell) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private FrameworkElement LevelGrid(List<Setting> levels)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
        var head = LevelColumns();
        head.Margin = new Thickness(8, 0, 0, 2);
        for (int i = 0; i < SettingShapes.Levels.Length; i++)
        {
            var name = new TextBlock { Text = SettingShapes.Levels[i], Style = Ui.Res<Style>("Caption"), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(name, i + 1);
            head.Children.Add(name);
        }
        stack.Children.Add(head);
        foreach (var s in levels) stack.Children.Add(LevelRow(s));
        return stack;
    }

    private static int OptionOf(Setting s, string? value) => value is null ? -1 : s.Def.Options!.FindIndex(o => Setting.SameValue(Setting.Format(o.Value), value));

    private Border LevelRow(Setting s)
    {
        var host = new Border { Padding = new Thickness(8, 3, 0, 3) };
        Ui.HoverHighlight(host);
        bool wasChanged = s.IsChanged;
        void Fill()
        {
            bool grow = !wasChanged;
            var grid = LevelColumns();
            var head = new DockPanel { Margin = new Thickness(0, 0, 10, 0), LastChildFill = true };
            var actions = ItemActions(s, Fill);
            DockPanel.SetDock(actions, Dock.Right);
            head.Children.Add(actions);
            var text = ItemHead(s, () => !wasChanged);
            wasChanged = s.IsChanged;
            text.ToolTip = Details(s);
            ToolTipService.SetInitialShowDelay(text, 700);
            head.Children.Add(text);
            grid.Children.Add(head);
            var options = s.Def.Options!;
            var map = SettingShapes.LevelMap(s.Def);
            int current = OptionOf(s, s.Draft), std = OptionOf(s, Session!.DefaultOf(s));
            int picked = Array.FindIndex(map, m => m == current);
            for (int level = 0; level < map.Length; level++)
            {
                FrameworkElement cell;
                if (map[level] is int option)
                {
                    bool pick = level == picked, lit = picked >= 0 && level < picked;
                    var b = new Button
                    {
                        Style = Ui.Res<Style>("QuietButton"), Content = SettingShapes.Levels[level], Height = 28, Padding = new Thickness(0),
                        Margin = new Thickness(LevelGap / 2, 0, LevelGap / 2, 0), HorizontalContentAlignment = HorizontalAlignment.Center,
                        FontWeight = pick ? FontWeights.SemiBold : FontWeights.Normal, Opacity = lit ? 0.6 : 1,
                    };
                    b.SetResourceReference(Control.BackgroundProperty, pick || lit ? "AccentSoft" : "MeterTrack");
                    b.SetResourceReference(Control.BorderBrushProperty, pick ? "Accent" : lit ? "AccentSoft" : "MeterTrack");
                    b.SetResourceReference(Control.ForegroundProperty, pick ? "AccentText" : lit ? "Text" : "TextFaint");
                    AutomationProperties.SetName(b, s.Def.Label + ": " + options[option].Label);
                    b.ToolTip = (options[option].Tip ?? options[option].Label) + (option == std ? "\niRacing default" : "");
                    b.Click += (_, _) => { if (!pick) Apply(s, Setting.Format(options[option].Value), Fill); };
                    var wrap = new Grid { VerticalAlignment = VerticalAlignment.Center };
                    wrap.Children.Add(b);
                    if (option == std)
                    {
                        var mark = new Border { Height = 2, Width = LevelCell * 0.4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 3), IsHitTestVisible = false };
                        mark.SetResourceReference(Border.BackgroundProperty, "Gold");
                        wrap.Children.Add(mark);
                    }
                    cell = wrap;
                }
                else
                {
                    var none = new Border { Height = 28, Margin = new Thickness(LevelGap / 2, 0, LevelGap / 2, 0), Opacity = 0.35, ToolTip = "Not an option for this setting" };
                    none.SetResourceReference(Border.BackgroundProperty, "MeterTrack");
                    cell = none;
                }
                Grid.SetColumn(cell, level + 1);
                grid.Children.Add(cell);
            }
            host.Opacity = s.IsInert ? 0.6 : 1;
            AddUnsavedEdge(grid, host, s, grow);
            host.Child = grid;
        }
        Fill();
        return host;
    }

    private const double EdgeWidth = 2, EdgeGrowMs = 240;

    private static Border AddUnsavedEdge(Grid grid, Border host, Setting s, bool grow)
    {
        var edge = new Border
        {
            Width = EdgeWidth, HorizontalAlignment = HorizontalAlignment.Left, IsHitTestVisible = false,
            Margin = new Thickness(-host.Padding.Left, -host.Padding.Top, 0, -host.Padding.Bottom), RenderTransformOrigin = new Point(0.5, 0),
        };
        edge.SetResourceReference(Border.BackgroundProperty, "Accent");
        Grid.SetRowSpan(edge, Math.Max(1, grid.RowDefinitions.Count));
        grid.Children.Add(edge);
        ShowEdge(edge, s, grow);
        return edge;
    }

    private static void ShowEdge(Border edge, Setting s, bool grow)
    {
        edge.Visibility = s.IsChanged ? Visibility.Visible : Visibility.Collapsed;
        if (!grow || !s.IsChanged || !Motion.Flourish) return;
        var scale = new ScaleTransform();
        edge.RenderTransform = scale;
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, Reveal.Keys(EdgeGrowMs, Motion.Ease, (0, 0), (EdgeGrowMs, 1)));
    }

    private const double SweepStepMs = 120, SweepHoldMs = 520;

    private Task WrittenSweep()
    {
        var chips = _pendingChips?.Children.OfType<Border>().ToList() ?? new List<Border>();
        if (!Motion.Flourish || chips.Count == 0) return Task.CompletedTask;
        var done = new TaskCompletionSource();
        if (_writeButton is not null) _writeButton.IsEnabled = false;
        int next = 0;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SweepStepMs) };
        timer.Tick += (_, _) =>
        {
            if (next < chips.Count)
            {
                var chip = chips[next++];
                chip.SetResourceReference(Border.BackgroundProperty, "SageSoft");
                if (chip.Child is DockPanel line)
                    foreach (var part in line.Children.OfType<FrameworkElement>())
                    {
                        if (part is Button drop) drop.Visibility = Visibility.Hidden;
                        if (part is TextBlock text) text.SetResourceReference(TextBlock.ForegroundProperty, "SageDeep");
                    }
                Reveal.PopIn(chip, from: 0.8);
                return;
            }
            timer.Stop();
            var hold = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(SweepHoldMs) };
            hold.Tick += (_, _) =>
            {
                hold.Stop();
                done.TrySetResult();
            };
            hold.Start();
        };
        timer.Start();
        if (_pendingCount is not null) TextCase.Set(_pendingCount, "Written to iRacing");
        return done.Task;
    }

    private sealed class AreaBand : Tile
    {
        private readonly TextBlock _total = Number("Heading"), _changed = Number("AccentText"), _unsaved = Number("AccentText");

        private static TextBlock Number(string brush)
        {
            var number = RaceParts.LiveFigure("", 26, brush, italic: false, heavy: false);
            number.HorizontalAlignment = HorizontalAlignment.Left;
            return number;
        }

        public AreaBand()
        {
            Style = Ui.Res<Style>("Card");
            Padding = new Thickness(16, 11, 16, 11);
            Margin = new Thickness(0, 0, 0, 16);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Cell(_total, "settings here"));
            row.Children.Add(Cell(_changed, "changed from default"));
            row.Children.Add(Cell(_unsaved, "not written yet"));
            Child = row;
        }

        private static StackPanel Cell(FrameworkElement value, string caption)
        {
            var cell = new StackPanel { Margin = new Thickness(0, 0, 34, 0), VerticalAlignment = VerticalAlignment.Bottom };
            cell.Children.Add(value);
            cell.Children.Add(new TextBlock { Text = caption, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 3, 0, 0) });
            return cell;
        }

        public void Show(IReadOnlyList<Setting> listed)
        {
            TextCase.Set(_total, listed.Count.ToString(CultureInfo.CurrentCulture));
            ShowCount(_changed, listed.Count(s => s.IsModified));
            ShowCount(_unsaved, listed.Count(s => s.IsChanged));
        }

        private static void ShowCount(TextBlock number, int count)
        {
            TextCase.Set(number, count.ToString(CultureInfo.CurrentCulture));
            number.SetResourceReference(TextBlock.ForegroundProperty, count > 0 ? "AccentText" : "TextFaint");
        }
    }

    private Action? _refill;
    private WrapPanel? _pendingChips;

    private void FillPendingChips(List<Setting> pending)
    {
        if (_pendingChips is null) return;
        var known = _pendingChips.Children.OfType<FrameworkElement>().Select(c => c.Tag).OfType<string>().ToHashSet();
        _pendingChips.Children.Clear();
        foreach (var s in pending.Take((int)TrayChips))
        {
            var chip = new Border { Padding = new Thickness(8, 0, 2, 0), Margin = new Thickness(0, 6, 6, 0), Height = 24, Tag = s.Id };
            chip.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
            var line = new DockPanel();
            var drop = Ui.IconButton("I.X", "Drop this change (keep " + s.Show(s.Applied) + ")", () =>
            {
                Session!.Discard(s);
                UpdatePending();
                _refill?.Invoke();
            });
            drop.Width = drop.Height = 20;
            drop.MinHeight = 0;
            DockPanel.SetDock(drop, Dock.Right);
            line.Children.Add(drop);
            var text = TextFit.TipWhenTrimmed(new TextBlock { Text = $"{s.Def.Label}: {s.Show(s.Applied)} → {s.Show(s.Draft)}", MaxWidth = TrayChipText, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            text.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            line.Children.Add(text);
            chip.Child = line;
            _pendingChips.Children.Add(chip);
            if (known.Count > 0 && !known.Contains(s.Id)) Reveal.PopIn(chip, from: 0.6);
        }
        if (pending.Count > TrayChips)
        {
            var more = Ui.LabeledButton("I.Eye", $"{pending.Count - (int)TrayChips} more", () => _ = Review(), "GhostButton");
            more.Margin = new Thickness(0, 6, 0, 0);
            _pendingChips.Children.Add(more);
        }
    }
}
