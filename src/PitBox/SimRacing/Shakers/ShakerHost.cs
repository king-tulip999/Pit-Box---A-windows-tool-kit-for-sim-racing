using System.IO;
using PitBox.Audio;
using PitBox.Config;
using PitBox.Processes;
using PitBox.Services;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing.Shakers;

internal enum ShakerSource { None, Effects, GameAudio }

internal sealed class ShakerHost : IDisposable
{
    private const string Consumer = "Bass shakers";
    private static readonly TimeSpan TestTail = TimeSpan.FromSeconds(ShakerMixer.TestSeconds + 0.5);

    private readonly AppServices _app;
    private readonly ShakerFeed _feed = new();
    private readonly object _gate = new();
    private readonly SynchronizationContext _ui;
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
        bool telemetry = _active && S.InIracing == ShakerIracing.Effects;
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
        if (names.Count > 0 || S.InIracing == ShakerIracing.GameAudio) names.Add(TelemetryService.SimProcess);
        return names;
    }

    private void OnApp(string _) => Sync();

    private void OnStatus(SimStatus status) => _ui.Post(_ => Sync(), null);

    private void OnFrame(TelemetryFrame f)
    {
        var levels = _feed.Update(f);
        lock (_gate) _levels = levels;
    }

    private bool IracingRunning =>
        _telemetryHeld && _app.Telemetry.Status == SimStatus.Connected || _watching && _app.Watcher.IsRunning(TelemetryService.SimProcess);

    private (ShakerSource Source, string? Game, string? Process) Pick()
    {
        if (!_active || S.DeviceId is null) return (ShakerSource.None, null, null);
        if (DateTime.UtcNow < _testUntil) return (ShakerSource.Effects, null, null);
        if (IracingRunning)
            return S.InIracing switch
            {
                ShakerIracing.Effects when _app.Telemetry.Status == SimStatus.Connected => (ShakerSource.Effects, null, null),
                ShakerIracing.GameAudio => (ShakerSource.GameAudio, "iRacing", TelemetryService.SimProcess),
                _ => (ShakerSource.None, null, null),
            };
        if (S.OtherGames && _watching && S.Games.FirstOrDefault(g => _app.Watcher.IsRunning(g)) is { } game)
            return (ShakerSource.GameAudio, Path.GetFileNameWithoutExtension(game), AppWatcher.Normalize(game));
        return (ShakerSource.None, null, null);
    }

    private void Sync()
    {
        var (source, game, process) = Pick();
        Source = source;
        Game = game;
        string key = $"{source}|{S.DeviceId}|{S.Channel}|{process}";
        if (source == ShakerSource.None)
        {
            StopRender();
            Problem = null;
        }
        else if (_render is null || key != _renderKey)
        {
            StopRender();
            _renderKey = key;
            Problem = null;
            Start(source, process);
        }
        Changed?.Invoke();
    }

    private void Start(ShakerSource source, string? process)
    {
        int[] channels = S.Channel switch { ShakerChannel.Left => new[] { 0 }, ShakerChannel.Right => new[] { 1 }, _ => new[] { 0, 1 } };
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
            loopback.Failed += Fail;
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
        render.Failed += Fail;
        _render = render;
        render.Start();
    }

    private void Fail(string message) => _ui.Post(_ =>
    {
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

    public void Dispose() => SetActive(false);
}
