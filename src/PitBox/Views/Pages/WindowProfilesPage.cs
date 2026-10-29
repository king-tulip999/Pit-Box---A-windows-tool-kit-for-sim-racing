using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Plugins;
using PitBox.Services;
using PitBox.Theme;
using PitBox.WindowProfiles;

namespace PitBox.Views.Pages;

internal sealed class WindowProfilesPage : UserControl, IPage, IScreenshotStates
{
    public const string AutoAnchor = "windows.auto";
    private const string Consumer = "Window Profiles page";
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(23, 0, 23, 23) };
    private bool _shown, _buildQueued, _scanned;
    private HashSet<string> _watched = new(StringComparer.OrdinalIgnoreCase);
    private WindowMapView? _map;
    private readonly StackPanel _openHost = new();
    private List<OpenWindow> _open = new();
    private int _scan;
    private List<(string Name, List<LayoutTile> Screens)> _layouts = new();
    private string? _mapLayout;
    private const double MapHeight = 210;

    public WindowProfilesPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        Content = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body), Focusable = false };
    }

    private WindowProfileSettings S => _app.Config.WindowProfiles;

    public void OnShown()
    {
        _shown = true;
        _mapLayout = null;
        Watch();
        Build();
        _app.Watcher.Started += OnProcess;
        _app.Watcher.Stopped += OnProcess;
        _app.WindowProfiles.Changed += OnChanged;
        _app.Displays.Changed += QueueBuild;
        _window.Activated += OnActivated;
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _shown = false;
        _app.Watcher.Release(Consumer);
        _watched.Clear();
        _app.Watcher.Started -= OnProcess;
        _app.Watcher.Stopped -= OnProcess;
        _app.WindowProfiles.Changed -= OnChanged;
        _app.Displays.Changed -= QueueBuild;
        _window.Activated -= OnActivated;
    }

    private void OnActivated(object? sender, EventArgs e) => _ = ScanAsync();

    private void Watch()
    {
        _watched = S.Profiles.Where(p => p.ProcessName.Length > 0).Select(p => Processes.AppWatcher.Normalize(p.ProcessName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_watched.Count > 0 && !_app.Demo) _app.Watcher.Acquire(Consumer, _watched);
        else _app.Watcher.Release(Consumer);
    }

    private void OnChanged()
    {
        Watch();
        QueueBuild();
    }

    private void OnProcess(string name)
    {
        if (_watched.Contains(name)) QueueBuild();
    }

    private void QueueBuild()
    {
        if (_buildQueued) return;
        _buildQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _buildQueued = false;
            if (_shown) Build();
        }, DispatcherPriority.Background);
    }

    private bool Running(WindowProfile p) =>
        _app.Demo ? p.ProcessName.Equals("SimHubWPF.exe", StringComparison.OrdinalIgnoreCase) || p.ProcessName.Equals("RRRE64.exe", StringComparison.OrdinalIgnoreCase)
            : p.ProcessName.Length > 0 && _app.Watcher.IsRunning(p.ProcessName);

    private void Build()
    {
        S.Normalize();
        _body.Children.Clear();
        var header = new Grid { Margin = Ui.HeaderMargin };
        header.Children.Add(Ui.PluginTitle(PluginIds.WindowProfiles, "Window Profiles"));
        _body.Children.Add(header);
        _screens = Screens();
        _layouts = SavedLayouts();
        if (_mapLayout is not null && !_layouts.Any(l => l.Name == _mapLayout)) _mapLayout = null;
        if (_screens.Count > 0) _body.Children.Add(MapPanel());
        else _map = null;

        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.25, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Ui.PanelSpacing) });
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        columns.Children.Add(left);
        if (_openHost.Parent is Panel old) old.Children.Remove(_openHost);
        Grid.SetColumn(_openHost, 2);
        columns.Children.Add(_openHost);
        _body.Children.Add(columns);

        if (S.Profiles.Count == 0 && S.Groups.Count == 0)
        {
            var empty = EmptyState();
            Ui.Anchor(empty, AutoAnchor);
            left.Children.Add(empty);
        }
        else
        {
            header.Children.Add(AutoApplySwitch());
            var newGroup = Ui.LabeledButton("I.Folder", "New group", () => _ = EditGroupAsync(new WindowGroup(), isNew: true), "GhostButton");
            newGroup.Margin = new Thickness(0, 0, 6, 0);
            var create = Ui.LabeledButton("I.Plus", "New profile", () => _ = EditAsync(new WindowProfile(), isNew: true), "PrimaryButton");
            int n = S.Profiles.Count;
            left.Children.Add(Ui.SectionHeader("Your profiles", RaceFormatCount(n) + (S.Layout.Count > 1 ? " · drag to arrange" : ""), newGroup, create));

            var grid = new PanelGrid(S.Layout, _app.Save) { LiveEdit = true, RowHeight = 1, Gap = 0, PanelSpacing = Ui.PanelSpacing, TwoColumnsFrom = double.PositiveInfinity, ColumnGap = Ui.PanelSpacing };
            int order = 0;
            foreach (var slot in S.Layout.ToList())
            {
                if (S.Groups.FirstOrDefault(g => WindowProfileSettings.KeyOf(g) == slot.Id) is { } group)
                {
                    var g = group;
                    grid.Add(new PanelDef(slot.Id, g.Name, _ => GroupCard(g)) { Order = order++, VeilClick = () => _ = EditGroupAsync(g, isNew: false) });
                }
                else if (S.Profiles.FirstOrDefault(p => WindowProfileSettings.KeyOf(p) == slot.Id) is { } profile)
                {
                    var p = profile;
                    grid.Add(new PanelDef(slot.Id, p.Name, _ => ProfileCard(p)) { Order = order++, VeilClick = () => _ = EditAsync(p, isNew: false) });
                }
            }
            left.Children.Add(grid);
        }
        FillWindows();
        UpdateMap();
        _ = ScanAsync();
    }

    private Tile MapPanel()
    {
        if (_map is null)
        {
            _map = new WindowMapView { Height = MapHeight };
            _map.WindowClicked += OnMapClicked;
        }
        else if (_map.Parent is Panel holder) holder.Children.Remove(_map);
        _map.SetScreens(_layouts.FirstOrDefault(l => l.Name == _mapLayout).Screens ?? _screens);
        var stack = new StackPanel();
        if (_layouts.Count > 0)
        {
            var names = _layouts.Select(l => l.Name).ToList();
            var options = new List<string> { "Current" };
            options.AddRange(names);
            var tips = new List<string?> { "The displays that are on now" };
            tips.AddRange(names.Select(n => (string?)$"Your “{n}” display profile"));
            var picker = Ui.Segmented(options, _mapLayout is null ? 0 : names.IndexOf(_mapLayout) + 1, i => ShowLayout(i == 0 ? null : names[i - 1]), "window-map-layout", tips);
            picker.HorizontalAlignment = HorizontalAlignment.Left;
            picker.Margin = new Thickness(0, 0, 0, 13);
            stack.Children.Add(picker);
        }
        stack.Children.Add(_map);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(13), Margin = new Thickness(0, 0, 0, Ui.PanelSpacing), Child = stack };
    }

    private void ShowLayout(string? name)
    {
        if (_mapLayout == name) return;
        _mapLayout = name;
        QueueBuild();
    }

    private List<(string Name, List<LayoutTile> Screens)> SavedLayouts()
    {
        var layouts = new List<(string, List<LayoutTile>)>();
        foreach (var profile in _app.Config.Displays.Profiles)
        {
            if (profile.Layout is null || profile.Name.Length == 0) continue;
            var screens = _app.Displays.Describe(profile.Layout).Where(m => m.Width > 0 && m.Height > 0)
                .DistinctBy(m => (m.X, m.Y, m.Width, m.Height)).OrderBy(m => m.X).ThenBy(m => m.Y)
                .Select((m, i) => new LayoutTile(m.X, m.Y, m.Width, m.Height, (i + 1).ToString(), "", false)).ToList();
            if (screens.Count > 0) layouts.Add((profile.Name, screens));
        }
        return layouts;
    }

    private static List<ScreenArea> Areas(IEnumerable<LayoutTile> tiles) =>
        tiles.Select(t => new ScreenArea(t.Label, (int)t.X, (int)t.Y, (int)t.Width, (int)t.Height, false)).ToList();

    private (string Name, List<LayoutTile> Screens)? FitOf(WindowProfile p)
    {
        if (p.Width is not { } w || p.Height is not { } h || _screens.Count == 0 || WindowPresets.Covers(Areas(_screens), p.X, p.Y, w, h)) return null;
        foreach (var layout in _layouts)
            if (WindowPresets.Covers(Areas(layout.Screens), p.X, p.Y, w, h)) return layout;
        return null;
    }

    private void OnMapClicked(string key)
    {
        if (S.Profiles.FirstOrDefault(p => p.Id == key) is { } profile) _ = EditAsync(profile, isNew: false);
        else if (_open.FirstOrDefault(w => OpenKey(w) == key) is { } open) _ = SaveAsync(open);
    }

    private static string OpenKey(OpenWindow w) => "open:" + w.Process;

    private List<WindowProfile> Owners(string process)
    {
        string name = Processes.AppWatcher.Normalize(process);
        return S.Profiles.Where(p => p.ProcessName.Length > 0 && Processes.AppWatcher.Normalize(p.ProcessName).Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void UpdateMap()
    {
        if (_map is null) return;
        var items = new List<MapWindow>();
        foreach (var p in S.Profiles)
            if (p.Width is { } w && p.Height is { } h) items.Add(new MapWindow(p.Id, new Rect(p.X, p.Y, w, h), p.Label, false));
        foreach (var open in _open)
        {
            var area = new Rect(open.X, open.Y, open.Width, open.Height);
            var owners = Owners(open.Process);
            if (owners.Count == 0) items.Add(new MapWindow(OpenKey(open), area, "", true));
            foreach (var p in owners) items.Add(new MapWindow(p.Id, area, "", true));
        }
        _map.SetWindows(items);
    }

    private async Task ScanAsync()
    {
        int scan = ++_scan;
        var open = _app.Demo ? DemoWindows() : await Task.Run(() =>
        {
            try { return WindowProfileService.OpenWindows(); }
            catch (Exception ex) { App.LogError(ex); return new List<OpenWindow>(); }
        });
        if (scan != _scan || !_shown) return;
        _open = open;
        _scanned = true;
        FillWindows();
        UpdateMap();
    }

    private static List<OpenWindow> DemoWindows() => new()
    {
        new("acs.exe", "Assetto Corsa", 320, 180, 1920, 1080),
        new("Discord.exe", "Discord", 2900, 120, 1280, 900),
        new("iRacingSim64DX11.exe", "iRacing.com Simulator", -2240, 180, 1920, 1080),
        new("RRRE64.exe", "RaceRoom Racing Experience", 0, 0, 2560, 1440),
        new("SimHubWPF.exe", "SimHub", 2800, 300, 1600, 900),
        new("Spotify.exe", "Spotify Premium", -2400, 200, 1400, 1000),
    };

    private void FillWindows()
    {
        _openHost.Children.Clear();
        var free = _open.Where(w => Owners(w.Process).Count == 0).ToList();
        var refresh = Ui.IconButton("I.Refresh", "Look again", () => _ = ScanAsync());
        _openHost.Children.Add(Ui.SectionHeader("Open windows", free.Count == 0 ? null : $"{SimRacing.Race.RaceFormat.Count(free.Count, "program")} without a profile", refresh));
        var list = new StackPanel();
        if (free.Count == 0)
        {
            if (_scanned) list.Children.Add(new TextBlock { Text = "Every program with a window open has a profile.", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) });
        }
        else
        {
            for (int i = 0; i < free.Count; i++)
            {
                if (i > 0) list.Children.Add(Ui.Divider());
                list.Children.Add(OpenRow(free[i]));
            }
        }
        _openHost.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(15, 4, 15, 4), Child = list });
    }

    private Grid OpenRow(OpenWindow w)
    {
        var row = new Grid { Margin = new Thickness(0, 7, 0, 7), Background = System.Windows.Media.Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = Ui.Badge("I.AppWindow", "SurfaceAlt", "TextMuted", 32, 0, 16);
        badge.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(badge);

        var names = new StackPanel { Margin = new Thickness(11, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = TextFit.TipWhenTrimmed(new TextBlock { Text = w.Process, Style = Ui.Res<Style>("H3"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis });
        TextCase.Verbatim(title);
        names.Children.Add(title);
        var detail = TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, Margin = new Thickness(0, 2, 0, 0) });
        detail.Text = $"{w.Width}×{w.Height}{Where(new Rect(w.X, w.Y, w.Width, w.Height))} at {w.X}, {w.Y}";
        row.ToolTip = w.Title;
        TextCase.Verbatim(detail);
        names.Children.Add(detail);
        Grid.SetColumn(names, 1);
        row.Children.Add(names);

        var save = Ui.LabeledButton("I.Plus", "Save as profile", () => _ = SaveAsync(w), "GhostButton");
        save.ToolTip = "Start a profile with the window's size and position now";
        save.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(save, 2);
        row.Children.Add(save);
        HighlightOnHover(row, OpenKey(w));
        return row;
    }

    private void HighlightOnHover(FrameworkElement element, string key)
    {
        element.MouseEnter += (_, _) => _map?.Highlight(key);
        element.MouseLeave += (_, _) => _map?.Highlight(null);
    }

    private Task SaveAsync(OpenWindow w)
    {
        string name = WindowPresets.FriendlyName(w.Process);
        if (name.Length > Ui.NameLength) name = name[..Ui.NameLength];
        return EditAsync(new WindowProfile { Name = name, ProcessName = w.Process, X = w.X, Y = w.Y, Width = w.Width, Height = w.Height }, isNew: true);
    }

    private StackPanel AutoApplySwitch()
    {
        var label = new TextBlock { Text = "Apply automatically", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var toggle = Ui.Switch(S.AutoApply, v => { S.AutoApply = v; _app.WindowProfiles.RaiseChanged(); });
        toggle.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(toggle, "Apply automatically");
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = S.AutoApply ? "Each profile set to apply when its program starts does so." : "Off: profiles apply only with Apply, their keybind or the tray.",
            Children = { label, toggle },
        };
        Ui.Anchor(row, AutoAnchor);
        return row;
    }

    private Tile EmptyState()
    {
        var create = Ui.LabeledButton("I.Plus", "New profile", () => _ = EditAsync(new WindowProfile(), isNew: true), "PrimaryButton");
        var stack = Ui.EmptyState(Plugins.BuiltIn.WindowProfilesPlugin.PageIcon, "No window profiles yet",
            "Pick a program and the exact size and spot for its window, e.g. a game stretched across triple screens or held at 1920×1080, borderless.", create);
        return new Tile { Style = Ui.Res<Style>("Card"), Child = stack };
    }

    private List<LayoutTile> _screens = new();

    private List<LayoutTile> Screens()
    {
        try
        {
            return _app.Displays.GetMonitors().Where(m => m.Active).OrderBy(m => m.X)
                .Select(m => new LayoutTile(m.X, m.Y, m.Width, m.Height, m.Number > 0 ? m.Number.ToString() : "", "", false)).ToList();
        }
        catch { return new List<LayoutTile>(); }
    }

    private Tile ProfileCard(WindowProfile p)
    {
        var row = ProfileRow(p);
        row.Margin = new Thickness(15, 10, 15, 10);
        var body = new Grid { ClipToBounds = true };
        if (Running(p))
        {
            var fade = new Border { IsHitTestVisible = false, BorderThickness = new Thickness(4, 0, 0, 0) };
            fade.SetResourceReference(Border.BackgroundProperty, "Place4Fade");
            fade.SetResourceReference(Border.BorderBrushProperty, "Place4");
            body.Children.Add(fade);
        }
        body.Children.Add(row);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Child = body, ClipToBounds = true, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Click to edit, drag to arrange" };
    }

    private Grid ProfileRow(WindowProfile p)
    {
        bool running = Running(p);
        var row = new Grid { Background = System.Windows.Media.Brushes.Transparent };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = Ui.Badge("I.AppWindow", running ? "AccentSoft" : "SurfaceAlt", running ? "AccentText" : "TextMuted", 32, 0, 16);
        badge.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(badge);

        var names = new StackPanel { Margin = new Thickness(11, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TrimRow();
        title.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = p.Name, Style = Ui.Res<Style>("H3"), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }));
        names.Children.Add(title);
        var line = new TrimRow { Margin = new Thickness(0, 2, 0, 0) };
        var detail = TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Caption"), TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
        detail.Inlines.Add(new System.Windows.Documents.Run(p.ProcessName.Length > 0 ? p.ProcessName : "No program picked") { FontWeight = FontWeights.SemiBold });
        detail.Inlines.Add(new System.Windows.Documents.Run("   " + Summary(p)));
        TextCase.Verbatim(detail);
        line.Children.Add(detail);
        names.Children.Add(line);
        if (running)
        {
            var chip = new Grid { Margin = new Thickness(0, 2, 5, 2), VerticalAlignment = VerticalAlignment.Center };
            chip.Children.Add(new Slant("SageSoft", 0.14, 0, 1, 0, 0.86, 1, 0, 1));
            var chipText = RaceParts.Big("Running", 12.5, "SageDeep", italic: false, heavy: false);
            chipText.Margin = new Thickness(11, 1, 11, 2);
            chip.Children.Add(chipText);
            title.Children.Add(chip);
        }
        Grid.SetColumn(names, 1);
        row.Children.Add(names);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var apply = Ui.LabeledButton("I.Zap", "Apply", () => { _map?.Pulse(p.Id); _ = _app.WindowProfiles.ApplyAsync(p); });
        apply.ToolTip = running ? "Move the window now" + Keys(p.Hotkey) : p.ProcessName.Length == 0 ? "No program picked" : $"{p.ProcessName} isn't running";
        actions.Children.Add(apply);
        var edit = Ui.LabeledButton("I.Pencil", "Edit", () => _ = EditAsync(p, isNew: false), "GhostButton");
        edit.Margin = new Thickness(6, 0, 0, 0);
        actions.Children.Add(edit);
        if (FitOf(p) is { } fit && fit.Name != _mapLayout)
        {
            var show = Ui.LabeledButton("I.Monitor", fit.Name, () => ShowLayout(fit.Name), "GhostButton");
            show.ToolTip = $"Show the “{fit.Name}” layout on the map";
            show.Margin = new Thickness(0, 0, 6, 0);
            actions.Children.Insert(0, show);
        }
        Grid.SetColumn(actions, 2);
        row.Children.Add(actions);
        HighlightOnHover(row, p.Id);
        return row;
    }

    private string Summary(WindowProfile p)
    {
        var fit = FitOf(p);
        var parts = new List<string>
        {
            p.Width is { } w && p.Height is { } h
                ? fit is { } layout ? $"{w}×{h}{Where(new Rect(p.X, p.Y, w, h), layout.Screens)} in “{layout.Name}” at {p.X}, {p.Y}" : $"{w}×{h}{Where(new Rect(p.X, p.Y, w, h))} at {p.X}, {p.Y}"
                : $"keeps its size{Where(new Rect(p.X, p.Y, 1, 1))}, at {p.X}, {p.Y}",
        };
        if (StyleOf(p) is { } style) parts.Add(style);
        if (p.Hotkey is not null) parts.Add(Hotkeys.Hotkey.Display(p.Hotkey));
        parts.Add(ApplyingOf(p));
        return string.Join(" · ", parts);
    }

    private string Where(Rect window, IReadOnlyList<LayoutTile>? screens = null)
    {
        var hit = (screens ?? _screens).Where(s => s.Label.Length > 0 && Rect.Intersect(new Rect(s.X, s.Y, s.Width, s.Height), window) is { IsEmpty: false, Width: > 0, Height: > 0 }).ToList();
        return hit.Count switch
        {
            1 => $" on display {hit[0].Label}",
            > 1 => $" across {SimRacing.Race.RaceFormat.Count(hit.Count, "display")}",
            _ => "",
        };
    }

    private static string? StyleOf(WindowProfile p) => (p.RemoveBorders, p.HideTitleBar) switch
    {
        (true, true) => "borders removed, title bar hidden",
        (true, false) => "borders removed",
        (false, true) => "title bar hidden",
        _ => null,
    };

    private string ApplyingOf(WindowProfile p) =>
        !p.Auto ? "by hand"
        : !S.AutoApply ? "by hand (automatic is off)"
        : p.DelayMs > 0 ? $"when it starts, after {p.DelayMs / 1000.0:0.##} {Units.S}" : "when it starts";

    private static string Keys(string? hotkey) => hotkey is null ? "" : $" ({Hotkeys.Hotkey.Display(hotkey)})";

    private Tile GroupCard(WindowGroup g)
    {
        var members = S.Members(g).ToList();
        var stack = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = Ui.Badge("I.Folder", "AccentSoft", "AccentText", 32, 0, 16);
        badge.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(badge);
        var text = new StackPanel { Margin = new Thickness(11, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var title = new TrimRow();
        title.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = g.Name, Style = Ui.Res<Style>("H3"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }));
        if (g.Hotkey is not null)
        {
            var keys = Ui.Chip(Hotkeys.Hotkey.Display(g.Hotkey));
            keys.Margin = new Thickness(0);
            keys.VerticalAlignment = VerticalAlignment.Center;
            title.Children.Add(keys);
        }
        text.Children.Add(title);
        text.Children.Add(Ui.Text(members.Count == 0 ? "Empty: pick this group in a profile's editor" : $"Group · {RaceFormatCount(members.Count)}", "Caption"));
        Grid.SetColumn(text, 1);
        head.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var apply = Ui.LabeledButton("I.Zap", "Apply all", () => _ = _app.WindowProfiles.ApplyGroupAsync(g));
        apply.ToolTip = "Move every program in the group that's running" + Keys(g.Hotkey);
        apply.IsEnabled = members.Count > 0;
        actions.Children.Add(apply);
        var add = Ui.LabeledButton("I.Plus", "Add profile", () => _ = EditAsync(new WindowProfile { GroupId = g.Id }, isNew: true), "SoftButton");
        add.Margin = new Thickness(6, 0, 0, 0);
        actions.Children.Add(add);
        var edit = Ui.LabeledButton("I.Pencil", "Edit", () => _ = EditGroupAsync(g, isNew: false), "GhostButton");
        edit.Margin = new Thickness(6, 0, 0, 0);
        actions.Children.Add(edit);
        Grid.SetColumn(actions, 2);
        head.Children.Add(actions);
        stack.Children.Add(head);

        if (members.Count > 0)
        {
            var list = new ReorderGrid { MaxColumns = 1, MinCellWidth = 162, Gap = 5, Margin = new Thickness(0, 10, 0, 2) };
            foreach (var m in members)
            {
                var member = m;
                var tile = new Tile { Style = Ui.Res<Style>("Inset"), Padding = new Thickness(11, 8, 11, 8), Child = ProfileRow(member), ToolTip = "Drag to arrange within the group" };
                tile.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is not DependencyObject d || !InButton(d)) _ = EditAsync(member, isNew: false); };
                list.Children.Add(tile);
            }
            list.Moved += (from, to) => { S.MoveWithinGroup(g, from, to); _app.Save(); };
            PanelGrid.SetNoDrag(list, true);
            stack.Children.Add(list);
        }
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(13, 10, 13, 11), Child = stack, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = "Click to edit the group, drag to arrange" };
    }

    private static bool InButton(DependencyObject d)
    {
        for (var e = d; e is not null; e = System.Windows.Media.VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is System.Windows.Controls.Primitives.ButtonBase) return true;
        return false;
    }

    private static string RaceFormatCount(int n) => SimRacing.Race.RaceFormat.Count(n, "profile");

    private async Task EditAsync(WindowProfile profile, bool isNew)
    {
        bool deleteAsked = false;
        var editor = new WindowProfileEditor(_app, _window, profile, isNew, isNew ? null : () => { deleteAsked = true; _window.CloseModal(false); });
        if (!await _window.ShowModal(editor.Card))
        {
            if (deleteAsked && !await DeleteAsync(profile)) await EditAsync(profile, false);
            return;
        }
        if (isNew) S.Profiles.Add(profile);
        S.Normalize();
        _app.WindowProfiles.RaiseChanged();
        _app.Notify(isNew ? "Profile saved" : "Profile updated", $"{profile.Name} · {WindowProfileService.Describe(profile)}", NoticeKind.Success);
    }

    private async Task EditGroupAsync(WindowGroup group, bool isNew)
    {
        bool deleteAsked = false;
        var editor = new WindowGroupEditor(_app, _window, group, isNew, isNew ? null : () => { deleteAsked = true; _window.CloseModal(false); });
        if (!await _window.ShowModal(editor.Card))
        {
            if (deleteAsked && !await DeleteGroupAsync(group)) await EditGroupAsync(group, false);
            return;
        }
        if (isNew) S.Groups.Add(group);
        S.Normalize();
        _app.WindowProfiles.RaiseChanged();
    }

    private async Task<bool> DeleteAsync(WindowProfile profile)
    {
        if (!await _window.Confirm($"Delete “{profile.Name}”?", "The profile is removed. Windows stay where they are now.", "Delete", danger: true)) return false;
        S.Profiles.Remove(profile);
        S.Normalize();
        _app.WindowProfiles.RaiseChanged();
        return true;
    }

    private async Task<bool> DeleteGroupAsync(WindowGroup group)
    {
        if (!await _window.Confirm($"Delete the group “{group.Name}”?", "Its profiles stay, outside any group.", "Delete", danger: true)) return false;
        S.Remove(group);
        S.Normalize();
        _app.WindowProfiles.RaiseChanged();
        return true;
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("map-layout", () =>
        {
            if (_layouts.Count > 0) ShowLayout(_layouts[^1].Name);
            return Task.CompletedTask;
        });
        yield return ("map-current", () =>
        {
            ShowLayout(null);
            return Task.CompletedTask;
        });
        yield return ("editor", () =>
        {
            if (S.Profiles.FirstOrDefault() is { } first) _ = EditAsync(first, isNew: false);
            return Task.CompletedTask;
        });
        yield return ("editor-bottom", () =>
        {
            if (_window.FindModalScroll() is { } sv) sv.ScrollToEnd();
            return Task.CompletedTask;
        });
        yield return ("close-editor", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("new-editor", () =>
        {
            _ = EditAsync(new WindowProfile(), isNew: true);
            return Task.CompletedTask;
        });
        yield return ("close-new-editor", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("group-editor", () =>
        {
            if (S.Groups.FirstOrDefault() is { } group) _ = EditGroupAsync(group, isNew: false);
            return Task.CompletedTask;
        });
        yield return ("close-group-editor", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
    }
}
