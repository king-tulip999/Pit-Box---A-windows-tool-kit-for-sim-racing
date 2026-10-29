using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Config;
using PitBox.Controls;
using PitBox.Services;
using PitBox.SimRacing.IracingFiles;
using PitBox.SimRacing.Race;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed partial class IracingControllerSection : StackPanel
{
    private static readonly string[] Tabs = { "All settings", "Keybinds", "Health and backups" };
    private const int MaxRows = 80;
    private static readonly HashSet<string> HiddenSections = new(StringComparer.OrdinalIgnoreCase) { "renderer|AutoCfg", "renderer|Debug" };
    private const string WideSection = "renderer|Graphics Options";
    private const int WideMinRows = 6;

    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly Workspace _ws;
    private readonly StackPanel _tab = new();
    private readonly StackPanel _tabTools = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Tile _pending = new();
    private int _tabIndex;
    private string _search = "";
    private int _area;
    private bool _simRunning;
    private readonly HashSet<string> _searchFolded = new(StringComparer.OrdinalIgnoreCase);
    private string[] _words = Array.Empty<string>();
    private bool _changedOnly;
    private const double SideNavMin = 875;
    private Action? _draftsChanged;
    private FrameworkElement? _where, _modes;
    private KeybindsBoard? _keybinds;

    public IracingControllerSection(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        _ws = new Workspace(app.Config.SimRacing.IracingController, app.Demo);
        _ws.Load();
        Build();
    }

    private IracingControllerSettings S => _app.Config.SimRacing.IracingController;
    private SettingsSession? Session => _ws.Session;

    private void Build()
    {
        Children.Clear();
        if (Session is null)
        {
            Children.Add(NotFound());
            return;
        }
        if (!_ws.Demo && LayoutGuard.CaptureIfEmpty(Session, S.SavedLayout)) _app.Save();

        var tabs = Ui.PageTabs(Tabs, _tabIndex, i => { _tabIndex = i; ShowTab(); }, "TiTab");
        tabs.HorizontalAlignment = HorizontalAlignment.Left;
        tabs.VerticalAlignment = VerticalAlignment.Center;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        (_tabTools.Parent as Panel)?.Children.Remove(_tabTools);
        tools.Children.Add(_tabTools);
        _modes = null;
        if (_ws.Modes.Count > 1)
        {
            var modes = Ui.Combo(_ws.Modes.Select(m => new Choice(m.File, m.Label + " graphics")), Session.RendererFile, c => _ = SwitchMode(c.Id!), 138);
            modes.ToolTip = "Which display mode's graphics file to show (iRacing keeps one per mode)";
            tools.Children.Add(modes);
            _modes = modes;
        }
        if (!_ws.Demo && _ws.Folder is { } folder)
            tools.Children.Add(Ui.IconButton("I.Folder", "Open the iRacing folder", () => Ui.OpenUrl(folder)));
        tools.Children.Add(Ui.IconButton("I.Refresh", "Read the files again", () => _ = Reload()));
        var bar = Ui.SplitRow(tabs, tools);
        bar.Margin = new Thickness(0, 0, 0, 10);
        Children.Add(bar);

        var where = TextFit.TipWhenTrimmed(new TextBlock { Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis });
        if (_ws.Demo) where.Inlines.Add(new System.Windows.Documents.Run("Demo settings (nothing is read from or written to the PC) · "));
        else where.Inlines.Add(TextCase.Verbatim(new System.Windows.Documents.Run(_ws.Folder + " · ")));
        where.Inlines.Add(new System.Windows.Documents.Run($"{Session.Settings.Count} settings in "));
        where.Inlines.Add(TextCase.Verbatim(new System.Windows.Documents.Run(string.Join(", ", Session.FileNames))));
        Children.Add(where);
        _where = where;

        _pending.Style = Ui.Res<Style>("Card");
        _pending.Padding = new Thickness(14, 9, 10, 10);
        _pending.Margin = new Thickness(0, 12, 0, 0);
        _pending.SetResourceReference(Border.BorderBrushProperty, "Accent");
        _pending.BorderThickness = new Thickness(0, 2, 0, 0);
        Children.Add(_tab);
        Children.Add(_pending);
        if (!_trayPinned)
        {
            _trayPinned = true;
            Sticky.InstallBottom(_pending, this, gap: 11);
        }
        ShowTab();
    }

    private Tile NotFound()
    {
        var stack = new StackPanel();
        stack.Children.Add(Ui.Text(_ws.LoadError is null ? "iRacing's settings weren't found" : "iRacing's settings couldn't be read", "H3"));
        stack.Children.Add(new TextBlock
        {
            Text = _ws.LoadError ?? "iRacing Controller reads Documents\\iRacing (app.ini, core.ini and the graphics files). Start iRacing once so it creates them, then check again.",
            Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 10),
        });
        var again = Ui.LabeledButton("I.Refresh", "Check again", () => _ = Reload());
        again.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(again);
        return new Tile { Style = Ui.Res<Style>("Card"), Child = stack };
    }

    private void ShowTab()
    {
        _tab.Children.Clear();
        _tabTools.Children.Clear();
        _draftsChanged = null;
        _simRunning = _ws.SimRunning;
        UpdatePending();
        _keybinds = null;
        var hidden = _tabIndex == 1 ? Visibility.Collapsed : Visibility.Visible;
        if (_where is not null) _where.Visibility = hidden;
        if (_modes is not null) _modes.Visibility = hidden;
        switch (_tabIndex)
        {
            case 0: AllSettings(); break;
            case 1: _tab.Children.Add(_keybinds = IracingKeybindsView.Build(_app, _ws)); break;
            default:
                IReadOnlyList<Backup>? backups = null;
                string? backupProblem = null;
                if (!_ws.Demo)
                {
                    try { backups = _ws.Backups(); }
                    catch (Exception ex) { backupProblem = ex.Message; }
                }
                Overview(backups?.Count);
                BackupsTab(backups, backupProblem);
                break;
        }
    }

    public void OnShown()
    {
        if (Session is { } session && !session.Pending.Any() && _ws.ChangedOnDisk())
        {
            _ws.Load();
            Build();
        }
    }

    private async Task Reload()
    {
        if (!await ConfirmDropPending()) return;
        _ws.Load();
        Build();
    }

    private async Task SwitchMode(string file)
    {
        if (file == Session?.RendererFile) return;
        if (!await ConfirmDropPending()) { Build(); return; }
        _ws.SetMode(file);
        _app.Save();
        Build();
    }

    private async Task<bool> ConfirmDropPending()
    {
        int n = Session?.Pending.Count() ?? 0;
        return n == 0 || await _window.Confirm("Discard changes?", $"{n} change{(n == 1 ? " hasn't" : "s haven't")} been written to iRacing yet.", "Discard", danger: true);
    }

    private async Task DiscardAsync()
    {
        if (Session is null || !await ConfirmDropPending()) return;
        Session.DiscardAll();
        ShowTab();
    }

    private long _simCheckedAt = long.MinValue / 2;

    private void UpdatePending()
    {
        _draftsChanged?.Invoke();
        var pending = Session?.Pending.ToList() ?? new List<Setting>();
        _pending.Visibility = pending.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (pending.Count == 0) return;
        if (Environment.TickCount64 - _simCheckedAt > 2000)
        {
            _simRunning = _ws.SimRunning;
            _simCheckedAt = Environment.TickCount64;
        }

        bool running = _simRunning;
        if (_pending.Child is null || _pendingCount is null || _pendingNote is null || _writeButton is null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(_pendingCount = new TextBlock { FontWeight = FontWeights.SemiBold });
            text.Children.Add(_pendingNote = new TextBlock { Style = Ui.Res<Style>("Caption") });
            text.Children.Add(_pendingChips = new WrapPanel());
            grid.Children.Add(text);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Ui.LabeledButton("I.Eye", "Review", () => _ = Review(), "GhostButton"));
            var discard = Ui.LabeledButton("I.Undo", "Discard", () => _ = DiscardAsync(), "GhostButton");
            discard.Margin = new Thickness(3, 0, 3, 0);
            buttons.Children.Add(discard);
            buttons.Children.Add(_writeButton = Ui.LabeledButton("I.Save", "Write to iRacing", () => _ = Write(), "PrimaryButton"));
            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);
            _pending.Child = grid;
        }
        TextCase.Set(_pendingCount, $"{RaceFormat.Count(pending.Count, "change")} not written yet");
        TextCase.Set(_pendingNote, running ? "iRacing is running. It rewrites its files when it closes, so close it first, then write."
            : _ws.Demo ? "Demo: nothing is written." : "Everything is backed up before writing.");
        _pendingNote.SetResourceReference(TextBlock.ForegroundProperty, running ? "WarnDeep" : "TextMuted");
        _writeButton.IsEnabled = !running && !_ws.Demo;
        FillPendingChips(pending);
    }

    private bool _trayPinned;

    private TextBlock? _pendingCount, _pendingNote;
    private Button? _writeButton;

    private async Task Review()
    {
        var list = new StackPanel();
        foreach (var s in Session!.Pending)
        {
            var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new StackPanel();
            name.Children.Add(new TextBlock { Text = s.Def.Label, FontWeight = FontWeights.SemiBold });
            name.Children.Add(TextCase.Verbatim(new TextBlock { Text = $"{s.FileName} · [{s.Section}] {s.Key}", Style = Ui.Res<Style>("Caption") }));
            row.Children.Add(name);
            var change = new TextBlock { Text = $"{s.Show(s.Applied)}  →  {s.Show(s.Draft)}", Foreground = Ui.Res<Brush>("AccentText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            Grid.SetColumn(change, 1);
            row.Children.Add(change);
            list.Children.Add(row);
        }
        var close = new StackPanel();
        var ok = new Button { Content = "Close", Style = Ui.Res<Style>("PrimaryButton") };
        ok.Click += (_, _) => _window.CloseModal(false);
        close.Children.Add(ok);
        await _window.ShowModal(Ui.Dialog(_window, "Changes to write", "These go into iRacing's files when you write them.", list, close, null, 502));
    }

    private async Task Write()
    {
        if (_ws.SimRunning)
        {
            _app.Notify("Close iRacing first", "It rewrites its settings when it closes, which would undo the changes.", NoticeKind.Warning, important: true);
            _simRunning = true;
            UpdatePending();
            return;
        }
        var danger = Session!.Pending.Where(s => s.Def.Danger is not null).ToList();
        if (danger.Count > 0)
        {
            if (!await _window.Confirm("Write risky changes?", string.Join("\n\n", danger.Select(s => $"{s.Def.Label}: {s.Def.Danger}")), "Write anyway", danger: true))
                return;
            if (_ws.SimRunning)
            {
                _app.Notify("Close iRacing first", "It rewrites its settings when it closes, which would undo the changes.", NoticeKind.Warning, important: true);
                _simRunning = true;
                UpdatePending();
                return;
            }
        }
        if (Session is null) return;
        var layoutKeys = Session.Pending.Select(s => s.Section + "|" + s.Key).ToList();
        try
        {
            var result = Session.Write(_ws.Store);
            string skipped = result.Skipped.Count == 0 ? "" : $" Not found any more: {string.Join(", ", result.Skipped)}.";
            _app.Notify($"Wrote {RaceFormat.Count(result.Written, "change")} to iRacing", $"Backup #{result.Backup?.Number} has the files from before.{skipped}", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify("Changes weren't written", ex.Message + " Your changes are still here to try again.", NoticeKind.Warning, important: true);
            return;
        }
        await WrittenSweep();
        _ws.Load();
        if (Session is { } written) { LayoutGuard.Adopt(written, S.SavedLayout, layoutKeys); _app.Save(); }
        Build();
    }

    private static string Number(int n) => n.ToString(CultureInfo.CurrentCulture);

    private void Overview(int? backupCount)
    {
        HealthReport report;
        try { report = _ws.Health(); }
        catch (Exception ex)
        {
            _tab.Children.Add(FindingCard(Rating.Concerning, "The overview couldn't be built", ex.Message));
            return;
        }
        var layout = _ws.ChangedOnDisk() ? new List<LayoutGuard.Difference>() : LayoutGuard.Differences(Session!, S.SavedLayout);
        int problems = report.Findings.Count + (report.TelemetryLarge ? 1 : 0) + (layout.Count > 0 ? 1 : 0);

        var tiles = RaceParts.Tiles(4,
            RaceParts.Tile(Number(problems), problems == 1 ? "thing to look at" : "things to look at", problems == 0 ? "Sage" : "Warn", problems == 0 ? "SageDeep" : "WarnDeep"),
            RaceParts.Tile(Number(report.Unusual.Count), "changed from iRacing's default", "Slate", "SlateDeep"),
            RaceParts.Tile(Number(report.Passed.Count), "checks passed", "Sage", "SageDeep"),
            RaceParts.Tile(backupCount is { } n ? Number(n) : RaceFormat.Missing, backupCount == 1 ? "backup" : "backups", "Accent", "AccentText"));
        _tab.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 12), Child = tiles });

        if (layout.Count > 0)
            _tab.Children.Add(FindingCard(Rating.Concerning, "iRacing changed your triple-screen layout",
                $"{RaceFormat.Count(layout.Count, "value")} {(layout.Count == 1 ? "differs" : "differ")} from the layout you saved ({string.Join(", ", layout.Take(4).Select(d => d.Setting.Def.Label))}). This often happens after a driver update or a crash.",
                ("Put it back", () => { LayoutGuard.StageRestore(Session!, S.SavedLayout); ShowTab(); }),
                ("Keep iRacing's layout", () =>
                {
                    var before = S.SavedLayout;
                    S.SavedLayout = LayoutGuard.Capture(Session!);
                    _app.Save();
                    ShowTab();
                    _app.Notify("Layout kept", "iRacing's layout is now the one Pit-Box watches for.", NoticeKind.Success, action: new NoticeAction("Undo", () =>
                    {
                        S.SavedLayout = before;
                        _app.Save();
                        ShowTab();
                    }));
                })));

        foreach (var f in report.Findings)
        {
            var actions = new List<(string, Action)>();
            if (f.Id == "telemetry-off" && f.Setting is { } mem)
                actions.Add(("Turn it on", () => { Session!.SetDraft(mem, "1"); ShowTab(); }));
            actions.Add(("Ignore", () => { S.IgnoredFindings.Add(f.Id); _app.Save(); ShowTab(); }));
            _tab.Children.Add(FindingCard(f.Rating, f.Title, f.Detail, actions.ToArray()));
        }

        if (report.TelemetryLarge && report.Telemetry is { } t)
        {
            var old = t.OlderThan(30);
            int unread = 0;
            if (Plugins.BuiltIn.SessionStatsPlugin.Of(_app)?.Store is { } stats)
            {
                var read = old.Where(f => stats.ReadInFull(f.Name)).ToList();
                unread = old.Count - read.Count;
                old = read;
            }
            _tab.Children.Add(FindingCard(Rating.Concerning, $"The telemetry folder is {HealthCheck.Gb(t.TotalBytes)}",
                $"{t.Files.Count} .ibt files from auto-logging. {old.Count} are older than 30 days ({HealthCheck.Gb(old.Sum(f => f.Length))})."
                + (unread > 0 ? $" {RaceFormat.Count(unread, "older file")} Session Stats hasn't read yet {(unread == 1 ? "is" : "are")} kept." : ""),
                ("Move old files to the Recycle Bin", () => _ = CleanTelemetry(old)),
                ("Ignore", () => { S.IgnoredFindings.Add("telemetry-large"); _app.Save(); ShowTab(); })));
        }

        if (report.Unusual.Count > 0)
        {
            _tab.Children.Add(Ui.EditorSection("Changed from iRacing's default"));
            _tab.Children.Add(new TextBlock { Text = "Usually on purpose. Reset one if you didn't mean to change it, or keep it to stop it being listed.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 0, 5) });
            var card = new StackPanel();
            foreach (var s in report.Unusual.Take(30))
                card.Children.Add(UnusualRow(s, card.Children.Count == 0));
            if (report.Unusual.Count > 30)
                card.Children.Add(new TextBlock { Text = $"+{report.Unusual.Count - 30} more: search for them under All settings.", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 6, 0, 0) });
            _tab.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(15, 2, 12, 2), Child = card });
        }

        if (report.Passed.Count > 0)
        {
            _tab.Children.Add(Ui.EditorSection("Passed"));
            var wrap = new WrapPanel();
            foreach (var p in report.Passed)
                wrap.Children.Add(PassedChip(p));
            _tab.Children.Add(wrap);
        }
    }

    private static Tile FindingCard(Rating rating, string title, string detail, params (string Label, Action Run)[] actions)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bool bad = rating == Rating.Bad;
        var edge = new Border { Width = 3, Margin = new Thickness(0, 0, 13, 0) };
        edge.SetResourceReference(Border.BackgroundProperty, bad ? "Danger" : "Warn");
        grid.Children.Add(edge);
        var badge = new Tile
        {
            Width = 30, Height = 30, CornerRadius = ThemeManager.Round(8), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0),
            Background = Ui.Res<Brush>(bad ? "DangerSoft" : "WarnSoft"),
            Child = new Icon { Data = Ui.Res<Geometry>("I.Alert"), Width = 15, Height = 15, Foreground = Ui.Res<Brush>(bad ? "Danger" : "Warn") },
        };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 0, 10) };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = detail, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        for (int i = 0; i < actions.Length; i++)
        {
            var (label, run) = actions[i];
            var b = new Button { Content = label, Style = Ui.Res<Style>(i == 0 && label != "Ignore" ? "SoftButton" : "GhostButton"), Margin = new Thickness(3, 0, 0, 0) };
            b.Click += (_, _) => run();
            buttons.Children.Add(b);
        }
        Grid.SetColumn(buttons, 3);
        grid.Children.Add(buttons);
        var card = new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0, 0, 11, 0), Margin = new Thickness(0, 0, 0, 6), Child = grid };
        if (bad)
        {
            card.Background = Ui.Res<Brush>("DangerSoft");
            card.BorderBrush = Ui.Res<Brush>("Danger");
            card.BorderThickness = new Thickness(1.5);
            var heading = (TextBlock)text.Children[0];
            heading.Foreground = Ui.Res<Brush>("DangerDeep");
            heading.FontWeight = FontWeights.Bold;
        }
        return card;
    }

    private Border UnusualRow(Setting s, bool first)
    {
        var row = new WideGrid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = s.Def.Label, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock { Text = s.SectionInfo.Name, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
        row.Children.Add(text);

        var std = Session!.DefaultOf(s);
        var values = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 16, 0) };
        var mine = Ui.Chip(s.Show(s.Draft), "SlateSoft", "SlateDeep");
        mine.VerticalAlignment = VerticalAlignment.Center;
        mine.ToolTip = "Yours";
        values.Children.Add(mine);
        string? was = std is not null && s.Def.UnusualNote is null ? "Default " + s.Show(std) : s.UnusualNote;
        if (was is not null)
        {
            var note = TextFit.TipWhenTrimmed(new TextBlock { Text = was, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis });
            values.Children.Add(note);
        }
        Grid.SetColumn(values, 1);
        row.Children.Add(values);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (std is not null && !s.IsReadOnly)
        {
            var reset = Ui.LabeledButton("I.Undo", "Reset to default", () => { Session.SetDraft(s, std); ShowTab(); }, "SoftButton");
            buttons.Children.Add(reset);
        }
        var keep = new Button { Content = "Keep mine", Style = Ui.Res<Style>("GhostButton"), Margin = new Thickness(3, 0, 0, 0), ToolTip = "It's on purpose: stop listing it until it changes" };
        keep.Click += (_, _) => { Session.Ignore(s); _app.Save(); ShowTab(); };
        buttons.Children.Add(keep);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(buttons);
        var host = new Border { BorderBrush = Ui.Res<Brush>("Line"), Padding = new Thickness(0, 8, 0, 8), Child = row };
        return first ? host : Hairline.Frame(host, Sides.Top);
    }

    private static Border PassedChip(string text)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var check = new Icon { Data = Ui.Res<Geometry>("I.Check"), Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        check.SetResourceReference(Control.ForegroundProperty, "SageDeep");
        line.Children.Add(check);
        var label = new TextBlock { Text = ThemeManager.Case(text), VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "SageDeep");
        line.Children.Add(label);
        var chip = new Border { Height = 28, Padding = new Thickness(10, 0, 12, 0), Margin = new Thickness(0, 0, 6, 6), Child = line };
        chip.SetResourceReference(Border.BackgroundProperty, "SageSoft");
        return chip;
    }

    private async Task CleanTelemetry(List<FileInfo> files)
    {
        if (files.Count == 0)
        {
            _app.Notify("Nothing to move", "No telemetry files are older than 30 days.", NoticeKind.Info);
            return;
        }
        if (!await _window.Confirm("Move old telemetry to the Recycle Bin?", $"{files.Count} .ibt files ({HealthCheck.Gb(files.Sum(f => f.Length))}) older than 30 days. The newest file is always kept, and you can restore them from the Recycle Bin.", "Move to Recycle Bin", danger: true))
            return;
        try
        {
            int moved = RecycleBin.Send(files.Select(f => f.FullName).ToList());
            _app.Notify("Telemetry tidied", $"{RaceFormat.Count(moved, "file")} moved to the Recycle Bin.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify("Telemetry files weren't moved", ex.Message, NoticeKind.Warning, important: true);
        }
        _ws.ForgetTelemetry();
        ShowTab();
    }

    private void AllSettings()
    {
        var areas = Session!.Catalog.Areas;
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var sideNav = new StackPanel();
        var railLayer = new Canvas { IsHitTestVisible = false };
        var sideCard = new Tile { Style = Ui.Res<Style>("Card"), Width = 215, Padding = new Thickness(6, 6, 6, 3), Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top, Child = new Grid { Children = { railLayer, sideNav } } };
        var topNav = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetRow(topNav, 0);
        Grid.SetColumnSpan(topNav, 2);
        Grid.SetRow(sideCard, 1);
        var content = new StackPanel();
        Grid.SetRow(content, 1);
        layout.Children.Add(topNav);
        layout.Children.Add(sideCard);
        layout.Children.Add(content);
        _tab.Children.Add(layout);
        bool side = false;

        var search = new TextBox { Width = 194, Text = _search, Tag = "Search names, keys and values", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var onlyChanged = Ui.LabeledButton("I.Sliders", "Changed only", () => { });
        onlyChanged.ToolTip = "Show only settings that differ from iRacing's default or aren't saved yet";
        onlyChanged.Margin = new Thickness(0, 0, 6, 0);
        Ui.SetToggled(onlyChanged, _changedOnly);
        var cards = new List<SectionCard>();
        var foldAll = new CollapseAllButton { VerticalAlignment = VerticalAlignment.Center };
        foldAll.Requested += fold => FoldAll(fold);
        _tabTools.Children.Add(search);
        _tabTools.Children.Add(onlyChanged);
        _tabTools.Children.Add(foldAll);
        var band = new AreaBand();
        content.Children.Add(band);
        var wide = new StackPanel();
        content.Children.Add(wide);
        var results = new MasonryPanel { MinColumnWidth = 421, MaxColumns = 3 };
        content.Children.Add(results);

        IEnumerable<Setting> Listed(SectionInfo sec) => Session.In(sec).Where(s => !s.IsHidden);
        bool Global() => _search.Length > 0 || _changedOnly;
        bool IsFolded(string key) => Global() ? _searchFolded.Contains(key) : S.IsCollapsed(key);
        void SetFolded(string key, bool folded)
        {
            if (Global())
            {
                if (folded) _searchFolded.Add(key); else _searchFolded.Remove(key);
                return;
            }
            S.SetCollapsed(key, folded);
            _app.Save();
        }
        void UpdateFoldAll()
        {
            foldAll.SetOpen(cards.Count(c => c.Expanded), Math.Max(1, cards.Count));
        }
        void FoldAll(bool fold)
        {
            foreach (var card in cards)
            {
                SetFolded(card.Key, fold);
                card.SetExpanded(!fold);
            }
            UpdateFoldAll();
        }

        void FillTabs()
        {
            Rect? glideFrom = side && !Global() && _railArea != _area ? Glide.Of(_railSelected, railLayer) : null;
            Button? landing = null;
            ((string Soft, string Deep) Hue, string Icon) landingLook = default;
            Panel tabs = side ? sideNav : topNav;
            sideNav.Children.Clear();
            topNav.Children.Clear();
            sideCard.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
            topNav.Visibility = side ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 0; i < areas.Count; i++)
            {
                int index = i;
                var listed = Session.Sections.Where(sec => sec.AreaIndex == i && Shown(sec)).SelectMany(Listed).ToList();
                int modified = listed.Count(x => x.IsModified);
                int unsaved = listed.Count(x => x.IsChanged);
                bool selected = !Global() && i == _area;
                var look = AreaLook(areas[i].Title, i);
                var glyph = new Icon { Data = Ui.Res<Geometry>(look.Icon), Width = 11, Height = 11, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
                glyph.SetResourceReference(Control.ForegroundProperty, look.Hue.Deep);
                var row = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(glyph, Dock.Left);
                row.Children.Add(glyph);
                var counts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                if (unsaved > 0)
                {
                    var u = Ui.Chip(unsaved.ToString(CultureInfo.InvariantCulture), "AccentSoft", "AccentText");
                    u.Padding = new Thickness(4, 0, 4, 0);
                    u.Margin = new Thickness(0);
                    u.VerticalAlignment = VerticalAlignment.Center;
                    u.ToolTip = RaceFormat.Count(unsaved, "unsaved change");
                    counts.Children.Add(u);
                }
                if (modified > 0)
                {
                    var m = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = RaceFormat.Count(modified, "setting") + " changed from iRacing's default" };
                    var mdot = Ui.ChangedDot();
                    mdot.Margin = new Thickness(0, 0, 4, 0);
                    mdot.VerticalAlignment = VerticalAlignment.Center;
                    m.Children.Add(mdot);
                    m.Children.Add(new TextBlock { Text = modified.ToString(CultureInfo.InvariantCulture), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
                    counts.Children.Add(m);
                }
                DockPanel.SetDock(counts, Dock.Right);
                row.Children.Add(counts);
                row.Children.Add(TextFit.TipWhenTrimmed(new TextBlock { Text = areas[i].Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }));
                var b = new Button
                {
                    Content = row, Style = Ui.Res<Style>(selected ? "SelectedButton" : side ? "QuietButton" : "GhostButton"),
                    Margin = side ? new Thickness(0, 0, 0, 3) : new Thickness(0, 0, 5, 5),
                    HorizontalAlignment = side ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                if (side)
                {
                    b.Height = 31;
                    b.Padding = new Thickness(10, 0, 8, 0);
                }
                if (selected && glideFrom is not null)
                {
                    b.Style = Ui.Res<Style>("QuietButton");
                    landing = b;
                    landingLook = look;
                }
                else if (selected) RailSelected(b, look.Hue);
                if (selected) _railSelected = b;
                AutomationProperties.SetName(b, areas[i].Title);
                b.Click += (_, _) =>
                {
                    _area = index;
                    _search = "";
                    _changedOnly = false;
                    Ui.SetToggled(onlyChanged, false);
                    search.Text = "";
                    FillTabs();
                    Fill();
                };
                tabs.Children.Add(b);
            }
            if (!Global()) _railArea = _area;
            if (landing is { } target && glideFrom is { } from)
                Glide.To(railLayer, from, target, marker =>
                {
                    Hairline.Frame(marker);
                    marker.SetResourceReference(Border.BackgroundProperty, landingLook.Hue.Soft);
                    marker.SetResourceReference(Border.BorderBrushProperty, landingLook.Hue.Deep);
                }, () => RailSelected(target, landingLook.Hue));
        }

        void Fill()
        {
            wide.Children.Clear();
            results.Children.Clear();
            cards.Clear();
            var words = _search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            _words = words;
            bool Hit(Setting s, string w) =>
                s.Def.Label.Contains(w, StringComparison.OrdinalIgnoreCase) || s.Key.Contains(w, StringComparison.OrdinalIgnoreCase)
                || s.Def.Help.Contains(w, StringComparison.OrdinalIgnoreCase) || s.SectionInfo.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                || s.Draft.Contains(w, StringComparison.OrdinalIgnoreCase) || s.Show(s.Draft).Contains(w, StringComparison.OrdinalIgnoreCase);
            var matches = Session.Sections
                .Where(sec => Shown(sec) && (Global() || sec.AreaIndex == _area))
                .SelectMany(Listed)
                .Where(s => (!_changedOnly || s.IsModified || s.IsChanged) && words.All(w => Hit(s, w)))
                .ToList();
            band.Visibility = Global() ? Visibility.Collapsed : Visibility.Visible;
            if (!Global()) band.Show(matches);
            if (matches.Count == 0)
            {
                results.Children.Add(new TextBlock
                {
                    Text = words.Length > 0 ? "No settings match." : _changedOnly ? "Everything is at iRacing's default." : "None of this area's settings are in your files.",
                    Style = Ui.Res<Style>("Muted"), Margin = new Thickness(2, 5, 0, 0),
                });
                UpdateFoldAll();
                return;
            }
            int max = words.Length > 0 ? MaxRows : int.MaxValue;
            foreach (var group in matches.Take(max).GroupBy(s => s.SectionInfo))
            {
                var settings = group.ToList();
                string key = SectionKey(group.Key);
                bool span = settings.Count >= WideMinRows && key == WideSection;
                var card = new SectionCard(this, group.Key, key, settings, Global(), span, !IsFolded(key), expanded =>
                {
                    SetFolded(key, !expanded);
                    UpdateFoldAll();
                });
                cards.Add(card);
                if (span)
                {
                    card.Margin = new Thickness(0, 0, 0, results.Gap);
                    wide.Children.Add(card);
                }
                else results.Children.Add(card);
            }
            if (matches.Count > max)
                results.Children.Add(new TextBlock { Text = $"Showing the first {max} of {matches.Count}. Add a word to narrow it down.", Style = Ui.Res<Style>("Caption") });
            UpdateFoldAll();
        }

        _draftsChanged = () =>
        {
            foreach (var card in cards) card.UpdateSummary();
            if (!Global()) band.Show(Session.Sections.Where(sec => Shown(sec) && sec.AreaIndex == _area).SelectMany(Listed).ToList());
            FillTabs();
        };
        _refill = () =>
        {
            FillTabs();
            Fill();
        };

        void Place()
        {
            bool now = layout.ActualWidth >= SideNavMin;
            if (now == side && (sideNav.Children.Count + topNav.Children.Count) > 0) return;
            side = now;
            Grid.SetColumn(content, side ? 1 : 0);
            Grid.SetColumnSpan(content, side ? 1 : 2);
            FillTabs();
        }
        layout.SizeChanged += (_, _) => Place();
        Sticky.Install(sideCard, layout, () => side, gap: 11);

        onlyChanged.Click += (_, _) =>
        {
            _changedOnly = !_changedOnly;
            Ui.SetToggled(onlyChanged, _changedOnly);
            _searchFolded.Clear();
            FillTabs();
            Fill();
        };
        var debounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            if (Session is null || layout.Parent != _tab) return;
            FillTabs();
            Fill();
        };
        search.TextChanged += (_, _) =>
        {
            var text = search.Text.Trim();
            if (text == _search) return;
            _search = text;
            _searchFolded.Clear();
            debounce.Stop();
            debounce.Start();
        };
        FillTabs();
        Fill();
    }

    private Button? _railSelected;
    private int _railArea = -1;

    private static void RailSelected(Button b, (string Soft, string Deep) hue)
    {
        b.Style = Ui.Res<Style>("SelectedButton");
        b.SetResourceReference(Control.BackgroundProperty, hue.Soft);
        b.SetResourceReference(Control.BorderBrushProperty, hue.Deep);
        b.SetResourceReference(Control.ForegroundProperty, hue.Deep);
    }

    private static ((string Soft, string Deep) Hue, string Icon) AreaLook(string title, int index) => title switch
    {
        "Graphics and display" => (Hues.Accent, "I.Monitor"),
        "View and HUD" => (Hues.Named("Slate"), "I.Eye"),
        "Wheel and controls" => (Hues.Named("Coral"), "I.Wheel"),
        "Sound" => (Hues.Named("Gold"), "I.Speaker"),
        "Spotter and chat" => (Hues.Named("Plum"), "I.Keyboard"),
        "Pit and setups" => (Hues.Named("Sage"), "I.Sliders"),
        "Replay and broadcast" => (Hues.Named("Coral"), "I.Play"),
        "Telemetry and system" => (Hues.Named("Slate"), "I.Gauge"),
        _ => (Hues.At(index), "I.Sliders"),
    };

    private sealed class SectionCard : Tile
    {
        private readonly IracingControllerSection _owner;
        private readonly List<Setting> _settings;
        private readonly bool _grouped;
        private readonly bool _searching;
        private readonly bool _span;
        private readonly SectionInfo _section;
        private readonly Action<bool> _toggled;
        private readonly Icon _chevron = new() { Width = 11, Height = 11, VerticalAlignment = VerticalAlignment.Center };
        private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        private readonly Border _body = new();
        private readonly Button _header;
        private readonly TextBlock _fileLine;
        private readonly TextBlock? _note;

        public SectionCard(IracingControllerSection owner, SectionInfo section, string key, List<Setting> settings, bool searching, bool span, bool expanded, Action<bool> toggled)
        {
            _owner = owner;
            _section = section;
            Key = key;
            _settings = Ordered(settings);
            _grouped = _settings.Select(x => x.Def.Group ?? "").Distinct().Count() > 1;
            _searching = searching;
            _span = span;
            _toggled = toggled;
            Style = Ui.Res<Style>("Card");
            Padding = new Thickness(0);
            _body.Margin = new Thickness(10, 0, 10, 11);

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var hue = AreaLook(section.Area, section.AreaIndex).Hue;
            _chevron.SetResourceReference(Control.ForegroundProperty, hue.Deep);
            _chevron.Width = _chevron.Height = 10;
            var badge = new Tile { Width = 18, Height = 18, CornerRadius = ThemeManager.Round(6), VerticalAlignment = VerticalAlignment.Center, Child = _chevron };
            badge.SetResourceReference(Border.BackgroundProperty, hue.Soft);
            head.Children.Add(badge);
            var name = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            var title = Ui.Text(section.Name, "H3");
            title.TextWrapping = TextWrapping.Wrap;
            title.VerticalAlignment = VerticalAlignment.Center;
            name.Children.Add(title);
            if (section.IsRare)
            {
                var chip = Ui.Chip("expert", "PlumSoft", "PlumDeep");
                chip.Margin = new Thickness(6, 0, 0, 0);
                chip.Padding = new Thickness(5, 0, 5, 0);
                chip.VerticalAlignment = VerticalAlignment.Center;
                name.Children.Add(chip);
            }
            Grid.SetColumn(name, 1);
            head.Children.Add(name);
            _summary.Style = Ui.Res<Style>("Caption");
            Grid.SetColumn(_summary, 2);
            head.Children.Add(_summary);
            _fileLine = TextFit.TipWhenTrimmed(new TextBlock
            {
                Text = $"{(_searching ? _section.Area + " · " : "")}{_section.FileName} [{_section.Section}]",
                Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 8, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis,
            });
            _fileLine.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            TextCase.SetVerbatim(_fileLine, !_searching);
            TextFit.TipWhenTrimmed(_fileLine);
            var headStack = new StackPanel();
            headStack.Children.Add(head);
            headStack.Children.Add(_fileLine);
            if (section.Note is { } note)
            {
                _note = new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) };
                headStack.Children.Add(_note);
            }
            _header = new Button
            {
                Style = Ui.Res<Style>("QuietButton"), Content = headStack,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Padding = new Thickness(19, 18, 21, 18),
            };
            AutomationProperties.SetName(_header, section.Name);
            ClickGuard.SetAllowRepeat(_header, true);
            _header.Click += (_, _) =>
            {
                SetExpanded(!Expanded);
                _toggled(Expanded);
            };

            var stack = new StackPanel();
            stack.Children.Add(_header);
            stack.Children.Add(_body);
            Child = stack;
            UpdateSummary();
            SetExpanded(expanded);
        }

        public string Key { get; }
        public bool Expanded { get; private set; }

        public void SetExpanded(bool expanded)
        {
            Expanded = expanded;
            _chevron.Data = Ui.Res<Geometry>(expanded ? "I.ChevronDown" : "I.ChevronRight");
            _header.ToolTip = expanded ? "Fold this section" : "Show this section's settings";
            _fileLine.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (_note is not null) _note.Visibility = _fileLine.Visibility;
            if (expanded && _body.Child is null) _body.Child = BuildBody();
            _body.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        }

        public void UpdateSummary()
        {
            int modified = _settings.Count(s => s.IsModified);
            int unsaved = _settings.Count(s => s.IsChanged);
            _summary.Text = RaceFormat.Count(_settings.Count, "setting") + (modified > 0 ? $" · {modified} changed" : "") + (unsaved > 0 ? $" · {unsaved} unsaved" : "");
            _summary.ToolTip = modified > 0 ? "Changed = differs from iRacing's default" : null;
            _summary.SetResourceReference(TextBlock.ForegroundProperty, modified + unsaved > 0 ? "AccentText" : "TextMuted");
        }

        private StackPanel BuildBody()
        {
            var body = new StackPanel { Margin = new Thickness(10, 6, 10, 0) };
            var groups = _settings.GroupBy(x => _grouped ? x.Def.Group ?? "" : "").Select(g => (Name: g.Key, Settings: g.ToList())).ToList();
            int next = 0;
            void Add(int count)
            {
                int added = 0;
                while (next < groups.Count && (added == 0 || added < count))
                {
                    var (name, settings) = groups[next];
                    if (_grouped)
                    {
                        var head = new TextBlock { Text = name.Length > 0 ? name : "Other", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, next == 0 ? 2 : 10, 0, 6) };
                        head.SetResourceReference(TextBlock.ForegroundProperty, "EyebrowText");
                        body.Children.Add(head);
                    }
                    foreach (var piece in _owner.Pieces(settings, _span)) body.Children.Add(piece);
                    added += settings.Count;
                    next++;
                }
            }
            void More()
            {
                if (_body.Child != body) return;
                Add(RowsPerBatch);
                if (next < groups.Count) Dispatcher.BeginInvoke(More, System.Windows.Threading.DispatcherPriority.Background);
            }
            Add(RowsPerBatch);
            if (next < groups.Count) Dispatcher.BeginInvoke(More, System.Windows.Threading.DispatcherPriority.Background);
            return body;
        }

        private const int RowsPerBatch = 24;

        private static List<Setting> Ordered(List<Setting> settings)
        {
            var groups = new Dictionary<string, int>();
            foreach (var x in settings) groups.TryAdd(x.Def.Group ?? "", groups.Count);
            var numbered = settings.GroupBy(x => x.Def.Group ?? "").Where(g => g.Count() > 1 && g.All(x => Numbered(x.Key).Prefix is { } p && p == Numbered(g.First().Key).Prefix)).Select(g => g.Key).ToHashSet();
            return settings.Select((x, i) => (x, i))
                .OrderBy(t => groups[t.x.Def.Group ?? ""])
                .ThenBy(t => numbered.Contains(t.x.Def.Group ?? "") ? Numbered(t.x.Key).Number : t.i)
                .Select(t => t.x).ToList();
        }

        private static (string? Prefix, int Number) Numbered(string key)
        {
            int at = key.Length;
            while (at > 0 && char.IsAsciiDigit(key[at - 1])) at--;
            return at > 0 && at < key.Length && int.TryParse(key.AsSpan(at), NumberStyles.None, CultureInfo.InvariantCulture, out int n) ? (key[..at], n) : (null, 0);
        }
    }

    public void CollapseAll()
    {
        if (Session is null) return;
        foreach (var sec in Session.Sections.Where(Shown))
            S.SetCollapsed(SectionKey(sec), true);
        _app.Save();
        if (_tabIndex == 0) ShowTab();
    }

    private static string SectionKey(SectionInfo sec) => SettingsCatalog.FileKey(sec.FileName) + "|" + sec.Section;

    private static bool Shown(SectionInfo sec) => !HiddenSections.Contains(SectionKey(sec));

    private sealed class SplitRows : Panel
    {
        public double MinColumnWidth { get; set; } = 389;
        public double Gap { get; set; } = 26;

        private int _split = -1;
        private double _splitWidth = double.NaN;
        private int _splitCount;

        private bool TwoColumns(double width) => !double.IsInfinity(width) && width >= MinColumnWidth * 2 + Gap && InternalChildren.Count > 1;

        protected override Size MeasureOverride(Size available)
        {
            int n = InternalChildren.Count;
            if (!TwoColumns(available.Width))
            {
                double h = 0, w = 0;
                foreach (UIElement child in InternalChildren)
                {
                    child.Measure(new Size(available.Width, double.PositiveInfinity));
                    h += child.DesiredSize.Height;
                    w = Math.Max(w, child.DesiredSize.Width);
                }
                return new Size(double.IsInfinity(available.Width) ? w : available.Width, h);
            }

            double col = (available.Width - Gap) / 2;
            var heights = new double[n];
            for (int i = 0; i < n; i++)
            {
                InternalChildren[i].Measure(new Size(col, double.PositiveInfinity));
                heights[i] = InternalChildren[i].DesiredSize.Height;
            }
            if (_split < 0 || available.Width != _splitWidth || n != _splitCount)
            {
                double total = heights.Sum(), left = 0, best = double.MaxValue;
                for (int k = 1; k < n; k++)
                {
                    left += heights[k - 1];
                    double tallest = Math.Max(left, total - left);
                    if (tallest < best) { best = tallest; _split = k; }
                }
                _splitWidth = available.Width;
                _splitCount = n;
            }
            double l = heights.Take(_split).Sum(), r = heights.Skip(_split).Sum();
            return new Size(available.Width, Math.Max(l, r));
        }

        protected override Size ArrangeOverride(Size final)
        {
            if (!TwoColumns(final.Width) || _split < 0 || _splitCount != InternalChildren.Count)
            {
                double y = 0;
                foreach (UIElement child in InternalChildren)
                {
                    child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
                    y += child.DesiredSize.Height;
                }
                return final;
            }
            double col = (final.Width - Gap) / 2, top = 0;
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                if (i == _split) top = 0;
                var child = InternalChildren[i];
                child.Arrange(new Rect(i < _split ? 0 : col + Gap, top, col, child.DesiredSize.Height));
                top += child.DesiredSize.Height;
            }
            return final;
        }
    }

    private Border SettingRow(Setting s)
    {
        var host = Hairline.Frame(new Border { BorderBrush = Ui.Res<Brush>("Line"), Padding = new Thickness(10, 6, 8, 6) }, Sides.Top);
        Ui.HoverHighlight(host);
        bool wasChanged = s.IsChanged;
        void Fill()
        {
            var words = _words;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel { Margin = new Thickness(0, 0, 13, 0), VerticalAlignment = VerticalAlignment.Center };
            var title = new WrapPanel();
            var label = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
            Ui.Highlight(label, s.Def.Label, words);
            title.Children.Add(label);
            if (s.IsChanged)
            {
                var chip = UnsavedChip(s);
                title.Children.Add(chip);
                if (!wasChanged) Reveal.PopIn(chip);
            }
            var std = Session!.DefaultOf(s);
            text.Children.Add(Ui.Marked(title, out var dot));
            dot.Visibility = s.IsModified ? Visibility.Visible : Visibility.Collapsed;
            if (dot is FrameworkElement d) d.ToolTip = std is null ? null : "Changed from iRacing's default (" + s.Show(std) + ")";
            if (s.Def.Help.Length > 0)
            {
                var help = new TextBlock { Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                Ui.Highlight(help, s.Def.Help, words);
                text.Children.Add(help);
            }
            Notes(s, text);
            if (words.Length > 0 && words.Any(w => s.Key.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                var key = new TextBlock { Style = Ui.Res<Style>("Caption"), Foreground = Ui.Res<Brush>("TextFaint"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
                Ui.Highlight(key, $"[{s.Section}] {s.Key}", words);
                text.Children.Add(key);
            }
            text.ToolTip = Details(s);
            ToolTipService.SetInitialShowDelay(text, 700);
            grid.Children.Add(text);

            var control = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (s.IsModified && std is not null)
            {
                var reset = Ui.IconButton("I.Undo", "Back to iRacing's default (" + s.Show(std) + ")", () => { Session.SetDraft(s, std); Fill(); UpdatePending(); });
                reset.Margin = new Thickness(0, 0, 5, 0);
                control.Children.Add(reset);
            }
            control.Children.Add(Editor(s, Fill));
            if (s.IsChanged)
            {
                var undo = Ui.IconButton("I.X", "Drop this change (keep " + s.Show(s.Applied) + ")", () => { Session.Discard(s); Fill(); UpdatePending(); });
                undo.Margin = new Thickness(5, 0, 0, 0);
                control.Children.Add(undo);
            }
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            grid.Opacity = s.IsInert ? 0.6 : 1;
            AddUnsavedEdge(grid, host, s, !wasChanged);
            host.Child = grid;
            wasChanged = s.IsChanged;
        }
        Fill();
        return host;
    }

    private FrameworkElement Editor(Setting s, Action refresh)
    {
        void Set(string value)
        {
            if (Session!.SetDraft(s, value) is { } problem)
                _app.Notify(s.Def.Label, problem, NoticeKind.Warning);
            refresh();
            UpdatePending();
        }

        if (s.IsReadOnly)
            return new TextBlock
            {
                Text = s.Show(s.Draft), Style = Ui.Res<Style>("Muted"), MaxWidth = 194, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = (s.Def.ReadOnlyNote ?? "iRacing marks this setting as not to be edited.") + (s.Draft.Length > 0 ? "\n" + s.Draft : ""),
            };

        switch (s.Def.Type)
        {
            case "text" when s.Def.Pick == ValueSources.Folder:
                return FolderEditor(s, Set);
            case "text" when TextChoices(s) is { } textChoices:
            {
                var choices = textChoices.Select(c => new Choice(c.Value, c.Label, c.Tip)).ToList();
                var box = Ui.Combo(choices, s.Draft, c => { if (c.Id is { } v && v != s.Draft) Set(v); }, 146);
                box.MaxWidth = 211;
                return box;
            }
            case "bool":
                return Ui.Switch(s.IsOn(s.Draft), on => Set(on != s.Def.Invert ? "1" : "0"));
            case "enum" when s.Def.Options is { Count: > 0 } options:
            {
                var choices = options.Select(o => new Choice(Setting.Format(o.Value), o.Label, o.Tip)).ToList();
                if (!choices.Any(c => Setting.SameValue(c.Id, s.Draft)))
                    choices.Insert(0, new Choice(s.Draft, s.Draft + " (not a listed option)"));
                var selected = choices.First(c => Setting.SameValue(c.Id, s.Draft)).Id;
                return Ui.Combo(choices, selected, c => Set(c.Id ?? ""), 146);
            }
            case "num" or "range" or "enum":
            {
                CommentInference.TryNumber(s.Draft, out var n);
                static int Places(string v) => v.IndexOf('.') is var at and >= 0 ? v.Length - at - 1 : 0;
                int decimals = Math.Min(4, Math.Max(Math.Max(Places(Setting.Format(n)), s.Def.Step is { } st ? Places(Setting.Format(st)) : 0), s.Draft.Contains('.') ? 2 : 0));
                var box = new NumberBox(n, s.Def.Unit ?? "")
                {
                    Minimum = s.Def.Min ?? -1e9, Maximum = s.Def.Max ?? 1e9, Decimals = decimals,
                    Step = s.Def.Step ?? (decimals > 0 ? Math.Pow(10, -Math.Min(decimals, 2)) : 1),
                };
                box.ValueChanged += v => { Session!.SetDraft(s, v.ToString(CultureInfo.InvariantCulture)); UpdatePending(); };
                box.LostKeyboardFocus += (_, _) => refresh();
                return box;
            }
            default:
            {
                var box = new TextBox { Text = s.Draft, Width = 211 };
                box.SetResourceReference(Control.FontFamilyProperty, "MonoFont");
                box.LostKeyboardFocus += (_, _) => { if (box.Text != s.Draft) Set(box.Text); };
                box.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && box.Text != s.Draft) { e.Handled = true; Set(box.Text); } };
                return box;
            }
        }
    }

    private List<TextChoice>? TextChoices(Setting s)
    {
        var pick = s.Def.Pick;
        if (pick is null) return null;
        if (pick == ValueSources.Choices)
            return s.Def.Choices is { Count: > 0 } ? ValueSources.Merge(s.Def.Choices, null, s.Draft) : null;
        if (!ValueSources.IsListed(pick) || _ws.Found(pick) is not { } found) return null;
        return ValueSources.Merge(s.Def.Choices, found, s.Draft);
    }

    private static StackPanel FolderEditor(Setting s, Action<string> set)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        bool blank = s.Draft.Length == 0;
        panel.Children.Add(new TextBlock
        {
            Text = s.Show(s.Draft), Style = Ui.Res<Style>(blank ? "Muted" : "Body"), MaxWidth = 130, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), ToolTip = blank ? s.Def.Help : s.Draft,
        });
        var browse = Ui.LabeledButton("I.Folder", "Browse", () =>
        {
            if (!PitBox.Services.External.Allowed("folder dialog")) return;
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = s.Def.Label };
            if (!blank && Directory.Exists(s.Draft)) dialog.InitialDirectory = s.Draft;
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.FolderName)) set(dialog.FolderName);
        }, "GhostButton");
        browse.ToolTip = "Pick a folder";
        panel.Children.Add(browse);
        if (!blank)
        {
            var reset = Ui.IconButton("I.X", "Clear: back to the default folder", () => set(""));
            reset.Margin = new Thickness(3, 0, 0, 0);
            panel.Children.Add(reset);
        }
        return panel;
    }

    private void BackupsTab(IReadOnlyList<Backup>? backups, string? problem)
    {
        var now = Ui.LabeledButton("I.Save", "Back up now", () => _ = BackupNow(), "PrimaryButton");
        now.IsEnabled = !_ws.Demo;
        if (_ws.Demo) now.ToolTip = "Not with demo data";
        now.VerticalAlignment = VerticalAlignment.Bottom;
        var title = Ui.EditorSection("Backups");
        var head = Ui.SplitRow(title, now);
        head.Margin = new Thickness(0, 0, 0, 6);
        _tab.Children.Add(head);
        _tab.Children.Add(new TextBlock
        {
            Text = "A full backup (settings, controls, calibration, cameras and profiles) is taken before every write and restore. The last 30 automatic ones are kept; ones you take stay until you delete them.",
            Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });

        if (_ws.Demo)
        {
            _tab.Children.Add(Ui.EmptyState("I.Save", "No backups in demo", "Backups are made from your real iRacing folder."));
            return;
        }
        if (problem is not null || backups is null)
        {
            _tab.Children.Add(Ui.DangerBanner("Backups couldn't be listed", problem ?? ""));
            return;
        }

        IReadOnlyList<StrayCopy> strays;
        try { strays = _ws.StrayCopies(); }
        catch (Exception ex)
        {
            _tab.Children.Add(Ui.DangerBanner("Backups couldn't be listed", ex.Message));
            return;
        }

        if (strays.Count > 0)
            _tab.Children.Add(FindingCard(Rating.Concerning, $"{strays.Count} hand-made cop{(strays.Count == 1 ? "y" : "ies")} in your iRacing folder",
                string.Join(", ", strays.Take(4).Select(c => c.FileName)) + (strays.Count > 4 ? "…" : "") + ". Turn them into dated backups here so you can compare and restore them (the copies themselves stay where they are).",
                ("Turn into backups", () => ImportStrays(strays))));

        if (backups.Count == 0)
        {
            _tab.Children.Add(Ui.EmptyState("I.Save", "No backups yet", "One is taken before the first write or restore."));
            return;
        }
        var list = new StackPanel();
        foreach (var day in backups.GroupBy(b => b.Created.Date))
        {
            var label = new TextBlock { Text = ThemeManager.Case(Dates.Day(day.Key)), Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, list.Children.Count == 0 ? 8 : 14, 0, 4) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "EyebrowText");
            list.Children.Add(label);
            var rows = day.ToList();
            for (int i = 0; i < rows.Count; i++)
                list.Children.Add(BackupRow(rows[i], i == rows.Count - 1));
        }
        _tab.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(15, 0, 12, 8), Child = list });
        var where = TextCase.Verbatim(TextFit.TipWhenTrimmed(new TextBlock { Text = "Kept in " + _ws.Store.Root, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(2, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }));
        _tab.Children.Add(where);
    }

    private Border BackupRow(Backup b, bool last)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var (soft, deep) = b.Kind switch { BackupKind.Manual => Hues.Accent, BackupKind.Auto => Hues.Named("Slate"), _ => Hues.Named("Gold") };
        var number = RaceParts.Big("#" + b.Number.ToString(CultureInfo.CurrentCulture), 15, deep, italic: true);
        number.HorizontalAlignment = HorizontalAlignment.Center;
        var badge = new Border { Width = 46, Height = 34, Margin = new Thickness(0, 0, 13, 0), Child = number };
        badge.SetResourceReference(Border.BackgroundProperty, soft);
        grid.Children.Add(badge);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(new TextBlock { Text = b.Title, FontWeight = FontWeights.SemiBold });
        if (b.Kind != BackupKind.Auto)
        {
            var chip = Ui.Chip(b.Kind == BackupKind.Manual ? "kept" : "imported", soft, deep);
            chip.Margin = new Thickness(7, 0, 0, 0);
            chip.Padding = new Thickness(5, 0, 5, 0);
            title.Children.Add(chip);
        }
        text.Children.Add(title);
        text.Children.Add(new TextBlock { Text = $"{Dates.Time(b.Created)} · {SetupTree.Plural(b.Files.Count, "file")}", Style = Ui.Res<Style>("Caption") });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(Ui.LabeledButton("I.Eye", "Compare", () => _ = Compare(b), "GhostButton"));
        var restore = Ui.LabeledButton("I.Restore", "Restore", () => _ = Restore(b), "GhostButton");
        restore.Margin = new Thickness(5, 0, 2, 0);
        buttons.Children.Add(restore);
        buttons.Children.Add(Ui.IconButton("I.Trash", "Delete this backup", () => _ = DeleteBackup(b)));
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        var row = new Border { BorderBrush = Ui.Res<Brush>("Line"), Padding = new Thickness(0, 6, 0, 6), Child = grid };
        return last ? row : Hairline.Frame(row, Sides.Bottom);
    }

    private async Task BackupNow()
    {
        var name = await _window.Prompt("Back up iRacing's settings", "A name to recognize it by (optional).", "", _ => null);
        if (name is null || _ws.Folder is not { } folder) return;
        try
        {
            var b = _ws.Store.Create("Taken by hand", IracingPaths.BackupFiles(folder), folder, BackupKind.Manual, name);
            _app.Notify($"Backup #{b.Number} taken", $"{SetupTree.Plural(b.Files.Count, "file")}.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't back up", ex.Message, NoticeKind.Warning, important: true);
        }
        ShowTab();
    }

    private void ImportStrays(IReadOnlyList<StrayCopy> strays)
    {
        int done = 0;
        string? problem = null;
        foreach (var copy in strays)
        {
            try { _ws.Store.Import(copy, _ws.Folder!); done++; }
            catch (Exception ex) { problem ??= ex.Message; }
        }
        if (problem is null) _app.Notify("Copies imported", $"{done} of {strays.Count} turned into backups.", NoticeKind.Success);
        else _app.Notify("Not every copy was imported", $"{done} of {strays.Count} turned into backups. {problem}", NoticeKind.Warning, important: true);
        ShowTab();
    }

    private async Task Compare(Backup b)
    {
        var list = new StackPanel();
        try
        {
            var diffs = BackupStore.Compare(b, _ws.Folder);
            foreach (var d in diffs.Where(d => d.State != FileState.Same))
            {
                list.Children.Add(new TextBlock { Text = d.Name + (d.State == FileState.MissingNow ? " (missing now)" : ""), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
                foreach (var k in d.Keys.Take(60))
                    list.Children.Add(new TextBlock { Text = $"[{k.Section}] {k.Key}: {k.Then ?? "(not there)"}  →  now {k.Now ?? "(not there)"}", Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap });
                if (d.Keys.Count > 60)
                    list.Children.Add(new TextBlock { Text = $"+{d.Keys.Count - 60} more", Style = Ui.Res<Style>("Caption") });
            }
            if (list.Children.Count == 0)
                list.Children.Add(new TextBlock { Text = "No differences: your files match this backup.", Style = Ui.Res<Style>("Muted") });
        }
        catch (Exception ex)
        {
            list.Children.Add(Ui.DangerBanner("The backup couldn't be compared with your files", ex.Message));
        }
        var close = new StackPanel();
        var ok = new Button { Content = "Close", Style = Ui.Res<Style>("PrimaryButton") };
        ok.Click += (_, _) => _window.CloseModal(false);
        close.Children.Add(ok);
        await _window.ShowModal(Ui.Dialog(_window, $"Backup #{b.Number} compared with now", "What restoring it would change (backup → current value).", list, close, null, 551));
    }

    private async Task Restore(Backup b)
    {
        if (_ws.SimRunning)
        {
            _app.Notify("Close iRacing first", "It rewrites its settings when it closes, which would undo the restore.", NoticeKind.Warning, important: true);
            return;
        }
        if (!await ConfirmDropPending()) return;
        if (!await _window.Confirm($"Restore backup #{b.Number}?", $"Puts back {SetupTree.Plural(b.Files.Count, "file")} from {Dates.DateTime(b.Created)}. Your current files are backed up first, so this can be undone.", "Restore"))
            return;
        if (_ws.SimRunning)
        {
            _app.Notify("Close iRacing first", "It rewrites its settings when it closes, which would undo the restore.", NoticeKind.Warning, important: true);
            return;
        }
        var layoutBefore = Session is { } current ? LayoutGuard.Capture(current) : new Dictionary<string, string>();
        try
        {
            var before = _ws.Store.Restore(b, _ws.Folder);
            _app.Notify($"Backup #{b.Number} restored", $"Backup #{before.Number} has the files from before.", NoticeKind.Success);
            _ws.Load();
            if (Session is { } restored)
            {
                var changed = LayoutGuard.Capture(restored).Where(kv => !layoutBefore.TryGetValue(kv.Key, out var was) || !Setting.SameValue(was, kv.Value)).Select(kv => kv.Key).ToList();
                LayoutGuard.Adopt(restored, S.SavedLayout, changed);
                _app.Save();
            }
        }
        catch (Exception ex)
        {
            _app.Notify("Couldn't restore", ex.Message, NoticeKind.Warning, important: true);
            _ws.Load();
        }
        Build();
    }

    private async Task DeleteBackup(Backup b)
    {
        if (!await _window.Confirm($"Delete backup #{b.Number}?", "Its copies of your files are removed from Pit-Box's backup folder. Your iRacing files aren't touched.", "Delete", danger: true))
            return;
        try { BackupStore.Delete(b); }
        catch (Exception ex) { _app.Notify("Couldn't delete the backup", ex.Message, NoticeKind.Warning, important: true); }
        ShowTab();
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        for (int i = 1; i < Tabs.Length; i++)
        {
            int tab = i;
            yield return (Tabs[i].Replace(' ', '-').ToLowerInvariant(), () => { _tabIndex = tab; Build(); return Task.CompletedTask; });
        }
        yield return ("keybinds-not-set", () => { _tabIndex = 1; Build(); _keybinds?.ShowFilter("not-set"); return Task.CompletedTask; });
        yield return ("keybinds-view", () => { _tabIndex = 1; Build(); _keybinds?.ShowGroup("View"); return Task.CompletedTask; });
        yield return ("keybinds-find", () => { _tabIndex = 1; Build(); _keybinds?.ShowFind("T"); return Task.CompletedTask; });
        yield return ("keybinds-find-free", () => { _tabIndex = 1; Build(); _keybinds?.ShowFind("Ctrl+Alt+J"); return Task.CompletedTask; });
        yield return ("keybinds-clash", () => { _tabIndex = 1; Build(); _keybinds?.ShowClash("Ignition", "Starter"); return Task.CompletedTask; });
        yield return ("search", () => { _tabIndex = 0; _search = "fps"; Build(); return Task.CompletedTask; });
        yield return ("changed-only", () => { _tabIndex = 0; _search = ""; _changedOnly = true; Build(); return Task.CompletedTask; });
        yield return ("back-to-start", () => { _tabIndex = 0; _search = ""; _changedOnly = false; Build(); return Task.CompletedTask; });
    }
}
