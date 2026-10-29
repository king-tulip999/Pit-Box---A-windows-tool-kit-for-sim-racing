using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using PitBox.Config;
using PitBox.Plugins;
using PitBox.Plugins.BuiltIn;
using PitBox.Services;
using PitBox.SimRacing.IracingFiles;
using PitBox.SimRacing.Telemetry;

namespace PitBox.SimRacing;

internal sealed class LiveryPictures
{
    private const string Consumer = "Car pictures";
    private const string UiProcess = "iracingui";
    private const string DefaultPattern = "1", DefaultColors = "FF0000,00FF00,0000FF";
    private const string Service = "http://127.0.0.1:32034/pk_car.png?";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static IReadOnlyDictionary<string, string> _files = new Dictionary<string, string>();
    private static int _version;

    private readonly AppServices _app;
    private DispatcherTimer? _due;
    private bool _on, _sweeping, _again;

    public LiveryPictures(AppServices app) => _app = app;

    public static string Folder => Path.Combine(ConfigStore.Folder, "liveries");

    public static int Version => Volatile.Read(ref _version);

    public static string? FileFor(string key) => Volatile.Read(ref _files).TryGetValue(key, out var file) ? file : null;

    public static bool On(AppServices app) => SimRacingPlugin.FeatureOn(app, "tradingpaints") && app.Config.SimRacing.PaintedCarPictures;

    public void SetActive(bool on)
    {
        on = on && !_app.Demo;
        if (on == _on) return;
        _on = on;
        if (on)
        {
            Publish(Cached());
            _app.Watcher.Started += OnStarted;
            _app.Watcher.Stopped += OnStopped;
            _app.Watcher.Acquire(Consumer, new[] { UiProcess, TelemetryService.SimProcess });
            Schedule(TimeSpan.FromSeconds(10));
        }
        else
        {
            _app.Watcher.Started -= OnStarted;
            _app.Watcher.Stopped -= OnStopped;
            _app.Watcher.Release(Consumer);
            _due?.Stop();
            Publish(new Dictionary<string, string>());
        }
    }

    private void OnStarted(string name)
    {
        if (name == UiProcess) Schedule(TimeSpan.FromSeconds(20));
    }

    private void OnStopped(string name)
    {
        if (name == TelemetryService.SimProcess) Schedule(TimeSpan.FromSeconds(20));
    }

    private void Schedule(TimeSpan delay)
    {
        if (_due is null)
        {
            _due = new DispatcherTimer();
            _due.Tick += OnDue;
        }
        _due.Stop();
        _due.Interval = delay;
        _due.Start();
    }

    private async void OnDue(object? sender, EventArgs e)
    {
        _due?.Stop();
        if (!_on || _app.Watcher.IsRunning(TelemetryService.SimProcess)) return;
        if (_sweeping)
        {
            _again = true;
            return;
        }
        _sweeping = true;
        long? member = StatsMember();
        Dictionary<string, string>? found = null;
        try { found = await Task.Run(() => Sweep(member)); }
        catch (Exception ex) { App.LogError(ex); }
        _sweeping = false;
        if (!_on) return;
        if (found is not null) Publish(found);
        if (_again)
        {
            _again = false;
            Schedule(TimeSpan.FromSeconds(1));
        }
    }

    private long? StatsMember()
    {
        try
        {
            return _app.Plugins.Find(PluginIds.SessionStats) is SessionStatsPlugin { Service.Store: { } store } ? store.LatestDriverId() : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return null;
        }
    }

    private static void Publish(Dictionary<string, string> files)
    {
        var current = Volatile.Read(ref _files);
        if (current.Count == files.Count && files.All(f => current.TryGetValue(f.Key, out var v) && v == f.Value)) return;
        Volatile.Write(ref _files, files);
        Interlocked.Increment(ref _version);
    }

    private static Dictionary<string, string> Cached()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(Folder))
                foreach (var png in Directory.EnumerateFiles(Folder, "*.png"))
                    files[Path.GetFileNameWithoutExtension(png)] = png;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return files;
    }

    private static Dictionary<string, string> Sweep(long? member)
    {
        if (IracingPaths.FindDocumentsFolder() is not { } documents) return Cached();
        var telemetry = Path.Combine(documents, "telemetry");
        var recordings = Directory.Exists(telemetry)
            ? new DirectoryInfo(telemetry).EnumerateFiles("*.ibt").OrderByDescending(f => f.LastWriteTimeUtc).ToList()
            : new List<FileInfo>();
        member ??= recordings.Take(3).Select(f => Session(f.FullName)?.Player?.UserId).FirstOrDefault(user => user > 0);
        if (member is not { } id) return Cached();

        var paints = new Dictionary<string, (FileInfo Paint, string Dir)>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in CarArt.Dirs)
        {
            if (CarArt.KeyOfDir(dir) is not { } key) continue;
            var paint = new FileInfo(Path.Combine(documents, "paint", dir, $"car_{id}.tga"));
            if (!paint.Exists) continue;
            if (!paints.TryGetValue(key, out var known) || paint.LastWriteTimeUtc > known.Paint.LastWriteTimeUtc) paints[key] = (paint, dir);
        }

        Directory.CreateDirectory(Folder);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool reachable = true;
        foreach (var (key, (paint, dir)) in paints)
        {
            string png = Path.Combine(Folder, key + ".png"), stampFile = Path.Combine(Folder, key + ".stamp");
            var driver = Driver(recordings, dir, id);
            var query = Query(dir, paint.FullName, driver?.CarDesign, driver?.NumberDesign, driver?.CarNumber);
            string stamp = Stamp(paint.Length, paint.LastWriteTimeUtc, query);
            bool cached = File.Exists(png);
            if (cached && ReadText(stampFile) == stamp || !reachable)
            {
                if (cached) files[key] = png;
                continue;
            }
            try
            {
                var bytes = Http.GetByteArrayAsync(Service + Encode(query)).GetAwaiter().GetResult();
                SaveCropped(bytes, png);
                File.WriteAllText(stampFile, stamp);
                files[key] = png;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                reachable = false;
                if (cached) files[key] = png;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or ExternalException)
            {
                App.LogError(new InvalidOperationException($"Couldn't draw the car picture for {dir}.", ex));
                if (cached) files[key] = png;
            }
        }

        foreach (var old in Cached().Keys.Where(k => !paints.ContainsKey(k)))
        {
            TryDelete(Path.Combine(Folder, old + ".png"));
            TryDelete(Path.Combine(Folder, old + ".stamp"));
        }
        return files;
    }

    private static SessionInfo? Session(string path)
    {
        try
        {
            using var ibt = IbtFile.Open(path, out _);
            return ibt is null ? null : SessionInfo.Parse(ibt.SessionInfoYaml);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    private static DriverEntry? Driver(List<FileInfo> recordings, string dir, long member)
    {
        foreach (var file in recordings.Where(f => f.Name.StartsWith(dir + "_", StringComparison.OrdinalIgnoreCase)).Take(3))
            if (Session(file.FullName)?.Drivers.FirstOrDefault(d => d.UserId == member && d.CarPath.Equals(dir, StringComparison.OrdinalIgnoreCase)) is { } driver)
                return driver;
        return null;
    }

    internal static List<(string Name, string Value)> Query(string dir, string paint, string? carDesign, string? numberDesign, string? number)
    {
        var query = new List<(string, string)> { ("size", "2"), ("carPath", dir.Replace(' ', '\\')) };
        var car = Parts(carDesign);
        if (car.Length >= 4)
        {
            query.Add(("carPat", car[0]));
            query.Add(("carCol", string.Join(",", car[1..4])));
            if (car.Length >= 5) query.Add(("carRimCol", car[4]));
        }
        else
        {
            query.Add(("carPat", DefaultPattern));
            query.Add(("carCol", DefaultColors));
        }
        var digits = Parts(numberDesign);
        if (digits.Length >= 5)
        {
            query.Add(("numPat", digits[0]));
            query.Add(("numSlnt", digits[1]));
            query.Add(("numCol", string.Join(",", digits[2..5])));
        }
        if (!string.IsNullOrWhiteSpace(number)) query.Add(("number", number.Trim()));
        query.Add(("carCustPaint", paint));
        return query;
    }

    private static string[] Parts(string? design) =>
        string.IsNullOrWhiteSpace(design) ? Array.Empty<string>() : design.Split(',').Select(p => p.Trim()).ToArray() is var parts && parts.All(p => p.Length > 0) ? parts : Array.Empty<string>();

    internal static string Encode(IEnumerable<(string Name, string Value)> query) =>
        string.Join("&", query.Select(q => q.Name + "=" + Uri.EscapeDataString(q.Value)));

    internal static string Stamp(long length, DateTime writtenUtc, IEnumerable<(string Name, string Value)> query) =>
        $"{length}|{writtenUtc.Ticks}|{Encode(query)}";

    internal static Rectangle? Bounds(byte[] bgra, int width, int height, int stride, byte threshold = 8, int pad = 2)
    {
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            for (int x = 0; x < width; x++)
            {
                if (bgra[row + x * 4 + 3] <= threshold) continue;
                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }
        if (right < 0) return null;
        left = Math.Max(0, left - pad);
        top = Math.Max(0, top - pad);
        right = Math.Min(width - 1, right + pad);
        bottom = Math.Min(height - 1, bottom + pad);
        return new Rectangle(left, top, right - left + 1, bottom - top + 1);
    }

    private static void SaveCropped(byte[] bytes, string png)
    {
        using var stream = new MemoryStream(bytes);
        using var loaded = new Bitmap(stream);
        using var picture = loaded.Clone(new Rectangle(0, 0, loaded.Width, loaded.Height), PixelFormat.Format32bppArgb);
        var all = new Rectangle(0, 0, picture.Width, picture.Height);
        var data = picture.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] pixels;
        int stride = Math.Abs(data.Stride);
        try
        {
            pixels = new byte[stride * picture.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
        }
        finally { picture.UnlockBits(data); }
        using var cropped = picture.Clone(Bounds(pixels, picture.Width, picture.Height, stride) ?? all, PixelFormat.Format32bppArgb);
        string temp = png + ".tmp";
        cropped.Save(temp, ImageFormat.Png);
        File.Move(temp, png, overwrite: true);
    }

    private static string? ReadText(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
