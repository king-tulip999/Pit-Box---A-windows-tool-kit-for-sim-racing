using System.IO;
using PitBox.Audio;
using PitBox.Config;
using PitBox.Processes;
using PitBox.Services;
using PitBox.SimRacing.Shakers.Games;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Shakers;

internal enum ShakerSource { None, Effects, GameAudio }

internal sealed class ShakerHost : IDisposable
{
    private const string Consumer = "Bass shakers";
    private const string IracingFeed = "iracing";
    private static readonly TimeSpan TestTail = TimeSpan.FromSeconds(ShakerMixer.TestSeconds + 0.5);

    private readonly AppServices _app;
    private readonly ShakerFeed _feed = new();
    private readonly object _gate = new();
    private readonly object _feedGate = new();
    private readonly SynchronizationContext _ui;
    private readonly Dictionary<string, GameReader> _readers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenLive = new(StringComparer.OrdinalIgnoreCase);
    private volatile string? _feeding;
    private ShakerLevels _levels;
    private AudioRender? _render;
    private AudioLoopback? _loopback;
    private ShakerMixer? _mixer;
    private string? _renderKey;
    private bool _active, _telemetryHeld, _watching;
    private string _watchKey = "";
    private DateTime _testUntil;
    private int _pendingTest = -1;
    private System.Windows.Threading.DispatcherTimer? _testTimer;

    public ShakerHost(AppServices app)
    {
        _app = app;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public event Action? Changed;

    public string? Problem { get; private set; }

    public bool Playing => _render is not null;

    public ShakerSource Source { get; private set; }

    public string? Game { get; private set; }

    public ShakerGame? WaitingFor { get; private set; }

    private ShakerSettings S => _app.Config.SimRacing.Shakers;

    public void SetActive(bool on)
    {
        if (_app.Demo) on = false;
        if (on == _active) return;
        _active = on;
        if (!on)
        {
            StopRender();
            _testTimer?.Stop();
            _seenLive.Clear();
        }
        SyncInputs();
        Sync();
    }

    public void Test(ShakerEffect effect)
    {
        if (!_active || S.DeviceId is null) return;
        _testUntil = DateTime.UtcNow + TestTail;
        if (_mixer is { } mixer) mixer.Test(effect);
        else Interlocked.Exchange(ref _pendingTest, (int)effect);
        Sync();
        if (_testTimer is null)
        {
            _testTimer = new System.Windows.Threading.DispatcherTimer { Interval = TestTail };
            _testTimer.Tick += (_, _) =>
            {
                _testTimer!.Stop();
                Sync();
            };
        }
        _testTimer.Stop();
        _testTimer.Start();
    }

    public void SettingsChanged()
    {
        SyncInputs();
        Sync();
    }

    private void SyncInputs()
    {
        bool telemetry = _active && S.Mode == ShakerMode.Effects && S.IracingOn;
        if (telemetry != _telemetryHeld)
        {
            _telemetryHeld = telemetry;
            if (telemetry)
            {
                _app.Telemetry.AddObserver(OnFrame);
                _app.Telemetry.StatusChanged += OnStatus;
                _app.Telemetry.Acquire(Consumer);
            }
            else
            {
                _app.Telemetry.StatusChanged -= OnStatus;
                _app.Telemetry.RemoveObserver(OnFrame);
                _app.Telemetry.Release(Consumer);
            }
        }

        var names = WatchedNames();
        string key = string.Join("|", names);
        bool watch = _active && names.Count > 0;
        if (watch && (!_watching || key != _watchKey))
        {
            if (!_watching)
            {
                _app.Watcher.Started += OnApp;
                _app.Watcher.Stopped += OnApp;
            }
            _watching = true;
            _watchKey = key;
            _app.Watcher.Acquire(Consumer, names);
        }
        else if (!watch && _watching)
        {
            _watching = false;
            _watchKey = "";
            _app.Watcher.Started -= OnApp;
            _app.Watcher.Stopped -= OnApp;
            _app.Watcher.Release(Consumer);
        }
    }

    private List<string> WatchedNames()
    {
        var names = new List<string>();
        if (S.OtherGames) names.AddRange(S.Games);
        if (S.Mode != ShakerMode.Off)
            foreach (var game in EnabledGames()) names.AddRange(game.Processes);
        if (S.IracingOn && (names.Count > 0 || S.Mode == ShakerMode.GameAudio)) names.Add(TelemetryService.SimProcess);
        return names;
    }

    private IEnumerable<ShakerGame> EnabledGames() => ShakerGames.All.Where(g => S.GameOn(g.Id));

    private string? RunningProcess(ShakerGame game) => _watching ? game.Processes.FirstOrDefault(p => _app.Watcher.IsRunning(p)) : null;

    private void OnApp(string _) => Sync();

    private void OnStatus(SimStatus status) => _ui.Post(_ => Sync(), null);

    private void OnFrame(TelemetryFrame f)
    {
        if (_feeding != IracingFeed) return;
        ShakerLevels levels;
        lock (_feedGate) levels = _feed.Update(f);
        lock (_gate) _levels = levels;
    }

    private void SyncReaders()
    {
        var wanted = new Dictionary<string, ShakerGame>(StringComparer.OrdinalIgnoreCase);
        if (_active && S.Mode == ShakerMode.Effects)
            foreach (var game in EnabledGames())
                if (RunningProcess(game) is not null) wanted[ReaderKey(game)] = game;
        foreach (var game in ShakerGames.All)
            if (RunningProcess(game) is null) _seenLive.Remove(game.Id);
        foreach (string key in _readers.Keys.Where(k => !wanted.ContainsKey(k)).ToList())
        {
            _readers[key].Dispose();
            _readers.Remove(key);
        }
        foreach (var (key, game) in wanted)
        {
            if (_readers.ContainsKey(key)) continue;
            string id = game.Id;
            var reader = game.Open(S.Port(id, game.Port), input =>
            {
                if (_feeding != id) return;
                ShakerLevels levels;
                lock (_feedGate) levels = _feed.Update(input);
                lock (_gate) _levels = levels;
            });
            reader.StateChanged += () => _ui.Post(_ => Sync(), null);
            _readers[key] = reader;
            reader.Start();
        }
    }

    private string ReaderKey(ShakerGame game) => $"{game.Id}|{S.Port(game.Id, game.Port)}";

    private GameReader? ReaderFor(ShakerGame game) => _readers.GetValueOrDefault(ReaderKey(game));

    private bool IracingConnected => _app.Telemetry.Status == SimStatus.Connected && !_app.Telemetry.Simulated;

    private bool IracingRunning =>
        _telemetryHeld && IracingConnected || _watching && _app.Watcher.IsRunning(TelemetryService.SimProcess);

    private sealed record Choice(ShakerSource Source, string? Game = null, string? Process = null, string? Feed = null, ShakerGame? Waiting = null, string? Problem = null);

    private Choice Pick()
    {
        if (!_active || S.DeviceId is null) return new(ShakerSource.None);
        if (DateTime.UtcNow < _testUntil) return new(ShakerSource.Effects);
        if (S.IracingOn && IracingRunning)
            return S.Mode switch
            {
                ShakerMode.Effects when IracingConnected => new(ShakerSource.Effects, "iRacing", Feed: IracingFeed),
                ShakerMode.GameAudio => new(ShakerSource.GameAudio, "iRacing", TelemetryService.SimProcess),
                _ => new(ShakerSource.None),
            };
        ShakerGame? waiting = null;
        string? problem = null;
        if (S.Mode != ShakerMode.Off)
            foreach (var game in EnabledGames())
            {
                if (RunningProcess(game) is not { } running) continue;
                string process = AppWatcher.Normalize(running);
                if (S.Mode == ShakerMode.GameAudio) return new(ShakerSource.GameAudio, game.Name, process);
                var reader = ReaderFor(game);
                if (reader?.Live == true) _seenLive.Add(game.Id);
                if (_seenLive.Contains(game.Id)) return new(ShakerSource.Effects, game.Name, Feed: game.Id);
                waiting ??= game;
                problem ??= reader?.Problem;
                if (S.OtherGames && S.Games.Any(g => AppWatcher.Normalize(g) == process))
                    return new(ShakerSource.GameAudio, game.Name, process, Waiting: game);
            }
        if (S.OtherGames && _watching && S.Games.FirstOrDefault(g => _app.Watcher.IsRunning(g)) is { } listed)
            return new(ShakerSource.GameAudio, Path.GetFileNameWithoutExtension(listed), AppWatcher.Normalize(listed), Waiting: waiting, Problem: problem);
        return new(ShakerSource.None, Waiting: waiting, Problem: problem);
    }

    private void Sync()
    {
        SyncReaders();
        var choice = Pick();
        Source = choice.Source;
        Game = choice.Game;
        WaitingFor = choice.Waiting;
        if (choice.Feed != _feeding)
        {
            _feeding = choice.Feed;
            lock (_feedGate) _feed.Reset();
            lock (_gate) _levels = default;
        }
        string key = $"{choice.Source}|{S.DeviceId}|{S.Channel}|{choice.Process}";
        if (choice.Source == ShakerSource.None)
        {
            StopRender();
            Problem = choice.Problem;
        }
        else if (_render is null || key != _renderKey)
        {
            StopRender();
            _renderKey = key;
            Problem = null;
            Start(choice.Source, choice.Process);
        }
        Changed?.Invoke();
    }

    private void Start(ShakerSource source, string? process)
    {
        int[] channels = S.Channel switch { ShakerChannel.Left => new[] { 0 }, ShakerChannel.Right => new[] { 1 }, _ => new[] { 0, 1 } };
        int generation = ++_renderGeneration;
        AudioRender render;
        if (source == ShakerSource.GameAudio)
        {
            if (ProcessId(process!) is not { } processId)
            {
                Problem = "The game isn't running";
                return;
            }
            var bridge = new GameAudioBridge();
            var loopback = new AudioLoopback(processId, (mono, rate) => bridge.Write(mono, rate, S.GameAudioCutoffHz));
            loopback.Failed += message => Fail(generation, message);
            _loopback = loopback;
            render = new AudioRender(S.DeviceId!, channels, rate => span => bridge.Render(span, rate, S));
            loopback.Start();
        }
        else
        {
            render = new AudioRender(S.DeviceId!, channels, rate =>
            {
                var mixer = _mixer = new ShakerMixer(rate);
                if (Interlocked.Exchange(ref _pendingTest, -1) is >= 0 and var pending) mixer.Test((ShakerEffect)pending);
                return span =>
                {
                    ShakerLevels levels;
                    lock (_gate) levels = _levels;
                    mixer.Render(span, levels, S);
                };
            });
        }
        render.Failed += message => Fail(generation, message);
        _render = render;
        render.Start();
    }

    private int _renderGeneration;

    private void Fail(int generation, string message) => _ui.Post(_ =>
    {
        if (generation != _renderGeneration || _render is null) return;
        Problem = message;
        StopRender();
        Changed?.Invoke();
    }, null);

    private static int? ProcessId(string name)
    {
        var found = System.Diagnostics.Process.GetProcessesByName(name);
        try { return found.Length > 0 ? found.Min(p => p.Id) : null; }
        finally
        {
            foreach (var p in found) p.Dispose();
        }
    }

    private void StopRender()
    {
        _loopback?.Dispose();
        _loopback = null;
        _render?.Dispose();
        _render = null;
        _mixer = null;
        _renderKey = null;
    }

    public void Dispose()
    {
        SetActive(false);
        foreach (var reader in _readers.Values) reader.Dispose();
        _readers.Clear();
    }
}
