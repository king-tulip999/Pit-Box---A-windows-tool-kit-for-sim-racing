using System.Runtime.InteropServices;

namespace PitBox.Audio;

[ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}

[ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceAsyncOperation
{
    [PreserveSig] int GetActivateResult(out int result, [MarshalAs(UnmanagedType.IUnknown)] out object? activated);
}

[ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IActivateAudioInterfaceCompletionHandler
{
    [PreserveSig] int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation);
}

[ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAgileObject
{
}

internal sealed class AudioLoopback : IDisposable
{
    public const int Rate = 48000, Channels = 2;

    private const string ProcessLoopbackPath = @"VAD\Process_Loopback";
    private const uint LoopbackFlag = 0x00020000, EventCallback = 0x00040000, AutoConvertPcm = 0x80000000, DefaultQualitySrc = 0x08000000, SilentFlag = 0x2;
    private const int ProcessLoopbackActivation = 1, IncludeProcessTree = 0;
    private const ushort VtBlob = 65;
    private const long BufferDuration = 1_000_000;

    private readonly int _processId;
    private readonly Action<ReadOnlySpan<float>, int> _sink;
    private Thread? _thread;
    private volatile bool _stop;

    public AudioLoopback(int processId, Action<ReadOnlySpan<float>, int> sink)
    {
        _processId = processId;
        _sink = sink;
    }

    public event Action<string>? Failed;

    public void Start()
    {
        if (_thread is not null) return;
        _stop = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "Bass shakers game audio", Priority = ThreadPriority.AboveNormal };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(1000);
        _thread = null;
    }

    [DllImport("mmdevapi.dll", ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath, ref Guid riid, IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler, out IActivateAudioInterfaceAsyncOperation operation);

    private sealed class Activation : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public readonly ManualResetEventSlim Done = new(false);

        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            Done.Set();
            return 0;
        }
    }

    private IAudioClient Activate()
    {
        IntPtr parameters = Marshal.AllocHGlobal(12), variant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(parameters, 0, ProcessLoopbackActivation);
            Marshal.WriteInt32(parameters, 4, _processId);
            Marshal.WriteInt32(parameters, 8, IncludeProcessTree);
            for (int i = 0; i < 24; i += 4) Marshal.WriteInt32(variant, i, 0);
            Marshal.WriteInt16(variant, 0, (short)VtBlob);
            Marshal.WriteInt32(variant, 8, 12);
            Marshal.WriteIntPtr(variant, 16, parameters);
            var activation = new Activation();
            var clientId = typeof(IAudioClient).GUID;
            Check(ActivateAudioInterfaceAsync(ProcessLoopbackPath, ref clientId, variant, activation, out var operation));
            try
            {
                if (!activation.Done.Wait(5000)) throw new InvalidOperationException("Windows didn't open the game's sound");
                Check(operation.GetActivateResult(out int result, out var activated));
                Check(result);
                return activated as IAudioClient ?? throw new InvalidOperationException("Windows didn't open the game's sound");
            }
            finally
            {
                Marshal.ReleaseComObject(operation);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(parameters);
        }
    }

    private void Run()
    {
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        IntPtr format = Marshal.AllocHGlobal(18);
        using var ready = new AutoResetEvent(false);
        try
        {
            client = Activate();
            Marshal.WriteInt16(format, 0, 3);
            Marshal.WriteInt16(format, 2, Channels);
            Marshal.WriteInt32(format, 4, Rate);
            Marshal.WriteInt32(format, 8, Rate * Channels * sizeof(float));
            Marshal.WriteInt16(format, 12, Channels * sizeof(float));
            Marshal.WriteInt16(format, 14, 32);
            Marshal.WriteInt16(format, 16, 0);
            Check(client.Initialize(0, LoopbackFlag | EventCallback | AutoConvertPcm | DefaultQualitySrc, BufferDuration, 0, format, IntPtr.Zero));
            Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
            var captureId = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref captureId, out var service));
            capture = (IAudioCaptureClient)service;
            var mono = new float[Rate];
            var floats = new float[Rate * Channels];
            Check(client.Start());
            while (!_stop)
            {
                ready.WaitOne(20);
                while (!_stop)
                {
                    Check(capture.GetNextPacketSize(out uint packet));
                    if (packet == 0) break;
                    Check(capture.GetBuffer(out var data, out uint frames, out uint flags, out _, out _));
                    int count = (int)Math.Min(frames, (uint)mono.Length);
                    var span = mono.AsSpan(0, count);
                    if ((flags & SilentFlag) != 0) span.Clear();
                    else Read(data, span, floats);
                    capture.ReleaseBuffer(frames);
                    _sink(span, Rate);
                }
            }
            client.Stop();
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or InvalidCastException or EntryPointNotFoundException)
        {
            if (!_stop) Failed?.Invoke(ex is COMException or EntryPointNotFoundException ? "Windows couldn't capture the game's sound" : ex.Message);
        }
        finally
        {
            Marshal.FreeHGlobal(format);
            foreach (var o in new object?[] { capture, client })
                if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }

    private static void Read(IntPtr data, Span<float> mono, float[] floats)
    {
        Marshal.Copy(data, floats, 0, mono.Length * Channels);
        for (int i = 0; i < mono.Length; i++)
            mono[i] = (floats[i * Channels] + floats[i * Channels + 1]) * 0.5f;
    }

    private static void Check(int hr)
    {
        if (hr < 0) throw new COMException("Game sound error", hr);
    }
}
