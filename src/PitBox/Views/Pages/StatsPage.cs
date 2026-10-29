using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.Plugins;
using PitBox.Plugins.BuiltIn;
using PitBox.Services;
using PitBox.SessionStats;
using PitBox.Theme;

namespace PitBox.Views.Pages;

internal sealed class StatsPage : UserControl, IPage, IScreenshotStates, IPageHistory
{
    private readonly AppServices _app;
    private readonly MainWindow _window;
    private readonly StackPanel _body = new() { Margin = new Thickness(23, 0, 23, 23) };
    private readonly TextBlock _driver = TextFit.TipWhenTrimmed(new TextBlock { FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
    private readonly TextBlock _number = new() { FontSize = 14, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly Tile _numberPlate = new() { MinWidth = 36, Height = 24, Padding = new Thickness(7, 0, 7, 0), BorderThickness = new Thickness(1.5), Margin = new Thickness(0, 0, 8, 0) };
    private readonly StackPanel _driverPlate = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly StackPanel _crumbs = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly TextBlock _sessionsCount = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(11, 0, 0, 0) };
    private readonly StackPanel _tools = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly ContentControl _progress = new() { Focusable = false }, _content = new() { Focusable = false };
    private readonly ScrollViewer _scroll;
    private readonly SessionFilter _filter = new();
    private readonly HashSet<int> _ticked = new();
    private long? _sessionId;
    private List<int>? _analysisLaps;
    private string _reference = "session";
    private ReplayMemory _replay = new();

    internal void StartReplayAt(double distance) => _replay.Distance = distance;
    private double _overviewOffset;
    private int _version;
    private bool _shown;

    public StatsPage(AppServices app, MainWindow window)
    {
        _app = app;
        _window = window;
        Content = _scroll = new ScrollViewer { Style = Ui.Res<Style>("PageScroll"), Content = Ui.PageFrame(_body) };
        Ui.ChainWheel(_scroll);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || _sessionId is null || Keyboard.FocusedElement is TextBox) return;
            e.Handled = true;
            Back();
        };
        var header = new StackPanel { Margin = Ui.HeaderMargin };
        var left = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = Ui.PluginTitle(PluginIds.SessionStats, "Session Stats");
        title.Margin = new Thickness(0, 0, 23, 0);
        left.Children.Add(title);
        left.Children.Add(_driverPlate);
        left.Children.Add(_crumbs);
        header.Children.Add(Ui.SplitRow(left, _tools, 13, stackLeft: true));
        _body.Children.Add(header);
        _body.Children.Add(_progress);
        _body.Children.Add(_content);
        Ui.Anchor(_content, "stats.all");
        if (Stats is { } stats)
        {
            BuildDriverPlate();
            _tools.Children.Add(StatsUi.RangePicker(stats, "page", Changed));
            var more = Ui.IconButton("I.Sliders", "Units and car number", () => { });
            more.Click += (_, _) => OpenOptions(more, stats);
            more.Margin = new Thickness(8, 0, 0, 0);
            _tools.Children.Add(more);
        }
    }

    private SessionStatsService? Stats => SessionStatsPlugin.Of(_app);

    private void BuildDriverPlate()
    {
        _numberPlate.SetResourceReference(Tile.BackgroundProperty, "AccentSoft");
        _numberPlate.SetResourceReference(Tile.BorderBrushProperty, "AccentText");
        _numberPlate.VerticalAlignment = VerticalAlignment.Center;
        _number.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        _number.SetResourceReference(TextBlock.FontFamilyProperty, "MetricFont");
        _numberPlate.Child = _number;
        _numberPlate.ToolTip = "Your car number";
        _driver.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
        _driver.MaxWidth = 292;
        var edit = Ui.IconButton("I.Pencil", "Your car number (blank = the one you used most, league races first)", () => _ = EditNumberAsync(), "QuietIconButton");
        edit.Margin = new Thickness(5, 0, 0, 0);
        edit.VerticalAlignment = VerticalAlignment.Center;
        _sessionsCount.Style = Ui.Res<Style>("Caption");
        _driverPlate.Children.Add(_numberPlate);
        _driverPlate.Children.Add(_driver);
        _driverPlate.Children.Add(edit);
        _driverPlate.Children.Add(_sessionsCount);
    }

    private void OpenOptions(Button anchor, SessionStatsService stats)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom, MinWidth = 178 };
        Ui.DropBelow(menu);
        foreach (var (label, imperial) in new[] { ("Metric units", false), ("Imperial units", true) })
        {
            var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = stats.Settings.Imperial == imperial };
            item.Click += (_, _) => { stats.Settings.Imperial = imperial; Changed(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var number = new MenuItem { Header = "Car number…" };
        number.Click += (_, _) => _ = EditNumberAsync();
        menu.Items.Add(number);
        menu.IsOpen = true;
    }

    private void ShowCrumbs(SessionSummary? session)
    {
        bool drilled = _sessionId is not null;
        _crumbs.Children.Clear();
        _crumbs.Visibility = drilled ? Visibility.Visible : Visibility.Collapsed;
        _driverPlate.Visibility = !drilled && _driver.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _tools.Visibility = drilled ? Visibility.Collapsed : Visibility.Visible;
        if (!drilled) return;
        void Crumb(string text, Action? go)
        {
            if (_crumbs.Children.Count > 0)
            {
                var chevron = new Icon { Data = Ui.Res<Geometry>("I.ChevronRight"), Width = 11, Height = 11, Margin = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center };
                chevron.SetResourceReference(Control.ForegroundProperty, "TextFaint");
                _crumbs.Children.Add(chevron);
            }
            if (go is null)
            {
                var here = new TextBlock { Text = text, Style = Ui.Res<Style>("Body"), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                here.SetResourceReference(TextBlock.ForegroundProperty, "Heading");
                _crumbs.Children.Add(here);
                return;
            }
            var button = new Button { Style = Ui.Res<Style>("QuietButton"), Content = text, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Back (Esc)" };
            button.Click += (_, _) => go();
            _crumbs.Children.Add(button);
        }
        Crumb("Overview", () => { _analysisLaps = null; _sessionId = null; Refresh(restoreOffset: true); _window.RecordHistory(); });
        string name = session is null ? "Session" : $"{Dates.Short(session.StartUtc.ToLocalTime())} {StatsSessions.TypeName(session.Type)} · {session.Place}";
        if (_analysisLaps is null) Crumb(name, null);
        else
        {
            Crumb(name, () => { _analysisLaps = null; Refresh(); _window.RecordHistory(); });
            Crumb("Lap analysis", null);
        }
    }

    private async Task EditNumberAsync()
    {
        if (Stats is not { } stats) return;
        var typed = await _window.Prompt("Car number", "The number shown next to your name. Leave it empty to use the one you raced with most (league races first).",
            stats.Settings.DriverNumber ?? "", text => text.Trim().TrimStart('#').Length > 4 ? "Up to 4 characters" : null);
        if (typed is null) return;
        stats.Settings.DriverNumber = typed;
        stats.Settings.Normalize();
        Changed();
    }

    public void OnShown()
    {
        _shown = true;
        if (Stats is { } stats) stats.Changed += OnStatsChanged;
        Refresh();
        Motion.Stagger(_body);
    }

    public void OnHidden()
    {
        _shown = false;
        if (Stats is { } stats) stats.Changed -= OnStatsChanged;
    }

    private long _importRefreshed;

    private void OnStatsChanged()
    {
        if (_shown && Stats is { } stats && (_analysisLaps is not null || stats.Importing is not null && Environment.TickCount64 - _importRefreshed < ImportRefreshMs))
        {
            _progress.Content = StatsUi.Progress(stats);
            return;
        }
        _importRefreshed = Environment.TickCount64;
        Refresh();
    }

    private const long ImportRefreshMs = 15_000;

    private void Changed()
    {
        _app.Save();
        Refresh();
    }

    private void Open(long session, List<int>? laps = null)
    {
        if (_sessionId is null) _overviewOffset = _scroll.VerticalOffset;
        if (_sessionId != session) { _ticked.Clear(); _reference = "session"; _replay = new(); }
        _sessionId = session;
        _analysisLaps = laps;
        Refresh();
        _scroll.ScrollToTop();
        _window.RecordHistory();
    }

    private void Back()
    {
        if (_analysisLaps is not null) _analysisLaps = null;
        else _sessionId = null;
        Refresh(restoreOffset: _sessionId is null);
        _window.RecordHistory();
    }

    private sealed record View(long? Session, string? Laps);

    public object? HistoryState => new View(_sessionId, _analysisLaps is null ? null : string.Join(",", _analysisLaps));

    public void RestoreHistory(object? state)
    {
        if (state is not View view) return;
        bool toOverview = view.Session is null && _sessionId is not null;
        if (_sessionId is null && view.Session is not null) _overviewOffset = _scroll.VerticalOffset;
        if (_sessionId != view.Session) { _ticked.Clear(); _reference = "session"; _replay = new(); }
        _sessionId = view.Session;
        _analysisLaps = view.Laps?.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        Refresh(restoreOffset: toOverview);
        if (!toOverview) _scroll.ScrollToTop();
    }

    private FrameworkElement Drilled(FrameworkElement view, SessionSummary session)
    {
        ShowCrumbs(session);
        return view;
    }

    private async Task RefreshDrill(SessionStatsService stats, StatsStore store, int version)
    {
        long id = _sessionId!.Value;
        var traces = stats.Traces;
        if (_analysisLaps is { } laps)
        {
            string reference = _reference;
            var data = await Task.Run(() => traces is null ? null : LoadAnalysis(store, traces, id, laps, reference));
            if (version != _version || !_shown) return;
            if (data is null || data.Laps.Count == 0) { _analysisLaps = null; Refresh(); return; }
            var actions = new AnalysisActions(
                pick => { _reference = pick; Refresh(); },
                lap => { _analysisLaps = _analysisLaps?.Where(l => l != lap).ToList(); _ticked.Remove(lap); Refresh(); },
                lap => ExportLap(store, traces!, id, lap),
                () => _ = ImportLapAsync(traces!, data.Session),
                shared => _ = DeleteSharedAsync(traces!, shared),
                _app.Save);
            var view = LapAnalysisView.Build(data, stats.Settings, _reference, actions, _replay, out _);
            _content.Content = Drilled(view, data.Session);
            return;
        }
        string driver = _driver.Text;
        var detail = await Task.Run(() =>
        {
            if (store.Session(id) is not { } s) return null;
            var (tank, tyres) = store.CarExtras(id);
            var race = RaceParts.Raced(s) ? RaceInfo.Read(store, driver).Data(s) : null;
            var (trackId, trackName) = store.TrackIdentity(id);
            return new SessionDetail(s, store.Laps(id), traces?.LapsWithTrace(id) ?? new HashSet<int>(), store.BestLap(s.Car, s.Track, s.Config), store.Conditions(id), tank, tyres, race, trackId, trackName);
        });
        if (version != _version || !_shown) return;
        if (detail is null) { _sessionId = null; Refresh(); return; }
        _content.Content = Drilled(StatsSessionView.Build(detail, stats.Settings, StatsSessions.NoTraceReason(detail.Session, stats.Settings, DateTime.UtcNow), _ticked, picked => Open(id, picked.ToList())), detail.Session);
    }

    internal static AnalysisData? LoadAnalysis(StatsStore store, TraceStore traces, long id, IReadOnlyList<int> picked, string reference = "session")
    {
        if (store.Session(id) is not { } session) return null;
        var traced = traces.LapsWithTrace(id);
        var all = store.Laps(id).Where(l => traced.Contains(l.Lap)).Select(l => (Row: l, Trace: traces.Load(id, l.Lap))).Where(x => x.Trace is not null).ToList();
        if (all.Count == 0) return null;
        var shown = all.Where(x => picked.Contains(x.Row.Lap)).Select(x => (x.Row.Lap, x.Row.Time, x.Trace!)).ToList();
        var pool = all.Where(x => x.Row.Clean).ToList();
        if (pool.Count == 0) pool = all;
        var best = pool.MinBy(x => x.Row.Time);
        var starts = new List<double>();
        if (best.Trace is { } bt && best.Row?.Sectors is { Count: > 1 } sectors)
        {
            starts.Add(0);
            double at = 0;
            for (int k = 0; k < sectors.Count - 1; k++) { at += sectors[k]; starts.Add(bt.DistanceAtTime(at) / bt.LengthM); }
        }
        var optimal = starts.Count > 1 && pool.Count >= 2 ? LapTrace.Optimal(pool.Select(x => x.Trace!).ToList(), starts) : null;
        (string, LapTrace)? trackBest = null;
        if (traces.BestOf(session.Car, session.Track, session.Config) is { } tb && !(tb.Session == id && tb.Lap == best.Row?.Lap) && traces.Load(tb.Session, tb.Lap) is { } tbTrace)
        {
            string when = store.Session(tb.Session) is { } other ? Dates.Day(other.StartUtc.ToLocalTime()) : "";
            trackBest = ($"{StatFormat.LapTime(tb.Time)}{(when.Length > 0 ? " · " + when : "")}", tbTrace);
        }
        var shared = traces.SharedAt(session.Track, session.Config);
        (SharedLapInfo, LapTrace)? sharedPicked = null;
        if (reference.StartsWith("shared:") && long.TryParse(reference[7..], out long sid) && shared.FirstOrDefault(x => x.Id == sid) is { } info && traces.LoadShared(sid) is { } lap)
            sharedPicked = (info, lap.Trace);
        return new AnalysisData(session, shown, best.Trace is { } t && best.Row is { } br ? (br.Lap, t) : null, trackBest, optimal, starts,
            store.DriverOf(id), shared, sharedPicked, MapOf(store, session));
    }

    private static SimRacing.TrackMaps.TrackMap? MapOf(StatsStore store, SessionSummary session)
    {
        var (trackId, trackName) = store.TrackIdentity(session.Id);
        try { return SimRacing.TrackMaps.TrackMapLibrary.ForSession(trackId, trackName, session.Track, session.Config).GetAwaiter().GetResult(); }
        catch (Exception ex) { App.LogError(ex); return null; }
    }

    private void ExportLap(StatsStore store, TraceStore traces, long session, int lap)
    {
        if (_app.Demo) { _app.Notify("Demo mode", "Demo laps aren't saved to files.", NoticeKind.Info); return; }
        SharedLap shared;
        try
        {
            var summary = store.Session(session);
            var trace = traces.Load(session, lap);
            if (summary is null || trace is null) { _app.Notify("Couldn't export the lap", "Its telemetry isn't stored any more.", NoticeKind.Warning); return; }
            var row = store.Laps(session).FirstOrDefault(l => l.Lap == lap);
            shared = new SharedLap(store.DriverOf(session), summary.Car, summary.Track, summary.Config, row?.Sectors, summary.StartUtc, trace);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            _app.Notify("Couldn't export the lap", ex.Message, NoticeKind.Warning);
            return;
        }
        if (!PitBox.Services.External.Allowed("save dialog")) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "Export lap", Filter = LapFile.Filter, FileName = LapFile.FileName(shared), DefaultExt = LapFile.Extension, AddExtension = true };
        if (dialog.ShowDialog(_window) != true) return;
        try
        {
            using (var file = File.Create(dialog.FileName)) LapFile.Write(file, shared);
            _app.Notify("Lap exported", Path.GetFileName(dialog.FileName), NoticeKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _app.Notify("Couldn't export the lap", ex.Message, NoticeKind.Error, important: true);
        }
    }

    private async Task ImportLapAsync(TraceStore traces, SessionSummary session)
    {
        if (!PitBox.Services.External.Allowed("open dialog")) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Import lap", Filter = LapFile.Filter + "|All files (*.*)|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(_window) != true) return;
        string path = dialog.FileName;
        SharedLap lap;
        try
        {
            lap = await Task.Run(() =>
            {
                using var file = File.OpenRead(path);
                return LapFile.Read(file);
            });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _app.Notify("Couldn't import the lap", ex.Message, NoticeKind.Error, important: true);
            return;
        }
        if (!string.Equals(lap.Track, session.Track, StringComparison.OrdinalIgnoreCase) || !string.Equals(lap.Config, session.Config, StringComparison.OrdinalIgnoreCase))
        {
            string where = lap.Config.Length > 0 ? $"{lap.Track} · {lap.Config}" : lap.Track;
            _app.Notify("Different track", $"That lap is from {where}. Open a session there to compare against it.", NoticeKind.Warning, important: true);
            return;
        }
        long sid = await Task.Run(() => traces.AddShared(lap, DateTime.UtcNow));
        _reference = "shared:" + sid;
        _app.Notify("Lap imported", LapAnalysisView.SharedLabel(new SharedLapInfo(sid, lap.Driver, lap.Car, lap.Track, lap.Config, lap.LapTime, lap.RecordedUtc), session.Car) + " is the reference now.", NoticeKind.Success);
        Refresh();
    }

    private async Task DeleteSharedAsync(TraceStore traces, long id)
    {
        if (!await _window.Confirm("Remove this imported lap?", "It's taken out of every comparison. The file you imported it from stays.", "Remove", danger: true)) return;
        await Task.Run(() => traces.DeleteShared(id));
        _reference = "session";
        Refresh();
    }

    private async void Refresh(bool restoreOffset = false)
    {
        try { await RefreshCore(restoreOffset); }
        catch (Exception ex)
        {
            App.LogError(ex);
            if (_shown) _content.Content = Ui.DangerBanner("Stats unavailable", ex.Message);
        }
    }

    private async Task RefreshCore(bool restoreOffset)
    {
        if (!_shown || Stats is not { } stats) return;
        int version = ++_version;
        _progress.Content = StatsUi.Progress(stats);
        if (stats.Problem is { } problem) { _content.Content = Ui.DangerBanner("Stats unavailable", problem); return; }
        var (name, number) = stats.Driver();
        _driver.Text = name ?? "";
        _number.Text = number is null ? "" : "#" + number;
        _numberPlate.Visibility = number is null ? Visibility.Collapsed : Visibility.Visible;
        if (_sessionId is null || _crumbs.Visibility != Visibility.Visible) ShowCrumbs(null);
        if (stats.Store is not { } store || store.Empty)
        {
            _content.Content = new Tile { Style = Ui.Res<Style>("Card"), Child = Ui.Text(StatsSessionsNote(stats), "Muted") };
            return;
        }
        if (_sessionId is not null) { await RefreshDrill(stats, store, version); return; }
        var q = stats.Query();
        var range = stats.Range;
        var results = await stats.EvaluateAsync(stats.Available, q);
        var charts = await Task.Run(() => StatsCharts.Read(store, q, range, stats.Settings.TimelineCombo, stats.Settings.TimelinePickedUtc));
        var traces = stats.Traces;
        string driver = name ?? "";
        var (sessions, traced, races) = await Task.Run(() =>
        {
            var list = store.Sessions(q.From, q.To);
            return (list, traces?.SessionsWithTraces(list.Select(s => s.Id)) ?? new HashSet<long>(), RaceInfo.Read(store, driver));
        });
        await Motion.PageSettled(Dispatcher);
        if (version != _version || !_shown || _sessionId is not null) return;

        _sessionsCount.Text = $"{SimRacing.Race.RaceFormat.Count(charts.Sessions, "session")} · {StatsUi.RangeName(range)}";
        var page = new StackPanel();
        if (stats.Leagues is { Count: 0 }) page.Children.Add(LeagueNote(store));
        if (charts.Sessions == 0)
        {
            page.Children.Add(new Tile { Style = Ui.Res<Style>("Card"), Child = Ui.Text("No sessions in this range yet: pick a longer one to see the rest.", "Muted") });
            _content.Content = page;
            return;
        }
        var sessionsCard = StatsSessions.Card(sessions, traced, _filter, stats.Settings, Refresh, s => Open(s.Id), TraceSettings.Button(_app, stats, _window), races);
        page.Children.Add(StatsSections.Page(results, charts, stats, q, Changed, sessionsCard, races.Card(sessions.FirstOrDefault(RaceParts.Raced))));
        page.Children.Add(new TelemetryFilesCard(_app, _window, stats) { Margin = new Thickness(0, Ui.PanelSpacing, 0, 0) });
        _content.Content = page;
        if (restoreOffset)
        {
            double offset = _overviewOffset;
            await Dispatcher.InvokeAsync(() => _scroll.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
        }
    }

    private void Refresh() => Refresh(false);

    private IEnumerable<(string Name, Func<Task> Enter)> LapStates()
    {
        yield return ("sessions", async () =>
        {
            if (Stats is { } stats) stats.Settings.Range = "all";
            _sessionId = null;
            Refresh();
            await Task.Delay(500);
            if (Ui.FindAnchor(this, "stats.sessions") is { } card) card.BringIntoView();
            await Task.Delay(300);
        });
        long? Traced() => Stats?.Store?.Sessions(0, long.MaxValue).FirstOrDefault(s => Stats.Traces?.Has(s.Id) == true)?.Id;
        yield return ("session", async () =>
        {
            if (Traced() is { } id) Open(id);
            await Task.Delay(500);
        });
        yield return ("session-race", async () =>
        {
            if (Stats?.Store?.Sessions(0, long.MaxValue).FirstOrDefault(s => s.Type == "Race" && s.Laps > 18) is { } race) Open(race.Id);
            await Task.Delay(600);
        });
        yield return ("session-runs", async () =>
        {
            await Task.Delay(100);
            _scroll.ScrollToVerticalOffset(_scroll.ScrollableHeight * 0.55);
            await Task.Delay(300);
        });
        yield return ("session-tools", async () =>
        {
            _scroll.ScrollToVerticalOffset(_scroll.ScrollableHeight);
            await Task.Delay(300);
        });
        yield return ("analysis", async () =>
        {
            if (Traced() is { } id && Stats?.Traces?.LapsWithTrace(id).OrderBy(l => l).Take(3).ToList() is { Count: > 0 } laps) Open(id, laps);
            await Task.Delay(700);
        });
        yield return ("analysis-one-lap", async () =>
        {
            if (Traced() is { } id && Stats?.Traces?.LapsWithTrace(id).OrderBy(l => l).FirstOrDefault() is { } lap and > 0)
            {
                Open(id, new List<int> { lap });
                _reference = "lap:" + lap;
                Refresh();
            }
            await Task.Delay(700);
        });
        yield return ("analysis-one-lap-replay", async () =>
        {
            FindChild<LapReplayView>(this)?.BringIntoView();
            await Task.Delay(400);
        });
        yield return ("analysis-again", async () =>
        {
            if (Traced() is { } id && Stats?.Traces?.LapsWithTrace(id).OrderBy(l => l).Take(3).ToList() is { Count: > 0 } laps) { _reference = "session"; Open(id, laps); }
            await Task.Delay(700);
        });
        yield return ("analysis-zoomed", async () =>
        {
            if (FindState() is { } s) { s.Zoom(s.Length * 0.32, s.Length * 0.45); s.SetCursor(s.Length * 0.38); }
            await Task.Delay(400);
        });
        yield return ("analysis-speed", async () =>
        {
            if (FindState() is { } s) { s.MapColors = MapColors.Speed; s.ResetZoom(); s.RaiseCursor(); }
            await Task.Delay(400);
        });
        yield return ("analysis-replay", async () =>
        {
            if (Stats is { } stats)
            {
                stats.Settings.ReplayZoom = 0.8;
                if (stats.Traces?.SharedAt("Circuit de Spa-Francorchamps", "Grand Prix Pits").FirstOrDefault() is { } rival) _reference = "shared:" + rival.Id;
            }
            _replay = new ReplayMemory { Distance = 2350 };
            Refresh();
            await Task.Delay(800);
            FindChild<LapReplayView>(this)?.BringIntoView();
            await Task.Delay(300);
        });
        async Task Replay(string sync, double zoom, double distance)
        {
            if (Stats is { } stats) { stats.Settings.ReplaySync = sync; stats.Settings.ReplayZoom = zoom; }
            _replay.Distance = distance;
            Refresh();
            await Task.Delay(700);
            FindChild<LapReplayView>(this)?.BringIntoView();
            await Task.Delay(200);
        }
        yield return ("analysis-replay-time", () => Replay("time", 0.62, 4300));
        yield return ("analysis-replay-whole", () => Replay("distance", 0, 1200));
        yield return ("analysis-replay-playing", async () =>
        {
            await Replay("distance", 0.8, 2350);
            if (FindChild<LapReplayView>(this) is not { } view) return;
            view.Play();
            await Task.Delay(1500);
            view.Pause();
            await Task.Delay(200);
        });
        yield return ("analysis-replay-closed", () =>
        {
            if (Stats is { } stats) stats.Settings.ReplaySync = "distance";
            _reference = "session";
            Refresh();
            return Task.Delay(300);
        });
        yield return ("traces-settings", async () =>
        {
            _sessionId = null;
            _analysisLaps = null;
            Refresh();
            await Task.Delay(600);
            if (Ui.FindAnchor(this, "stats.traces") is Button gear) { gear.BringIntoView(); await Task.Delay(200); gear.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); }
            await Task.Delay(500);
        });
        yield return ("traces-shrink", async () =>
        {
            TraceSettings.PickLapsPerSession("1");
            await Task.Delay(800);
        });
        yield return ("traces-shrink-cancelled", async () =>
        {
            _window.CloseModal(false);
            await Task.Delay(300);
        });
        yield return ("traces-closed", () =>
        {
            TraceSettings.CloseOpen();
            if (Stats is { } stats) stats.Settings.Range = "season";
            Refresh();
            return Task.Delay(300);
        });
    }

    private AnalysisState? FindState() => FindChild<TraceChart>(this) is { } chart ? chart.State : null;

    private static T? FindChild<T>(DependencyObject root, Func<T, bool>? match = null) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T found && (match is null || match(found))) return found;
            if (FindChild(child, match) is { } deeper) return deeper;
        }
        return null;
    }

    private static string StatsSessionsNote(SessionStatsService stats) =>
        stats.Importing is not null ? "Your telemetry files are being read: sessions show up here as they're done."
            : "No sessions yet: drive in iRacing and they show up here.";

    private static FrameworkElement LeagueNote(StatsStore store)
    {
        var seen = store.LeaguesSeen();
        string detail = "League figures count sessions by iRacing league id: add yours in your league's settings."
            + (seen.Count > 0 ? " League ids in your sessions: " + string.Join(", ", seen.Take(5).Select(l => $"{l.Id} ({l.Sessions})")) + "." : "");
        var note = Ui.Notice("Accent", "I.Flag", "Which league is yours?", detail);
        note.Margin = new Thickness(0, 0, 0, Ui.PanelSpacing);
        return note;
    }

    public IEnumerable<(string Name, Func<Task> Enter)> ScreenshotStates()
    {
        foreach (var state in LapStates()) yield return state;
        foreach (var range in new[] { "week", "month", "all" })
        {
            var r = range;
            yield return ("range-" + r, () =>
            {
                if (Stats is { } stats) { stats.Settings.Range = r; Refresh(); }
                return Task.Delay(400);
            });
        }
        foreach (var state in new[] { "activity", "cars", "telemetry" })
        {
            string anchor = "stats." + state;
            yield return (state, async () =>
            {
                await Task.Delay(300);
                Ui.FindAnchor(this, anchor)?.BringIntoView();
                await Task.Delay(300);
            });
        }
        yield return ("imperial", () =>
        {
            if (Stats is { } stats) { stats.Settings.Range = "season"; stats.Settings.Imperial = true; Refresh(); }
            return Task.Delay(400);
        });
        yield return ("metric", () =>
        {
            if (Stats is { } stats) { stats.Settings.Imperial = false; Refresh(); }
            return Task.Delay(400);
        });
    }
}

internal static class StatsUi
{
    private static readonly string[] Ranges = { "Week", "Month", "Season", "All time" };

    public static string RangeName(StatRange range) => range switch
    {
        StatRange.Week => "this week", StatRange.Month => "this month", StatRange.Season => "this season", _ => "all time",
    };

    public static FrameworkElement RangePicker(SessionStatsService stats, string group, Action changed) =>
        Ui.Segmented(Ranges, (int)stats.Range, i =>
        {
            stats.Settings.Range = SessionStatsService.NameOf((StatRange)i);
            changed();
        }, "statsRange" + group, new[] { "Since Monday", "This calendar month", "This iRacing season (12 weeks + week 13)", "Everything" });

    internal static string? TipOf(StatDef def, StatResult result, bool full)
    {
        var tip = def.Help;
        if (def.LiveOnly) tip = (tip is null ? "" : tip + ". ") + "Live sessions only";
        if (result.NoData) tip = (tip is null ? "" : tip + ". ") + "Nothing recorded for this in the range";
        if (full) tip = string.Join("\n", new[] { result.NoData ? null : result.Value, result.Detail, tip }.Where(x => !string.IsNullOrEmpty(x)));
        return string.IsNullOrEmpty(tip) ? null : tip;
    }

    internal static (string Soft, string Deep) Hue(string name) => name switch
    {
        "Gold" => ("Tool1Soft", "Tool1Deep"), "Slate" => ("Tool2Soft", "Tool2Deep"), "Plum" => ("Tool3Soft", "Tool3Deep"),
        "Coral" => ("Tool4Soft", "Tool4Deep"), "Sage" => ("Tool5Soft", "Tool5Deep"), "Orange" => ("Tool6Soft", "Tool6Deep"),
        "Cyan" => ("Tool7Soft", "Tool7Deep"), _ => Hues.Accent,
    };

    internal readonly record struct Tone(string Soft, string Deep, string? Icon, bool Fill);

    internal static Tone? ToneOf(StatDef def, StatResult result)
    {
        if (result.NoData) return null;
        var (goldSoft, goldDeep) = Hue("Gold");
        var (sageSoft, sageDeep) = Hue("Sage");
        var (coralSoft, coralDeep) = Hue("Coral");
        bool some = result.Value != "0";
        return def.Id switch
        {
            "race.wins" or "race.podiums" or "race.top5" when some => new Tone(goldSoft, goldDeep, "I.Trophy", true),
            "inc.zero" when some => new Tone(sageSoft, sageDeep, "I.Check", true),
            "race.gained" or "race.irating" or "race.sr" when result.Value.StartsWith('+') => new Tone(sageSoft, sageDeep, null, false),
            "race.gained" or "race.irating" or "race.sr" when result.Value.StartsWith('−') => new Tone(coralSoft, coralDeep, null, false),
            _ => null,
        };
    }

    public static FrameworkElement? Progress(SessionStatsService stats)
    {
        if (stats.Importing is not { } p || p.Total == 0) return null;
        var stack = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        stack.Children.Add(Ui.Text($"Reading telemetry files · {p.Done} / {p.Total}", "Caption"));
        stack.Children.Add(new Meter { Value = (double)p.Done / p.Total, Fill = Ui.Res<Brush>("Accent"), Margin = new Thickness(0, 3, 0, 0) });
        return stack;
    }
}

internal static class StatsSections
{
    public static FrameworkElement Page(List<(StatDef Def, StatResult Result)> results, StatsCharts.Data charts, SessionStatsService service, StatQuery q, Action changed,
        FrameworkElement? sessions = null, FrameworkElement? lastRace = null)
    {
        var byId = results.ToDictionary(r => r.Def.Id, r => r);
        (StatDef Def, StatResult Result)? Get(string id) => byId.TryGetValue(id, out var r) ? r : null;
        IEnumerable<(StatDef, StatResult)> Many(params string[] ids) => ids.Select(Get).OfType<(StatDef, StatResult)>();

        var page = new StackPanel();
        void Add(FrameworkElement? element)
        {
            if (element is null) return;
            element.Margin = new Thickness(0, page.Children.Count == 0 ? 0 : Ui.PanelSpacing, 0, 0);
            page.Children.Add(element);
        }

        var groups = new StatGrid { MinTileWidth = 340, Gap = 32, RowGap = 23, Columns = 3 };
        void Group(string title, IEnumerable<(StatDef, StatResult)> stats)
        {
            var list = stats.ToList();
            if (list.Count > 0) groups.Children.Add(Chart(title, null, Metrics(list)!));
        }
        Group("Results", Many("race.starts", "race.finishes", "race.wins", "race.podiums", "race.top5", "race.league"));
        Group("Positions and ratings", Many("race.avgstart", "race.avgfinish", "race.gained", "race.irating", "race.sr"));
        Group("Incidents, pit lane and weather", Many("inc.total", "inc.zero", "style.pits", "style.fuel", "cond.rain", "cond.night"));
        var known = new HashSet<string> { "pace.laps", "time.total", "pace.distance", "pace.top",
            "time.week", "time.month", "time.weekly", "time.streak", "time.session", "time.stint", "time.garage", "time.practice", "time.qualify", "time.race",
            "car.unique", "track.unique", "combo.top", "car.time", "car.laps", "track.time", "track.laps", "class.time",
            "race.starts", "race.finishes", "race.wins", "race.podiums", "race.top5", "race.league", "race.avgstart", "race.avgfinish", "race.gained", "race.irating", "race.sr",
            "inc.total", "inc.zero", "style.fuel", "style.pits", "cond.rain", "cond.night" };
        Group("More", results.Where(r => !known.Contains(r.Def.Id)));
        Add(Hero(charts, q, StatsUi.RangeName(service.Range), Many("pace.laps", "time.total", "pace.distance", "pace.top").ToList(), groups, lastRace));
        Add(sessions);

        var performance = Section("Performance", "I.Stats", null, PickPair(charts, service, changed),
            StatsLayout.Pair(
                Chart("Personal best progression", "Your best clean lap in this car and track, session by session", charts.Timeline.Count switch
                {
                    0 => Empty("No clean laps for this pair yet."),
                    1 => Empty($"One best so far: {StatFormat.LapTime(charts.Timeline[0].Lap)} on {Dates.Day(charts.Timeline[0].When.ToLocalTime())}. The line starts once you beat it."),
                    _ => StatsCharts.Progression(charts.Timeline),
                }),
                Chart("Lap consistency", "Clean laps per session: the box holds the middle half, the line runs from the fastest lap to the 90th percentile, the tick is the median",
                    charts.Consistency.Count == 0 ? Empty("No session with 3 or more clean laps in this pair yet.") : StatsCharts.Consistency(charts.Consistency)), 1050, gap: 26)!);

        var types = charts.SessionTypes;
        var activity = Section("Driving activity", "I.Clock", $"{SimRacing.Race.RaceFormat.Count(charts.Sessions, "session")}", null, Stack(
            Chart("Session mix", null, StatsCharts.SessionMix(types)),
            StatsLayout.Pair(
                Chart("When you drive", "Darker squares are the hours you drive most", StatsCharts.Heatmap(charts.Heatmap, charts.HeatmapDays)),
                Chart(charts.Trend.Title, $"Each bar is your total driving time for that {charts.Trend.Unit}", StatsCharts.Trend(charts.Trend)), 1050, gap: 26),
            Metrics(Many("time.week", "time.month", "time.weekly", "time.streak", "time.session", "time.stint", "time.garage"))));
        Ui.Anchor(activity, "stats.activity");
        Add(activity);

        string? variety = string.Join(" · ", new[] { Count(Get("car.unique"), "car"), Count(Get("track.unique"), "track") }.OfType<string>());
        var rankings = new StatGrid { MinTileWidth = 227, Gap = 26, Columns = 3 };
        rankings.Children.Add(Chart("Cars", "By driving time", StatsCharts.Ranked(charts.Cars, "Accent", "No cars in this range", cars: true)));
        rankings.Children.Add(Chart("Tracks", "By driving time", StatsCharts.Ranked(charts.Tracks, "Slate", "No tracks in this range")));
        rankings.Children.Add(Chart("Classes", "By driving time", StatsCharts.Ranked(charts.Classes, "Gold", "No classes in this range")));
        var cars = Section("Cars and tracks", "I.Flag", variety.Length > 0 ? variety : null, null, Stack(rankings, PersonalBests(charts, q, Get("combo.top"))));
        Ui.Anchor(cars, "stats.cars");
        Add(cars);
        Add(performance);
        return page;
    }

    private static string? Count((StatDef Def, StatResult Result)? stat, string noun) =>
        stat is { Result: { NoData: false } r } ? $"{r.Value} {noun}{(r.Value == "1" ? "" : "s")}" : null;

    internal static FrameworkElement Empty(string text) => new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 3, 0, 0) };

    internal static Tile Section(string title, string icon, string? note, FrameworkElement? tools, FrameworkElement body)
    {
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        var mark = new Icon { Data = Ui.Res<Geometry>(icon), Width = 16, Height = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        mark.SetResourceReference(Control.ForegroundProperty, "AccentText");
        left.Children.Add(mark);
        var text = Ui.Text(title, "H2");
        text.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(text);
        if (note is not null)
            left.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(13, 0, 0, 0) });
        FrameworkElement head = left;
        if (tools is not null)
        {
            tools.Margin = new Thickness(0);
            head = Ui.SplitRow(left, tools, stackLeft: true);
        }
        body.Margin = new Thickness(0, 15, 0, 0);
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(19, 16, 19, 19), Child = new StackPanel { Children = { head, body } } };
    }

    internal static StackPanel Stack(params FrameworkElement?[] parts)
    {
        var stack = new StackPanel();
        foreach (var part in parts.OfType<FrameworkElement>())
        {
            if (stack.Children.Count > 0)
            {
                var rule = Hairline.Rule(new Border { Margin = new Thickness(0, 18, 0, 16) });
                rule.SetResourceReference(Border.BackgroundProperty, "Line");
                stack.Children.Add(rule);
            }
            stack.Children.Add(part);
        }
        return stack;
    }

    internal static StackPanel Chart(string title, string? caption, FrameworkElement content)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Style = Ui.Res<Style>("H3") });
        if (caption is not null) stack.Children.Add(new TextBlock { Text = caption, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0) });
        content.Margin = new Thickness(0, 10, 0, 0);
        stack.Children.Add(content);
        return stack;
    }

    private static FrameworkElement? Metrics(IEnumerable<(StatDef Def, StatResult Result)> stats, double size = 18)
    {
        var grid = new StatGrid { MinTileWidth = 122, Gap = 0, RowGap = 13 };
        foreach (var (def, result) in stats)
        {
            var element = Metric(def, result, size);
            if (def.Kind == StatKind.Text && !result.NoData && result.Value.Length > 10) StatGrid.SetSpan(element, 2);
            grid.Children.Add(element);
        }
        return grid.Children.Count == 0 ? null : grid;
    }

    private static FrameworkElement Metric(StatDef def, StatResult result, double size, string? hue = null)
    {
        var tone = StatsUi.ToneOf(def, result);
        bool name = def.Kind == StatKind.Text && !result.NoData && result.Value.Length > 10;
        var value = new TextBlock { Text = result.NoData ? "No data" : result.Value, Style = Ui.Res<Style>("BigValue"), FontSize = size * 1.25, TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.ExtraBold };
        value.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        if (result.NoData)
        {
            value.FontSize = 12;
            value.FontWeight = FontWeights.SemiBold;
            value.ClearValue(TextBlock.FontFamilyProperty);
            value.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        }
        else
        {
            if (name) { value.FontSize = 15; value.FontWeight = FontWeights.Bold; value.MaxHeight = 15 * 2.8; value.TextTrimming = TextTrimming.CharacterEllipsis; TextFit.TipWhenTrimmed(value); }
            if (tone is { } t) value.SetResourceReference(TextBlock.ForegroundProperty, t.Deep);
            else if (hue is not null) value.SetResourceReference(TextBlock.ForegroundProperty, hue);
        }
        var label = new DockPanel { Margin = new Thickness(0, 2, 0, 0) };
        if (tone is { Icon: { } icon } mark)
        {
            var glyph = new Icon { Data = Ui.Res<Geometry>(icon), Width = 11, Height = 11, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
            glyph.SetResourceReference(Control.ForegroundProperty, mark.Deep);
            label.Children.Add(glyph);
        }
        label.Children.Add(new TextBlock { Text = def.Label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var bar = new Border { Width = 18, Height = 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 6), RenderTransform = new SkewTransform(-30, 0) };
        bar.SetResourceReference(Border.BackgroundProperty, result.NoData ? "Line" : tone?.Deep ?? hue ?? "Place4");
        FrameworkElement shown = value;
        if (!result.NoData && !name)
        {
            var rolling = new Odometer(size * 1.25, tone?.Deep ?? hue ?? "Heading", italic: true) { CountUpOnLoad = true, RollMs = 900, HorizontalAlignment = HorizontalAlignment.Left };
            rolling.Set(result.Value);
            shown = rolling;
        }
        var stack = new StackPanel { Children = { bar, shown, label } };
        if (result.Detail is { Length: > 0 } detail && !result.NoData)
        {
            var line = new TextBlock { Text = detail, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
            line.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
            stack.Children.Add(line);
        }
        var cell = Hairline.Frame(new Border { Padding = new Thickness(11, 2, 10, 2), Child = stack, ToolTip = StatsUi.TipOf(def, result, full: name) }, Sides.Left);
        cell.SetResourceReference(Border.BorderBrushProperty, "Line");
        return cell;
    }

    private static Tile Hero(StatsCharts.Data charts, StatQuery q, string range, List<(StatDef Def, StatResult Result)> headline, StatGrid racecraft, FrameworkElement? lastRace)
    {
        var block = new Grid { MinHeight = 172, ClipToBounds = true };
        block.Children.Add(new Slant("Place4Wash", 0, 0, 0.58, 0, 0.44, 1, 0, 1));
        block.Children.Add(new Slant("Place4Faint", 0.58, 0, 0.64, 0, 0.5, 1, 0.44, 1));
        var best = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(23, 18, 23, 16), HorizontalAlignment = HorizontalAlignment.Left };
        best.Children.Add(Scoped("Personal best", "all time, in the car and track you drove last"));
        if (charts.Bests.FirstOrDefault() is { } pb)
        {
            var carZone = RaceParts.CarOnStripe(pb.Car, "Place4", 172, 128, 330, carArea: CarArea);
            var outline = new Border
            {
                Width = OutlineWidth, Height = OutlineHeight, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 16, CarArea, 0), IsHitTestVisible = false,
                Child = new TrackSketch(pb.Track, pb.Config, "Place4Text", 4, lap: true) { PlayOnLoad = true, TrackId = pb.TrackId, TrackName = pb.TrackName },
            };
            block.Children.Add(carZone);
            block.Children.Add(outline);
            var lap = RaceParts.Big(StatFormat.LapTime(pb.Best), 58, "Place4Text", italic: true);
            lap.HorizontalAlignment = HorizontalAlignment.Left;
            lap.Margin = new Thickness(0, 2, 0, 0);
            best.Children.Add(lap);
            var carName = new TextBlock { Text = pb.Car, Style = Ui.Res<Style>("H3"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            var trackName = new TextBlock { Text = pb.Config.Length > 0 ? $"{pb.Track} · {pb.Config}" : pb.Track, Style = Ui.Res<Style>("Muted"), TextWrapping = TextWrapping.Wrap };
            best.Children.Add(carName);
            best.Children.Add(trackName);
            carZone.KeepClearOf(best);
            block.SizeChanged += (_, e) =>
            {
                var shown = e.NewSize.Width - best.ActualWidth >= CarRoom ? Visibility.Visible : Visibility.Collapsed;
                if (carZone.Car is { } shownCar && shownCar.Visibility != shown) shownCar.Visibility = shown;
                double text = best.Margin.Left + new FrameworkElement[] { best.Children[0] as FrameworkElement ?? lap, lap, carName, trackName }.Max(t => t.ActualWidth);
                var map = e.NewSize.Width - text - CarArea >= OutlineWidth + 16 ? Visibility.Visible : Visibility.Collapsed;
                if (outline.Visibility != map) outline.Visibility = map;
            };
            var facts = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            void Fact(FrameworkElement element) { element.Margin = new Thickness(0, 0, 8, 3); element.VerticalAlignment = VerticalAlignment.Center; facts.Children.Add(element); }
            if (pb.InRange && q.From > 0)
            {
                var (goldSoft, goldDeep) = StatsUi.Hue("Gold");
                Fact(Ui.Chip($"New {range}", goldSoft, goldDeep));
            }
            Fact(new TextBlock { Text = $"Set {Dates.Day(pb.When.ToLocalTime())} · {SimRacing.Race.RaceFormat.Count(pb.Laps, "clean lap")} {range}", Style = Ui.Res<Style>("Caption") });
            if (charts.HeroFirst is { } first && first.Lap - pb.Best > 0.0005)
            {
                var gain = new TextBlock { Text = $"{StatFormat.Value(StatKind.LapDelta, pb.Best - first.Lap, false)} since your first session here ({Dates.Day(first.When.ToLocalTime())})", Style = Ui.Res<Style>("Caption"), FontWeight = FontWeights.SemiBold };
                gain.SetResourceReference(TextBlock.ForegroundProperty, StatsUi.Hue("Sage").Deep);
                Fact(gain);
            }
            best.Children.Add(facts);
            best.ToolTip = "The all-time best clean lap in the car and track you drove last in this range";
        }
        else best.Children.Add(new TextBlock { Text = "No clean laps in this range yet", Style = Ui.Res<Style>("Muted"), Margin = new Thickness(0, 6, 0, 0) });
        block.Children.Add(best);

        var right = new StackPanel { Children = { block } };
        if (headline.Count > 0)
            right.Children.Add(RaceParts.Tiles(headline.Count, headline.Select(h => RaceParts.Tile(h.Result.NoData ? "No data" : h.Result.Value, h.Def.Label, "Place4",
                h.Result.NoData ? "TextFaint" : "Heading", h.Result.NoData ? 16 : 30, StatsUi.TipOf(h.Def, h.Result, false))).ToArray<FrameworkElement?>()));
        if (racecraft.Children.Count > 0)
        {
            var rule = Hairline.Rule(new Border());
            rule.SetResourceReference(Border.BackgroundProperty, "Line");
            right.Children.Add(rule);
            right.Children.Add(new Border { Padding = new Thickness(23, 18, 23, 21), Child = racecraft });
        }
        FrameworkElement body = right;
        if (lastRace is Border card)
        {
            card.VerticalAlignment = VerticalAlignment.Stretch;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardWidth) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(card);
            Grid.SetColumn(right, 1);
            grid.Children.Add(right);
            body = grid;
        }
        return new Tile { Style = Ui.Res<Style>("Card"), Padding = new Thickness(0), Child = body, ClipToBounds = true };
    }

    internal const double CardWidth = 300;
    private const double CarRoom = 300;
    private const double CarArea = 380;
    private const double OutlineWidth = 200, OutlineHeight = 120;

    private static TextBlock Scoped(string title, string scope)
    {
        var text = new TextBlock { Style = Ui.Res<Style>("Eyebrow"), TextWrapping = TextWrapping.Wrap };
        text.Inlines.Add(new System.Windows.Documents.Run(title));
        var rest = new System.Windows.Documents.Run(" · " + scope) { FontWeight = FontWeights.Normal };
        rest.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextMuted");
        text.Inlines.Add(rest);
        return text;
    }

    private static FrameworkElement? PickPair(StatsCharts.Data charts, SessionStatsService service, Action changed)
    {
        if (charts.Bests.Count == 0) return null;
        var combos = charts.Bests.Select(b => (b.Key, Label: $"{b.Car} · {b.Track}{(b.Config.Length > 0 ? " · " + b.Config : "")}")).ToList();
        var pick = Ui.Combo(combos.Select(c => new Choice(c.Key, c.Label)), charts.TimelineKey ?? combos[0].Key, c => { service.Settings.TimelineCombo = c.Id; service.Settings.TimelinePickedUtc = StatsStore.Unix(DateTime.UtcNow); changed(); }, 211);
        pick.MaxWidth = 373;
        pick.VerticalAlignment = VerticalAlignment.Center;
        pick.ToolTip = "The car and track the charts below show";
        return pick;
    }

    private const int BestsShown = 12;

    private static FrameworkElement PersonalBests(StatsCharts.Data charts, StatQuery q, (StatDef Def, StatResult Result)? top)
    {
        string? caption = top is { Result: { NoData: false } t } ? $"Most driven pair in this range: {t.Value} ({t.Detail})" : null;
        if (charts.Bests.Count == 0) return Chart("Personal bests", caption, Empty("No clean laps in this range."));
        bool everything = q.From <= 0;
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1000, GridUnitType.Star), MinWidth = 73, MaxWidth = 300 });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1300, GridUnitType.Star), MinWidth = 73, MaxWidth = 380 });
        for (int c = 0; c < 4; c++) table.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        table.ColumnDefinitions.Add(new ColumnDefinition());
        int row = 0;
        void Cell(FrameworkElement element, int col, bool right = false, bool center = false)
        {
            element.Margin = new Thickness(col == 0 ? 0 : 15, 8, 0, 8);
            element.VerticalAlignment = VerticalAlignment.Center;
            if (right) element.HorizontalAlignment = HorizontalAlignment.Right;
            if (center) element.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(element, row);
            Grid.SetColumn(element, col);
            table.Children.Add(element);
        }
        TextBlock Text(string text, string style) => new() { Text = text, Style = Ui.Res<Style>(style), TextWrapping = TextWrapping.Wrap };
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Cell(Text("Car", "Eyebrow"), 0); Cell(Text("Track", "Eyebrow"), 1); Cell(Text("Clean laps", "Eyebrow"), 2, center: true); Cell(Text("Best lap", "Eyebrow"), 3, true); Cell(Text("Set", "Eyebrow"), 5, true);
        var (goldSoft, goldDeep) = StatsUi.Hue("Gold");
        var later = new List<UIElement>();
        foreach (var pb in charts.Bests)
        {
            int before = table.Children.Count;
            row++;
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var rule = Hairline.Rule(new Border { VerticalAlignment = VerticalAlignment.Top });
            rule.SetResourceReference(Border.BackgroundProperty, "Line");
            Grid.SetRow(rule, row);
            Grid.SetColumnSpan(rule, 7);
            table.Children.Add(rule);
            Cell(CarPictures.Beside(Text(pb.Car, "Body"), pb.Car, 18, 6.5), 0);
            var track = Text(pb.Config.Length > 0 ? $"{pb.Track} · {pb.Config}" : pb.Track, "Body");
            track.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            Cell(track, 1);
            Cell(Text(pb.Laps.ToString("N0", CultureInfo.CurrentCulture), "Caption"), 2, center: true);
            var lap = new TextBlock { Text = StatFormat.LapTime(pb.Best), FontWeight = FontWeights.SemiBold, FontSize = 11.5 };
            lap.SetResourceReference(TextBlock.FontFamilyProperty, "MetricFont");
            lap.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            System.Windows.Documents.Typography.SetNumeralAlignment(lap, FontNumeralAlignment.Tabular);
            Cell(lap, 3, true);
            if (pb.InRange && !everything)
            {
                var chip = Ui.Chip("New", goldSoft, goldDeep);
                chip.ToolTip = "Set in this range";
                Cell(chip, 4, center: true);
            }
            Cell(new TextBlock { Text = Dates.Day(pb.When.ToLocalTime()), Style = Ui.Res<Style>("Caption") }, 5, true);
            if (row > BestsShown)
                for (int i = before; i < table.Children.Count; i++)
                {
                    table.Children[i].Visibility = Visibility.Collapsed;
                    later.Add(table.Children[i]);
                }
        }
        var content = new StackPanel { Children = { table } };
        if (charts.Bests.Count > BestsShown)
        {
            Button more = null!;
            more = Ui.LabeledButton("I.ChevronDown", $"Show more ({charts.Bests.Count - BestsShown} left)", () =>
            {
                foreach (var element in later) element.Visibility = Visibility.Visible;
                content.Children.Remove(more);
            });
            more.Margin = new Thickness(0, 10, 0, 0);
            more.HorizontalAlignment = HorizontalAlignment.Left;
            content.Children.Add(more);
        }
        string what = everything ? "Best clean lap per car and track, the latest driven first" : "All-time best clean lap of each car and track driven in this range, the latest driven first";
        return Chart("Personal bests", caption is null ? what : what + ". " + caption, content);
    }
}

internal static class StatsLayout
{
    public static FrameworkElement? Pair(FrameworkElement? first, FrameworkElement? second, double sideBySide, double a = 1, double b = 1, double gap = 19, bool center = false)
    {
        if (first is null) return second;
        if (second is null) return first;
        return new PairPanel { Threshold = sideBySide, A = a, B = b, Gap = gap, Center = center, Children = { first, second } };
    }

    private sealed class PairPanel : Panel
    {
        public double Threshold, A = 1, B = 1, Gap = 19;
        public bool Center;

        private bool Side(double width) => double.IsInfinity(width) || width >= Threshold;

        private (double First, double Second) Widths(double width) => ((width - Gap) * A / (A + B), (width - Gap) * B / (A + B));

        protected override Size MeasureOverride(Size available)
        {
            var (first, second) = (InternalChildren[0], InternalChildren[1]);
            if (Side(available.Width))
            {
                var (w1, w2) = double.IsInfinity(available.Width) ? (double.PositiveInfinity, double.PositiveInfinity) : Widths(available.Width);
                first.Measure(new Size(w1, double.PositiveInfinity));
                second.Measure(new Size(w2, double.PositiveInfinity));
                return new Size(double.IsInfinity(available.Width) ? first.DesiredSize.Width + Gap + second.DesiredSize.Width : available.Width,
                    Math.Max(first.DesiredSize.Height, second.DesiredSize.Height));
            }
            first.Measure(new Size(available.Width, double.PositiveInfinity));
            second.Measure(new Size(available.Width, double.PositiveInfinity));
            return new Size(available.Width, first.DesiredSize.Height + Gap + second.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var (first, second) = (InternalChildren[0], InternalChildren[1]);
            if (Side(final.Width))
            {
                var (w1, w2) = Widths(final.Width);
                double Top(UIElement e) => Center ? (final.Height - e.DesiredSize.Height) / 2 : 0;
                first.Arrange(new Rect(0, Top(first), w1, first.DesiredSize.Height));
                second.Arrange(new Rect(w1 + Gap, Top(second), w2, second.DesiredSize.Height));
            }
            else
            {
                first.Arrange(new Rect(0, 0, final.Width, first.DesiredSize.Height));
                second.Arrange(new Rect(0, first.DesiredSize.Height + Gap, final.Width, second.DesiredSize.Height));
            }
            return final;
        }
    }
}

internal sealed class StatGrid : Panel
{
    public static readonly DependencyProperty SpanProperty = DependencyProperty.RegisterAttached("Span", typeof(int), typeof(StatGrid),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static void SetSpan(UIElement element, int span) => element.SetValue(SpanProperty, span);
    public static int GetSpan(UIElement element) => (int)element.GetValue(SpanProperty);

    public double MinTileWidth { get; init; } = 128;
    public double Gap { get; init; } = 8;
    public double? RowGap { get; init; }
    public int Columns { get; init; } = int.MaxValue;

    private readonly List<(UIElement Child, int Row, int Col, int Span)> _places = new();
    private readonly List<double> _rows = new();
    private double _measuredWidth = double.NaN;

    private double Between => RowGap ?? Gap;

    private int ColumnsFor(double width)
    {
        int most = Math.Clamp((int)((width + Gap) / (MinTileWidth + Gap)), 1, Columns);
        var spans = InternalChildren.OfType<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).Select(c => Math.Clamp(GetSpan(c), 1, most)).ToList();
        int units = spans.Sum();
        if (units == 0 || spans.Max() > 1 && spans.Min() == 1) return most;
        int rows = (int)Math.Ceiling(units / (double)most);
        int even = (int)Math.Ceiling(units / (double)rows);
        return even % spans[0] == 0 ? even : most;
    }

    private double CellWidth(double width, int columns) => Math.Max(0, (width - Gap * (columns - 1)) / columns);

    private void Place(double width, bool measure)
    {
        int columns = ColumnsFor(width);
        double cell = CellWidth(width, columns);
        _places.Clear();
        _rows.Clear();
        int row = 0, col = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            int span = Math.Clamp(GetSpan(child), 1, columns);
            if (col + span > columns) { row++; col = 0; }
            if (measure) child.Measure(new Size(cell * span + Gap * (span - 1), double.PositiveInfinity));
            if (_rows.Count <= row) _rows.Add(0);
            _rows[row] = Math.Max(_rows[row], child.DesiredSize.Height);
            _places.Add((child, row, col, span));
            col += span;
        }
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? Math.Max(1, InternalChildren.Count) * (MinTileWidth + Gap) - Gap : available.Width;
        _measuredWidth = width;
        Place(width, measure: true);
        return new Size(width, _rows.Sum() + Between * Math.Max(0, _rows.Count - 1));
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Math.Abs(final.Width - _measuredWidth) > 0.5) Place(final.Width, measure: false);
        double cell = CellWidth(final.Width, ColumnsFor(final.Width));
        var tops = new double[_rows.Count];
        for (int r = 1; r < tops.Length; r++) tops[r] = tops[r - 1] + _rows[r - 1] + Between;
        foreach (var (child, row, col, span) in _places)
            child.Arrange(new Rect(col * (cell + Gap), tops[row], cell * span + Gap * (span - 1), _rows[row]));
        return final;
    }
}

internal static class StatsCharts
{
    internal sealed record PersonalBest(string Key, string Car, string Track, string Config, double Best, DateTime When, long Laps, bool InRange)
    {
        public long Latest { get; init; }
        public int? TrackId { get; init; }
        public string TrackName { get; init; } = "";
    }

    internal sealed record Ranking(string Label, double Seconds, long Laps);

    internal sealed record SessionSpread(DateTime When, int Laps, double Best, double Q1, double Median, double Q3, double P90, double StdDev)
    {
        public string Type { get; init; } = "";
    }

    internal sealed record TrendBin(string Label, string Tip, double Seconds, int Sessions);

    internal sealed record TrendData(string Title, string Unit, List<TrendBin> Bins);

    internal sealed record Data(int Sessions, double[,] Heatmap, int[,] HeatmapDays, List<Ranking> Cars, List<Ranking> Tracks, List<Ranking> Classes,
        List<(string Type, double Seconds)> SessionTypes, TrendData Trend,
        List<PersonalBest> Bests, (DateTime When, double Lap)? HeroFirst, string? TimelineKey, List<(DateTime When, double Lap)> Timeline, List<SessionSpread> Consistency);

    public static Data Read(StatsStore store, StatQuery q, StatRange range, string? timelineKey, long? pickedAt)
    {
        var db = store.Db;
        var heat = new double[7, 24];
        var hoursDriven = new HashSet<DateTime>();
        var starts = new List<(DateTime Local, double Seconds)>();
        foreach (var r in db.Query(StatRegistry.Scope + "SELECT start_utc, end_utc, drive_s FROM s", q.From, q.To))
        {
            var local = StatsStore.FromUnix(Convert.ToInt64(r[0])).ToLocalTime();
            var end = StatsStore.FromUnix(Convert.ToInt64(r[1])).ToLocalTime();
            double seconds = Convert.ToDouble(r[2] ?? 0d);
            SpreadOverHours(heat, local, end, seconds, hoursDriven);
            starts.Add((local, seconds));
        }
        List<Ranking> Top(string column) => db.Query(StatRegistry.Scope + $"SELECT {column}, SUM(drive_s) t, SUM(laps) FROM s GROUP BY {column} ORDER BY t DESC LIMIT 10", q.From, q.To)
            .Select(r => new Ranking((string)(r[0] ?? ""), Convert.ToDouble(r[1] ?? 0d), Convert.ToInt64(r[2] ?? 0L))).ToList();
        var types = db.Query(StatRegistry.Scope + "SELECT session_type, SUM(drive_s) t FROM s GROUP BY session_type ORDER BY t DESC", q.From, q.To)
            .Select(r => ((string)(r[0] ?? ""), Convert.ToDouble(r[1] ?? 0d))).ToList();
        var bests = db.Query(StatRegistry.Scope + """
            , pairs AS (SELECT s.car, s.track, s.track_config, COUNT(*) n, MAX(s.start_utc) latest FROM l JOIN s ON s.id = l.session_id WHERE l.clean = 1 GROUP BY s.car, s.track, s.track_config)
            SELECT p.car, p.track, p.track_config, MIN(laps.time), sessions.start_utc, p.n, p.latest,
                (SELECT x.track_id FROM sessions x WHERE x.car = p.car AND x.track = p.track AND x.track_config = p.track_config AND x.track_id IS NOT NULL ORDER BY x.start_utc DESC LIMIT 1),
                (SELECT x.track_name FROM sessions x WHERE x.car = p.car AND x.track = p.track AND x.track_config = p.track_config AND x.track_name IS NOT NULL ORDER BY x.start_utc DESC LIMIT 1)
            FROM pairs p
            JOIN sessions ON sessions.car = p.car AND sessions.track = p.track AND sessions.track_config = p.track_config
            JOIN laps ON laps.session_id = sessions.id
            WHERE laps.clean = 1 GROUP BY p.car, p.track, p.track_config ORDER BY p.latest DESC, p.car
            """, q.From, q.To)
            .Select(r =>
            {
                long at = Convert.ToInt64(r[4]);
                return new PersonalBest($"{r[0]}|{r[1]}|{r[2]}", (string)r[0]!, (string)r[1]!, (string)r[2]!, Convert.ToDouble(r[3]), StatsStore.FromUnix(at), Convert.ToInt64(r[5]), at >= q.From && at < q.To)
                    { Latest = Convert.ToInt64(r[6]), TrackId = r[7] is null ? null : Convert.ToInt32(r[7]), TrackName = r[8] as string ?? "" };
            }).ToList();

        List<(DateTime When, double Lap)> SessionBests(PersonalBest pb) => db.Query("""
            SELECT s.start_utc, MIN(l.time) FROM laps l JOIN sessions s ON s.id = l.session_id
            WHERE l.clean = 1 AND s.car = ?1 AND s.track = ?2 AND s.track_config = ?3 GROUP BY s.id ORDER BY s.start_utc, s.id
            """, pb.Car, pb.Track, pb.Config).Select(r => (StatsStore.FromUnix((long)r[0]!), Convert.ToDouble(r[1]))).ToList();

        (DateTime, double)? heroFirst = bests.Count > 0 && SessionBests(bests[0]) is [var first, ..] ? first : null;
        var picked = (timelineKey is { } key && bests.FirstOrDefault(b => b.Key == key) is { } chosen && pickedAt is { } at && bests[0].Latest <= at ? chosen : null)
            ?? bests.FirstOrDefault();
        var timeline = new List<(DateTime, double)>();
        var spreads = new List<SessionSpread>();
        if (picked is not null)
        {
            double best = double.MaxValue;
            foreach (var (when, lap) in SessionBests(picked))
            {
                if (lap >= best) continue;
                best = lap;
                timeline.Add((when, lap));
            }
            var laps = db.Query("""
                SELECT s.id, s.start_utc, l.time, s.session_type FROM laps l JOIN sessions s ON s.id = l.session_id
                WHERE l.clean = 1 AND s.car = ?1 AND s.track = ?2 AND s.track_config = ?3 ORDER BY s.start_utc, s.id
                """, picked.Car, picked.Track, picked.Config);
            foreach (var session in laps.GroupBy(r => (long)r[0]!))
            {
                var times = session.Select(r => Convert.ToDouble(r[2])).OrderBy(t => t).ToList();
                if (times.Count < 3) continue;
                spreads.Add(new SessionSpread(StatsStore.FromUnix((long)session.First()[1]!), times.Count, times[0], Quantile(times, 0.25), Quantile(times, 0.5),
                    Quantile(times, 0.75), Quantile(times, 0.9), StatRegistry.StdDev(times)) { Type = (string)(session.First()[3] ?? "") });
            }
            if (spreads.Count > 24) spreads = spreads.Skip(spreads.Count - 24).ToList();
        }
        return new Data(starts.Count, heat, DaysPerCell(hoursDriven), Top("car"), Top("track"), Top("car_class"), types, TrendOf(starts, q, range), bests, heroFirst, picked?.Key, timeline, spreads);
    }

    internal static void SpreadOverHours(double[,] heat, DateTime start, DateTime end, double seconds, ISet<DateTime>? hoursDriven = null)
    {
        if (seconds <= 0) return;
        if (end > start.AddDays(7)) end = start.AddDays(7);
        double span = (end - start).TotalSeconds;
        var hour = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, start.Kind);
        if (span <= 0)
        {
            heat[(int)start.DayOfWeek, start.Hour] += seconds;
            hoursDriven?.Add(hour);
            return;
        }
        for (int i = 0; hour < end && i < 7 * 24; i++, hour = hour.AddHours(1))
        {
            var from = hour < start ? start : hour;
            var to = hour.AddHours(1) > end ? end : hour.AddHours(1);
            heat[(int)hour.DayOfWeek, hour.Hour] += seconds * (to - from).TotalSeconds / span;
            hoursDriven?.Add(hour);
        }
    }

    internal static int[,] DaysPerCell(IEnumerable<DateTime> hoursDriven)
    {
        var days = new int[7, 24];
        foreach (var hour in hoursDriven) days[(int)hour.DayOfWeek, hour.Hour]++;
        return days;
    }

    internal static double Quantile(IReadOnlyList<double> sorted, double p)
    {
        double at = (sorted.Count - 1) * p;
        int lo = (int)Math.Floor(at), hi = (int)Math.Ceiling(at);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (at - lo);
    }

    internal static TrendData TrendOf(IReadOnlyList<(DateTime Local, double Seconds)> sessions, StatQuery q, StatRange range)
    {
        var now = q.NowUtc.ToLocalTime();
        var from = range == StatRange.All ? (sessions.Count > 0 ? sessions.Min(s => s.Local) : now) : StatsStore.FromUnix(q.From).ToLocalTime();
        var to = range == StatRange.All ? now : StatsStore.FromUnix(q.To).ToLocalTime().AddTicks(-1);
        if (to > now && range != StatRange.Week && range != StatRange.Month) to = now;
        string unit = range is StatRange.Week or StatRange.Month ? "day" : range == StatRange.Season || (to - from).TotalDays <= 26 * 7 ? "week" : "month";
        DateTime Start(DateTime t) => unit switch
        {
            "day" => t.Date,
            "week" => t.Date.AddDays(-(((int)t.DayOfWeek + 6) % 7)),
            _ => new DateTime(t.Year, t.Month, 1),
        };
        DateTime Next(DateTime t) => unit switch { "day" => t.AddDays(1), "week" => t.AddDays(7), _ => t.AddMonths(1) };
        var starts = new List<DateTime>();
        for (var t = Start(from); t <= to && starts.Count < 400; t = Next(t)) starts.Add(t);
        var seconds = new double[starts.Count];
        var counts = new int[starts.Count];
        foreach (var (local, s) in sessions)
        {
            int i = starts.FindLastIndex(t => t <= local);
            if (i < 0 || local >= Next(starts[i])) continue;
            seconds[i] += s;
            counts[i]++;
        }
        var c = CultureInfo.CurrentCulture;
        var bins = starts.Select((t, i) => new TrendBin(
            unit switch { "day" => range == StatRange.Week ? Dates.Weekday(t) : t.Day.ToString(c), "week" => Dates.Short(t), _ => Dates.ShortMonthYear(t) },
            unit switch { "day" => Dates.Weekday(t) + " " + Dates.Short(t), "week" => "Week of " + Dates.Date(t), _ => Dates.MonthYear(t) },
            seconds[i], counts[i])).ToList();
        return new TrendData(unit switch { "day" => "Driving per day", "week" => "Driving per week", _ => "Driving per month" }, unit is "day" or "week" ? unit : "month", bins);
    }

    private static readonly string[] Weekdays = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
    private static readonly string[] Weekday = { "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };
    private static readonly string[] EveryWeekday = { "Sundays", "Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays", "Saturdays" };

    internal static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public static FrameworkElement Heatmap(double[,] seconds, int[,] days)
    {
        double max = 0;
        foreach (double v in seconds) max = Math.Max(max, v);
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent };
        var tips = new string[7, 24];
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(31) });
        for (int h = 0; h < 24; h++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 9 });
        var accent = ((SolidColorBrush)Ui.Res<Brush>("Accent")).Color;
        Brush Shade(double share) => Frozen(Color.FromArgb((byte)(50 + 205 * Math.Sqrt(share)), accent.R, accent.G, accent.B));
        var cells = new List<(Border Cell, int Row, int Hour, bool Driven)>();
        for (int row = 0; row < 7; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(19) });
            int day = (row + 1) % 7;
            var label = new TextBlock { Text = Weekdays[day], Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            for (int h = 0; h < 24; h++)
            {
                double v = seconds[day, h];
                string when = $"{EveryWeekday[day]}, {Dates.Hour(h)} to {Dates.Hour(h + 1)}";
                double each = v / Math.Max(1, days[day, h]);
                string usual = each >= 60 ? $"{StatFormat.Duration(each)} of the hour on average" : "Under a minute of the hour on average";
                tips[row, h] = v > 0 ? $"{when}\nDrove on {SimRacing.Race.RaceFormat.Count(days[day, h], Weekday[day])} in this hour\n{usual}" : $"{when}\nNo driving";
                var cell = new Border { Margin = new Thickness(1.5) };
                if (v > 0) cell.Background = Shade(v / max);
                else cell.SetResourceReference(Border.BackgroundProperty, "MeterTrack");
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, h + 1);
                grid.Children.Add(cell);
                cells.Add((cell, row, h, v > 0));
            }
        }
        HoverCells(grid, tips);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int h = 0; h < 24; h += 3)
        {
            var hour = new TextBlock { Text = Dates.HourLabel(h), Style = Ui.Res<Style>("Caption"), Margin = new Thickness(1.5, 3, 0, 0) };
            Grid.SetRow(hour, 7);
            Grid.SetColumn(hour, h + 1);
            Grid.SetColumnSpan(hour, 3);
            grid.Children.Add(hour);
        }
        var legend = new DockPanel { Margin = new Thickness(31, 10, 0, 0) };
        var less = new TextBlock { Text = "None", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(less, Dock.Left);
        legend.Children.Add(less);
        var more = new TextBlock { Text = "More", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(more, Dock.Right);
        legend.Children.Add(more);
        var steps = new UniformGrid { Rows = 1 };
        var none = new Border { Height = 6, Margin = new Thickness(1.5, 0, 1.5, 0) };
        none.SetResourceReference(Border.BackgroundProperty, "MeterTrack");
        steps.Children.Add(none);
        foreach (double share in new[] { 0.1, 0.35, 0.65, 1.0 })
            steps.Children.Add(new Border { Height = 6, Margin = new Thickness(1.5, 0, 1.5, 0), Background = Shade(share) });
        legend.Children.Add(steps);
        Grid.SetRow(legend, 8);
        Grid.SetColumnSpan(legend, 25);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(legend);
        Reveal.Once(grid, () => { foreach (var c in cells) c.Cell.Opacity = 0; }, () => SweepIn(cells));
        return grid;
    }

    private static void HoverCells(Grid grid, string[,] tips)
    {
        var tip = new ToolTip { PlacementTarget = grid, Placement = PlacementMode.Mouse };
        (int Row, int Hour) hover = (-1, -1);
        (int Row, int Hour) CellAt(Point p)
        {
            int row = (int)Math.Floor(p.Y / grid.RowDefinitions[0].ActualHeight);
            if (row < 0 || row >= 7) return (-1, -1);
            double x = grid.ColumnDefinitions[0].ActualWidth;
            for (int h = 0; h < 24; h++)
            {
                double w = grid.ColumnDefinitions[h + 1].ActualWidth;
                if (p.X >= x && p.X < x + w) return (row, h);
                x += w;
            }
            return (-1, -1);
        }
        grid.MouseMove += (_, e) =>
        {
            var near = CellAt(e.GetPosition(grid));
            if (near == hover) return;
            hover = near;
            tip.IsOpen = false;
            if (near.Row < 0) return;
            tip.Content = tips[near.Row, near.Hour];
            tip.IsOpen = true;
        };
        grid.MouseLeave += (_, _) =>
        {
            hover = (-1, -1);
            tip.IsOpen = false;
        };
    }

    private const double SweepStepMs = 14, CellMs = 300;

    private static void SweepIn(List<(Border Cell, int Row, int Hour, bool Driven)> cells)
    {
        foreach (var (cell, row, hour, driven) in cells)
        {
            cell.Opacity = 1;
            double at = (hour + row * 1.5) * SweepStepMs;
            if (driven) Reveal.PopIn(cell, at, 0.2);
            else cell.BeginAnimation(UIElement.OpacityProperty, Reveal.Keys(at + CellMs, Motion.Ease, (0, 0), (at, 0), (at + CellMs, 1)));
        }
    }

    public static FrameworkElement Ranked(List<Ranking> items, string fill, string empty, bool cars = false)
    {
        if (items.Count == 0) return Ui.Text(empty, "Caption");
        var stack = new StackPanel();
        double max = Math.Max(1, items.Max(i => i.Seconds));
        int place = 0;
        foreach (var item in items.Take(8))
        {
            place++;
            string name = item.Label.Length > 0 ? item.Label : "Unknown";
            string figures = $"{StatFormat.Duration(item.Seconds)} · {SimRacing.Race.RaceFormat.Count(item.Laps, "lap")}";
            var line = new DockPanel { Margin = new Thickness(0, place == 1 ? 0 : 10, 0, 5), ToolTip = $"{name}\n{figures}" };
            var rank = new TextBlock { Text = place.ToString(CultureInfo.CurrentCulture), Style = Ui.Res<Style>("Caption"), Width = 18, FontWeight = FontWeights.SemiBold };
            if (place == 1) rank.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            DockPanel.SetDock(rank, Dock.Left);
            line.Children.Add(rank);
            var value = new TextBlock { Text = figures, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom };
            System.Windows.Documents.Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(value, Dock.Right);
            line.Children.Add(value);
            if (cars && CarPictures.Slotted(item.Label, 16) is { } picture)
            {
                picture.Margin = new Thickness(0, 0, 6, 0);
                DockPanel.SetDock(picture, Dock.Left);
                line.Children.Add(picture);
            }
            line.Children.Add(new TextBlock { Text = name, Style = Ui.Res<Style>("Body"), FontSize = 10.5, FontWeight = place == 1 ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(line);
            var track = new Grid { Height = 5, Margin = new Thickness(18, 0, 0, 0), ToolTip = line.ToolTip };
            track.SetResourceReference(Panel.BackgroundProperty, "MeterTrack");
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(item.Seconds, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, max - item.Seconds), GridUnitType.Star) });
            var bar = new Border { MinWidth = 2 };
            bar.SetResourceReference(Border.BackgroundProperty, fill);
            track.Children.Add(bar);
            stack.Children.Add(track);
        }
        if (items.Count > 8) stack.Children.Add(new TextBlock { Text = $"Top 8 of {items.Count}{(items.Count == 10 ? "+" : "")}", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(18, 8, 0, 0) });
        return stack;
    }

    public static FrameworkElement SessionMix(List<(string Type, double Seconds)> types)
    {
        double total = types.Sum(t => t.Seconds);
        if (total <= 0) return Ui.Text("No driving time in this range.", "Caption");
        static (string Name, string Brush, int Order) Look(string type) => type switch
        {
            "Practice" => ("Practice", "Slate", 0), "Qualify" => ("Qualifying", "Gold", 1), "Race" => ("Race", "Accent", 2),
            _ => (StatsSessions.TypeName(type), "LineStrong", 3),
        };
        var parts = types.Where(t => t.Seconds > 0).Select(t => (Look: Look(t.Type), t.Seconds)).OrderBy(p => p.Look.Order).ToList();
        var bar = new Grid { Height = 13 };
        var pieces = new List<Border>();
        foreach (var (look, seconds) in parts)
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(seconds, GridUnitType.Star) });
            var piece = new Border { Margin = new Thickness(0, 0, bar.ColumnDefinitions.Count < parts.Count ? 2 : 0, 0), ToolTip = $"{look.Name} · {StatFormat.Duration(seconds)} · {seconds / total:P0}" };
            piece.SetResourceReference(Border.BackgroundProperty, look.Brush);
            Grid.SetColumn(piece, bar.ColumnDefinitions.Count - 1);
            bar.Children.Add(piece);
            pieces.Add(piece);
        }
        var legend = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (look, seconds) in parts)
        {
            var item = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 23, 3) };
            var swatch = new Border { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, look.Brush);
            item.Children.Add(swatch);
            item.Children.Add(new TextBlock { Text = look.Name, Style = Ui.Res<Style>("Body"), FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center });
            var figure = new TextBlock { Text = $"{StatFormat.Duration(seconds)} · {seconds / total:P0}", Style = Ui.Res<Style>("Caption"), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            item.Children.Add(figure);
            legend.Children.Add(item);
        }
        Reveal.Once(bar, () => { bar.Opacity = 0; foreach (var piece in pieces) piece.Opacity = 0; }, () => WipeIn(bar, pieces, legend));
        return new StackPanel { Children = { bar, legend } };
    }

    private const double WipeMs = 720, PieceLagMs = 110;

    private static void WipeIn(Grid bar, List<Border> pieces, Panel legend)
    {
        bar.Opacity = 1;
        var wipe = new ScaleTransform();
        bar.RenderTransformOrigin = new Point(0, 0.5);
        bar.RenderTransform = wipe;
        wipe.BeginAnimation(ScaleTransform.ScaleXProperty, Reveal.Keys(WipeMs, Motion.Ease, (0, 0), (WipeMs, 1)));
        for (int i = 0; i < pieces.Count; i++)
        {
            pieces[i].Opacity = 1;
            double at = i * PieceLagMs;
            pieces[i].BeginAnimation(UIElement.OpacityProperty, Reveal.Keys(at + 260, null, (0, 0), (at, 0), (at + 260, 1)));
        }
        int n = 0;
        foreach (UIElement item in legend.Children) Motion.Enter(item, 240 + n++ * 70, 6);
    }

    public static FrameworkElement Trend(TrendData trend)
    {
        var chart = new TrendChart(trend.Bins) { Height = 162 };
        Reveal.Once(chart, () => chart.Grow = 0, () =>
        {
            chart.Grow = 1;
            chart.BeginAnimation(TrendChart.GrowProperty, Reveal.Keys(TrendChart.GrowMs, null, (0, 0), (TrendChart.GrowMs, 1)));
        });
        return chart;
    }

    public static FrameworkElement Progression(List<(DateTime When, double Lap)> points) => new ProgressionChart(points) { Height = 186 };

    public static FrameworkElement Consistency(List<SessionSpread> sessions) => new ConsistencyChart(sessions) { Height = 186 };

    internal static string AxisLap(double seconds, double step)
    {
        string text = StatFormat.LapTime(seconds + 0.0004);
        int decimals = step >= 1 ? 0 : Math.Abs(step * 10 - Math.Round(step * 10)) < 1e-9 ? 1 : 2;
        int dot = text.LastIndexOf('.');
        return dot < 0 ? text : decimals == 0 ? text[..dot] : text[..Math.Min(text.Length, dot + 1 + decimals)];
    }

    internal static (double Lo, double Hi, double Step) LapScale(double lo, double hi)
    {
        double span = Math.Max(0.2, hi - lo);
        double step = new[] { 0.05, 0.1, 0.2, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60 }.FirstOrDefault(s => span / s <= 4, 120);
        return (Math.Floor((lo - step * 0.25) / step) * step, Math.Ceiling((hi + step * 0.25) / step) * step, step);
    }

    internal abstract class HoverChart : FrameworkElement
    {
        protected const double Left = 57, Bottom = 19, Top = 8, Right = 11;
        private readonly ToolTip _tip;
        protected int Hover = -1;

        protected HoverChart() => _tip = new ToolTip { PlacementTarget = this, Placement = PlacementMode.Mouse };

        protected abstract int HitAt(Point p);
        protected abstract string TipOf(int index);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int near = HitAt(e.GetPosition(this));
            if (near == Hover) return;
            Hover = near;
            _tip.IsOpen = false;
            if (near >= 0)
            {
                _tip.Content = TipOf(near);
                _tip.IsOpen = true;
            }
            InvalidateVisual();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            Hover = -1;
            _tip.IsOpen = false;
            InvalidateVisual();
        }

        protected double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

        protected FormattedText Text(string s, Brush? brush = null, bool bold = false, double size = 9) =>
            new(ThemeManager.Case(s), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, ThemeManager.UiFace(bold: bold), size, brush ?? Ui.Res<Brush>("TextMuted"), Dpi);

        protected Func<double, double> LapAxis(DrawingContext dc, double lo, double hi, double step)
        {
            double w = ActualWidth, plotH = ActualHeight - Top - Bottom;
            double Y(double lap) => Top + plotH * (hi - lap) / (hi - lo);
            double px = Hairline.ScaleOf(this);
            var grid = Hairline.Pen(Ui.Res<Brush>("GridLine"), px);
            for (double v = lo; v <= hi + step / 2; v += step)
            {
                double y = Hairline.Snap(Y(v), px);
                dc.DrawLine(grid, new Point(Left, y), new Point(w - Right, y));
                var label = Text(AxisLap(v, step));
                dc.DrawText(label, new Point(Left - 8 - label.Width, y - label.Height / 2));
            }
            dc.DrawLine(Hairline.Pen(Ui.Res<Brush>("LineStrong"), px), new Point(Left, Hairline.Snap(ActualHeight - Bottom, px)), new Point(w - Right, Hairline.Snap(ActualHeight - Bottom, px)));
            return Y;
        }
    }

    private sealed class ProgressionChart : HoverChart
    {
        private readonly List<(DateTime When, double Lap)> _points;

        public ProgressionChart(List<(DateTime When, double Lap)> points) => _points = points;

        private (double Min, double Max) TimeScale()
        {
            double min = _points[0].When.Ticks, max = _points[^1].When.Ticks;
            if (max - min < TimeSpan.TicksPerDay) { min -= TimeSpan.TicksPerDay; max += TimeSpan.TicksPerDay; }
            return (min, max);
        }

        private Point At(int i)
        {
            var (lo, hi, _) = LapScale(_points.Min(p => p.Lap), _points.Max(p => p.Lap));
            var (min, max) = TimeScale();
            double w = Math.Max(1, ActualWidth - Left - Right), h = Math.Max(1, ActualHeight - Top - Bottom);
            return new Point(Left + w * (_points[i].When.Ticks - min) / (max - min), Top + h * (hi - _points[i].Lap) / (hi - lo));
        }

        protected override int HitAt(Point p)
        {
            int near = -1;
            double best = 14;
            for (int i = 0; i < _points.Count; i++)
                if ((At(i) - p).Length is var d && d < best) { best = d; near = i; }
            return near;
        }

        protected override string TipOf(int i)
        {
            var p = _points[i];
            string gain = i > 0 ? $" ({StatFormat.Value(StatKind.LapDelta, p.Lap - _points[i - 1].Lap, false)})" : " (first clean lap)";
            return $"{StatFormat.LapTime(p.Lap)}{gain}\n{Dates.DateTime(p.When.ToLocalTime())}";
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_points.Count == 0 || w < Left + Right + 20) return;
            var (lo, hi, step) = LapScale(_points.Min(p => p.Lap), _points.Max(p => p.Lap));
            LapAxis(dc, lo, hi, step);

            var axis = Hairline.Pen(Ui.Res<Brush>("LineStrong"), this);
            var (min, max) = TimeScale();
            string format = (max - min) / TimeSpan.TicksPerDay > 300 ? CultureInfo.CurrentCulture.DateTimeFormat.YearMonthPattern.Replace("MMMM", "MMM") : Dates.ShortPattern;
            double plotW = w - Left - Right;
            var sample = Text(new DateTime(2026, 12, 28).ToString(format, CultureInfo.CurrentCulture));
            int ticks = Math.Clamp((int)(plotW / (sample.Width + 28)), 1, 6);
            string? previous = null;
            for (int i = 0; i <= ticks; i++)
            {
                double x = Left + plotW * i / ticks;
                string text = new DateTime((long)(min + (max - min) * i / ticks), DateTimeKind.Utc).ToLocalTime().ToString(format, CultureInfo.CurrentCulture);
                if (text == previous) continue;
                previous = text;
                var label = Text(text);
                dc.DrawLine(axis, new Point(Hairline.Snap(x, Hairline.ScaleOf(this)), h - Bottom), new Point(Hairline.Snap(x, Hairline.ScaleOf(this)), h - Bottom + 4));
                dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, Left - 20, w - label.Width), h - Bottom + 6));
            }

            var accent = Ui.Res<Brush>("Graph");
            var steps = new StreamGeometry();
            using (var g = steps.Open())
            {
                g.BeginFigure(At(0), false, false);
                for (int i = 1; i < _points.Count; i++)
                {
                    var p = At(i);
                    g.LineTo(new Point(p.X, At(i - 1).Y), true, true);
                    g.LineTo(p, true, true);
                }
            }
            steps.Freeze();
            dc.DrawGeometry(null, new Pen(accent, 2) { LineJoin = PenLineJoin.Round }, steps);
            var ring = new Pen(Ui.Res<Brush>("Surface"), 2);
            for (int i = 0; i < _points.Count; i++)
            {
                bool current = i == _points.Count - 1;
                double r = i == Hover ? 6 : current ? 5 : 3.5;
                dc.DrawEllipse(accent, current || i == Hover ? ring : null, At(i), r, r);
            }
            var last = At(_points.Count - 1);
            var best = Text(StatFormat.LapTime(_points[^1].Lap), Ui.Res<Brush>("Heading"), bold: true, size: 10);
            double bx = last.X + 10 + best.Width <= w ? last.X + 10 : last.X - 10 - best.Width;
            dc.DrawText(best, new Point(bx, Math.Max(0, last.Y - best.Height - 4)));
        }
    }

    private sealed class ConsistencyChart : HoverChart
    {
        private readonly List<SessionSpread> _sessions;

        public ConsistencyChart(List<SessionSpread> sessions) => _sessions = sessions;

        private double Slot => (ActualWidth - Left - Right) / Math.Max(1, _sessions.Count);

        protected override int HitAt(Point p)
        {
            if (p.X < Left || p.X > ActualWidth - Right || p.Y > ActualHeight - Bottom) return -1;
            return Math.Clamp((int)((p.X - Left) / Slot), 0, _sessions.Count - 1);
        }

        protected override string TipOf(int i)
        {
            var s = _sessions[i];
            return $"{Dates.DateTime(s.When.ToLocalTime())}{(s.Type.Length > 0 ? " · " + StatsSessions.TypeName(s.Type) : "")} · {SimRacing.Race.RaceFormat.Count(s.Laps, "clean lap")}\n"
                + $"Fastest {StatFormat.LapTime(s.Best)} · median {StatFormat.LapTime(s.Median)}\n"
                + $"Middle half {StatFormat.LapTime(s.Q1)}–{StatFormat.LapTime(s.Q3)} · spread {StatFormat.Value(StatKind.Seconds, s.StdDev, false)}";
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_sessions.Count == 0 || w < Left + Right + 20) return;
            var (lo, hi, step) = LapScale(_sessions.Min(s => s.Best), _sessions.Max(s => s.P90));
            var y = LapAxis(dc, lo, hi, step);
            double slot = Slot, box = Math.Clamp(slot * 0.5, 4, 22);
            var accent = Ui.Res<Brush>("Graph");
            var soft = Ui.Res<Brush>("GraphSoft");
            var whisker = new Pen(Ui.Res<Brush>("TextMuted"), 1.5);
            var edge = new Pen(accent, 1.5);
            var median = new Pen(Ui.Res<Brush>("Heading"), 2);
            var hover = Ui.Res<Brush>("Hover");
            for (int i = 0; i < _sessions.Count; i++)
            {
                var s = _sessions[i];
                double x = Left + slot * (i + 0.5);
                if (i == Hover) dc.DrawRectangle(hover, null, new Rect(Left + slot * i, Top, slot, h - Top - Bottom));
                dc.DrawLine(whisker, new Point(x, y(s.Best)), new Point(x, y(s.P90)));
                dc.DrawLine(whisker, new Point(x - box / 4, y(s.Best)), new Point(x + box / 4, y(s.Best)));
                double top = y(s.Q3), bottom = y(s.Q1);
                dc.DrawRectangle(soft, edge, new Rect(x - box / 2, top, box, Math.Max(2, bottom - top)));
                dc.DrawLine(median, new Point(x - box / 2, y(s.Median)), new Point(x + box / 2, y(s.Median)));
            }
            var days = _sessions.GroupBy(x => x.When.ToLocalTime().Date).ToDictionary(g => g.Key, g => g.Count());
            string LabelOf(SessionSpread x)
            {
                var local = x.When.ToLocalTime();
                string date = Dates.Short(local);
                return days[local.Date] > 1 && x.Type.Length > 0 ? $"{date} {StatsSessions.TypeName(x.Type)[0]}" : date;
            }
            var sample = Text("28 Dec Q");
            int every = Math.Max(1, (int)Math.Ceiling((sample.Width + 16) / slot));
            for (int i = 0; i < _sessions.Count; i += every)
            {
                var label = Text(LabelOf(_sessions[i]));
                double x = Left + slot * (i + 0.5);
                dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, Left - 20, w - label.Width), h - Bottom + 6));
            }
        }
    }

    private sealed class TrendChart : HoverChart
    {
        private new const double Left = 37;
        public const double GrowMs = 1000;
        private const double GrowSpread = 6;
        private readonly List<TrendBin> _bins;

        public static readonly DependencyProperty GrowProperty = DependencyProperty.Register(nameof(Grow), typeof(double), typeof(TrendChart),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Grow { get => (double)GetValue(GrowProperty); set => SetValue(GrowProperty, value); }

        public TrendChart(List<TrendBin> bins) => _bins = bins;

        private double Slot => (ActualWidth - Left - Right) / Math.Max(1, _bins.Count);

        protected override int HitAt(Point p)
        {
            if (_bins.Count == 0 || p.X < Left || p.X > ActualWidth - Right || p.Y > ActualHeight - Bottom) return -1;
            return Math.Clamp((int)((p.X - Left) / Slot), 0, _bins.Count - 1);
        }

        protected override string TipOf(int i)
        {
            var b = _bins[i];
            return $"{b.Tip}\n{(b.Seconds > 0 ? StatFormat.Duration(b.Seconds) : "No driving")}{(b.Sessions > 0 ? $" · {SimRacing.Race.RaceFormat.Count(b.Sessions, "session")}" : "")}";
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_bins.Count == 0 || w < Left + Right + 20) return;
            double maxHours = Math.Max(_bins.Max(b => b.Seconds) / 3600, 0.01);
            double step = new[] { 0.25, 0.5, 1, 2, 3, 5, 10, 20, 50 }.FirstOrDefault(s => maxHours / s <= 4, 100);
            double top = Math.Ceiling(maxHours / step) * step, plotH = h - Top - Bottom;
            double Y(double hours) => Top + plotH * (1 - hours / top);
            double px = Hairline.ScaleOf(this);
            var grid = Hairline.Pen(Ui.Res<Brush>("GridLine"), px);
            for (double v = 0; v <= top + step / 2; v += step)
            {
                double yy = Hairline.Snap(Y(v), px);
                dc.DrawLine(grid, new Point(Left, yy), new Point(w - Right, yy));
                var label = Text(v == 0 ? "0" : v < 1 ? $"{v * 60:0}m" : $"{v:0.#}h");
                dc.DrawText(label, new Point(Left - 8 - label.Width, yy - label.Height / 2));
            }
            double slot = Slot, bar = Math.Max(2, slot * 0.66);
            var accent = Ui.Res<Brush>("Graph");
            var hover = Ui.Res<Brush>("GraphText");
            for (int i = 0; i < _bins.Count; i++)
            {
                if (_bins[i].Seconds <= 0) continue;
                double hours = _bins[i].Seconds / 3600;
                if (Grow < 1) hours *= FrameDriver.Spring((Grow * (_bins.Count + GrowSpread) - i) / GrowSpread);
                if (hours <= 0) continue;
                double x = Left + slot * i + (slot - bar) / 2, yy = Math.Max(0, Y(hours));
                dc.DrawRectangle(i == Hover ? hover : accent, null, new Rect(x, yy, bar, Math.Max(1, h - Bottom - yy)));
            }
            dc.DrawLine(Hairline.Pen(Ui.Res<Brush>("LineStrong"), px), new Point(Left, Hairline.Snap(h - Bottom, px)), new Point(w - Right, Hairline.Snap(h - Bottom, px)));
            var sample = Text(_bins.OrderByDescending(b => b.Label.Length).First().Label);
            int every = Math.Max(1, (int)Math.Ceiling((sample.Width + 12) / slot));
            for (int i = 0; i < _bins.Count; i += every)
            {
                var label = Text(_bins[i].Label);
                double x = Left + slot * (i + 0.5);
                dc.DrawText(label, new Point(Math.Clamp(x - label.Width / 2, 0, w - label.Width), h - Bottom + 6));
            }
        }
    }
}
