using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Core.Overlay;

internal readonly struct EtwEvent
{
    public required Guid Provider { get; init; }
    public required int Id { get; init; }
    public required int ProcessId { get; init; }
    public required double TimeMs { get; init; }
    public required IntPtr Data { get; init; }
    public required int DataLength { get; init; }
    public required int PointerSize { get; init; }
}

internal delegate void EtwHandler(in EtwEvent e);

internal sealed class EtwSession : IDisposable
{
    public const byte LevelInformational = 4, LevelVerbose = 5;

    private const uint RealTimeMode = 0x100, MsFlushTimer = 0x10, TracedGuid = 0x20000, QpcClock = 1;
    private const uint ControlStop = 1, EnableProvider = 1;
    private const uint ProcessRealTime = 0x100, ProcessEventRecord = 0x10000000, ProcessRawTimestamp = 0x1000;
    private const uint EventIdFilter = 0x80000200;
    private const int AlreadyExists = 183, NameRoom = 1024;
    private const ulong InvalidTrace = ulong.MaxValue;

    private readonly string _name;
    private readonly EtwHandler _handler;
    private readonly EventRecordCallback _callback;
    private readonly List<(long Lo, long Hi, Guid Id)> _providers = new();
    private readonly double _msPerTick = 1000.0 / Stopwatch.Frequency;
    private ulong _session;
    private ulong _trace = InvalidTrace;
    private IntPtr _loggerName;
    private bool _disposed;

    private EtwSession(string name, EtwHandler handler)
    {
        _name = name;
        _handler = handler;
        _callback = OnRecord;
    }

    public static EtwSession Start(string name, EtwHandler handler)
    {
        StopNamed(name);
        var session = new EtwSession(name, handler);
        int error = session.StartTrace(MsFlushTimer, 200);
        if (error == AlreadyExists)
        {
            StopNamed(name);
            error = session.StartTrace(MsFlushTimer, 200);
        }
        if (error != 0) error = session.StartTrace(0, 1);
        if (error != 0) throw new Win32Exception(error);
        return session;
    }

    private int StartTrace(uint extraMode, uint flushTimer)
    {
        var props = Properties(_name, extraMode, flushTimer);
        try { return NativeStartTrace(out _session, _name, props); }
        finally { Marshal.FreeHGlobal(props); }
    }

    public void Enable(Guid provider, byte level, params int[] eventIds)
    {
        var bytes = provider.ToByteArray();
        _providers.Add((BitConverter.ToInt64(bytes, 0), BitConverter.ToInt64(bytes, 8), provider));
        int filterSize = 4 + 2 * eventIds.Length;
        IntPtr filter = Marshal.AllocHGlobal(filterSize), descriptor = Marshal.AllocHGlobal(16), parameters = Marshal.AllocHGlobal(48);
        try
        {
            Marshal.WriteByte(filter, 0, 1);
            Marshal.WriteByte(filter, 1, 0);
            Marshal.WriteInt16(filter, 2, (short)eventIds.Length);
            for (int i = 0; i < eventIds.Length; i++) Marshal.WriteInt16(filter, 4 + 2 * i, (short)eventIds[i]);
            Marshal.WriteInt64(descriptor, 0, filter.ToInt64());
            Marshal.WriteInt32(descriptor, 8, filterSize);
            Marshal.WriteInt32(descriptor, 12, unchecked((int)EventIdFilter));
            for (int i = 0; i < 48; i += 4) Marshal.WriteInt32(parameters, i, 0);
            Marshal.WriteInt32(parameters, 0, 2);
            Marshal.WriteIntPtr(parameters, 32, descriptor);
            Marshal.WriteInt32(parameters, 40, 1);
            int error = EnableTraceEx2(_session, ref provider, EnableProvider, level, ulong.MaxValue, 0, 0, parameters);
            if (error != 0) error = EnableTraceEx2(_session, ref provider, EnableProvider, level, ulong.MaxValue, 0, 0, IntPtr.Zero);
            if (error != 0) throw new Win32Exception(error);
        }
        finally
        {
            Marshal.FreeHGlobal(parameters);
            Marshal.FreeHGlobal(descriptor);
            Marshal.FreeHGlobal(filter);
        }
    }

    public void Process()
    {
        _loggerName = Marshal.StringToHGlobalUni(_name);
        var logfile = new TraceLogfile
        {
            LoggerName = _loggerName,
            ProcessTraceMode = ProcessRealTime | ProcessEventRecord | ProcessRawTimestamp,
            EventRecordCallback = Marshal.GetFunctionPointerForDelegate(_callback),
        };
        _trace = OpenTrace(ref logfile);
        if (_trace == InvalidTrace) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (_disposed) return;
        ulong[] handles = { _trace };
        int error = ProcessTrace(handles, 1, IntPtr.Zero, IntPtr.Zero);
        if (error != 0 && !_disposed) throw new Win32Exception(error);
    }

    private void OnRecord(IntPtr record)
    {
        long lo = Marshal.ReadInt64(record, 24), hi = Marshal.ReadInt64(record, 32);
        Guid? provider = null;
        foreach (var p in _providers)
            if (p.Lo == lo && p.Hi == hi) { provider = p.Id; break; }
        if (provider is not { } id) return;
        ushort flags = (ushort)Marshal.ReadInt16(record, 4);
        var e = new EtwEvent
        {
            Provider = id,
            Id = (ushort)Marshal.ReadInt16(record, 40),
            ProcessId = Marshal.ReadInt32(record, 12),
            TimeMs = Marshal.ReadInt64(record, 16) * _msPerTick,
            DataLength = (ushort)Marshal.ReadInt16(record, 86),
            Data = Marshal.ReadIntPtr(record, 96),
            PointerSize = (flags & 0x20) != 0 ? 4 : 8,
        };
        _handler(in e);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var props = Properties(_name, 0, 0);
        try { ControlTrace(_session, null, props, ControlStop); }
        finally { Marshal.FreeHGlobal(props); }
        if (_trace != InvalidTrace) CloseTrace(_trace);
        if (_loggerName != IntPtr.Zero) Marshal.FreeHGlobal(_loggerName);
        _loggerName = IntPtr.Zero;
    }

    private static void StopNamed(string name)
    {
        var props = Properties(name, 0, 0);
        try { ControlTrace(0, name, props, ControlStop); }
        finally { Marshal.FreeHGlobal(props); }
    }

    private static IntPtr Properties(string name, uint extraMode, uint flushTimer)
    {
        int size = Marshal.SizeOf<TraceProperties>() + NameRoom * 2;
        var props = new TraceProperties
        {
            Wnode = new WnodeHeader { BufferSize = (uint)size, Flags = TracedGuid, ClientContext = QpcClock, Guid = Guid.NewGuid() },
            BufferSize = 64,
            LogFileMode = RealTimeMode | extraMode,
            FlushTimer = flushTimer,
            LoggerNameOffset = (uint)Marshal.SizeOf<TraceProperties>(),
        };
        var buffer = Marshal.AllocHGlobal(size);
        for (int i = 0; i < size; i += 4) Marshal.WriteInt32(buffer, i, 0);
        Marshal.StructureToPtr(props, buffer, false);
        return buffer;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void EventRecordCallback(IntPtr record);

    [StructLayout(LayoutKind.Sequential)]
    private struct WnodeHeader
    {
        public uint BufferSize;
        public uint ProviderId;
        public ulong HistoricalContext;
        public long TimeStamp;
        public Guid Guid;
        public uint ClientContext;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TraceProperties
    {
        public WnodeHeader Wnode;
        public uint BufferSize;
        public uint MinimumBuffers;
        public uint MaximumBuffers;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint FlushTimer;
        public uint EnableFlags;
        public int AgeLimit;
        public uint NumberOfBuffers;
        public uint FreeBuffers;
        public uint EventsLost;
        public uint BuffersWritten;
        public uint LogBuffersLost;
        public uint RealTimeBuffersLost;
        public IntPtr LoggerThreadId;
        public uint LogFileNameOffset;
        public uint LoggerNameOffset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TimeZoneInformation
    {
        public int Bias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string StandardName;
        public SystemTime StandardDate;
        public int StandardBias;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DaylightName;
        public SystemTime DaylightDate;
        public int DaylightBias;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TraceLogfileHeader
    {
        public uint BufferSize;
        public uint Version;
        public uint ProviderVersion;
        public uint NumberOfProcessors;
        public long EndTime;
        public uint TimerResolution;
        public uint MaximumFileSize;
        public uint LogFileMode;
        public uint BuffersWritten;
        public Guid LogInstanceGuid;
        public IntPtr LoggerName;
        public IntPtr LogFileName;
        public TimeZoneInformation TimeZone;
        public long BootTime;
        public long PerfFreq;
        public long StartTime;
        public uint ReservedFlags;
        public uint BuffersLost;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTraceHeader
    {
        public ushort Size;
        public ushort FieldTypeFlags;
        public uint Version;
        public uint ThreadId;
        public uint ProcessId;
        public long TimeStamp;
        public Guid Guid;
        public ulong ProcessorTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTrace
    {
        public EventTraceHeader Header;
        public uint InstanceId;
        public uint ParentInstanceId;
        public Guid ParentGuid;
        public IntPtr MofData;
        public uint MofLength;
        public uint ClientContext;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TraceLogfile
    {
        public IntPtr LogFileName;
        public IntPtr LoggerName;
        public long CurrentTime;
        public uint BuffersRead;
        public uint ProcessTraceMode;
        public EventTrace CurrentEvent;
        public TraceLogfileHeader LogfileHeader;
        public IntPtr BufferCallback;
        public uint BufferSize;
        public uint Filled;
        public uint EventsLost;
        public IntPtr EventRecordCallback;
        public uint IsKernelTrace;
        public IntPtr Context;
    }

    [DllImport("advapi32.dll", EntryPoint = "StartTraceW", CharSet = CharSet.Unicode)]
    private static extern int NativeStartTrace(out ulong session, string name, IntPtr properties);

    [DllImport("advapi32.dll", EntryPoint = "ControlTraceW", CharSet = CharSet.Unicode)]
    private static extern int ControlTrace(ulong session, string? name, IntPtr properties, uint control);

    [DllImport("advapi32.dll")]
    private static extern int EnableTraceEx2(ulong session, ref Guid provider, uint control, byte level, ulong matchAny, ulong matchAll, uint timeout, IntPtr parameters);

    [DllImport("advapi32.dll", EntryPoint = "OpenTraceW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ulong OpenTrace(ref TraceLogfile logfile);

    [DllImport("advapi32.dll")]
    private static extern int ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);

    [DllImport("advapi32.dll")]
    private static extern int CloseTrace(ulong trace);
}
