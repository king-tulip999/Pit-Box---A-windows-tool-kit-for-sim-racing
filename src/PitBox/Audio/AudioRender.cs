using System.Runtime.InteropServices;

namespace PitBox.Audio;

[ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioRenderClient
{
    [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
    [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
}

internal sealed class AudioRender : IDisposable
{
    private const uint ClsCtxAll = 23, EventCallback = 0x00040000;
    private const long BufferDuration = 300_000;
    private static readonly Guid FloatFormat = new("00000003-0000-0010-8000-00AA00389B71");

    private readonly Func<int, Action<Span<float>>> _makeSource;
    private readonly string _deviceId;
    private readonly int[] _channels;
    private Thread? _thread;
    private volatile bool _stop;

    public AudioRender(string deviceId, int[] channels, Func<int, Action<Span<float>>> makeSource)
    {
        _deviceId = deviceId;
        _channels = channels;
        _makeSource = makeSource;
    }

    public event Action<string>? Failed;

    public void Start()
    {
        if (_thread is not null) return;
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "Bass shakers", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(1000);
        _thread = null;
    }

    private void Run()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioRenderClient? render = null;
        IntPtr format = IntPtr.Zero;
        using var ready = new AutoResetEvent(false);
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDevice(_deviceId, out device) != 0 || device is null) throw new InvalidOperationException("The output isn't connected");
            var clientId = typeof(IAudioClient).GUID;
            Check(device.Activate(ref clientId, ClsCtxAll, IntPtr.Zero, out var instance));
            client = (IAudioClient)instance;
            Check(client.GetMixFormat(out format));
            int channels = Marshal.ReadInt16(format, 2);
            int rate = Marshal.ReadInt32(format, 4);
            int bits = Marshal.ReadInt16(format, 14);
            ushort tag = (ushort)Marshal.ReadInt16(format, 0);
            bool isFloat = tag == 3 || tag == 0xFFFE && Marshal.PtrToStructure<Guid>(format + 24) == FloatFormat;
            if (!(isFloat && bits == 32) && bits != 16) throw new InvalidOperationException($"Unsupported output format ({bits}-bit)");
            Check(client.Initialize(0, EventCallback, BufferDuration, 0, format, IntPtr.Zero));
            Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            Check(client.GetBufferSize(out uint size));
            var renderId = typeof(IAudioRenderClient).GUID;
            Check(client.GetService(ref renderId, out var service));
            render = (IAudioRenderClient)service;
            var source = _makeSource(rate);
            var mono = new float[size];
            var floats = isFloat ? new float[size * channels] : Array.Empty<float>();
            var shorts = isFloat ? Array.Empty<short>() : new short[size * channels];
            var targets = _channels.Where(c => c < channels).DefaultIfEmpty(0).ToArray();
            Check(client.Start());
            while (!_stop)
            {
                if (!ready.WaitOne(200)) continue;
                if (client.GetCurrentPadding(out uint padding) != 0) break;
                int frames = (int)(size - padding);
                if (frames <= 0) continue;
                if (render.GetBuffer((uint)frames, out var data) != 0) break;
                var span = mono.AsSpan(0, frames);
                source(span);
                Write(data, span, channels, targets, floats, shorts, isFloat);
                render.ReleaseBuffer((uint)frames, 0);
            }
            client.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or InvalidCastException)
        {
            Failed?.Invoke(ex.Message);
        }
        finally
        {
            if (format != IntPtr.Zero) Marshal.FreeCoTaskMem(format);
            foreach (var o in new object?[] { render, client, device, enumerator })
                if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }

    private static void Write(IntPtr data, ReadOnlySpan<float> mono, int channels, int[] targets, float[] floats, short[] shorts, bool isFloat)
    {
        int count = mono.Length * channels;
        if (isFloat)
        {
            Array.Clear(floats, 0, count);
            for (int i = 0; i < mono.Length; i++)
                foreach (int c in targets) floats[i * channels + c] = mono[i];
            Marshal.Copy(floats, 0, data, count);
        }
        else
        {
            Array.Clear(shorts, 0, count);
            for (int i = 0; i < mono.Length; i++)
            {
                short v = (short)Math.Clamp(mono[i] * short.MaxValue, short.MinValue, short.MaxValue);
                foreach (int c in targets) shorts[i * channels + c] = v;
            }
            Marshal.Copy(shorts, 0, data, count);
        }
    }

    private static void Check(int hr)
    {
        if (hr < 0) throw new COMException("Audio output error", hr);
    }
}
