using System.Diagnostics;
using System.IO;
using System.Windows;
using Core.Config;
using Core.Hardware;
using WinForms = System.Windows.Forms;

namespace Core.Services;

internal static class FanService
{
    public const string Arg = "--fan-service";
    private const string MutexName = @"Local\Core.FanService";
    private const string StopEventName = @"Local\Core.FanService.Stop";
    private const string AppMutexName = @"Local\Core.SingleInstance";
    private static string PidFile => Path.Combine(ConfigStore.Folder, "fan-service.pid");

    private static Mutex? _mutex;
    private static HardwareService? _hardware;
    private static WinForms.NotifyIcon? _tray;

    public static bool IsRunning => Held(MutexName);

    private static bool Held(string mutex)
    {
        if (!Mutex.TryOpenExisting(mutex, out var m)) return false;
        m.Dispose();
        return true;
    }

    public static bool Start()
    {
        if (!External.Allowed("start the fan service")) return false;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, Arg) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
        }
    }

    public static bool StopRunning(TimeSpan wait)
    {
        if (!IsRunning) return false;
        try
        {
            using var stop = EventWaitHandle.OpenExisting(StopEventName);
            stop.Set();
        }
        catch { }
        var until = DateTime.UtcNow + wait;
        while (IsRunning && DateTime.UtcNow < until) Thread.Sleep(100);
        if (IsRunning && ReadPid() is { } pid)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                using var self = Process.GetCurrentProcess();
                if (pid != self.Id && p.ProcessName.Equals(self.ProcessName, StringComparison.OrdinalIgnoreCase)) p.Kill();
            }
            catch { }
        }
        return true;
    }

    private static int? ReadPid()
    {
        try { return int.TryParse(File.ReadAllText(PidFile).Trim(), out var pid) ? pid : null; }
        catch { return null; }
    }

    public static void Run(Application app)
    {
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _mutex = new Mutex(true, MutexName, out bool created);
        if (!created) { app.Shutdown(); return; }

        var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName);
        try { File.WriteAllText(PidFile, Environment.ProcessId.ToString()); } catch { }

        var until = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (AppRunning() && DateTime.UtcNow < until && !stop.WaitOne(200)) { }
        if (stop.WaitOne(0) || AppRunning()) { Leave(app); return; }

        ConfigStore.ReadOnly = true;
        var config = ConfigStore.Load(out _);
        if (!config.Fans.Enabled) { Leave(app); return; }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) App.LogError(ex);
            _hardware?.EmergencyRestore();
        };
        app.Exit += (_, _) => { _hardware?.Dispose(); _hardware = null; };
        _hardware = new HardwareService();
        var fans = ConfigStore.Clone(config.Fans);
        fans.Enabled = true;
        _hardware.SetFanSettings(fans);
        _hardware.Acquire("Fan service");
        if (stop.WaitOne(0) || AppRunning()) { Leave(app); return; }

        new Thread(() => { stop.WaitOne(); app.Dispatcher.BeginInvoke(new Action(() => Leave(app))); }) { IsBackground = true, Name = "Fan service stop" }.Start();
        ShowTray(app, config.Fans.ActivePreset ?? "your fan preset", config.Ui.Theme);
        _hardware.Problem += (title, message) => _tray?.ShowBalloonTip(10_000, title, message, WinForms.ToolTipIcon.Warning);

        bool told = false;
        var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        watch.Tick += (_, _) =>
        {
            if (_hardware is null) { watch.Stop(); return; }
            if (_hardware.ThreadAlive) return;
            if (!told)
            {
                told = true;
                watch.Interval = TimeSpan.FromSeconds(10);
                _tray?.ShowBalloonTip(10_000, "Fan control stopped",
                    "The hardware sensors stopped answering, so the fans are back with the BIOS / driver. Open Tecnica to start fan control again.", WinForms.ToolTipIcon.Warning);
                return;
            }
            watch.Stop();
            Leave(app);
        };
        watch.Start();
    }

    private static bool AppRunning()
    {
        if (!Mutex.TryOpenExisting(AppMutexName, out var m)) return false;
        m.Dispose();
        return true;
    }

    private static void ShowTray(Application app, string preset, Config.AppTheme theme)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(new WinForms.ToolStripMenuItem($"Fan control running · {preset}") { Enabled = false });
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Open Tecnica", null, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false })?.Dispose(); }
            catch (Exception ex) { App.LogError(ex); }
        });
        menu.Items.Add("Stop fan control", null, (_, _) => Leave(app));
        _tray = new WinForms.NotifyIcon
        {
            Icon = TrayService.FanIcon(theme) ?? System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!), ContextMenuStrip = menu, Visible = true,
            Text = "Tecnica fan control (running without the app)",
        };
    }

    private static void Leave(Application app)
    {
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); _tray = null; }
        _hardware?.Dispose();
        _hardware = null;
        try { File.Delete(PidFile); } catch { }
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        _mutex = null;
        app.Shutdown();
    }
}
