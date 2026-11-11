using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Core.Controls;
using Core.Services;
using Core.SimRacing;
using Core.SimRacing.IracingFiles;
using Core.SimRacing.Race;
using Core.Theme;

namespace Core.Views.Pages;

internal static class IracingKeybindsView
{
    public static KeybindsBoard Build(AppServices app, Workspace ws) => new(app, ws);

    public static FrameworkElement? ProfileRow(AppServices app, Action changed)
    {
        var keys = app.IracingKeys;
        if (keys.Profiles.Count == 0) return null;
        var pick = Ui.Combo(ProfileChoices(keys), keys.Profile ?? "", c =>
        {
            keys.SelectProfile(c.Id == keys.DefaultProfile ? null : c.Id);
            app.Save();
            changed();
        }, 211);
        var row = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var label = new TextBlock { Text = "Control profile", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);
        DockPanel.SetDock(pick, Dock.Left);
        row.Children.Add(pick);
        row.Children.Add(new TextBlock
        {
            Text = ProfileExplained, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        });
        return row;
    }

    public const string ProfileExplained = "Each iRacing control profile has its own keys, and cars can use different profiles. Changes go to the profile picked here.";

    public static List<Choice> ProfileChoices(IracingKeybinds keys)
    {
        var choices = keys.Profiles.Select(p => new Choice(p, p == keys.DefaultProfile ? p + " (iRacing's default)" : p)).ToList();
        choices.Add(new Choice("", "No profile (Documents\\iRacing\\controls.cfg)"));
        return choices;
    }
}

internal sealed class KeybindsBoard : StackPanel
{
    private enum Filter { All, Set, Keyboard, NotSet, Changed, Wheel }

    private const double RailWidth = 340, RowHeight = 36, SlotWidth = 26, GearWidth = 86;
    private static string? _lastGroup;

    private readonly AppServices _app;
    private readonly Workspace _ws;
    private readonly IracingKeybinds _keys;
    private readonly Border _summary = new();
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _rail = new();
    private readonly Border _pane = new();
    private readonly TextBox _search = Ui.Search(new TextBox { Tag = "Search keybinds", Width = 300, VerticalAlignment = VerticalAlignment.Center });
    private readonly HotkeyBox _findBox = new() { Placeholder = "Find a key", ForSearch = true, NumpadByPosition = true, Width = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _setNumber = SetNumber();

    private static TextBlock SetNumber()
    {
        var number = RaceParts.LiveFigure("", 26, "Heading");
        number.HorizontalAlignment = HorizontalAlignment.Left;
        number.VerticalAlignment = VerticalAlignment.Center;
        return number;
    }
    private List<HotkeyEntry> _entries = new();
    private Filter _filter;
    private string _query = "";
    private string? _find;
    private bool _quiet;
    private Clash? _clash;

    private sealed record Clash(HotkeyEntry Entry, HotkeyEntry Other, string Keys, string? Before);

    public KeybindsBoard(AppServices app, Workspace ws)
    {
        _app = app;
        _ws = ws;
        _keys = app.IracingKeys;
        _search.TextChanged += (_, _) =>
        {
            if (_quiet) return;
            _query = _search.Text.Trim();
            if (_find is not null) { _quiet = true; _findBox.SetHotkey(null); _quiet = false; _find = null; }
            _clash = null;
            Render();
        };
        _findBox.ToolTip = "Press a key combination to see what it does";
        _findBox.HotkeyChanged += hk =>
        {
            _find = hk;
            if (hk is not null && _query.Length > 0) { _quiet = true; _search.Text = ""; _quiet = false; _query = ""; }
            _clash = null;
            Refresh();
        };
        _findBox.Margin = new Thickness(6, 0, 12, 0);
        Build();
    }

    private static void Detach(FrameworkElement part)
    {
        switch (part.Parent)
        {
            case Panel panel: panel.Children.Remove(part); break;
            case Decorator decorator: decorator.Child = null; break;
        }
    }

    private void Build()
    {
        Children.Clear();
        foreach (var part in new FrameworkElement[] { _summary, _filters, _rail, _pane, _search, _findBox }) Detach(part);
        _keys.Refresh();
        _entries = Load();
        Children.Add(Band());
        if (_keys.File is null || _entries.Count == 0) return;
        Children.Add(Toolbar());
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RailWidth) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        var rail = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top, Child = _rail, ClipToBounds = true };
        body.Children.Add(rail);
        var pane = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top, Child = _pane, ClipToBounds = true };
        Grid.SetColumn(pane, 2);
        body.Children.Add(pane);
        Children.Add(body);
        Render();
    }

    private List<HotkeyEntry> Load() =>
        _app.Hotkeys.Catalog().Where(e => e.Owner == HotkeyOwner.Iracing && _app.Hotkeys.IsAvailable(e) && e.Path.Count > 1).ToList();

    private List<string> Groups() => _entries.Select(GroupOf).Distinct().ToList();

    private static string GroupOf(HotkeyEntry e) => e.Path[1];

    private static bool IsSet(HotkeyEntry e) => !string.IsNullOrWhiteSpace(e.Get());

    private static bool OnWheel(HotkeyEntry e) => e.Locked is not null;

    private static bool Changed(HotkeyEntry e) => e.Locked is null && e.Default is { } d && !KeybindTree.Same(e, e.Get(), d);

    private bool Filtering => _filter != Filter.All || _query.Length > 0 || _find is not null;

    private bool Passes(HotkeyEntry e, Filter filter) => filter switch
    {
        Filter.Set => IsSet(e),
        Filter.Keyboard => IsSet(e) && !OnWheel(e),
        Filter.NotSet => !IsSet(e),
        Filter.Changed => Changed(e),
        Filter.Wheel => OnWheel(e),
        _ => true,
    };

    private bool Shown(HotkeyEntry e) =>
        Passes(e, _filter)
        && (_query.Length == 0 || KeybindTree.Matches(e, _query))
        && (_find is null || (!OnWheel(e) && KeybindTree.Same(e, e.Get(), _find)));

    private void Render()
    {
        _entries = Load();
        if (_entries.Count == 0)
        {
            Build();
            return;
        }
        RenderSummary();
        RenderFilters();
        RenderRail();
        RenderPane();
    }

    private void Refresh() => Dispatcher.BeginInvoke(Render, System.Windows.Threading.DispatcherPriority.Input);

    private const double StripHeight = 48;

    private FrameworkElement Band()
    {
        var grid = new Grid { MinHeight = StripHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(ProfileBlock());
        Grid.SetColumn(_summary, 1);
        grid.Children.Add(_summary);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), ClipToBounds = true, Child = grid };
    }

    private FrameworkElement ProfileBlock()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var eyebrow = new TextBlock { Text = "Profile", Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Ui.AddInfo(eyebrow, IracingKeybindsView.ProfileExplained + " iRacing saves its own copy of the keys when it exits, so changes here only work while it's closed. Wheel and button-box bindings are shown but set in iRacing. To bind a number-pad key, turn Num Lock off first.");
        row.Children.Add(eyebrow);
        row.Children.Add(ProfilePicker());

        var state = new TextBlock
        {
            Text = _ws.SimRunning ? "iRacing is running: close it to change keybinds" : "Saved as you change them",
            Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            ToolTip = _ws.SimRunning ? null : "A backup is made before the first change",
        };
        if (_ws.SimRunning) state.SetResourceReference(TextBlock.ForegroundProperty, "ApricotDeep");
        row.Children.Add(state);
        if (_keys.CarFiles > 0)
        {
            var cars = new TextBlock
            {
                Text = "· " + RaceFormat.Count(_keys.CarFiles, "car") + (_keys.CarFiles == 1 ? " uses its own controls" : " use their own controls"),
                Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0),
                ToolTip = "Their controls.cfg files in the setups folder aren't changed here",
            };
            row.Children.Add(cars);
        }
        if (_keys.Error is { } error)
        {
            var note = new TextBlock { Text = error, ToolTip = error, Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 260, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "ApricotDeep");
            row.Children.Add(note);
        }

        var edge = Hairline.Frame(new Border { Child = row, Padding = new Thickness(0, 0, 14, 0) }, Sides.Right);
        edge.SetResourceReference(Border.BorderBrushProperty, "Line");
        edge.Margin = new Thickness(0, 9, 0, 9);
        return edge;
    }

    private FrameworkElement ProfilePicker()
    {
        string name = _keys.Profile ?? "No profile";
        string? sub = _keys.Profile is null ? "Documents\\iRacing\\controls.cfg" : _keys.Profile == _keys.DefaultProfile ? "iRacing's default" : null;

        var text = TextFit.TipWhenTrimmed(new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Inlines.Add(new System.Windows.Documents.Run(name) { FontWeight = FontWeights.Bold });
        if (sub is not null)
        {
            var rest = new System.Windows.Documents.Run(" · " + sub);
            rest.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextMuted");
            if (_keys.Profile is null) TextCase.Verbatim(rest);
            text.Inlines.Add(rest);
        }
        var glyph = RaceParts.Glyph("I.Keyboard", 14, "TextMuted");
        glyph.Margin = new Thickness(0, 0, 8, 0);
        bool canPick = _keys.Profiles.Count > 0;
        var face = canPick ? Ui.MenuFace(text, glyph) : new DockPanel { Children = { glyph, text } };

        var button = new Button
        {
            Style = Ui.Res<Style>("MenuButton"), Content = face, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center,
            Width = 240, IsHitTestVisible = canPick,
            ToolTip = _keys.FilePath is { } path ? TextCase.PathTip(path) : null,
        };
        AutomationProperties.SetName(button, "Control profile " + name);
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, MinWidth = button.ActualWidth };
            Ui.DropBelow(menu);
            foreach (var choice in IracingKeybindsView.ProfileChoices(_keys))
            {
                var item = new MenuItem { Header = choice.Text, IsCheckable = true, IsChecked = string.Equals(choice.Id, _keys.Profile ?? "", StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => PickProfile(choice.Id);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return button;
    }

    private void RenderSummary()
    {
        int total = _entries.Count, set = _entries.Count(IsSet), wheel = _entries.Count(OnWheel), keyboard = set - wheel, none = total - set, changed = _entries.Count(Changed);
        TextCase.Set(_setNumber, set.ToString(CultureInfo.CurrentCulture));

        var row = new DockPanel { Margin = new Thickness(14, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, LastChildFill = true };
        if (changed > 0)
        {
            var count = RaceParts.Big(changed.ToString(CultureInfo.CurrentCulture), 18, "AccentText", heavy: false);
            count.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            count.LineHeight = 20;
            var label = new TextBlock { Text = "changed from default", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 1, 0, 0) };
            var chip = new Button
            {
                Style = Ui.Res<Style>("GhostButton"), Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { count, label } },
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), ToolTip = "Show only the keybinds changed from iRacing's default",
            };
            chip.Click += (_, _) => { _filter = Filter.Changed; _clash = null; Render(); };
            DockPanel.SetDock(chip, Dock.Right);
            row.Children.Add(chip);
        }
        (_setNumber.Parent as Panel)?.Children.Remove(_setNumber);
        var of = new TextBlock { Text = $"of {total} keybinds set", Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 14, 0) };
        var figure = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _setNumber, of } };
        DockPanel.SetDock(figure, Dock.Left);
        row.Children.Add(figure);

        var bar = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center };
        void Part(int n, string brush)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(n, GridUnitType.Star) });
            if (n == 0) return;
            var piece = new Border { Margin = new Thickness(1, 0, 1, 0), RenderTransform = new SkewTransform(-20, 0), RenderTransformOrigin = new Point(0.5, 0.5) };
            piece.SetResourceReference(Border.BackgroundProperty, brush);
            Grid.SetColumn(piece, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(piece);
        }
        Part(keyboard, KeyboardBrush);
        Part(wheel, WheelBrush);
        Part(none, NotSetBrush);
        row.Children.Add(bar);
        _summary.Child = row;
    }

    private const string KeyboardBrush = "Accent", WheelBrush = "TextFaint", NotSetBrush = "LineStrong";

    private FrameworkElement Toolbar()
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = false };
        row.Children.Add(_search);
        row.Children.Add(_findBox);
        row.Children.Add(_filters);
        return row;
    }

    private void RenderFilters()
    {
        _filters.Children.Clear();
        foreach (var (filter, label, dot) in new (Filter, string, string?)[]
        {
            (Filter.All, "All", null), (Filter.Set, "Set", null), (Filter.Keyboard, "Keyboard", KeyboardBrush), (Filter.Wheel, "Wheel or controller", WheelBrush),
            (Filter.NotSet, "Not set", NotSetBrush), (Filter.Changed, "Changed", null),
        })
        {
            int n = _entries.Count(e => Passes(e, filter));
            var count = new TextBlock { Text = n.ToString(CultureInfo.CurrentCulture), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            count.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            var face = new StackPanel { Orientation = Orientation.Horizontal };
            if (dot is not null)
            {
                var swatch = new Border { Width = 9, Height = 9, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
                swatch.SetResourceReference(Border.BackgroundProperty, dot);
                face.Children.Add(swatch);
            }
            face.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            face.Children.Add(count);
            var button = new Button { Content = face, Margin = Ui.AfterButton };
            Ui.SetToggled(button, _filter == filter);
            var pick = filter;
            button.Click += (_, _) => { _filter = pick; _clash = null; Render(); };
            AutomationProperties.SetName(button, label + " filter");
            _filters.Children.Add(button);
        }
    }

    private void ClearSearch()
    {
        _quiet = true;
        _search.Text = "";
        _findBox.SetHotkey(null);
        _quiet = false;
        _query = "";
        _find = null;
        _filter = Filter.All;
    }

    private void RenderRail()
    {
        _rail.Children.Clear();
        var groups = Groups();
        string current = Current(groups);
        bool filtering = Filtering;
        for (int i = 0; i < groups.Count; i++)
        {
            string group = groups[i];
            var inside = _entries.Where(e => GroupOf(e) == group).ToList();
            int set = inside.Count(IsSet), found = inside.Count(Shown);
            bool on = !filtering && group == current;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = TextFit.TipWhenTrimmed(new TextBlock { Text = group, FontSize = RailText, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 16, 0) });
            if (on) name.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            grid.Children.Add(name);
            var count = new TextBlock { Style = Ui.Res<Style>("Caption"), FontSize = RailCount, VerticalAlignment = VerticalAlignment.Center };
            count.Inlines.Add(new System.Windows.Documents.Run((filtering ? found : set).ToString(CultureInfo.CurrentCulture)) { FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("Text") });
            count.Inlines.Add(new System.Windows.Documents.Run(filtering ? " found" : $" / {inside.Count}"));
            Grid.SetColumn(count, 1);
            grid.Children.Add(count);

            var holder = new Grid();
            holder.Children.Add(new Border { Height = RailRowHeight, Padding = new Thickness(12, 0, 12, 0), Child = grid });
            if (on)
            {
                holder.SetResourceReference(Panel.BackgroundProperty, "AccentSoft");
                var bar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
                bar.SetResourceReference(Border.BackgroundProperty, "Accent");
                holder.Children.Add(bar);
            }
            var button = new Button
            {
                Style = Ui.Res<Style>("QuietButton"), Content = holder, Padding = new Thickness(0), BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Opacity = filtering && found == 0 ? 0.4 : 1,
            };
            AutomationProperties.SetName(button, group);
            button.Click += (_, _) =>
            {
                _lastGroup = group;
                ClearSearch();
                _clash = null;
                Render();
            };
            var cell = i == 0 ? (FrameworkElement)button : Hairline.Frame(new Border { Child = button }, Sides.Top);
            if (cell is Border edged) edged.SetResourceReference(Border.BorderBrushProperty, "Line");
            _rail.Children.Add(cell);
        }
    }

    private static string Current(List<string> groups) => _lastGroup is { } g && groups.Contains(g) ? g : groups.FirstOrDefault() ?? "";

    private const double RailText = 13.5, RailCount = 13, RailRowHeight = 32;

    private void RenderPane()
    {
        var stack = new StackPanel();
        if (!Filtering)
        {
            string group = Current(Groups());
            var inside = _entries.Where(e => GroupOf(e) == group).ToList();
            var head = Head(stack);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = group, Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            title.Children.Add(new TextBlock { Text = $"{inside.Count(IsSet)} of {inside.Count} set", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
            var changed = inside.Where(Changed).ToList();
            if (changed.Count > 0)
            {
                var reset = Ui.LabeledButton("I.Undo", "Back to default", () => ResetGroup(group, changed));
                reset.ToolTip = $"Put {RaceFormat.Count(changed.Count, "keybind")} in {group} back to iRacing's default";
                DockPanel.SetDock(reset, Dock.Right);
                head.Children.Add(reset);
            }
            head.Children.Add(title);
            stack.Children.Add(Sections(inside, new Thickness(18, 4, 18, 18)));
            _pane.Child = stack;
            return;
        }

        var shown = _entries.Where(Shown).ToList();
        var top = Head(stack);
        var heading = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (_find is { } find)
        {
            heading.Children.Add(new TextBlock { Text = "What", Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            heading.Children.Add(Caps(find));
            heading.Children.Add(new TextBlock { Text = "does", Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) });
        }
        else
        {
            string text = _query.Length > 0 ? $"Results for “{_query}”" : _filter switch
            {
                Filter.Set => "Set", Filter.Keyboard => "On the keyboard", Filter.NotSet => "Not set", Filter.Changed => "Changed from iRacing's default", _ => "On a wheel or controller",
            };
            heading.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = text, Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520, Margin = new Thickness(0, 0, 10, 0) }));
        }
        heading.Children.Add(new TextBlock { Text = RaceFormat.Count(shown.Count, "keybind"), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        var clear = Ui.LabeledButton("I.X", "Clear", () => { ClearSearch(); _clash = null; Render(); });
        DockPanel.SetDock(clear, Dock.Right);
        top.Children.Add(clear);
        top.Children.Add(heading);

        if (shown.Count == 0)
        {
            stack.Children.Add(_find is not null
                ? Ui.EmptyState("I.Keyboard", "Nothing uses this key", "It's free to use.")
                : Ui.EmptyState("I.Search", "No keybinds match", "Try another word or filter."));
            _pane.Child = stack;
            return;
        }
        foreach (var group in Groups())
        {
            var inside = shown.Where(e => GroupOf(e) == group).ToList();
            if (inside.Count == 0) continue;
            var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 16, 18, 0) };
            title.Children.Add(new TextBlock { Text = group, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(title);
            stack.Children.Add(Sections(inside, new Thickness(18, 0, 18, 6)));
        }
        _pane.Child = stack;
    }

    private static DockPanel Head(StackPanel into)
    {
        var head = new DockPanel { Margin = new Thickness(18, 12, 14, 12) };
        var line = Hairline.Frame(new Border { Child = head }, Sides.Bottom);
        line.SetResourceReference(Border.BorderBrushProperty, "Line");
        into.Children.Add(line);
        return head;
    }

    private UIElement Sections(List<HotkeyEntry> entries, Thickness margin)
    {
        var stack = new StackPanel { Margin = margin };
        var blocks = new List<(string? Title, List<HotkeyEntry> Items)>();
        List<HotkeyEntry>? gears = null;
        foreach (var cluster in entries.GroupBy(e => e.Cluster ?? e.Id).Select(g => g.ToList()))
        {
            string key = cluster[0].Cluster ?? cluster[0].Id;
            if (key == "Gears") { gears = cluster; continue; }
            string? title = cluster.Count > 1 || Titles.ContainsKey(key) || key.Contains(' ') ? SectionTitle(key) : null;
            if (title is null && blocks.Count > 0 && blocks[^1].Title is null) blocks[^1].Items.AddRange(cluster);
            else blocks.Add((title, cluster));
        }
        if (blocks.Count > 0)
        {
            var columns = new MasonryPanel { MinColumnWidth = 340, MaxColumns = 2, Gap = 28, Stable = true };
            bool titled = blocks.Count > 1 && blocks.Any(b => b.Title is not null);
            foreach (var (title, items) in blocks)
            {
                var block = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
                if (title is not null) block.Children.Add(SectionHead(title));
                else if (titled) block.Children.Add(SectionHead(""));
                foreach (var e in items) block.Children.Add(Row(e));
                columns.Children.Add(block);
            }
            stack.Children.Add(columns);
            if (_clash is { } rowClash && blocks.Any(b => b.Items.Any(e => e.Id == rowClash.Entry.Id)))
            {
                var strip = ClashStrip(rowClash, named: true);
                strip.Margin = new Thickness(10, 12, 0, 0);
                stack.Children.Add(strip);
            }
        }
        if (gears is not null)
        {
            var block = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            block.Children.Add(SectionHead("Gears"));
            var tiles = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            foreach (var e in gears) tiles.Children.Add(Gear(e));
            block.Children.Add(tiles);
            if (_clash is { } c && gears.Any(g => g.Id == c.Entry.Id)) block.Children.Add(ClashStrip(c));
            stack.Children.Add(block);
        }
        return stack;
    }

    private static TextBlock SectionHead(string title) =>
        new() { Text = title, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 0, 0, 4) };

    private static readonly Dictionary<string, string> Titles = new(StringComparer.Ordinal)
    {
        ["Shift"] = "Shifting", ["Limiters"] = "Limiters", ["Engine"] = "Engine", ["Boost"] = "Boost and aids", ["Reset"] = "Car",
        ["Driver"] = "Driver", ["ResetPoint"] = "Reset point", ["Look"] = "Look around", ["Recenter"] = "Mirror and recenter",
        ["FFB"] = "Force feedback", ["UI"] = "Interface", ["Capture"] = "Screenshots and video", ["Telemetry"] = "Telemetry",
        ["Displays"] = "Displays", ["MGU-K"] = "MGU-K", ["Custom sector"] = "Custom sectors",
    };

    internal static string SectionTitle(string cluster) =>
        Titles.TryGetValue(cluster, out var title) ? title
        : cluster.Contains(' ') ? cluster
        : IracingKeybinds.LabelOf(cluster);

    private FrameworkElement Row(HotkeyEntry entry)
    {
        bool set = IsSet(entry), changed = Changed(entry), locked = OnWheel(entry);
        var grid = new Grid { MinHeight = RowHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SlotWidth + 4) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(RowHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = TextFit.TipWhenTrimmed(new TextBlock { Text = entry.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 10, 0) });
        name.SetResourceReference(TextBlock.ForegroundProperty, set ? "Text" : "TextMuted");
        grid.Children.Add(name);

        var keys = new Border { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        TextBlock? hint = null;
        if (locked) keys.Child = Wheel(entry.Locked!);
        else if (set) keys.Child = Caps(entry.Get()!);
        else
        {
            hint = new TextBlock { Text = "Set keybind", FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            var action = Hairline.Frame(new Border { Height = 24, Padding = new Thickness(8, 0, 8, 0), Child = hint });
            action.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
            keys.Child = action;
        }
        Grid.SetColumn(keys, 1);
        grid.Children.Add(keys);

        Button? undo = null;
        if (changed)
        {
            undo = Ui.IconButton("I.Undo", "Back to iRacing's default: " + Core.Hotkeys.Hotkey.Display(entry.Default), () => Apply(entry, entry.Default), "QuietIconButton");
            undo.Width = undo.Height = SlotWidth;
            undo.Opacity = 0;
            undo.HorizontalAlignment = HorizontalAlignment.Right;
            undo.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(undo, 2);
            grid.Children.Add(undo);
        }

        bool clashing = _clash is { } clash && (clash.Entry.Id == entry.Id || clash.Other.Id == entry.Id);
        var holder = new Grid { Background = Brushes.Transparent, Children = { grid } };
        void Rest()
        {
            if (clashing) holder.SetResourceReference(Panel.BackgroundProperty, "ApricotSoft");
            else holder.Background = Brushes.Transparent;
        }
        Rest();
        if (clashing)
        {
            var bar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
            bar.SetResourceReference(Border.BackgroundProperty, "Apricot");
            holder.Children.Add(bar);
        }
        else if (changed)
        {
            var bar = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Height = RowHeight - 18, Margin = new Thickness(0, 9, 0, 0) };
            bar.SetResourceReference(Border.BackgroundProperty, "Accent");
            holder.Children.Add(bar);
        }
        var row = Hairline.Frame(new Border { Child = holder, Cursor = locked ? null : Cursors.Hand, ToolTip = locked ? entry.Locked : null }, Sides.Bottom);
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        AutomationProperties.SetName(row, entry.Label);
        row.MouseEnter += (_, _) =>
        {
            holder.SetResourceReference(Panel.BackgroundProperty, "Hover");
            if (undo is not null) undo.Opacity = 1;
            hint?.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        };
        row.MouseLeave += (_, _) =>
        {
            Rest();
            if (undo is not null) undo.Opacity = 0;
            hint?.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        };
        if (!locked)
            row.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject d && InButton(d)) return;
                Edit(entry, keys, 170);
            };
        return row;
    }

    private FrameworkElement Gear(HotkeyEntry entry)
    {
        bool set = IsSet(entry), locked = OnWheel(entry);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var big = RaceParts.Big(GearName(entry.Label), 20, set ? "Heading" : "TextMuted", heavy: false);
        big.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(big);
        var keyText = TextFit.TipWhenTrimmed(new TextBlock
        {
            Text = locked ? "Wheel" : set ? Core.Hotkeys.Hotkey.Display(entry.Get()) : "Not set", Style = Ui.Res<Style>("Caption"),
            HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 3, 4, 0),
        });
        keyText.SetResourceReference(TextBlock.ForegroundProperty, locked ? "TextMuted" : set ? "AccentText" : "TextFaint");
        if (set) keyText.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(keyText);
        var keys = new Border { Child = stack, VerticalAlignment = VerticalAlignment.Center };
        var tile = Hairline.Frame(new Border
        {
            Width = GearWidth, Height = 58, Margin = new Thickness(0, 0, 6, 6), Child = keys, Cursor = locked ? null : Cursors.Hand,
            ToolTip = locked ? entry.Locked : entry.Label,
        });
        tile.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        tile.SetResourceReference(Border.BorderBrushProperty, _clash?.Entry.Id == entry.Id ? "Apricot" : "Line");
        AutomationProperties.SetName(tile, entry.Label);
        tile.MouseEnter += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
        tile.MouseLeave += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, _clash?.Entry.Id == entry.Id ? "Apricot" : "Line");
        if (!locked) tile.MouseLeftButtonUp += (_, _) => Edit(entry, keys, GearWidth - 8);
        return tile;
    }

    internal static string GearName(string label)
    {
        var rest = label.StartsWith("Gear ", StringComparison.OrdinalIgnoreCase) ? label[5..] : label;
        return rest.ToLowerInvariant() switch { "reverse" => "R", "neutral" => "N", _ => rest };
    }

    private void Edit(HotkeyEntry entry, Border host, double width)
    {
        if (host.Child is HotkeyBox) return;
        var shown = host.Child;
        var align = host.HorizontalAlignment;
        bool done = false;
        var box = new HotkeyBox { NumpadByPosition = true, Width = width, VerticalAlignment = VerticalAlignment.Center };
        box.SetHotkey(entry.Get());
        box.HotkeyChanged += hk =>
        {
            done = true;
            Dispatcher.BeginInvoke(() => Apply(entry, hk), System.Windows.Threading.DispatcherPriority.Input);
        };
        box.LostKeyboardFocus += (_, _) =>
        {
            if (done) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (!ReferenceEquals(host.Child, box)) return;
                host.Child = shown;
                host.HorizontalAlignment = align;
            }, System.Windows.Threading.DispatcherPriority.Input);
        };
        host.HorizontalAlignment = HorizontalAlignment.Stretch;
        host.Child = box;
        box.Loaded += (_, _) => box.Focus();
    }

    private void Apply(HotkeyEntry entry, string? hotkey)
    {
        string? before = entry.Get();
        bool same = string.IsNullOrWhiteSpace(before) ? string.IsNullOrWhiteSpace(hotkey) : KeybindTree.Same(entry, before, hotkey);
        if (same)
        {
            Render();
            return;
        }
        if (_app.Hotkeys.Change(entry, hotkey) is { } problem)
        {
            _app.Notify("Keybind not changed", problem, NoticeKind.Warning, important: true);
            _clash = null;
            Render();
            return;
        }
        _entries = Load();
        _clash = hotkey is null ? null : FindClash(entry, hotkey, before);
        Render();
    }

    private Clash? FindClash(HotkeyEntry entry, string hotkey, string? before)
    {
        var mine = _entries.FirstOrDefault(e => e.Id == entry.Id) ?? entry;
        var other = _entries.FirstOrDefault(o => o.Id != entry.Id && !OnWheel(o) && KeybindTree.Same(o, o.Get(), hotkey)
            && IracingKeybinds.ActiveTogether(GroupOf(o), GroupOf(mine)));
        return other is null ? null : new Clash(mine, other, hotkey, before);
    }

    private FrameworkElement ClashStrip(Clash clash, bool named = false)
    {
        var line = new WrapPanel { Margin = new Thickness(10, 2, 0, 9) };
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 4), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ApricotDeep");
        string where = GroupOf(clash.Other) == GroupOf(clash.Entry) ? "" : $" ({GroupOf(clash.Other)})";
        text.Text = named
            ? $"{Core.Hotkeys.Hotkey.Display(clash.Keys)} for “{clash.Entry.Label}” also does “{clash.Other.Label}”{where}"
            : $"{Core.Hotkeys.Hotkey.Display(clash.Keys)} also does “{clash.Other.Label}”{where}";
        line.Children.Add(text);
        Button Act(string label, string tip, Action run)
        {
            var b = new Button { Content = label, Style = Ui.Res<Style>("GhostButton"), ToolTip = tip, Margin = new Thickness(0, 0, Ui.ButtonGap, Ui.ButtonGap) };
            b.Click += (_, _) => run();
            return b;
        }
        line.Children.Add(clash.Before is { Length: > 0 } before
            ? Act("Swap them", $"“{clash.Other.Label}” gets {Core.Hotkeys.Hotkey.Display(before)}", () => Resolve(clash, clash.Other, before))
            : Act("Clear the other", $"“{clash.Other.Label}” is left with no key", () => Resolve(clash, clash.Other, null)));
        line.Children.Add(Act("Keep both", "Both do something when you press it", () => { _clash = null; Render(); }));
        line.Children.Add(Act("Undo", "Put this keybind back", () => Resolve(clash, clash.Entry, clash.Before)));
        return line;
    }

    private void Resolve(Clash clash, HotkeyEntry target, string? hotkey)
    {
        _clash = null;
        if (_app.Hotkeys.Change(target, string.IsNullOrWhiteSpace(hotkey) ? null : hotkey) is { } problem)
            _app.Notify("Keybind not changed", problem, NoticeKind.Warning, important: true);
        Render();
    }

    private void ResetGroup(string group, List<HotkeyEntry> changed)
    {
        var before = changed.Select(e => (Entry: e, Key: e.Get())).ToList();
        for (int i = 0; i < changed.Count; i++)
            if (_app.Hotkeys.Change(changed[i], changed[i].Default) is { } problem)
            {
                for (int k = i - 1; k >= 0; k--) _app.Hotkeys.Change(before[k].Entry, before[k].Key);
                _app.Notify("Keybinds not changed", problem, NoticeKind.Warning, important: true);
                Render();
                return;
            }
        _clash = null;
        Render();
        _app.Notify($"{group} keybinds back to default", RaceFormat.Count(changed.Count, "keybind") + " changed", action: new NoticeAction("Undo", () =>
        {
            string? failed = null;
            foreach (var (entry, key) in before)
                if (_app.Hotkeys.Change(entry, key) is { } problem) failed ??= problem;
            if (failed is not null) _app.Notify("Some keybinds not put back", failed, NoticeKind.Warning, important: true);
            Render();
        }));
    }

    private static FrameworkElement Caps(string hotkey)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var parts = Core.Hotkeys.Hotkey.Display(hotkey).Split('+');
        var keys = new List<string>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0) keys.Add(parts[i]);
            else if (i == parts.Length - 1 || (i > 0 && parts[i - 1].Length == 0)) keys.Add("+");
        }
        for (int i = 0; i < keys.Count; i++)
        {
            if (i > 0)
            {
                var plus = new TextBlock { Text = "+", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0) };
                plus.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
                panel.Children.Add(plus);
            }
            var label = new TextBlock { Text = keys[i], FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var cap = new Border { MinWidth = 30, Height = 24, Padding = new Thickness(8, 2, 8, 0), BorderThickness = new Thickness(1, 1, 1, 3), Child = label };
            cap.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            cap.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
            panel.Children.Add(cap);
        }
        return panel;
    }

    private static FrameworkElement Wheel(string tip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(RaceParts.Glyph("I.Gamepad", 13, "TextMuted"));
        var text = new TextBlock { Text = "Wheel", FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        row.Children.Add(text);
        var chip = new Border { Height = 24, Padding = new Thickness(8, 0, 8, 0), Child = row, ToolTip = tip };
        chip.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        return chip;
    }

    private static bool InButton(DependencyObject d)
    {
        for (var cur = d; cur is not null; cur = cur is Visual ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is ButtonBase) return true;
        return false;
    }

    private void PickProfile(string? id)
    {
        _keys.SelectProfile(id == _keys.DefaultProfile ? null : id);
        _app.Save();
        _clash = null;
        Build();
    }

    public void ShowOtherProfile() => PickProfile(IracingKeybindsView.ProfileChoices(_keys).FirstOrDefault(c => c.Id != (_keys.Profile ?? _keys.DefaultProfile))?.Id);

    public void ShowFilter(string which)
    {
        ClearSearch();
        _filter = which switch { "not-set" => Filter.NotSet, "changed" => Filter.Changed, "wheel" => Filter.Wheel, "set" => Filter.Set, "keyboard" => Filter.Keyboard, _ => Filter.All };
        _clash = null;
        Render();
    }

    public void ShowGroup(string group)
    {
        ClearSearch();
        _lastGroup = group;
        _clash = null;
        Render();
    }

    public void ShowFind(string hotkey)
    {
        ClearSearch();
        _quiet = true;
        _findBox.SetHotkey(hotkey);
        _quiet = false;
        _find = hotkey;
        Render();
    }

    public void ShowClash(string labelA, string labelB)
    {
        ClearSearch();
        var a = _entries.FirstOrDefault(e => e.Label == labelA && !OnWheel(e));
        var b = _entries.FirstOrDefault(e => e.Label == labelB && !OnWheel(e) && IsSet(e));
        if (a is null || b is null) return;
        _lastGroup = GroupOf(a);
        Apply(a, b.Get());
    }
}
