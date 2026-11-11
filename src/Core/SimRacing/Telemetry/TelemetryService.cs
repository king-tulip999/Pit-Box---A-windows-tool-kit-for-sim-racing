using System.Diagnostics;
using Core.Processes;

namespace Core.SimRacing.Telemetry;

internal sealed record TelemetryDiagnostics(
    SimStatus Status, bool Simulated, double FramesPerSecond, int TickRate, int Variables, int SessionUpdates,
    int Connects, string? LastError, IReadOnlyList<string> MissingCore, DateTime? LastFrameUtc);

internal sealed class TelemetryService : IDisposable
{
    public const string SimProcess = "iracingsim64dx11";

    private static readonly string[] CoreVars =
    {
        "SessionTime", "SessionNum", "SessionState", "SessionFlags", "PlayerCarIdx", "Speed", "RPM", "Gear", "FuelLevel",
        "LapDistPct", "LapCurrentLapTime", "CarIdxLapDistPct", "CarIdxPosition", "CarIdxClassPosition", "CarIdxEstTime", "CarLeftRight",
    };

    private readonly AppWatcher? _watcher;
    private readonly SynchronizationContext _ui;
    private readonly HashSet<string> _consumers = new(StringComparer.Ordinal);
    private readonly TripleBuffer<TelemetryFrame> _frames = new(() => new TelemetryFrame());
    private readonly AutoResetEvent _wake = new(false);
    private readonly DemoTelemetry _demo = new();
    private Thread? _thread;
    private volatile bool _simRunning, _simulated;
    private RunToken? _run;
    private Thread? _stopping;

    private sealed class RunToken
    {
        public volatile bool Stop;
    }
    private volatile SimStatus _status = SimStatus.NotRunning;
    private SimStatus _raisedStatus = SimStatus.NotRunning;
    private long _sequence;

    private volatile int _tickRate, _varCount, _sessionUpdates, _connects;
    private volatile string? _lastError;
    private string[] _missing = Array.Empty<string>();
    private double _fps;
    private long _lastFrameTicks;

    public TelemetryService(AppWatcher? watcher)
    {
        _watcher = watcher;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
    }

    public event Action<SimStatus>? StatusChanged;

    private volatile Action<TelemetryFrame>[] _observers = Array.Empty<Action<TelemetryFrame>>();
    private readonly object _observerLock = new();

    public void AddObserver(Action<TelemetryFrame> observe)
    {
        lock (_observerLock)
            if (Array.IndexOf(_observers, observe) < 0) _observers = _observers.Append(observe).ToArray();
    }

    public void RemoveObserver(Action<TelemetryFrame> observe)
    {
        lock (_observerLock) _observers = _observers.Where(o => o != observe).ToArray();
    }

    private const int ObserverGivesUpAfter = 600;
    private readonly Dictionary<Action<TelemetryFrame>, int> _observerFailures = new();

    private void Observe(TelemetryFrame f)
    {
        foreach (var observe in _observers)
        {
            try
            {
                observe(f);
                if (_observerFailures.Count > 0) _observerFailures.Remove(observe);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                int failures = _observerFailures.GetValueOrDefault(observe) + 1;
                _observerFailures[observe] = failures;
                if (failures == 1) App.LogError(ex);
                if (failures < ObserverGivesUpAfter) continue;
                _observerFailures.Remove(observe);
                RemoveObserver(observe);
                App.LogError(new InvalidOperationException($"A telemetry reader failed on {ObserverGivesUpAfter} frames in a row and was stopped", ex));
            }
        }
    }

    public SimStatus Status => _status;

    public bool Simulated
    {
        get => _simulated;
        set
        {
            if (_simulated == value) return;
            _simulated = value;
            _demo.Restart();
            _wake.Set();
        }
    }

    public bool Running => _thread is not null;

    public void Acquire(string consumer)
    {
        if (!_consumers.Add(consumer) || _consumers.Count > 1) return;
        if (_watcher is not null)
        {
            _watcher.Started += OnProcessStarted;
            _watcher.Stopped += OnProcessStopped;
            _watcher.Acquire("iRacing Overlay telemetry", new[] { SimProcess });
            _simRunning = _watcher.IsRunning(SimProcess);
        }
        if (_stopping is { IsAlive: true } stopping) stopping.Join(2000);
        _stopping = null;
        var token = _run = new RunToken();
        _thread = new Thread(() => Run(token)) { IsBackground = true, Name = "Telemetry", Priority = ThreadPriority.Normal };
        _thread.Start();
    }

    public void Release(string consumer)
    {
        if (!_consumers.Remove(consumer) || _consumers.Count > 0) return;
        if (_watcher is not null)
        {
            _watcher.Started -= OnProcessStarted;
            _watcher.Stopped -= OnProcessStopped;
            _watcher.Release("iRacing Overlay telemetry");
        }
        if (_run is { } token) token.Stop = true;
        _run = null;
        _wake.Set();
        bool exited = _thread?.Join(2000) ?? true;
        _stopping = exited ? null : _thread;
        _thread = null;
        if (exited)
        {
            var f = _frames.Back;
            f.Reset();
            f.Session = SessionInfo.Empty;
            f.Sequence = ++_sequence;
            _frames.Publish();
        }
        SetStatus(SimStatus.NotRunning);
    }

    public TelemetryFrame Take()
    {
        _frames.TryTake();
        return _frames.Front;
    }

    public TelemetryDiagnostics Diagnostics() => new(_status, _simulated, Volatile.Read(ref _fps), _tickRate, _varCount, _sessionUpdates, _connects,
        _lastError, Volatile.Read(ref _missing), Interlocked.Read(ref _lastFrameTicks) is > 0 and var t ? new DateTime(t, DateTimeKind.Utc) : null);

    public event Action? SimStarted;

    private void OnProcessStarted(string name)
    {
        if (name != SimProcess) return;
        _simRunning = true;
        _wake.Set();
        SimStarted?.Invoke();
    }

    private void OnProcessStopped(string name)
    {
        if (name != SimProcess) return;
        _simRunning = false;
        _wake.Set();
    }

    private void Run(RunToken token)
    {
        bool logged = false;
        while (!Read(token, ref logged) && !token.Stop)
        {
            try { PublishStatus(SimStatus.Waiting); } catch (Exception) { }
            Thread.Sleep(1000);
        }
    }

    private bool Read(RunToken token, ref bool logged)
    {
        IrsdkMemory? mem = null;
        var decoder = new FrameDecoder();
        var session = SessionInfo.Empty;
        int sessionVersion = 0, lastSessionUpdate = -1, lastTick = int.MinValue;
        long lastData = 0;
        int framesThisSecond = 0;
        long second = Stopwatch.GetTimestamp();
        try
        {
            while (!token.Stop)
            {
                if (_simulated)
                {
                    if (mem is not null) { mem.Dispose(); mem = null; lastSessionUpdate = -1; }
                    var f = _frames.Back;
                    _demo.Fill(f);
                    f.Sequence = ++_sequence;
                    f.ReadAt = Stopwatch.GetTimestamp();
                    _frames.Publish();
                    Count(ref framesThisSecond, ref second);
                    SetStatus(SimStatus.Connected);
                    _wake.WaitOne(1000 / 30);
                    continue;
                }

                if (mem is null)
                {
                    if (!_simRunning && _watcher is not null)
                    {
                        PublishStatus(SimStatus.NotRunning);
                        _wake.WaitOne();
                        continue;
                    }
                    mem = IrsdkMemory.TryOpen();
                    if (mem is null)
                    {
                        PublishStatus(_simRunning ? SimStatus.Waiting : SimStatus.NotRunning);
                        _wake.WaitOne(1000);
                        continue;
                    }
                    _connects++;
                    session = SessionInfo.Empty;
                    sessionVersion++;
                    lastSessionUpdate = -1;
                    lastTick = int.MinValue;
                    lastData = Stopwatch.GetTimestamp();
                }

                bool signalled = mem.WaitForData(250);
                if (token.Stop || _simulated) continue;
                if (!mem.Connected || !signalled && Stopwatch.GetElapsedTime(lastData).TotalSeconds > 2)
                {
                    PublishStatus(SimStatus.Waiting);
                    if (_watcher is not null && !_simRunning)
                    {
                        mem.Dispose();
                        mem = null;
                    }
                    continue;
                }
                if (!signalled) continue;

                if (mem.VarsIfChanged() is { } vars)
                {
                    decoder.Bind(vars);
                    _varCount = vars.Count;
                    Volatile.Write(ref _missing, CoreVars.Where(v => !vars.ContainsKey(v)).ToArray());
                }
                if (mem.SessionInfoUpdate is var update && update != lastSessionUpdate)
                {
                    try
                    {
                        if (mem.SessionInfo() is { } yaml)
                        {
                            lastSessionUpdate = update;
                            session = SessionInfo.Parse(yaml);
                            sessionVersion++;
                            _sessionUpdates = sessionVersion;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastSessionUpdate = update;
                        _lastError = "Session information couldn't be read: " + ex.Message;
                    }
                }
                int tick = mem.CopyLatest();
                if (tick < 0 || tick == lastTick)
                {
                    if (Stopwatch.GetElapsedTime(lastData).TotalSeconds > 2) PublishStatus(SimStatus.Waiting);
                    continue;
                }
                lastTick = tick;
                lastData = Stopwatch.GetTimestamp();
                _tickRate = mem.TickRate;
                if (token.Stop) break;

                var frame = _frames.Back;
                frame.Session = session;
                frame.SessionVersion = sessionVersion;
                decoder.Decode(mem.Data, frame);
                frame.Status = SimStatus.Connected;
                frame.Tick = tick;
                frame.Sequence = ++_sequence;
                frame.ReadAt = lastData;
                _frames.Publish();
                Observe(frame);
                Count(ref framesThisSecond, ref second);
                SetStatus(SimStatus.Connected);
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            if (!logged) App.LogError(ex);
            logged = true;
            return false;
        }
        finally
        {
            mem?.Dispose();
        }
        return true;
    }

    private void Count(ref int frames, ref long second)
    {
        frames++;
        Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
        double elapsed = Stopwatch.GetElapsedTime(second).TotalSeconds;
        if (elapsed < 1) return;
        Volatile.Write(ref _fps, frames / elapsed);
        frames = 0;
        second = Stopwatch.GetTimestamp();
    }

    private void PublishStatus(SimStatus status)
    {
        if (_status == status) return;
        var f = _frames.Back;
        f.Reset();
        f.Session = SessionInfo.Empty;
        f.Status = status;
        f.Sequence = ++_sequence;
        f.ReadAt = Stopwatch.GetTimestamp();
        _frames.Publish();
        Observe(f);
        Volatile.Write(ref _fps, 0);
        SetStatus(status);
    }

    private void SetStatus(SimStatus status)
    {
        if (_status == status) return;
        _status = status;
        _ui.Post(_ =>
        {
            if (_raisedStatus == _status) return;
            _raisedStatus = _status;
            StatusChanged?.Invoke(_status);
        }, null);
    }

    public void Dispose()
    {
        foreach (var c in _consumers.ToList()) Release(c);
        _wake.Dispose();
    }
}
