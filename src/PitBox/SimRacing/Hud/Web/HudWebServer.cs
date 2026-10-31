using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PitBox.SimRacing.Hud.Web;

internal sealed record HudWebOverlay(string Kind, string Name, double W, double H);

internal sealed class HudWebClient
{
    public const int MaxPixels = 4096;

    private byte[]? _frame;
    private string? _message;
    private readonly SemaphoreSlim _wake = new(0, 1);

    public HudWebClient(string kind) => Kind = kind;

    public string Kind { get; }
    public volatile int Width, Height;

    public void Offer(byte[] frame)
    {
        Volatile.Write(ref _frame, frame);
        Wake();
    }

    public void Say(string json)
    {
        Volatile.Write(ref _message, json);
        Wake();
    }

    private void Wake()
    {
        try { if (_wake.CurrentCount == 0) _wake.Release(); }
        catch (SemaphoreFullException) { }
    }

    public async Task<(string? Message, byte[]? Frame)> NextAsync(CancellationToken token)
    {
        await _wake.WaitAsync(token).ConfigureAwait(false);
        return (Interlocked.Exchange(ref _message, null), Interlocked.Exchange(ref _frame, null));
    }
}

internal sealed class HudWebServer : IDisposable
{
    private const string SocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
    private const int MaxRequest = 16 * 1024;

    private readonly object _gate = new();
    private readonly List<HudWebClient> _clients = new();
    private readonly IReadOnlyList<HudWebOverlay> _overlays;
    private readonly HashSet<string> _kinds;
    private TcpListener? _listener;
    private CancellationTokenSource? _stop;
    private volatile string _themeCss = "";
    private HudWebClient[] _snapshot = Array.Empty<HudWebClient>();

    public HudWebServer(IReadOnlyList<HudWebOverlay> overlays)
    {
        _overlays = overlays;
        _kinds = overlays.Select(o => o.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public int Port { get; private set; }

    public bool Running => _listener is not null;

    public event Action? ClientsChanged;

    public IReadOnlyList<HudWebClient> Clients => Volatile.Read(ref _snapshot);

    public static string Url(int port, string? kind = null) => $"http://127.0.0.1:{port}/" + (kind is null ? "" : "overlay/" + kind);

    public string ThemeCss
    {
        set
        {
            if (_themeCss == value) return;
            _themeCss = value;
            foreach (var c in Clients) c.Say("{\"type\":\"theme\"}");
        }
    }

    public string? Start(int port)
    {
        if (Running && Port == port) return null;
        Stop();
        var listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            listener.ExclusiveAddressUse = true;
            listener.Start();
        }
        catch (SocketException ex)
        {
            listener.Stop();
            return ex.SocketErrorCode == SocketError.AddressAlreadyInUse ? $"Port {port} is already in use by another program." : ex.Message;
        }
        _listener = listener;
        Port = port;
        _stop = new CancellationTokenSource();
        _ = AcceptLoop(listener, _stop.Token);
        return null;
    }

    public void Stop()
    {
        if (_listener is null) return;
        _stop?.Cancel();
        _listener.Stop();
        _listener = null;
        _stop?.Dispose();
        _stop = null;
        lock (_gate)
        {
            _clients.Clear();
            _snapshot = Array.Empty<HudWebClient>();
        }
        ClientsChanged?.Invoke();
    }

    public void Dispose() => Stop();

    private async Task AcceptLoop(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient tcp;
            try { tcp = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false); }
            catch (Exception) when (token.IsCancellationRequested) { return; }
            catch (SocketException) { continue; }
            catch (ObjectDisposedException) { return; }
            _ = Serve(tcp, token);
        }
    }

    private async Task Serve(TcpClient tcp, CancellationToken token)
    {
        using (tcp)
        {
            try
            {
                tcp.NoDelay = true;
                var stream = tcp.GetStream();
                var request = await ReadRequest(stream, token).ConfigureAwait(false);
                if (request is null) return;
                var (path, headers) = request.Value;
                if (!headers.TryGetValue("host", out var host) || !LocalHost(host, Port))
                {
                    await Respond(stream, "403 Forbidden", "text/plain", "Forbidden"u8.ToArray(), token).ConfigureAwait(false);
                    return;
                }
                if (path.StartsWith("/ws/", StringComparison.Ordinal) && headers.TryGetValue("upgrade", out var up) && up.Equals("websocket", StringComparison.OrdinalIgnoreCase)
                    && headers.TryGetValue("sec-websocket-key", out var key) && _kinds.Contains(path[4..]))
                {
                    await Socket(stream, _overlays.First(o => o.Kind.Equals(path[4..], StringComparison.OrdinalIgnoreCase)).Kind, key, token).ConfigureAwait(false);
                    return;
                }
                var (status, type, body) = Page(path);
                await Respond(stream, status, type, body, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or WebSocketException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    internal static bool LocalHost(string host, int port) =>
        host.Equals($"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase) || host.Equals($"localhost:{port}", StringComparison.OrdinalIgnoreCase);

    private static async Task<(string Path, Dictionary<string, string> Headers)?> ReadRequest(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[MaxRequest];
        int length = 0;
        while (length < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
            if (n == 0) return null;
            length += n;
            if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0) break;
        }
        var lines = Encoding.ASCII.GetString(buffer, 0, length).Split("\r\n");
        var first = lines[0].Split(' ');
        if (first.Length < 2 || first[0] != "GET") return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            int colon = line.IndexOf(':');
            if (colon > 0) headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        string path = first[1];
        int query = path.IndexOf('?');
        return (query >= 0 ? path[..query] : path, headers);
    }

    private static async Task Respond(NetworkStream stream, string status, string type, byte[] body, CancellationToken token)
    {
        string head = $"HTTP/1.1 {status}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), token).ConfigureAwait(false);
        await stream.WriteAsync(body, token).ConfigureAwait(false);
    }

    private (string Status, string Type, byte[] Body) Page(string path)
    {
        const string Html = "text/html; charset=utf-8";
        switch (path)
        {
            case "/":
                var list = JsonSerializer.Serialize(_overlays.Select(o => new { kind = o.Kind, name = o.Name, w = o.W, h = o.H, url = Url(Port, o.Kind) }));
                return ("200 OK", Html, Encoding.UTF8.GetBytes(Asset("index.html").Replace("{{overlays}}", list, StringComparison.Ordinal)));
            case "/hud.js":
                return ("200 OK", "text/javascript; charset=utf-8", Encoding.UTF8.GetBytes(Asset("hud.js")));
            case "/hud.css":
                return ("200 OK", "text/css; charset=utf-8", Encoding.UTF8.GetBytes(Asset("hud.css")));
            case "/theme.css":
                return ("200 OK", "text/css; charset=utf-8", Encoding.UTF8.GetBytes(_themeCss));
        }
        if (path.StartsWith("/overlay/", StringComparison.Ordinal) && _overlays.FirstOrDefault(o => o.Kind.Equals(path[9..], StringComparison.OrdinalIgnoreCase)) is { } overlay)
            return ("200 OK", Html, Encoding.UTF8.GetBytes(Asset("overlay.html").Replace("{{kind}}", overlay.Kind, StringComparison.Ordinal).Replace("{{name}}", WebUtility.HtmlEncode(overlay.Name), StringComparison.Ordinal)));
        return ("404 Not Found", "text/plain", "Not found"u8.ToArray());
    }

    private static readonly Dictionary<string, string> Assets = new();

    internal static string Asset(string name)
    {
        lock (Assets)
        {
            if (Assets.TryGetValue(name, out var text)) return text;
            using var s = typeof(HudWebServer).Assembly.GetManifestResourceStream("web/" + name) ?? throw new FileNotFoundException(name);
            using var reader = new StreamReader(s, Encoding.UTF8);
            return Assets[name] = reader.ReadToEnd();
        }
    }

    private async Task Socket(NetworkStream stream, string kind, string key, CancellationToken token)
    {
        string accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + SocketGuid)));
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n"), token).ConfigureAwait(false);
        using var socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(20) });
        var client = new HudWebClient(kind);
        using var done = CancellationTokenSource.CreateLinkedTokenSource(token);
        Add(client);
        try
        {
            var sending = Send(socket, client, done.Token);
            await Receive(socket, client, done.Token).ConfigureAwait(false);
            done.Cancel();
            try { await sending.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
        finally
        {
            Remove(client);
        }
    }

    private async Task Receive(WebSocket socket, HudWebClient client, CancellationToken token)
    {
        var buffer = new byte[1024];
        while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close) return;
            if (result.MessageType != WebSocketMessageType.Text || !result.EndOfMessage) continue;
            try
            {
                using var doc = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
                if (doc.RootElement.TryGetProperty("w", out var w) && doc.RootElement.TryGetProperty("h", out var h) && w.TryGetInt32(out int pw) && h.TryGetInt32(out int ph))
                {
                    client.Width = Math.Clamp(pw, 0, HudWebClient.MaxPixels);
                    client.Height = Math.Clamp(ph, 0, HudWebClient.MaxPixels);
                    ClientsChanged?.Invoke();
                }
            }
            catch (JsonException) { }
        }
    }

    private static async Task Send(WebSocket socket, HudWebClient client, CancellationToken token)
    {
        while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
        {
            var (message, frame) = await client.NextAsync(token).ConfigureAwait(false);
            if (message is not null) await socket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            if (frame is not null) await socket.SendAsync(frame, WebSocketMessageType.Binary, true, token).ConfigureAwait(false);
        }
    }

    private void Add(HudWebClient client)
    {
        lock (_gate)
        {
            _clients.Add(client);
            _snapshot = _clients.ToArray();
        }
        ClientsChanged?.Invoke();
    }

    private void Remove(HudWebClient client)
    {
        lock (_gate)
        {
            if (!_clients.Remove(client)) return;
            _snapshot = _clients.ToArray();
        }
        ClientsChanged?.Invoke();
    }
}
