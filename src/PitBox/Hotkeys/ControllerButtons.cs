using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace PitBox.Hotkeys;

internal readonly record struct ControllerButton(string Device, int Button)
{
    public override string ToString() => $"Joy {Device} B{Button.ToString(CultureInfo.InvariantCulture)}";
}

internal sealed partial class ControllerButtons : IDisposable
{
    public static ControllerButtons Shared { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<ControllerButton, List<Action>> _actions = new();
    private readonly HashSet<ControllerButton> _held = new();
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
    private Action<ControllerButton>? _recorder;
    private Pump? _pump;
    private SynchronizationContext? _ui;

    [GeneratedRegex(@"^Joy ([0-9A-F]{4}:[0-9A-F]{4}) B(\d{1,3})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BindingRx();

    public static bool TryParse(string? text, out ControllerButton button)
    {
        button = default;
        if (text is null || BindingRx().Match(text.Trim()) is not { Success: true } m) return false;
        var ids = m.Groups[1].Value.Split(':');
        string device = $"{int.Parse(ids[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture):X4}:{int.Parse(ids[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture):X4}";
        button = new ControllerButton(device, int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        return button.Button > 0;
    }

    public static bool IsBinding(string? text) => TryParse(text, out _);

    public bool IsHeld(ControllerButton button)
    {
        lock (_lock) return _held.Contains(button);
    }

    public static string Display(ControllerButton b)
    {
        string? name;
        lock (Names) Names.TryGetValue(b.Device, out name);
        return $"{(string.IsNullOrWhiteSpace(name) ? "Controller " + b.Device : name.Trim())} · button {b.Button.ToString(CultureInfo.InvariantCulture)}";
    }

    public void Register(ControllerButton button, Action action)
    {
        lock (_lock)
        {
            if (!_actions.TryGetValue(button, out var list)) _actions[button] = list = new();
            list.Add(action);
        }
        Ensure();
    }

    public void Clear()
    {
        lock (_lock) _actions.Clear();
        StopIfIdle();
    }

    public void Record(Action<ControllerButton> recorder)
    {
        lock (_lock) _recorder = recorder;
        Ensure();
    }

    public void StopRecording()
    {
        lock (_lock) _recorder = null;
        StopIfIdle();
    }

    private void Ensure()
    {
        _ui ??= SynchronizationContext.Current;
        lock (_lock)
        {
            if (_pump is not null) return;
            var pump = _pump = new Pump();
            pump.Thread = new Thread(() => Run(pump)) { IsBackground = true, Name = "Controller buttons" };
            pump.Thread.SetApartmentState(ApartmentState.STA);
            pump.Thread.Start();
        }
    }

    private void StopIfIdle()
    {
        Pump? pump;
        lock (_lock)
        {
            if (_actions.Count > 0 || _recorder is not null || _pump is null) return;
            pump = _pump;
            _pump = null;
        }
        if (pump.Ready.Wait(1000) && pump.Id != 0) PostThreadMessage(pump.Id, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        pump.Thread?.Join(1000);
    }

    private sealed class Pump
    {
        public readonly ManualResetEventSlim Ready = new();
        public Thread? Thread;
        public volatile uint Id;
    }

    private void Run(Pump pump)
    {
        try
        {
            using var window = new InputWindow(this);
            pump.Id = GetCurrentThreadId();
            pump.Ready.Set();
            var devices = new[]
            {
                new RAWINPUTDEVICE { UsagePage = 1, Usage = 4, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = window.Handle },
                new RAWINPUTDEVICE { UsagePage = 1, Usage = 5, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = window.Handle },
                new RAWINPUTDEVICE { UsagePage = 1, Usage = 8, Flags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY, Target = window.Handle },
            };
            if (RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
                Application.Run();
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
        finally
        {
            pump.Id = 0;
            pump.Ready.Set();
        }
    }

    private sealed class Device
    {
        public required string Id;
        public required IntPtr Preparsed;
        public required ushort[] Usages;
        public readonly HashSet<int> Down = new();
        public readonly HashSet<int> Now = new();
    }

    private readonly Dictionary<IntPtr, Device?> _devices = new();
    private byte[] _buffer = new byte[256], _report = new byte[64];

    private Device? DeviceOf(IntPtr handle)
    {
        if (_devices.TryGetValue(handle, out var known)) return known;
        Device? device = null;
        uint size = 0;
        GetRawInputDeviceInfo(handle, RIDI_DEVICEINFO, IntPtr.Zero, ref size);
        var info = Marshal.AllocHGlobal((int)Math.Max(size, 32));
        try
        {
            Marshal.WriteInt32(info, (int)Math.Max(size, 32));
            size = (uint)Math.Max(size, 32);
            if (GetRawInputDeviceInfo(handle, RIDI_DEVICEINFO, info, ref size) != uint.MaxValue && Marshal.ReadInt32(info, 4) == RIM_TYPEHID)
            {
                string id = $"{Marshal.ReadInt32(info, 8):X4}:{Marshal.ReadInt32(info, 12):X4}";
                uint pre = 0;
                GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, IntPtr.Zero, ref pre);
                if (pre > 0)
                {
                    var preparsed = Marshal.AllocHGlobal((int)pre);
                    if (GetRawInputDeviceInfo(handle, RIDI_PREPARSEDDATA, preparsed, ref pre) != uint.MaxValue)
                    {
                        uint max = HidP_MaxUsageListLength(HidP_Input, ButtonPage, preparsed);
                        device = new Device { Id = id, Preparsed = preparsed, Usages = new ushort[Math.Max(1, max)] };
                        lock (_lock)
                            foreach (var held in _held)
                                if (held.Device == id) device.Down.Add(held.Button);
                        Remember(handle, id);
                    }
                    else Marshal.FreeHGlobal(preparsed);
                }
            }
        }
        finally { Marshal.FreeHGlobal(info); }
        _devices[handle] = device;
        return device;
    }

    private static void Remember(IntPtr handle, string id)
    {
        lock (Names) if (Names.ContainsKey(id)) return;
        string? name = null;
        uint size = 0;
        GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, IntPtr.Zero, ref size);
        if (size > 0)
        {
            var buffer = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                if (GetRawInputDeviceInfo(handle, RIDI_DEVICENAME, buffer, ref size) != uint.MaxValue && Marshal.PtrToStringUni(buffer) is { Length: > 0 } path)
                {
                    using var file = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    var text = new byte[256];
                    if (!file.IsInvalid && HidD_GetProductString(file, text, (uint)text.Length))
                        name = System.Text.Encoding.Unicode.GetString(text).TrimEnd('\0');
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        lock (Names) Names[id] = name ?? "";
    }

    private void OnInput(IntPtr lParam)
    {
        uint size = 0;
        int header = Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(lParam, RID_INPUT, null, ref size, (uint)header);
        if (size == 0) return;
        if (_buffer.Length < size) _buffer = new byte[size * 2];
        if (GetRawInputData(lParam, RID_INPUT, _buffer, ref size, (uint)header) == uint.MaxValue) return;
        if (BitConverter.ToInt32(_buffer, 0) != RIM_TYPEHID) return;
        var handle = IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(_buffer, 8) : BitConverter.ToInt32(_buffer, 8);
        if (DeviceOf(handle) is not { } device) return;
        int reportSize = BitConverter.ToInt32(_buffer, header), count = BitConverter.ToInt32(_buffer, header + 4);
        int at = header + 8;
        device.Now.Clear();
        if (_report.Length < reportSize) _report = new byte[reportSize];
        for (int r = 0; r < count && at + reportSize <= size; r++, at += reportSize)
        {
            uint length = (uint)device.Usages.Length;
            Buffer.BlockCopy(_buffer, at, _report, 0, reportSize);
            if (HidP_GetUsages(HidP_Input, ButtonPage, 0, device.Usages, ref length, device.Preparsed, _report, (uint)reportSize) != HidpStatusSuccess) return;
            for (int i = 0; i < length; i++) device.Now.Add(device.Usages[i]);
        }
        lock (_lock)
        {
            foreach (int b in device.Down) if (!device.Now.Contains(b)) _held.Remove(new ControllerButton(device.Id, b));
            foreach (int b in device.Now) _held.Add(new ControllerButton(device.Id, b));
        }
        foreach (int b in device.Now)
            if (!device.Down.Contains(b)) Pressed(new ControllerButton(device.Id, b));
        device.Down.Clear();
        device.Down.UnionWith(device.Now);
    }

    private void Pressed(ControllerButton button)
    {
        Action<ControllerButton>? recorder;
        List<Action>? actions;
        lock (_lock)
        {
            recorder = _recorder;
            actions = _actions.TryGetValue(button, out var list) ? list.ToList() : null;
        }
        var ui = _ui;
        if (ui is null) return;
        if (recorder is not null)
        {
            ui.Post(_ => recorder(button), null);
            return;
        }
        if (actions is null) return;
        ui.Post(_ =>
        {
            foreach (var a in actions)
            {
                try { a(); }
                catch (Exception ex) { App.LogError(ex); }
            }
        }, null);
    }

    private void OnDeviceChange(IntPtr wParam, IntPtr lParam)
    {
        if ((int)wParam != GIDC_REMOVAL || !_devices.Remove(lParam, out var d) || d is null) return;
        lock (_lock)
            foreach (int b in d.Down) _held.Remove(new ControllerButton(d.Id, b));
        Marshal.FreeHGlobal(d.Preparsed);
    }

    private sealed class InputWindow : NativeWindow, IDisposable
    {
        private readonly ControllerButtons _owner;

        public InputWindow(ControllerButtons owner)
        {
            _owner = owner;
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) { _owner.OnInput(m.LParam); base.WndProc(ref m); return; }
            if (m.Msg == WM_INPUT_DEVICE_CHANGE) { _owner.OnDeviceChange(m.WParam, m.LParam); return; }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (var d in _owner._devices.Values)
                if (d is not null) Marshal.FreeHGlobal(d.Preparsed);
            _owner._devices.Clear();
            DestroyHandle();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _actions.Clear();
            _recorder = null;
        }
        StopIfIdle();
    }

    private const int WM_INPUT = 0x00FF, WM_INPUT_DEVICE_CHANGE = 0x00FE, WM_QUIT = 0x0012, GIDC_REMOVAL = 2;
    private const uint RIDEV_INPUTSINK = 0x00000100, RIDEV_DEVNOTIFY = 0x00002000, RID_INPUT = 0x10000003;
    private const uint RIDI_PREPARSEDDATA = 0x20000005, RIDI_DEVICENAME = 0x20000007, RIDI_DEVICEINFO = 0x2000000b;
    private const int RIM_TYPEHID = 2, HidP_Input = 0;
    private const ushort ButtonPage = 9;
    private const int HidpStatusSuccess = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr input, uint command, byte[]? data, ref uint size, uint headerSize);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);

    [DllImport("hid.dll")]
    private static extern int HidP_GetUsages(int reportType, ushort usagePage, ushort linkCollection, [Out] ushort[] usages, ref uint length, IntPtr preparsed, byte[] report, uint reportLength);

    [DllImport("hid.dll")]
    private static extern uint HidP_MaxUsageListLength(int reportType, ushort usagePage, IntPtr preparsed);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetProductString(SafeFileHandle device, byte[] buffer, uint length);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint thread, int msg, IntPtr wParam, IntPtr lParam);
}
