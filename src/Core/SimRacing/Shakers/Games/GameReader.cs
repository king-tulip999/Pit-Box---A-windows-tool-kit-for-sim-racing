using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;

namespace Core.SimRacing.Shakers.Games;

internal interface IMemoryView
{
    bool Read(int map, long offset, byte[] into, int count);
}

internal interface IMemoryParser
{
    string[] Maps { get; }
    bool Parse(IMemoryView view, double now, out ShakerInput input);
}

internal interface IPacketParser
{
    bool Parse(ReadOnlySpan<byte> packet, double now, out ShakerInput input);
}

internal abstract class GameReader : IDisposable
{
    public const double QuietSeconds = 2;

    private readonly Action<ShakerInput> _sink;
    private readonly Thread _thread;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    protected readonly ManualResetEvent StopSignal = new(false);
    private double _lastFrame = double.NegativeInfinity;
    private bool _live;
    private string? _problem;

    protected GameReader(string name, Action<ShakerInput> sink)
    {
        _sink = sink;
        _thread = new Thread(Run) { IsBackground = true, Name = $"Shaker feed {name}", Priority = ThreadPriority.AboveNormal };
    }

    public event Action? StateChanged;

    public bool Live => _live;

    public string? Problem => _problem;

    protected double Now => _clock.Elapsed.TotalSeconds;

    public void Start() => _thread.Start();

    protected abstract void Loop();

    private void Run()
    {
        try { Loop(); }
        catch (Exception ex)
        {
            if (ex is not (IOException or SocketException or UnauthorizedAccessException or ObjectDisposedException)) App.LogError(ex);
            if (!StopSignal.WaitOne(0)) SetProblem(ex.Message);
        }
    }

    protected void Deliver(in ShakerInput input)
    {
        _lastFrame = Now;
        _sink(input);
        SetLive(true);
    }

    protected void CheckQuiet()
    {
        if (_live && Now - _lastFrame > QuietSeconds)
        {
            _sink(default);
            SetLive(false);
        }
    }

    private void SetLive(bool live)
    {
        if (live == _live) return;
        _live = live;
        StateChanged?.Invoke();
    }

    protected void SetProblem(string? problem)
    {
        if (problem == _problem) return;
        _problem = problem;
        StateChanged?.Invoke();
    }

    public virtual void Dispose()
    {
        StopSignal.Set();
        if (_thread.IsAlive && Thread.CurrentThread != _thread) _thread.Join(1000);
    }
}

internal sealed class MemoryReader : GameReader, IMemoryView
{
    public const int PollMs = 16, OpenRetryMs = 1000;
    public const double ReopenSeconds = 2;

    private readonly IMemoryParser _parser;
    private MemoryMappedViewAccessor?[] _views = Array.Empty<MemoryMappedViewAccessor?>();

    public MemoryReader(string name, IMemoryParser parser, Action<ShakerInput> sink) : base(name, sink) => _parser = parser;

    public bool Read(int map, long offset, byte[] into, int count)
    {
        var view = _views[map];
        if (view is null || offset < 0 || offset + count > view.Capacity) return false;
        return view.ReadArray(offset, into, 0, count) == count;
    }

    protected override void Loop()
    {
        while (!StopSignal.WaitOne(0))
        {
            var files = new MemoryMappedFile?[_parser.Maps.Length];
            var views = new MemoryMappedViewAccessor?[_parser.Maps.Length];
            try
            {
                if (!Open(files, views))
                {
                    if (StopSignal.WaitOne(OpenRetryMs)) return;
                    continue;
                }
                _views = views;
                double good = Now;
                while (!StopSignal.WaitOne(PollMs))
                {
                    if (_parser.Parse(this, Now, out var input))
                    {
                        good = Now;
                        Deliver(input);
                        continue;
                    }
                    CheckQuiet();
                    if (Now - good > ReopenSeconds) break;
                }
            }
            finally
            {
                _views = Array.Empty<MemoryMappedViewAccessor?>();
                foreach (var v in views) v?.Dispose();
                foreach (var f in files) f?.Dispose();
            }
        }
    }

    private bool Open(MemoryMappedFile?[] files, MemoryMappedViewAccessor?[] views)
    {
        try
        {
            for (int i = 0; i < files.Length; i++)
            {
                files[i] = OpenAny(_parser.Maps[i].Split('|'));
                if (files[i] is null) return false;
                views[i] = files[i]!.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            CheckQuiet();
        }
    }

    private static MemoryMappedFile? OpenAny(string[] names)
    {
        foreach (string name in names)
        {
            try { return MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read); }
            catch (FileNotFoundException) { }
        }
        return null;
    }
}

internal sealed class PacketReader : GameReader
{
    public const int ReceiveTimeoutMs = 500;

    private readonly IPacketParser _parser;
    private readonly int _port;
    private Socket? _socket;

    public PacketReader(string name, int port, IPacketParser parser, Action<ShakerInput> sink) : base(name, sink)
    {
        _port = port;
        _parser = parser;
    }

    protected override void Loop()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ReceiveTimeout = ReceiveTimeoutMs };
        _socket = socket;
        try
        {
            try { socket.Bind(new IPEndPoint(IPAddress.Loopback, _port)); }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                SetProblem($"Port {_port} is used by another program");
                return;
            }
            SetProblem(null);
            var buffer = new byte[2048];
            while (!StopSignal.WaitOne(0))
            {
                int n;
                try { n = socket.Receive(buffer); }
                catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.ConnectionReset or SocketError.MessageSize)
                {
                    CheckQuiet();
                    continue;
                }
                if (_parser.Parse(buffer.AsSpan(0, n), Now, out var input)) Deliver(input);
                else CheckQuiet();
            }
        }
        finally
        {
            socket.Dispose();
        }
    }

    public override void Dispose()
    {
        StopSignal.Set();
        _socket?.Dispose();
        base.Dispose();
    }
}
