using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.Services;
using PitBox.SimRacing;
using PitBox.SimRacing.IracingFiles;
using PitBox.SimRacing.Race;
using PitBox.Theme;

namespace PitBox.Views.Pages;

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
    private enum Filter { All, Set, NotSet, Changed, Wheel }

    private const double RailWidth = 290, RowHeight = 36, SlotWidth = 26, GearWidth = 86;
    private static string? _lastGroup;

    private readonly AppServices _app;
    private readonly Workspace _ws;
    private readonly IracingKeybinds _keys;
    private readonly Border _summary = new();
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _rail = new();
    private readonly Border _pane = new();
    private readonly TextBox _search = new() { Tag = "Search keybinds", Width = 300, VerticalAlignment = VerticalAlignment.Center };
    private readonly HotkeyBox _findBox = new() { Placeholder = "Find a key", ForSearch = true, NumpadByPosition = true, Width = 150, VerticalAlignment = VerticalAlignment.Center };
    private readonly Odometer _setNumber = new(38, "Heading") { RollMs = 420, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
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
        Build();
    }

    private void Build()
    {
        Children.Clear();
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
        Filter.NotSet => !IsSet(e),
        Filter.Changed => Changed(e),
        Filter.Wheel => OnWheel(e),
        _ => true,
    };

    private bool Shown(HotkeyEntry e) =>
        Passes(e, _filter)
        && (_query.Length == 0 || KeybindTree.Matches(e, _query))
        && (_find is null || (!OnWheel(e) && KeybindTree.Same(e, e.Get(), _find)));

    private static (string Soft, string Deep) HueOf(string group) =>
        KeybindTree.IracingLooks.TryGetValue(group, out var look) ? Hues.Named(look.Hue) : Hues.Accent;

    private static string IconOf(string group) =>
        KeybindTree.IracingLooks.TryGetValue(group, out var look) ? look.Icon : "I.Keyboard";

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

    private FrameworkElement Band()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(ProfileBlock());
        Grid.SetColumn(_summary, 1);
        grid.Children.Add(_summary);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 12), ClipToBounds = true, Child = grid };
    }

    private FrameworkElement ProfileBlock()
    {
        var stack = new StackPanel { Margin = new Thickness(20, 14, 22, 13) };
        var eyebrow = new TextBlock { Text = "Editing control profile", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 7) };
        eyebrow.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        Ui.AddInfo(eyebrow, IracingKeybindsView.ProfileExplained + " iRacing saves its own copy of the keys when it exits, so changes here only work while it's closed. Wheel and button-box bindings are shown but set in iRacing. To bind a number-pad key, turn Num Lock off first.");
        stack.Children.Add(eyebrow);
        stack.Children.Add(ProfilePicker());

        var chips = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        var state = _ws.SimRunning
            ? Ui.Chip("iRacing is running: close it to change keybinds", "ApricotSoft", "ApricotDeep")
            : Ui.Chip("Saved as you change them", "SageSoft", "SageDeep");
        if (!_ws.SimRunning) state.ToolTip = "A backup is made before the first change";
        state.Margin = new Thickness(0, 0, 6, 0);
        chips.Children.Add(state);
        if (_keys.CarFiles > 0)
        {
            var cars = Ui.Chip(RaceFormat.Count(_keys.CarFiles, "car") + (_keys.CarFiles == 1 ? " uses its own controls" : " use their own controls"));
            cars.ToolTip = "Their controls.cfg files in the setups folder aren't changed here";
            chips.Children.Add(cars);
        }
        stack.Children.Add(chips);
        if (_keys.Error is { } error)
        {
            var note = new TextBlock { Text = error, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(0, 8, 0, 0) };
            note.SetResourceReference(TextBlock.ForegroundProperty, "ApricotDeep");
            stack.Children.Add(note);
        }

        var block = new Grid { MinWidth = 420 };
        block.SetResourceReference(Panel.BackgroundProperty, "AccentSoft");
        var bar = new Border { Width = 3, HorizontalAlignment = HorizontalAlignment.Left };
        bar.SetResourceReference(Border.BackgroundProperty, "Accent");
        block.Children.Add(bar);
        block.Children.Add(stack);
        var edge = Hairline.Frame(new Border { Child = block }, Sides.Right);
        edge.SetResourceReference(Border.BorderBrushProperty, "Line");
        return edge;
    }

    private FrameworkElement ProfilePicker()
    {
        string name = _keys.Profile ?? "No profile";
        string? sub = _keys.Profile is null ? "Documents\\iRacing\\controls.cfg" : _keys.Profile == _keys.DefaultProfile ? "iRacing's default" : null;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new Tile { Width = 44, Height = 44, CornerRadius = ThemeManager.Round(0), Child = RaceParts.Glyph("I.Keyboard", 22, "AccentText") };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentSoft");
        grid.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 16, 0) };
        var title = RaceParts.Big(name, 28, "Heading", italic: false, heavy: false);
        title.HorizontalAlignment = HorizontalAlignment.Left;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.MaxWidth = 300;
        title.ToolTip = name;
        text.Children.Add(title);
        if (sub is not null)
        {
            var caption = new TextBlock { Text = sub, Style = Ui.Res<Style>("Caption") };
            if (_keys.Profile is null) TextCase.Verbatim(caption);
            text.Children.Add(caption);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        bool canPick = _keys.Profiles.Count > 0;
        if (canPick)
        {
            var caret = RaceParts.Glyph("I.ChevronDown", 16, "AccentText");
            Grid.SetColumn(caret, 2);
            grid.Children.Add(caret);
        }

        var button = new Button
        {
            Style = Ui.Res<Style>("QuietButton"), Content = grid, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(6, 6, 12, 6), MinWidth = 300, BorderThickness = Hairline.Edges(), IsHitTestVisible = canPick,
            ToolTip = _keys.FilePath is { } path ? TextCase.VerbatimTip(path) : null,
        };
        button.SetResourceReference(Control.BorderBrushProperty, "AccentEdge");
        button.SetResourceReference(Control.BackgroundProperty, "Surface");
        AutomationProperties.SetName(button, "Control profile " + name);
        button.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = PlacementMode.Bottom, MinWidth = button.ActualWidth };
            Ui.DropBelow(menu);
            foreach (var choice in IracingKeybindsView.ProfileChoices(_keys))
            {
                var item = new MenuItem { Header = choice.Text, IsCheckable = true, IsChecked = string.Equals(choice.Id, _keys.Profile ?? "", StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) =>
                {
                    _keys.SelectProfile(choice.Id == _keys.DefaultProfile ? null : choice.Id);
                    _app.Save();
                    _clash = null;
                    Build();
                };
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        };
        return button;
    }

    private void RenderSummary()
    {
        int total = _entries.Count, set = _entries.Count(IsSet), wheel = _entries.Count(OnWheel), keyboard = set - wheel, none = total - set, changed = _entries.Count(Changed);
        _setNumber.Set(set.ToString(CultureInfo.CurrentCulture));

        var top = new DockPanel();
        if (changed > 0)
        {
            var count = RaceParts.Big(changed.ToString(CultureInfo.CurrentCulture), 20, "AccentText", italic: true, heavy: false);
            var label = new TextBlock { Text = "changed from default", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 1, 0, 0) };
            var chip = new Button
            {
                Style = Ui.Res<Style>("GhostButton"), Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { count, label } },
                VerticalAlignment = VerticalAlignment.Center, ToolTip = "Show only the keybinds changed from iRacing's default",
            };
            chip.Click += (_, _) => { _filter = Filter.Changed; _clash = null; Render(); };
            DockPanel.SetDock(chip, Dock.Right);
            top.Children.Add(chip);
        }
        (_setNumber.Parent as Panel)?.Children.Remove(_setNumber);
        var of = new TextBlock { Text = $"of {total} keybinds set", Style = Ui.Res<Style>("Muted"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(10, 0, 0, 7) };
        top.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _setNumber, of } });

        var bar = new Grid { Height = 10, Margin = new Thickness(0, 10, 0, 9) };
        void Part(int n, string brush)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(n, GridUnitType.Star) });
            if (n == 0) return;
            var piece = new Border { Margin = new Thickness(1, 0, 1, 0), RenderTransform = new SkewTransform(-20, 0), RenderTransformOrigin = new Point(0.5, 0.5) };
            piece.SetResourceReference(Border.BackgroundProperty, brush);
            Grid.SetColumn(piece, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(piece);
        }
        Part(keyboard, "Accent");
        Part(wheel, "Slate");
        Part(none, "LineStrong");

        var legend = new WrapPanel();
        void Item(string label, int n, string brush)
        {
            var swatch = new Border { Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
            swatch.SetResourceReference(Border.BackgroundProperty, brush);
            var text = new TextBlock { Text = label, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            var number = new TextBlock { Text = n.ToString(CultureInfo.CurrentCulture), FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 22, 0) };
            legend.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2), Children = { swatch, text, number } });
        }
        Item("Keyboard", keyboard, "Accent");
        Item("Wheel or controller", wheel, "Slate");
        Item("Not set", none, "LineStrong");

        _summary.Child = new StackPanel { Margin = new Thickness(22, 12, 22, 12), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Stretch, Children = { top, bar, legend } };
    }

    private FrameworkElement Toolbar()
    {
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
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12), LastChildFill = false };
        _findBox.Margin = new Thickness(6, 0, 12, 0);
        row.Children.Add(_search);
        row.Children.Add(_findBox);
        row.Children.Add(_filters);
        return row;
    }

    private void RenderFilters()
    {
        _filters.Children.Clear();
        foreach (var (filter, label) in new[] { (Filter.All, "All"), (Filter.Set, "Set"), (Filter.NotSet, "Not set"), (Filter.Changed, "Changed"), (Filter.Wheel, "Wheel or controller") })
        {
            int n = _entries.Count(e => Passes(e, filter));
            var count = new TextBlock { Text = n.ToString(CultureInfo.CurrentCulture), Margin = new Thickness(7, 0, 0, 0), Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center };
            var button = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center }, count } }, Margin = new Thickness(0, 0, 4, 0) };
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
            var hue = HueOf(group);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(Badge(group, 30));
            var middle = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            middle.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = group, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }));
            middle.Children.Add(new SegmentMeter(hue.Deep) { Height = 5, CellWidth = 6, Margin = new Thickness(0, 6, 0, 0), Value = inside.Count == 0 ? 0 : (double)set / inside.Count });
            Grid.SetColumn(middle, 1);
            grid.Children.Add(middle);
            var count = new TextBlock { Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
            count.Inlines.Add(new System.Windows.Documents.Run((filtering ? found : set).ToString(CultureInfo.CurrentCulture)) { FontWeight = FontWeights.SemiBold, Foreground = Ui.Res<Brush>("Text") });
            count.Inlines.Add(new System.Windows.Documents.Run(filtering ? " found" : $" / {inside.Count}"));
            Grid.SetColumn(count, 2);
            grid.Children.Add(count);

            bool on = !filtering && group == current;
            var holder = new Grid();
            holder.Children.Add(new Border { Padding = new Thickness(12, 10, 12, 10), Child = grid });
            if (on)
            {
                holder.SetResourceReference(Panel.BackgroundProperty, "AccentSoft");
                var bar = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Left };
                bar.SetResourceReference(Border.BackgroundProperty, "Accent");
                holder.Children.Add(bar);
            }
            var button = new Button
            {
                Style = Ui.Res<Style>("QuietButton"), Content = holder, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch,
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

    private static FrameworkElement Badge(string group, double size)
    {
        var hue = HueOf(group);
        var badge = new Tile { Width = size, Height = size, CornerRadius = ThemeManager.Round(0), VerticalAlignment = VerticalAlignment.Center, Child = RaceParts.Glyph(IconOf(group), Math.Round(size * 0.55), hue.Deep) };
        badge.SetResourceReference(Border.BackgroundProperty, hue.Soft);
        return badge;
    }

    private void RenderPane()
    {
        var stack = new StackPanel();
        if (!Filtering)
        {
            string group = Current(Groups());
            var inside = _entries.Where(e => GroupOf(e) == group).ToList();
            var head = Head(stack);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(Badge(group, 30));
            title.Children.Add(new TextBlock { Text = group, Style = Ui.Res<Style>("H3"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 10, 0) });
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
            stack.Children.Add(Sections(inside, HueOf(group), new Thickness(18, 4, 18, 18)));
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
                Filter.Set => "Set", Filter.NotSet => "Not set", Filter.Changed => "Changed from iRacing's default", _ => "On a wheel or controller",
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
            title.Children.Add(Badge(group, 26));
            title.Children.Add(new TextBlock { Text = group, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) });
            stack.Children.Add(title);
            stack.Children.Add(Sections(inside, HueOf(group), new Thickness(18, 0, 18, 6)));
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

    private UIElement Sections(List<HotkeyEntry> entries, (string Soft, string Deep) hue, Thickness margin)
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
            foreach (var (title, items) in blocks)
            {
                var block = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
                if (title is not null) block.Children.Add(SectionHead(title, hue));
                foreach (var e in items) block.Children.Add(Row(e));
                columns.Children.Add(block);
            }
            stack.Children.Add(columns);
        }
        if (gears is not null)
        {
            var block = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            block.Children.Add(SectionHead("Gears", hue));
            var tiles = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            foreach (var e in gears) tiles.Children.Add(Gear(e));
            block.Children.Add(tiles);
            if (_clash is { } c && gears.Any(g => g.Id == c.Entry.Id)) block.Children.Add(ClashStrip(c));
            stack.Children.Add(block);
        }
        return stack;
    }

    private static TextBlock SectionHead(string title, (string Soft, string Deep) hue)
    {
        var head = new TextBlock { Text = title, Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        head.SetResourceReference(TextBlock.ForegroundProperty, hue.Deep);
        return head;
    }

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
            hint = new TextBlock { Text = "Set", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 6, 0) };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            keys.Child = hint;
        }
        Grid.SetColumn(keys, 1);
        grid.Children.Add(keys);

        Button? undo = null;
        if (changed)
        {
            undo = Ui.IconButton("I.Undo", "Back to iRacing's default: " + PitBox.Hotkeys.Hotkey.Display(entry.Default), () => Apply(entry, entry.Default), "QuietIconButton");
            undo.Width = undo.Height = SlotWidth;
            undo.Opacity = 0;
            undo.HorizontalAlignment = HorizontalAlignment.Right;
            undo.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(undo, 2);
            grid.Children.Add(undo);
        }

        if (_clash is { } clash && clash.Entry.Id == entry.Id)
        {
            var strip = ClashStrip(clash);
            Grid.SetRow(strip, 1);
            Grid.SetColumnSpan(strip, 3);
            grid.Children.Add(strip);
        }

        var holder = new Grid { Background = Brushes.Transparent, Children = { grid } };
        if (changed)
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
            holder.SetResourceReference(Panel.BackgroundProperty, "SurfaceAlt");
            if (undo is not null) undo.Opacity = 1;
            hint?.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        };
        row.MouseLeave += (_, _) =>
        {
            holder.Background = Brushes.Transparent;
            if (undo is not null) undo.Opacity = 0;
            hint?.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
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
            Text = locked ? "Wheel" : set ? PitBox.Hotkeys.Hotkey.Display(entry.Get()) : "Not set", Style = Ui.Res<Style>("Caption"),
            HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 3, 4, 0),
        });
        keyText.SetResourceReference(TextBlock.ForegroundProperty, locked ? "SlateDeep" : set ? "AccentText" : "TextFaint");
        if (set) keyText.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(keyText);
        var keys = new Border { Child = stack, VerticalAlignment = VerticalAlignment.Center };
        var tile = Hairline.Frame(new Border
        {
            Width = GearWidth, Height = 58, Margin = new Thickness(0, 0, 6, 6), Child = keys, Cursor = locked ? null : Cursors.Hand,
            ToolTip = locked ? entry.Locked : entry.Label,
        });
        tile.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        tile.SetResourceReference(Border.BorderBrushProperty, _clash?.Entry.Id == entry.Id ? "Apricot" : "LineStrong");
        AutomationProperties.SetName(tile, entry.Label);
        tile.MouseEnter += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, "TextMuted");
        tile.MouseLeave += (_, _) => tile.SetResourceReference(Border.BorderBrushProperty, _clash?.Entry.Id == entry.Id ? "Apricot" : "LineStrong");
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

    private FrameworkElement ClashStrip(Clash clash)
    {
        var line = new WrapPanel { Margin = new Thickness(10, 2, 0, 9) };
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 4), TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
        text.SetResourceReference(TextBlock.ForegroundProperty, "ApricotDeep");
        string where = GroupOf(clash.Other) == GroupOf(clash.Entry) ? "" : $" ({GroupOf(clash.Other)})";
        text.Text = $"{PitBox.Hotkeys.Hotkey.Display(clash.Keys)} also does “{clash.Other.Label}”{where}";
        line.Children.Add(text);
        Button Act(string label, string tip, Action run)
        {
            var b = new Button { Content = label, Style = Ui.Res<Style>("GhostButton"), ToolTip = tip, Margin = new Thickness(0, 0, 4, 4) };
            b.Click += (_, _) => run();
            return b;
        }
        line.Children.Add(clash.Before is { Length: > 0 } before
            ? Act("Swap them", $"“{clash.Other.Label}” gets {PitBox.Hotkeys.Hotkey.Display(before)}", () => Resolve(clash, clash.Other, before))
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
        foreach (var e in changed)
            if (_app.Hotkeys.Change(e, e.Default) is { } problem)
            {
                _app.Notify("Keybinds not changed", problem, NoticeKind.Warning, important: true);
                Render();
                return;
            }
        _clash = null;
        Render();
        _app.Notify($"{group} keybinds back to default", RaceFormat.Count(changed.Count, "keybind") + " changed", action: new NoticeAction("Undo", () =>
        {
            foreach (var (entry, key) in before) _app.Hotkeys.Change(entry, key);
            Render();
        }));
    }

    private static FrameworkElement Caps(string hotkey)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var parts = PitBox.Hotkeys.Hotkey.Display(hotkey).Split('+');
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
            var label = new TextBlock { Text = keys[i], FontWeight = FontWeights.SemiBold, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var cap = new Border { MinWidth = 26, Height = 24, Padding = new Thickness(7, 0, 7, 0), BorderThickness = new Thickness(1, 1, 1, 3), Child = label };
            cap.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
            cap.SetResourceReference(Border.BorderBrushProperty, "LineStrong");
            panel.Children.Add(cap);
        }
        return panel;
    }

    private static FrameworkElement Wheel(string tip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(RaceParts.Glyph("I.Gamepad", 13, "SlateDeep"));
        var text = new TextBlock { Text = "Wheel", FontWeight = FontWeights.SemiBold, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        text.SetResourceReference(TextBlock.ForegroundProperty, "SlateDeep");
        row.Children.Add(text);
        var chip = new Border { Height = 24, Padding = new Thickness(8, 0, 8, 0), Child = row, ToolTip = tip };
        chip.SetResourceReference(Border.BackgroundProperty, "SlateSoft");
        return chip;
    }

    private static bool InButton(DependencyObject d)
    {
        for (var cur = d; cur is not null; cur = cur is Visual ? VisualTreeHelper.GetParent(cur) : LogicalTreeHelper.GetParent(cur))
            if (cur is ButtonBase) return true;
        return false;
    }

    public void ShowFilter(string which)
    {
        ClearSearch();
        _filter = which switch { "not-set" => Filter.NotSet, "changed" => Filter.Changed, "wheel" => Filter.Wheel, "set" => Filter.Set, _ => Filter.All };
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
