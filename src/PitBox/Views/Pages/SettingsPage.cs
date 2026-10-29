using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Hardware;
using PitBox.Plugins;
using PitBox.Services;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed class SettingsPage : UserControl, IPage, IScreenshotStates, ISectionedPage, IPageHistory
{
    private const string GeneralId = "general";

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly PanelStack _body;
    private readonly MasonryPanel _sections = new() { MinColumnWidth = 356, MaxColumns = 3, ColumnOf = SectionColumn };

    private static readonly int[] ThreeColumns = { 0, 1, 2, 2, 1, 2 }, TwoColumns = { 0, 1, 1, 0, 1, 0 };

    private static int? SectionColumn(int columns, int index)
    {
        var plan = columns switch { 3 => ThreeColumns, 2 => TwoColumns, _ => null };
        return plan is not null && index < plan.Length ? plan[index] : null;
    }
    private readonly ScrollViewer _scroll;

    public event Action? SectionsChanged { add { } remove { } }

    private static readonly (PageSection Section, string Anchor)[] Cards =
    {
        (new("app", "Application", "I.Power"), "settings.app"),
        (new("plugins", "Tools", "I.Plug"), "settings.plugins"),
        (new("look", "Appearance", "I.Palette"), "settings.look"),
        (new("performance", "Performance", "I.Gauge"), "settings.performance"),
        (new("access", "Hardware access", "I.Cpu"), "settings.access"),
        (new("data", "Data and configuration", "I.Folder"), "settings.data"),
        (new("updates", "About and updates", "I.Info"), "settings.updates"),
    };

    private static readonly PageSection[] SidebarViews =
    {
        new(GeneralId, "General", "I.Tune"),
    };

    internal static SectionList SectionsFor(AppServices app) => new(SidebarViews, GeneralId, Movable: false);

    public IReadOnlyList<PageSection> Sections => SectionsFor(_app).Sections;

    public string? CurrentSection => SectionsFor(_app).Current;

    public bool SectionsMovable => false;

    public void MoveSections(IReadOnlyList<string> order) { }

    public object? HistoryState => CurrentSection;

    public void RestoreHistory(object? state)
    {
        if (state is string id) ShowSection(id);
    }

    public void ShowSection(string id)
    {
        string? anchor = Cards.FirstOrDefault(c => c.Section.Id == id).Anchor;
        if (anchor is null)
        {
            if (id == GeneralId) _scroll.ScrollToTop();
            return;
        }
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Ui.FindAnchor(_body, anchor) is not { } card || _scroll.Content is not UIElement frame) return;
            _body.UpdateLayout();
            double top = card.TranslatePoint(new Point(), frame).Y;
            _scroll.ScrollToVerticalOffset(Math.Max(0, top - 12));
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public bool ShowSectionOf(DependencyObject target)
    {
        foreach (var (_, anchor) in Cards)
            if (Ui.FindAnchor(_body, anchor) is { } card && (ReferenceEquals(card, target) || card.IsAncestorOf(target)))
                return true;
        return false;
    }

    public SettingsPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _body = new PanelStack { Margin = new Thickness(23, 0, 23, 23), IsPanel = child => child == _sections };
        _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body), Focusable = false };
        Content = _scroll;
    }

    private bool _shown;

    public void OnShown()
    {
        _shown = true;
        string built = Signature();
        if (built != _builtFor)
        {
            _builtFor = built;
            Build();
            Motion.Stagger(_body);
        }
        if (!s_pawnIoOffered)
        {
            s_pawnIoOffered = true;
            Dispatcher.BeginInvoke(new Action(() => { if (_shown) _ = OfferPawnIoAsync(); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        FillUpdates();
        _app.Updates.Changed += FillUpdates;
        _app.Plugins.Changed += OnPluginsChanged;
    }

    internal void Invalidate()
    {
        _builtFor = null;
        _startupState = null;
        _startupError = null;
    }

    private void OnPluginsChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (!_shown) return;
        _builtFor = Signature();
        Build();
    }), System.Windows.Threading.DispatcherPriority.Background);

    private string? _builtFor;

    private string Signature()
    {
        var p = _app.Config.Performance;
        return string.Join("|", _app.Plugins.All.Select(x => x.IsActive ? "1" : "0")) + $"|{p.Rate}|{p.IntervalMs}|{p.Background}|{_app.Hardware.Ready}|{_app.Hardware.OpenError}|{_app.Hardware.PawnIoState}|{_app.Hardware.CpuTempMissing}|{_app.Hardware.Devices?.Count}|{_app.Demo}";
    }

    public void OnHidden()
    {
        _shown = false;
        _app.Updates.Changed -= FillUpdates;
        _app.Plugins.Changed -= OnPluginsChanged;
    }

    private UiSettings U => _app.Config.Ui;

    private void Build()
    {
        _body.Children.Clear();
        var heading = Ui.PageTitle("I.Tune", "Settings");
        heading.Margin = Ui.HeaderMargin;
        _body.Children.Add(heading);
        _sections.Children.Clear();
        _body.Children.Add(_sections);

        BuildApplication();
        BuildAppearance();
        BuildPerformance();
        BuildHardware();
        BuildData();
        BuildAboutAndUpdates();
    }

    private StartupState? _startupState;
    private string? _startupError;
    private bool _startupBusy;

    private void BuildApplication()
    {
        var app = Section("Application settings", "I.Power", "settings.app");
        var startup = Group(app, "Startup", "settings.startup");
        BuildStartup(startup);
        var window = Group(app, "Window", "settings.window");
        BuildWindow(window);
        var plugins = PluginManager.InNavOrder(_app.Plugins.All, U.NavOrder).ToList();
        var jump = Ui.LabeledButton("I.Plug", "Choose tools", () => _ = ShowTools(), "GhostButton");
        window.Children.Add(Ui.Divider());
        var row = Ui.SettingRow("Tools", $"{plugins.Count(_app.Plugins.IsEnabled)} of {plugins.Count} on.", jump);
        Ui.Anchor(row, "settings.plugins");
        window.Children.Add(row);
    }

    private async Task ShowTools()
    {
        var host = new ContentControl { Focusable = false, Content = BuildTools() };
        void Refill() => Dispatcher.BeginInvoke(new Action(() => host.Content = BuildTools()));
        var close = new StackPanel();
        var ok = new Button { Content = "Done", Style = Ui.Res<Style>("PrimaryButton"), IsDefault = true };
        ok.Click += (_, _) => _window.CloseModal(true);
        close.Children.Add(ok);
        _app.Plugins.Changed += Refill;
        try
        {
            await _window.ShowModal(Ui.Dialog(_window, "Tools",
                "A tool that's off has no page, keybind, tray item or background work; its settings are kept. Drag tools in the sidebar to reorder them.",
                host, close, null, ToolsDialogWidth));
        }
        finally { _app.Plugins.Changed -= Refill; }
    }

    private FrameworkElement BuildTools()
    {
        var plugins = PluginManager.InNavOrder(_app.Plugins.All, U.NavOrder).ToList();
        var group = new StackPanel();
        var columns = new MasonryPanel { MinColumnWidth = PluginColumnWidth, Gap = 13, MaxColumns = 2 };
        group.Children.Add(columns);
        foreach (var plugin in plugins)
        {
            var p = plugin;
            var block = new StackPanel();
            columns.Children.Add(block);
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var (soft, deep) = p.Colors;
            head.Children.Add(Ui.Badge(p.Pages.Count > 0 ? p.Pages[0].Icon : p.Icon, soft, deep, 22.5, 6.5, 12));
            var titles = new StackPanel { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.SemiBold });
            head.Children.Add(titles);
            head.Children.Add(HudSettings.HelpTip.For($"Reads: {p.Reads}\n\nRuns in the background: {p.BackgroundWork}\n\nChanges on your PC: {p.Changes}", $"What {p.Name} reads and changes"));
            var sw = Ui.Switch(_app.Plugins.IsEnabled(p), on =>
            {
                _app.Plugins.SetEnabled(p, on);
                _app.Notify(on ? $"{p.Name} on" : $"{p.Name} off", on ? "It's in the sidebar." : "Its background work has stopped. Settings are kept.", NoticeKind.Success);
            });
            AutomationProperties.SetName(sw, p.Name);
            var row = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel();
            text.Children.Add(head);
            if (p.Id != PluginIds.SimRacing)
                text.Children.Add(new TextBlock { Text = p.Description, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(31, 2, 13, 0) });
            row.Children.Add(text);
            sw.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(sw, 1);
            row.Children.Add(sw);
            block.Children.Add(row);
            bool parentOn = _app.Plugins.IsEnabled(p);
            if (p.Id == PluginIds.SimRacing)
                foreach (var tool in SimRacingPage.ToolsInOrder(_app))
                    block.Children.Add(SubRow(tool.Title, PartNotes.GetValueOrDefault(tool.Id, ""), SimRacingPage.IsToolOn(_app, tool.Id), parentOn,
                        on => SimRacingPage.SetToolOn(_app, tool.Id, on)));
        }

        return group;
    }

    private const double PluginColumnWidth = 340, ToolsDialogWidth = 790;

    private static readonly Dictionary<string, string> PartNotes = new()
    {
        ["setups"] = "Your car setups by car: find, rename, move, import and share.",
        ["hud"] = "In-game widgets from iRacing's live telemetry; off stops reading it.",
        ["fov"] = "The correct field of view for your seat and screens.",
        ["iracing"] = "Every iRacing setting explained, a health check and backups.",
        ["tradingpaints"] = "Starts the Trading Paints Downloader by hand or with iRacing.",
        ["garage61"] = "Starts the Garage 61 Telemetry Agent by hand or with iRacing.",
    };

    private static Grid SubRow(string title, string note, bool on, bool enabled, Action<bool> set, bool beta = false)
    {
        var sw = Ui.Switch(on, set);
        sw.IsEnabled = enabled;
        AutomationProperties.SetName(sw, title);
        var row = new Grid { Margin = new Thickness(31, 2, 0, 3), Opacity = enabled ? 1 : 0.55 };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new Border { BorderThickness = new Thickness(2, 0, 0, 0), Padding = new Thickness(10, 2, 13, 2) };
        text.SetResourceReference(Border.BorderBrushProperty, "Line");
        var stack = new StackPanel();
        var name = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(beta ? new TrimRow { Children = { name, Ui.BetaChip() } } : name);
        if (note.Length > 0)
            stack.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        text.Child = stack;
        row.Children.Add(text);
        sw.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(sw, 1);
        row.Children.Add(sw);
        return row;
    }

    private static StackPanel Group(StackPanel section, string title, string anchor)
    {
        var group = new StackPanel { Margin = new Thickness(0, section.Children.Count > 1 ? 8 : 3, 0, 0) };
        group.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 3, 0, 0) });
        Ui.Anchor(group, anchor);
        section.Children.Add(group);
        return group;
    }

    private void BuildStartup(StackPanel startup)
    {
        if (_startupState is null && _startupError is null && !_app.Demo && !_startupBusy)
            RunStartupTask(null);
        bool on = _startupState?.Enabled == true;

        string? description = _app.Demo ? "Not available in demo mode."
            : on ? "Runs as administrator without a UAC prompt (a Windows scheduled task)."
            : null;
        var toggle = Ui.Switch(on, SetStartup);
        toggle.IsEnabled = !_app.Demo && !_startupBusy;
        startup.Children.Add(Ui.SettingRow("Start with Windows", description, toggle));

        if (_startupState?.PointsElsewhere == true)
        {
            var row = Notice("ApricotSoft", "ApricotDeep", "I.Alert",
                $"The startup entry points to a different copy of Pit-Box ({_startupState.Command}), so it won't start this one.");
            var repair = Ui.LabeledButton("I.Wand", "Repair", () => SetStartup(true), "SoftButton");
            Grid.SetColumn(repair, 2);
            row.Children.Add(repair);
            startup.Children.Add(row);
        }
        if (_startupError is not null)
        {
            var banner = Ui.DangerBanner("Windows didn't let Pit-Box change its startup task",
                $"{_startupError}\n" +
                $"To add it by hand: Task Scheduler → Create Task → tick “Run with highest privileges”, add an “At log on” trigger and the action “{Environment.ProcessPath}” with the argument --minimized.");
            banner.Margin = new Thickness(0, 3, 0, 6);
            var row = (Grid)banner.Child;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var open = Ui.LabeledButton("I.External", "Open Task Scheduler", () => Ui.OpenUrl("taskschd.msc"), "SoftButton");
            open.VerticalAlignment = VerticalAlignment.Center;
            open.Margin = new Thickness(10, 0, 0, 0);
            Grid.SetColumn(open, 2);
            row.Children.Add(open);
            startup.Children.Add(banner);
        }

        startup.Children.Add(Ui.Divider());
        startup.Children.Add(Ui.SettingRow("Start in background",
            "Opens in the tray; keybinds, fans and the overlay run as normal.",
            Ui.Switch(U.StartMinimized, v => { U.StartMinimized = v; _app.Save(); })));
    }

    private void SetStartup(bool on)
    {
        if (_app.Demo || _startupBusy) return;
        RunStartupTask(on);
    }

    private async void RunStartupTask(bool? enable)
    {
        _startupBusy = true;
        StartupState? state = null;
        string? error = null;
        await Task.Run(() =>
        {
            try
            {
                if (enable is { } on)
                    StartupManager.SetEnabled(on);
            }
            catch (Exception ex) { error = ex.Message; }
            try { state = StartupManager.State(); }
            catch (Exception ex) { error ??= ex.Message; }
        });
        _startupBusy = false;
        _startupState = state;
        _startupError = error;
        if (enable is { } enabled && error is null)
            _app.Notify(enabled ? "Starts with Windows" : "Won't start with Windows", "", NoticeKind.Success);
        if (_shown || enable is not null)
            Build();
    }

    private void BuildWindow(StackPanel window)
    {
        window.Children.Add(Ui.SettingRow("Close in background",
            U.CloseToTray
                ? "X hides to the tray; Pit-Box keeps running (keybinds, fans, overlay)."
                : "X quits Pit-Box.",
            Ui.Switch(U.CloseToTray, v => { U.CloseToTray = v; _app.Save(); Build(); })));
        window.Children.Add(Ui.Divider());
        var pages = _app.NavPages().Prepend((Key: UiSettings.LastUsedPage, Title: "Last page used")).ToList();
        var start = new ComboBox { MinWidth = 162, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (key, title) in pages) start.Items.Add(new ComboBoxItem { Content = title, Tag = key });
        int at = pages.FindIndex(p => p.Key == U.StartPage);
        start.SelectedIndex = Math.Max(0, at);
        start.SelectionChanged += (_, _) =>
        {
            if (start.SelectedItem is ComboBoxItem { Tag: string key }) { U.StartPage = key; _app.Save(); }
        };
        var startRow = Ui.SettingRow("Open on", null, start,
            new Ui.SettingDefault(() => U.StartPage == UiSettings.LastUsedPage, () => { U.StartPage = UiSettings.LastUsedPage; _app.Save(); Build(); }, "Last page used"));
        Ui.Anchor(startRow, "settings.startpage");
        window.Children.Add(startRow);
        window.Children.Add(Ui.Divider());
        void SetAllOpen(bool on)
        {
            if (U.SidebarAllOpen == on) return;
            U.SidebarAllOpen = on;
            _app.Save();
            _window.RefreshSidebar();
        }
        var allOpenRow = Ui.SettingRow("Show every tool's sections", "Off: only the open tool lists its sections in the sidebar. Drag tools and their sections in the sidebar to reorder them.",
            Ui.Switch(U.SidebarAllOpen, SetAllOpen),
            new Ui.SettingDefault(() => !U.SidebarAllOpen, () => { SetAllOpen(false); Build(); }, "Off"));
        Ui.Anchor(allOpenRow, "settings.sidebaropen");
        window.Children.Add(allOpenRow);
        window.Children.Add(Ui.Divider());
        var onTop = Ui.SettingRow("Keep on top", null,
            Ui.Switch(U.AlwaysOnTop, v => { U.AlwaysOnTop = v; _window.Topmost = v; _app.Save(); }),
            new Ui.SettingDefault(() => !U.AlwaysOnTop, () => { U.AlwaysOnTop = false; _window.Topmost = false; _app.Save(); Build(); }, "Off"));
        Ui.Anchor(onTop, "settings.ontop");
        window.Children.Add(onTop);
        window.Children.Add(Ui.Divider());
        window.Children.Add(Ui.SettingRow("Notifications", "Warnings always show.",
            Ui.Switch(_app.Config.ShowNotifications, v => { _app.Config.ShowNotifications = v; _app.Save(); }),
            new Ui.SettingDefault(() => _app.Config.ShowNotifications, () => { _app.Config.ShowNotifications = true; _app.Save(); Build(); }, "On")));
        window.Children.Add(Ui.Divider());
        var quit = Ui.LabeledButton("I.Power", "Quit Pit-Box", () => _ = _window.RequestQuitAsync(), "GhostButton");
        quit.ToolTip = "Closes Pit-Box: keybinds, the overlay and the tray icon stop";
        window.Children.Add(Ui.SettingRow("Quit", null, quit, more: "With fan control on, it asks first whether a small helper keeps your fan curves running or the fans go back to the BIOS / driver."));
    }

    private void BuildAppearance()
    {
        var look = Section("Appearance", "I.Palette", "settings.look");
        look.Children.Add(new TextBlock { Text = "Theme", Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 0) });
        look.Children.Add(new Border { Height = 8 });
        look.Children.Add(new ThemePicker(U.Theme, _app.SetTheme, columns: 2, height: 52, oled: _app.UseOledColors) { Margin = new Thickness(0, 0, -8, 3) });
        look.Children.Add(Ui.Divider());
        var custom = new CustomThemeSection(_app, _window);
        Ui.Anchor(custom, "settings.customcolors");
        look.Children.Add(custom);
        look.Children.Add(Ui.Divider());

        string animationNote = SystemParameters.ClientAreaAnimation
            ? "Page transitions, card entrances, spinning fan icons. Nothing animates while Pit-Box is in the tray." + (PcClass.Weak ? " This PC gets a lighter version: 30 frames a second, no blur." : "")
            : "Windows' “Animation effects” setting is off, so animations stay off.";
        var anim = Ui.Switch(U.Animations, v => { U.Animations = v; _app.Save(); NavIcon.RefreshAll(); Motion.Refresh(); });
        anim.IsEnabled = SystemParameters.ClientAreaAnimation;
        look.Children.Add(Ui.SettingRow("Animations", animationNote, anim,
            SystemParameters.ClientAreaAnimation ? new Ui.SettingDefault(() => U.Animations, () => { U.Animations = true; _app.Save(); NavIcon.RefreshAll(); Motion.Refresh(); Build(); }, "On") : null));
    }

    private TextBlock? _rateNote;

    private void BuildPerformance()
    {
        var section = Section("Performance", "I.Gauge", "settings.performance");
        var p = _app.Config.Performance;
        var options = PerformanceSettings.Presets.Append(SensorRate.Custom).ToArray();
        var rate = Ui.Segmented(options.Select(PerformanceSettings.Name).ToArray(), Array.IndexOf(options, p.Rate), i =>
        {
            _app.SetPerformance(options[i]);
            Build();
        }, "SensorRate", options.Select(r => (string?)(r switch
        {
            SensorRate.Low => "Low: every 2 s. The lightest on your PC.",
            SensorRate.Fast => "Fast: every 0.5 s. Smoothest charts; uses a little more CPU.",
            SensorRate.Custom => "Custom: pick your own interval, 0.25 to 5 s.",
            _ => "Balanced: every second. The normal rate.",
        })).ToList());
        _rateNote = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        FillRateNote();
        var rateRow = RowWithNote("Sensor update rate", _rateNote, rate,
            $"How often temperatures, loads, clocks and fan speeds are read for every page. Fan control still checks its curves and the failsafe at least every {PerformanceSettings.FanLoopMs / 1000} {Units.S}, and the overlay uses its own rate while it's showing. Applies at once.");
        Ui.Anchor(rateRow, "settings.sensorrate");
        section.Children.Add(rateRow);

        if (p.Rate == SensorRate.Custom)
        {
            var custom = new NumberBox(p.CustomMs, Units.Ms) { Minimum = PerformanceSettings.MinMs, Maximum = PerformanceSettings.MaxMs, Step = 50 };
            custom.ValueChanged += v =>
            {
                _app.SetPerformance(SensorRate.Custom, (int)Math.Round(v));
                FillRateNote();
            };
            section.Children.Add(Ui.SettingRow("Custom interval", $"From {PerformanceSettings.MinMs} {Units.Ms} to {PerformanceSettings.MaxMs / 1000} {Units.S}.", custom));
        }

        section.Children.Add(Ui.Divider());
        var background = Ui.Segmented(new[] { "Standard", "Reduced" }, (int)p.Background,
            i => _app.SetPerformance(_app.Config.Performance.Rate, background: (BackgroundUsage)i), "Background", new[]
            {
                "Standard: the same rate whether the window is open or not.",
                $"Reduced: while the window is in the tray or minimized, sensors are read every {PerformanceSettings.ReducedHiddenMs / 1000} {Units.S} at most.",
            });
        var backgroundRow = Ui.SettingRow("Background usage", "Fewer sensor reads while the window is hidden.", background,
            new Ui.SettingDefault(() => _app.Config.Performance.Background == BackgroundUsage.Reduced, () => { _app.SetPerformance(_app.Config.Performance.Rate, background: BackgroundUsage.Reduced); Build(); }, "Reduced"),
            $"Reduced reads sensors at most every {PerformanceSettings.ReducedHiddenMs / 1000} s while the window is in the tray or minimized (pages already pause then). Fan control, the overlays, keybinds and window profiles keep working as normal.");
        Ui.Anchor(backgroundRow, "settings.background");
        section.Children.Add(backgroundRow);

        section.Children.Add(Ui.Divider());
        var lightRow = Ui.SettingRow("Lighter drawing while iRacing runs", "Keeps pages quick mid-race.",
            Ui.Switch(p.LightWhileRacing, v => { p.LightWhileRacing = v; _app.Save(); _app.WatchSim(); }),
            new Ui.SettingDefault(() => p.LightWhileRacing, () => { p.LightWhileRacing = true; _app.Save(); _app.WatchSim(); Build(); }, "On"),
            "Pit-Box's window draws without the graphics card the sim keeps busy, and its animations pause, so pages open quickly mid-race. Back to normal when iRacing closes.");
        Ui.Anchor(lightRow, "settings.light");
        section.Children.Add(lightRow);
    }

    private void FillRateNote()
    {
        if (_rateNote is null) return;
        int ms = _app.Config.Performance.IntervalMs;
        _rateNote.Text = $"Sensors are read every {PerformanceSettings.Describe(ms)}.";
    }

    private static Grid RowWithNote(string title, TextBlock note, UIElement control, string? more = null)
    {
        var grid = Ui.SettingRow(title, null, control, more: more);
        ((StackPanel)grid.Children[0]).Children.Add(note);
        return grid;
    }

    private TextBlock _diagStatus = new();

    private void BuildDiagnostics(StackPanel parent)
    {
        var section = new StackPanel();
        Ui.Anchor(section, "settings.diagnostics");
        parent.Children.Add(section);
        section.Children.Add(new TextBlock { Text = ThemeManager.Case("Diagnostics"), Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 6, 0, 3) });
        section.Children.Add(new TextBlock
        {
            Text = "Tools for troubleshooting hardware and app problems. Reports leave out personal details (user and PC names, folders, IP addresses).",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8),
        });
        var buttons = new WrapPanel();
        var rescan = Ui.LabeledButton("I.Search", "Rescan hardware", Rescan);
        rescan.ToolTip = "Closes the sensor drivers and opens them again, so new or missing temperature sensors, fans and fan controllers are detected. Fans follow the BIOS for a moment meanwhile.";
        Ui.Anchor(rescan, "settings.rescan");
        buttons.Children.Add(Spaced(rescan));
        var refresh = Ui.LabeledButton("I.Refresh", "Refresh sensors", () =>
        {
            _app.Hardware.RefreshNow();
            DiagStatus(_app.Hardware.Running ? "Reading every sensor and fan now." : "The sensors are idle (nothing is using them), so there's nothing to refresh.");
        });
        refresh.ToolTip = "Reads every sensor and fan now instead of waiting for the next update";
        buttons.Children.Add(Spaced(refresh));
        var logs = Ui.LabeledButton("I.FileText", "Open logs", OpenLogs);
        logs.ToolTip = "Opens the settings folder with error.log selected (problems Pit-Box ran into)";
        Ui.Anchor(logs, "settings.logs");
        buttons.Children.Add(Spaced(logs));
        var export = Ui.LabeledButton("I.Save", "Export diagnostic report", () => _ = ExportReportAsync());
        export.ToolTip = "Saves a text report (versions, detected hardware, fans, sensors, plugins, settings counts, recent errors) to share when asking for help";
        Ui.Anchor(export, "settings.report");
        buttons.Children.Add(Spaced(export));
        var copy = Ui.LabeledButton("I.Copy", "Copy system information", () => _ = CopySystemInfoAsync());
        copy.ToolTip = "Copies a few lines (Pit-Box and Windows versions, CPU, GPU, sensor status) to paste into a message";
        buttons.Children.Add(Spaced(copy));
        section.Children.Add(buttons);
        _diagStatus = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 2, 0, 0) };
        section.Children.Add(_diagStatus);
    }

    private void DiagStatus(string text)
    {
        _diagStatus.Text = text;
        _diagStatus.Visibility = Visibility.Visible;
    }

    private void Rescan()
    {
        if (_app.Demo)
        {
            _app.Hardware.RefreshNow();
            DiagStatus("Demo data: nothing on this PC is scanned.");
            return;
        }
        DiagStatus(_app.Hardware.Rescan()
            ? "Rescanning: the sensors are opened again; new devices and fans show up in a few seconds."
            : "The sensors aren't in use right now; they're detected afresh the next time a page or plugin needs them.");
    }

    private void OpenLogs()
    {
        if (!PitBox.Services.External.Allowed("open the log folder")) return;
        try
        {
            if (File.Exists(DiagnosticReport.LogPath))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{DiagnosticReport.LogPath}\"") { UseShellExecute = true })?.Dispose();
            else
            {
                OpenFolder(ConfigStore.Folder);
                DiagStatus("No errors logged so far (there's no error.log).");
            }
        }
        catch { OpenFolder(ConfigStore.Folder); }
    }

    private async Task ExportReportAsync()
    {
        if (!PitBox.Services.External.Allowed("save dialog")) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export diagnostic report",
            Filter = "Text file (*.txt)|*.txt|Markdown (*.md)|*.md",
            FileName = $"PitBox-diagnostics-{DateTime.Now:yyyy-MM-dd-HHmm}.txt",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        DiagStatus("Collecting…");
        try
        {
            string report = await DiagnosticReport.BuildAsync(_app, full: true);
            await File.WriteAllTextAsync(dialog.FileName, report);
            DiagStatus($"Report saved as {Path.GetFileName(dialog.FileName)}.");
            _app.Notify("Diagnostic report saved", Path.GetFileName(dialog.FileName), NoticeKind.Success);
        }
        catch (Exception ex)
        {
            DiagStatus("");
            _diagStatus.Visibility = Visibility.Collapsed;
            _app.Notify("Couldn't save the report", ex.Message, NoticeKind.Error, important: true);
        }
    }

    private async Task CopySystemInfoAsync()
    {
        try
        {
            string info = await DiagnosticReport.BuildAsync(_app, full: false);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    Clipboard.SetText(info);
                    break;
                }
                catch (System.Runtime.InteropServices.COMException) when (attempt < 3)
                {
                    await Task.Delay(100);
                }
            }
            DiagStatus("System information copied. Paste it into your message.");
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't copy", ex.Message, NoticeKind.Warning);
        }
    }

    private void BuildData()
    {
        var section = Section("Data and configuration", "I.Folder", "settings.data");
        var transfer = new StackPanel { Orientation = Orientation.Horizontal };
        var export = Ui.LabeledButton("I.Save", "Export…", () => ExportSettings());
        export.ToolTip = "Saves every setting (theme, window profiles, fan curves and presets, overlay, keybinds, plugins, layouts…) to a file";
        export.Margin = new Thickness(0, 0, 6, 0);
        transfer.Children.Add(export);
        var import = Ui.LabeledButton("I.Download", "Import…", () => _ = ImportSettingsAsync());
        import.ToolTip = "Loads settings from an exported file (shows what's in it first; Pit-Box then restarts)";
        transfer.Children.Add(import);
        var transferRow = Ui.SettingRow("Export / import settings",
            "Importing replaces your settings (a copy is kept) and restarts Pit-Box.",
            transfer);
        Ui.Anchor(transferRow, "settings.export");
        section.Children.Add(transferRow);
        section.Children.Add(Ui.Divider());

        var folder = Ui.LabeledButton("I.Folder", "Open folder", () => OpenFolder(ConfigStore.Folder));
        folder.ToolTip = TextCase.VerbatimTip(ConfigStore.Folder);
        var fileRow = Ui.SettingRow("Settings file", "Your settings, the error log, backups made by Reset and Import, and iRacing backups, all in one folder.", folder);
        Ui.Anchor(fileRow, "settings.folder");
        section.Children.Add(fileRow);
        section.Children.Add(Ui.Divider());
        section.Children.Add(ResetRow());
        section.Children.Add(Ui.Divider());
        BuildDiagnostics(section);
    }

    private void ExportSettings()
    {
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Demo data isn't exported.", NoticeKind.Info);
            return;
        }
        if (!PitBox.Services.External.Allowed("save dialog")) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export settings",
            Filter = "Pit-Box settings (*.json)|*.json",
            FileName = $"PitBox-settings-{DateTime.Now:yyyy-MM-dd}.json",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        try
        {
            File.WriteAllText(dialog.FileName, ConfigTransfer.Export(_app.Config, UpdateService.Show(UpdateService.Current), DateTime.Now));
            _app.Notify("Settings exported", Path.GetFileName(dialog.FileName), NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't export", ex.Message, NoticeKind.Error, important: true);
        }
    }

    private async Task ImportSettingsAsync()
    {
        if (!PitBox.Services.External.Allowed("open dialog")) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import settings", Filter = "Pit-Box settings (*.json)|*.json|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;
        string text;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 20 * 1024 * 1024)
                throw new IOException("The file is too big to be a settings file.");
            text = await File.ReadAllTextAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't read the file", ex.Message, NoticeKind.Error, important: true);
            return;
        }
        var preview = ConfigTransfer.Read(text, UpdateService.Current);
        if (preview.Config is not { } imported)
        {
            _app.Notify("Nothing imported", preview.Error ?? "The file couldn't be used.", NoticeKind.Warning, important: true);
            return;
        }
        if (!await _window.Confirm("Import these settings?",
                $"{Path.GetFileName(dialog.FileName)} replaces all your current settings with:\n\n" +
                string.Join("\n", preview.Lines.Select(l => "•  " + l)) +
                "\n\nPit-Box will restart. A copy of your current settings is kept (config.before-import-….json in the settings folder). Start with Windows and changes already made to Windows aren't affected.",
                "Import and restart", danger: true))
            return;
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Demo data is never saved, so nothing was imported.", NoticeKind.Info);
            return;
        }
        try
        {
            _app.FlushPendingSave();
            ConfigStore.ReplaceWith(imported);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't import", ex.Message, NoticeKind.Error, important: true);
            return;
        }
        App.RelaunchAfterExit("");
    }

    private void BuildHardware()
    {
        var access = Section("Hardware access", "I.Cpu", "settings.access");
        access.Children.Add(StatusRow("Administrator", _app.IsAdmin || _app.Demo,
            _app.Demo ? "Demo mode: nothing on this PC is changed, so administrator rights aren't needed." : _app.IsAdmin ? "Running as administrator: FPS counting, sensors and fan control are available." : "Not running as administrator, so FPS, CPU temperature and fan control are limited."));
        access.Children.Add(Ui.Divider());
        var pawnState = _app.Hardware.PawnIoState;
        bool pawnOk = pawnState == PawnIoState.Installed;
        var pawn = StatusRow("PawnIO driver", pawnOk, pawnState switch
        {
            PawnIoState.Installed => _app.Hardware.CpuTempMissing
                ? "Installed, but the CPU's temperature sensors didn't answer. Restart Pit-Box; if it stays missing, this CPU may not be supported yet."
                : "Installed.",
            PawnIoState.RemovalPending => "Uninstalled, but still loaded: programs still have it open (Pit-Box, other fan or monitoring tools), so Windows can't remove it yet. Restart Windows to finish; it can be installed again after that.",
            _ => "Not installed. CPU temperatures and motherboard fan headers need the free PawnIO driver. Install it, then restart Pit-Box.",
        });
        if (pawnState == PawnIoState.Missing)
        {
            var install = Ui.LabeledButton("I.Download", "Install PawnIO", InstallPawnIo, "PrimaryButton");
            Grid.SetColumn(install, 1);
            pawn.Children.Add(install);
        }
        access.Children.Add(pawn);
        access.Children.Add(Explainer());
        access.Children.Add(Ui.Divider());
        var hw = _app.Hardware;
        var catalog = hw.Catalog;
        access.Children.Add(StatusRow("Sensors", hw.OpenError is null,
            hw.OpenError ?? (!hw.Running ? "Idle: no plugin or open page needs them right now."
                : $"{PitBox.SimRacing.Race.RaceFormat.Count(catalog?.Temperatures.Count ?? 0, "temperature sensor")} and {PitBox.SimRacing.Race.RaceFormat.Count(catalog?.Fans.Count ?? 0, "fan")} detected.")));
    }

    private FrameworkElement Explainer()
    {
        var details = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 3, 0, 3), MaxWidth = 616, HorizontalAlignment = HorizontalAlignment.Left };
        void Para(string title, string text)
        {
            details.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) });
            details.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
        }
        Para("What it is",
            "PawnIO is a small, free, open-source Windows driver. Windows only lets drivers read certain chips directly: the CPU's own temperature and power sensors, and the motherboard's fan controller. The sensor library Pit-Box uses talks to those chips through PawnIO. It replaces the older WinRing0 driver, which Windows Defender now flags as unsafe.");
        Para("What it's used for here",
            "Reading CPU temperature, CPU power and motherboard sensors, and setting motherboard fan speeds when Fan Control is on. Nothing else. Pit-Box doesn't send data anywhere and has no account or telemetry.");
        Para("Is it required?",
            "No. Without it, everything else works: GPU temperatures and fans, frame rate, displays, window profiles, overlays, sim racing tools. CPU temperature shows as “Not available” and motherboard fans can't be controlled.");
        Para("Is it safe?",
            "The driver is signed for Windows and works through small modules written for one job each (an AMD or Intel CPU's sensors, a fan controller chip), instead of the old driver's open access to any hardware address. Some anti-cheat systems are strict about any low-level driver; if a game ever complains, uninstall PawnIO and Pit-Box carries on without it.");
        Para("How to install or remove it",
            "Install PawnIO (above) runs “winget install namazso.PawnIO” in a window you can watch. Remove it any time from Windows Settings → Apps → Installed apps → PawnIO, or “winget uninstall namazso.PawnIO”. Restart Pit-Box after either.");
        Para("Why Pit-Box runs as administrator",
            "Counting frames (Windows' own event tracing), reading sensors, controlling fans, changing displays and watching which programs start all need it. Windows asks each time it starts; with “Start with Windows” it starts elevated at sign-in without a prompt.");
        var toggle = Ui.LabeledButton("I.Info", "What is PawnIO, and is it safe?", () => { }, "GhostButton");
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Margin = new Thickness(0, 2, 0, 2);
        toggle.Click += (_, _) => details.Visibility = details.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        var stack = new StackPanel();
        stack.Children.Add(toggle);
        stack.Children.Add(details);
        Ui.Anchor(stack, "settings.pawnio");
        return stack;
    }

    private StackPanel _updates = new();

    private void BuildAboutAndUpdates()
    {
        var section = Section("About and updates", "I.Info", "settings.updates");
        Ui.Anchor(section, "settings.about");
        var about = new Grid { Margin = new Thickness(0, 2, 0, 6) };
        about.ColumnDefinitions.Add(new ColumnDefinition());
        about.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        text.Children.Add(new TextBlock { Text = $"Pit-Box {UpdateService.Tag(_app.Updates.Installed)}" + (_app.Demo ? " · demo data" : ""), Style = Ui.Res<Style>("Body"), FontWeight = FontWeights.SemiBold });
        about.Children.Add(text);
        var project = Ui.LabeledButton("I.External", "Project page", () => Ui.OpenUrl(UpdateService.ProjectPage), "GhostButton");
        var changelog = Ui.LabeledButton("I.FileText", "Changelog", () => ChangelogDialog.Show(_window, _app.Updates, null, justUpdated: false), "GhostButton");
        changelog.Margin = new Thickness(0, 0, 6, 0);
        var links = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        links.Children.Add(changelog);
        links.Children.Add(project);
        Grid.SetColumn(links, 1);
        about.Children.Add(links);
        section.Children.Add(about);
        section.Children.Add(Ui.Divider());
        _updates = new StackPanel();
        section.Children.Add(_updates);
        FillUpdates();
    }

    private void FillUpdates()
    {
        var u = _app.Updates;
        _updates.Children.Clear();
        _updates.Children.Add(new TextBlock { Text = ThemeManager.Case("Updates"), Style = Ui.Res<Style>("Eyebrow"), Margin = new Thickness(0, 6, 0, 3) });

        if (u.Available && u.Latest is { } r)
        {
            var box = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(Ui.Badge("I.Download", "Accent", "Knob", 26, 8, 14));
            var title = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "A new version of Pit-Box is available", Style = Ui.Res<Style>("H3") });
            title.Children.Add(new TextBlock { Text = $"{UpdateService.Tag(u.Installed)} → {UpdateService.Tag(r.Version)}. Install it here; your settings stay.", Style = Ui.Res<Style>("Caption") });
            head.Children.Add(title);
            box.Children.Add(head);
            var buttons = new WrapPanel { Margin = new Thickness(36, 10, 0, 0) };
            if (u.State == UpdateState.Downloading)
            {
                buttons.Children.Add(new Meter { Width = 211, Height = 6, Steps = 8, Value = u.Progress, VerticalAlignment = VerticalAlignment.Center });
                buttons.Children.Add(new TextBlock { Text = $"Downloading… {u.Progress:0%}", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                bool self = r.ExeUrl is not null && UpdateService.CanSelfInstall && !_app.Demo;
                var install = Ui.LabeledButton("I.Download", self ? "Install and restart" : "Download", () => _ = u.InstallAsync(), "PrimaryButton");
                install.ToolTip = self ? "Downloads the new version, closes Pit-Box, swaps the file and starts it again (your settings stay)"
                    : "Opens the release page to download it";
                buttons.Children.Add(Spaced(install));
            }
            box.Children.Add(buttons);
            _updates.Children.Add(new Tile { Background = Ui.Res<Brush>("AccentSoft"), BorderBrush = Ui.Res<Brush>("Accent"), BorderThickness = new Thickness(1.5), CornerRadius = ThemeManager.Round(12), Padding = new Thickness(11), Margin = new Thickness(0, 3, 0, 8), Child = box });
        }

        string status = u.State switch
        {
            UpdateState.NotConfigured => "Update checks aren't set up in this build.",
            UpdateState.Checking => "Checking…",
            UpdateState.UpToDate => $"You have the latest version ({UpdateService.Tag(u.Installed)}).",
            UpdateState.Available or UpdateState.Downloading => "A newer version is ready (above).",
            UpdateState.Failed => $"The last check didn't work: {u.Error}",
            _ => _app.Config.Updates.LastCheckUtc is { } last ? $"Last checked {Dates.ShortTime(last.ToLocalTime())}." : "Not checked yet.",
        };
        var check = Ui.LabeledButton("I.Refresh", "Check now", () => _ = u.CheckAsync(manual: true));
        check.IsEnabled = u.State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.NotConfigured);
        _updates.Children.Add(Ui.SettingRow("Check for updates", status, check));
        _updates.Children.Add(Ui.Divider());
        var when = Ui.Segmented(new[] { "On start", "Daily", "Weekly", "Manually" }, (int)_app.Config.Updates.Check, i =>
        {
            _app.Config.Updates.Check = (UpdateCheck)i;
            _app.Save();
            u.ApplySchedule();
        }, "UpdateCheck", new[] { "Every time Pit-Box starts", "Once a day", "Once a week", "Only when you click Check now" });
        _updates.Children.Add(Ui.SettingRow("When to check", "Nothing is downloaded until you click Install.", when));
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        yield return ("tools", () =>
        {
            _window.GoTo("settings", "settings.plugins", flash: false);
            _ = ShowTools();
            return Task.CompletedTask;
        });
        yield return ("tools-closed", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("data-section", () =>
        {
            ShowSection("data");
            return Task.CompletedTask;
        });
        yield return ("sidebar-all-open", () =>
        {
            U.SidebarAllOpen = true;
            _window.RefreshSidebar();
            return Task.CompletedTask;
        });
        yield return ("sidebar-open-tool-only", () =>
        {
            U.SidebarAllOpen = false;
            _window.RefreshSidebar();
            ShowSection("app");
            return Task.CompletedTask;
        });
        yield return ("confirm-delete", () =>
        {
            _ = _window.Confirm("Delete saved colors?", "“Dusk” is removed from My colors. The colors in use now don't change.", "Delete", danger: true);
            return Task.CompletedTask;
        });
        yield return ("quit-with-fans", () =>
        {
            _window.CloseModal(false);
            _ = _window.ShowModal(_window.FanQuitCard(_ => { }, out _));
            return Task.CompletedTask;
        });
        yield return ("confirm-delete-closed", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        yield return ("pawnio-prompt", () =>
        {
            _ = OfferPawnIoAsync(force: true);
            return Task.CompletedTask;
        });
        yield return ("pawnio-prompt-closed", () =>
        {
            _window.CloseModal(false);
            return Task.CompletedTask;
        });
        var theme = U.Theme;
        yield return ("custom-colors-grayed", () =>
        {
            _window.GoTo("settings", "settings.customcolors", flash: false);
            return Task.CompletedTask;
        });
        yield return ("custom-colors-on", () =>
        {
            U.OledCustom.Enabled = true;
            U.OledCustom.Colors = CustomPalette.Presets[0].Colors.Copy();
            Themes.SetOledCustom(U.OledCustom);
            _app.SetTheme(AppTheme.Custom);
            _window.GoTo("settings", "settings.customcolors", flash: false);
            return Task.CompletedTask;
        });
        yield return ("custom-colors-main-page", () =>
        {
            _window.Navigate(_window.FirstPage);
            return Task.CompletedTask;
        });
        yield return ("custom-colors-back", () =>
        {
            U.OledCustom.Enabled = false;
            U.OledCustom.Colors = new ThemeColors();
            Themes.SetOledCustom(U.OledCustom);
            _app.SetTheme(theme);
            _window.Navigate("settings");
            return Task.CompletedTask;
        });
    }

    private Grid ResetRow()
    {
        var button = Ui.LabeledButton("I.Undo", "Reset Pit-Box", () => _ = ResetAsync(), "DangerButton");
        button.ToolTip = "Asks first. Your current settings are kept as a copy in the settings folder.";
        var row = Ui.SettingRow("Back to defaults",
            "Every setting, profile, layout and fan curve. Pit-Box restarts; a copy of your settings is kept.",
            button);
        Ui.Anchor(row, "settings.reset");
        return row;
    }

    private async Task ResetAsync()
    {
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Demo data is never saved, so there's nothing to reset.", NoticeKind.Info);
            return;
        }
        if (!await _window.Confirm("Reset Pit-Box to defaults?",
                "Everything goes back to how it was on first install: window profiles and groups, display profiles, " +
                "fan curves and calibration, overlay layouts, page layouts, the sidebar order and these settings. Pit-Box then restarts.\n\n" +
                "Kept: a copy of your current settings (config.before-reset-….json in the settings folder, rename it to config.json to go back), " +
                "iRacing backups, the PawnIO driver and the Start with Windows task. Changes already made to Windows (displays, sound) aren't undone.",
                "Reset and restart", danger: true))
            return;
        try
        {
            ConfigStore.ResetToDefaults();
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't reset", ex.Message, NoticeKind.Error, important: true);
            return;
        }
        App.RelaunchAfterExit("");
    }

    private static Grid Notice(string soft, string deep, string icon, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = Ui.Badge(icon, soft, deep, 22.5, 6.5, 12);
        badge.VerticalAlignment = VerticalAlignment.Top;
        grid.Children.Add(badge);
        var t = new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t, 1);
        grid.Children.Add(t);
        return grid;
    }

    private StackPanel Section(string title, string icon, string? anchor = null)
    {
        var stack = new StackPanel { Margin = new Thickness(15, 10, 15, 15) };
        var head = new StackPanel { Orientation = Orientation.Horizontal };
        head.Children.Add(new Tile { Style = Ui.Res<Style>("IconBadge"), Width = 24, Height = 24, CornerRadius = ThemeManager.Round(9),
            Child = new Icon { Data = Ui.Res<Geometry>(icon), Width = 13, Height = 13 } });
        var name = RaceParts.Big(title, 20, "Heading", italic: false, heavy: false);
        name.Margin = new Thickness(8, 0, 0, 0);
        head.Children.Add(name);
        var band = new Grid();
        band.Children.Add(new Slant("Place4Wash", 0, 0, 0.78, 0, 0.7, 1, 0, 1));
        band.Children.Add(new Slant("Place4Band", 0.78, 0, 0.8, 0, 0.72, 1, 0.7, 1));
        head.Margin = new Thickness(15, 9, 15, 9);
        band.Children.Add(head);
        var outer = new StackPanel { Children = { band, stack } };
        var card = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Child = outer, ClipToBounds = true };
        if (anchor is not null)
            Ui.Anchor(card, anchor);
        _sections.Children.Add(card);
        return stack;
    }

    private static Grid StatusRow(string title, bool ok, string text)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var row = new DockPanel();
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = Ui.Res<Brush>(ok ? "Sage" : "Apricot"), Margin = new Thickness(0, 5, 8, 0), VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        var text2 = new StackPanel();
        text2.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold });
        text2.Children.Add(new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 10, 0) });
        row.Children.Add(text2);
        grid.Children.Add(row);
        return grid;
    }

    private static FrameworkElement Spaced(FrameworkElement e)
    {
        e.Margin = new Thickness(0, 0, 6, 6);
        return e;
    }

    private static bool s_pawnIoOffered;
    private bool _pawnIoOpen;

    internal async Task OfferPawnIoAsync(bool force = false)
    {
        if (_pawnIoOpen || !force && (_app.Demo || U.HidePawnIoPrompt || _app.Hardware.PawnIoState != PawnIoState.Missing))
            return;
        _pawnIoOpen = true;
        try
        {
            var stack = new StackPanel { MaxWidth = 373, HorizontalAlignment = HorizontalAlignment.Left };
            void Para(string text) => stack.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
            Para("CPU temperatures and motherboard fans (case, CPU and pump headers) are read and controlled through PawnIO, a small free driver.");
            Para("It replaces WinRing0, the older driver Windows Defender now flags as unsafe, and is installed for the whole PC.");
            Para("Without it everything else still works: GPU temperatures and fans, frame rate, displays, window profiles and the overlays.");
            var hide = new CheckBox { Content = "Don't show this again", IsChecked = U.HidePawnIoPrompt, Margin = new Thickness(0, 13, 0, 0) };
            stack.Children.Add(hide);
            var card = Ui.Dialog(_window, "Install the PawnIO driver?", "It isn't installed on this PC.", stack,
                Ui.DialogButtons(_window, "Install", () => true), null, 437);
            bool install = await _window.ShowModal(card);
            if (hide.IsChecked == true != U.HidePawnIoPrompt)
            {
                U.HidePawnIoPrompt = hide.IsChecked == true;
                _app.Save();
            }
            if (install)
                InstallPawnIo();
        }
        finally
        {
            _pawnIoOpen = false;
        }
    }

    private void InstallPawnIo()
    {
        if (_app.Demo)
        {
            _app.Notify("Demo mode", "Nothing is installed in demo mode.", NoticeKind.Info);
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/k winget install namazso.PawnIO -e && (echo. & echo Done. Restart Pit-Box to use it.) || (echo. & echo If it says exit code 1072, an earlier copy is still waiting to be removed: restart Windows, then install again.)") { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            Ui.OpenUrl("https://pawnio.eu");
        }
    }

    private static void OpenFolder(string folder)
    {
        if (!PitBox.Services.External.Allowed("open " + folder)) return;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true })?.Dispose();
        }
        catch { }
    }
}
