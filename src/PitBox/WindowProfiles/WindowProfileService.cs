using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PitBox.Config;
using PitBox.Processes;
using PitBox.Services;

namespace PitBox.WindowProfiles;

internal enum WindowApplyResult { Applied, NotRunning, NoWindow, AccessDenied, Failed }

internal sealed record OpenWindow(string Process, string Title, int X, int Y, int Width, int Height);

internal sealed class WindowProfileService : IDisposable
{
    private const string Consumer = "Window profiles";
    private readonly AppServices _app;
    private readonly Dictionary<string, (IntPtr Window, CancellationTokenSource Hold, WindowProfile Placed)> _watchers = new();
    private readonly object _gate = new();
    private bool _watching;

    public WindowProfileService(AppServices app)
    {
        _app = app;
        _app.Watcher.Started += OnProgramStarted;
    }

    private WindowProfileSettings S => _app.Config.WindowProfiles;

    private bool Active => _app.Plugins.IsActive(Plugins.PluginIds.WindowProfiles);

    private List<WindowProfile> Automatic =>
        Active && S.AutoApply ? S.Profiles.Where(p => p.Auto && !string.IsNullOrWhiteSpace(p.ProcessName)).ToList() : new List<WindowProfile>();

    public event Action? Changed;

    public void RaiseChanged()
    {
        _app.Save();
        _app.Hotkeys.RegisterAll();
        Refresh();
        Changed?.Invoke();
    }

    public void Refresh()
    {
        if (!Active) StopWatching();
        var now = S.Profiles.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        lock (_gate)
            foreach (var (id, held) in _watchers.ToList())
                if (!now.TryGetValue(id, out var profile) || !SamePlace(profile, held.Placed))
                    if (_watchers.Remove(id, out var stale)) stale.Hold.Cancel();
        var names = Automatic.Select(p => p.ProcessName).ToList();
        if (!_app.Demo && names.Count > 0)
        {
            _app.Watcher.Acquire(Consumer, names);
            _watching = true;
        }
        else if (_watching)
        {
            _app.Watcher.Release(Consumer);
            _watching = false;
        }
    }

    internal static bool SamePlace(WindowProfile a, WindowProfile b) =>
        Same(a.ProcessName, b.ProcessName) && a.X == b.X && a.Y == b.Y && a.Width == b.Width && a.Height == b.Height
        && a.RemoveBorders == b.RemoveBorders && a.HideTitleBar == b.HideTitleBar;

    public WindowProfile? FindProfile(string name) => S.Profiles.FirstOrDefault(p => Same(p.Name, name));

    public WindowGroup? FindGroup(string name) => S.Groups.FirstOrDefault(g => Same(g.Name, name));

    public static bool IsRunning(WindowProfile p, HashSet<string> running) =>
        !string.IsNullOrWhiteSpace(p.ProcessName) && running.Contains(AppWatcher.Normalize(p.ProcessName));

    public async Task ApplyGroupAsync(WindowGroup group)
    {
        var members = S.Members(group).ToList();
        if (members.Count == 0)
        {
            _app.Notify($"“{group.Name}” is empty", "Add profiles to the group first.", NoticeKind.Info);
            return;
        }
        await ApplyRunningAsync(members, $"“{group.Name}”");
    }

    public void ApplyKey(string hotkey)
    {
        var groups = S.Groups.Where(g => Hotkeys.Hotkey.SameKey(g.Hotkey, hotkey)).ToList();
        bool group = groups.Count > 0;
        if (groups.Count == 1 && !S.Profiles.Any(p => Hotkeys.Hotkey.SameKey(p.Hotkey, hotkey)))
        {
            _ = ApplyGroupAsync(groups[0]);
            return;
        }
        var profiles = S.Profiles.Where(p => Hotkeys.Hotkey.SameKey(p.Hotkey, hotkey))
            .Concat(S.Groups.Where(g => Hotkeys.Hotkey.SameKey(g.Hotkey, hotkey)).SelectMany(S.Members))
            .Distinct().ToList();
        if (profiles.Count == 1 && !group) _ = ApplyAsync(profiles[0]);
        else _ = ApplyRunningAsync(profiles, Hotkeys.Hotkey.Display(hotkey));
    }

    private async Task ApplyRunningAsync(List<WindowProfile> profiles, string what)
    {
        if (_app.Demo)
        {
            _app.Notify("Demo mode", $"This would move the windows of {what}. Nothing is changed in demo mode.", NoticeKind.Info);
            return;
        }
        var running = AppWatcher.Snapshot();
        var targets = profiles.Where(p => IsRunning(p, running)).ToList();
        if (targets.Count == 0)
        {
            _app.Notify($"Nothing to move for {what}", "None of its programs are running.", NoticeKind.Info);
            return;
        }
        var problems = new List<string>();
        foreach (var p in targets)
            if (Problem(p, await ApplyAsync(p, quiet: true)) is { } problem) problems.Add(problem);
        if (problems.Count == 0) _app.Notify($"{what} applied", string.Join(", ", targets.Select(p => p.Label)), NoticeKind.Success);
        else _app.Notify($"{what}: not every window moved", string.Join("\n", problems), NoticeKind.Warning);
    }

    public static string? Problem(WindowProfile p, WindowApplyResult result) => result switch
    {
        WindowApplyResult.Applied => null,
        WindowApplyResult.NotRunning => $"{p.ProcessName} isn't running.",
        WindowApplyResult.NoWindow => $"{p.ProcessName} is running but has no window yet.",
        WindowApplyResult.AccessDenied => $"Windows didn't let Pit-Box move {p.ProcessName}'s window.",
        _ => $"{p.ProcessName}'s window didn't end up where the profile says. Some games resize themselves; try a longer delay, or borderless in the game's settings.",
    };

    public async Task<WindowApplyResult> ApplyAsync(WindowProfile profile, bool quiet = false, bool retry = true, bool watch = true)
    {
        if (_app.Demo)
        {
            if (!quiet) _app.Notify("Demo mode", $"This would move {profile.ProcessName}'s window to {Describe(profile)}. Nothing is changed in demo mode.", NoticeKind.Info);
            return WindowApplyResult.Applied;
        }
        var snapshot = profile.Copy();
        var result = await Task.Run(async () =>
        {
            for (int attempt = 0; ; attempt++)
            {
                if (!Active) return WindowApplyResult.Failed;
                var r = Win32.PerMonitor(() => ApplyOnce(snapshot, watch));
                if (r != WindowApplyResult.NoWindow || !retry || attempt >= 2) return r;
                await Task.Delay(5000);
            }
        });
        if (!quiet && Active)
        {
            if (Problem(profile, result) is { } problem) _app.Notify($"“{profile.Label}” not applied", problem, NoticeKind.Warning);
            else _app.Notify($"“{profile.Label}” applied", $"{profile.ProcessName}: {Describe(profile)}.", NoticeKind.Success);
        }
        return result;
    }

    public static string Describe(WindowProfile p) =>
        (p.Width is { } w && p.Height is { } h ? $"{w}×{h}" : "same size") + $" at {p.X}, {p.Y}" + (p.RemoveBorders ? ", borderless" : "");

    private WindowApplyResult ApplyOnce(WindowProfile p, bool watch)
    {
        var pids = ProcessIds(p.ProcessName);
        if (pids.Count == 0) return WindowApplyResult.NotRunning;
        if (Win32.LargestWindow(pids) is { } found)
            return MoveAndHold(found.Window, found.Pid, p, watch);
        if (Win32.PidByTitle(p.Name) is { } titled && Win32.LargestWindow(new[] { titled }) is { } byTitle)
            return MoveAndHold(byTitle.Window, titled, p, watch);
        return WindowApplyResult.NoWindow;
    }

    private WindowApplyResult MoveAndHold(IntPtr hwnd, int pid, WindowProfile p, bool watch)
    {
        StopHolding(hwnd);
        var target = Target(hwnd, p);
        var result = MoveAndCheck(hwnd, p, target);
        if (result == WindowApplyResult.Applied && watch) Watch(hwnd, pid, p, target);
        return result;
    }

    private void StopHolding(IntPtr hwnd)
    {
        lock (_gate)
            foreach (var id in _watchers.Where(w => w.Value.Window == hwnd).Select(w => w.Key).ToList())
                if (_watchers.Remove(id, out var held)) held.Hold.Cancel();
    }

    private static List<int> ProcessIds(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return new List<int>();
        var list = Process.GetProcessesByName(AppWatcher.Normalize(processName));
        var ids = list.Select(p => p.Id).ToList();
        foreach (var p in list) p.Dispose();
        return ids;
    }

    private static WindowApplyResult MoveAndCheck(IntPtr hwnd, WindowProfile p, (int X, int Y, int W, int H) target)
    {
        Win32.Restore(hwnd);
        if (p.RemoveBorders) Win32.RemoveBorders(hwnd);
        if (!Win32.MoveWindow(hwnd, target.X, target.Y, target.W, target.H, true))
            return Marshal.GetLastWin32Error() == 5 ? WindowApplyResult.AccessDenied : WindowApplyResult.Failed;
        if (p.RemoveBorders) Win32.HideUwpTitleBar(hwnd);
        return Win32.Rect(hwnd) == target ? WindowApplyResult.Applied : WindowApplyResult.Failed;
    }

    private static (int X, int Y, int W, int H) Target(IntPtr hwnd, WindowProfile p)
    {
        var now = Win32.Rect(hwnd);
        int w = p.Width ?? now.W, h = p.Height ?? now.H;
        if (p.HideTitleBar && Win32.UwpContentOffset(hwnd) is { } shift)
            return (p.X, p.Y - shift, w, h + shift);
        return (p.X, p.Y, w, h);
    }

    private void Watch(IntPtr hwnd, int pid, WindowProfile p, (int X, int Y, int W, int H) target)
    {
        var cts = new CancellationTokenSource();
        lock (_gate)
        {
            if (!Active) { cts.Dispose(); return; }
            if (_watchers.Remove(p.Id, out var old)) old.Hold.Cancel();
            _watchers[p.Id] = (hwnd, cts, p);
        }
        _ = Task.Run(async () =>
        {
            try
            {
                for (int i = 0; i < 15 && !cts.IsCancellationRequested; i++)
                {
                    await Task.Delay(i < 10 ? 1000 : 5000, cts.Token);
                    if (!Win32.IsWindow(hwnd) || Win32.Pid(hwnd) != pid) return;
                    if (Win32.IsIconic(hwnd)) continue;
                    Win32.PerMonitor(() =>
                    {
                        if (Win32.Rect(hwnd) != target)
                            MoveAndCheck(hwnd, p, target);
                        return 0;
                    });
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                lock (_gate)
                {
                    if (_watchers.TryGetValue(p.Id, out var current) && current.Hold == cts) _watchers.Remove(p.Id);
                }
                cts.Dispose();
            }
        });
    }

    private void OnProgramStarted(string name)
    {
        if (_app.Demo || _app.Watcher.CatchingUp) return;
        foreach (var p in Automatic.Where(p => AppWatcher.Normalize(p.ProcessName) == name).ToList())
            _ = ApplyWhenStartedAsync(p);
    }

    private async Task ApplyWhenStartedAsync(WindowProfile profile)
    {
        try
        {
            await Task.Delay(Math.Clamp(profile.DelayMs, 0, WindowProfileSettings.MaxDelayMs));
            if (!Automatic.Contains(profile)) return;
            var result = await ApplyAsync(profile, quiet: true);
            if (Problem(profile, result) is { } problem)
                _app.Notify($"“{profile.Label}” not applied", problem, NoticeKind.Warning);
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
    }

    public static (int X, int Y, int W, int H)? CurrentRect(string processName) =>
        Win32.PerMonitor<(int, int, int, int)?>(() =>
        {
            return Win32.LargestWindow(ProcessIds(processName)) is { } found ? Win32.Rect(found.Window) : null;
        });

    public static List<(string Process, string Title)> ProgramsWithWindows() => Win32.TitledWindows()
        .Select(w =>
        {
            try { using var p = Process.GetProcessById(w.Pid); return (Process: p.ProcessName + ".exe", w.Title); }
            catch { return (Process: "", w.Title); }
        })
        .Where(w => w.Process.Length > 0 && !w.Process.Equals(System.IO.Path.GetFileName(Environment.ProcessPath), StringComparison.OrdinalIgnoreCase))
        .GroupBy(w => w.Process, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
        .OrderBy(w => w.Process, StringComparer.OrdinalIgnoreCase).ToList();

    public static List<OpenWindow> OpenWindows() => Win32.PerMonitor(() =>
    {
        int self = Environment.ProcessId;
        var largest = new Dictionary<int, (string Title, (int X, int Y, int W, int H) Rect)>();
        foreach (var (pid, title, rect) in Win32.AppWindows())
        {
            if (pid == self) continue;
            if (!largest.TryGetValue(pid, out var held) || (long)rect.W * rect.H > (long)held.Rect.W * held.Rect.H) largest[pid] = (title, rect);
        }
        var list = new List<OpenWindow>();
        foreach (var (pid, w) in largest)
        {
            string process;
            try { using var p = Process.GetProcessById(pid); process = p.ProcessName + ".exe"; }
            catch { continue; }
            list.Add(new OpenWindow(process, w.Title, w.Rect.X, w.Rect.Y, w.Rect.W, w.Rect.H));
        }
        return list.GroupBy(w => w.Process, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.MaxBy(w => (long)w.Width * w.Height)!)
            .OrderBy(w => w.Process, StringComparer.OrdinalIgnoreCase).ToList();
    });

    private void StopWatching()
    {
        lock (_gate)
        {
            foreach (var held in _watchers.Values) held.Hold.Cancel();
            _watchers.Clear();
        }
    }

    public void Dispose()
    {
        _app.Watcher.Started -= OnProgramStarted;
        if (_watching) _app.Watcher.Release(Consumer);
        StopWatching();
    }

    private static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static class Win32
    {
        private const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
        private const long WS_VISIBLE = 0x10000000, WS_THICKFRAME = 0x00040000, WS_DLGFRAME = 0x00400000, WS_BORDER = 0x00800000;
        private const long WS_EX_TOOLWINDOW = 0x80;
        private const int DWMWA_CLOAKED = 14;
        private const long WS_EX_DLGMODALFRAME = 0x1, WS_EX_WINDOWEDGE = 0x100, WS_EX_CLIENTEDGE = 0x200, WS_EX_STATICEDGE = 0x20000;
        private const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_FRAMECHANGED = 0x20, SWP_NOOWNERZORDER = 0x200, SWP_NOSENDCHANGING = 0x400;
        private static readonly IntPtr PerMonitorAwareV2 = new(-4);

        private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        public static T PerMonitor<T>(Func<T> work)
        {
            var old = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
            try { return work(); }
            finally { if (old != IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
        }

        public static (int X, int Y, int W, int H) Rect(IntPtr hwnd) =>
            GetWindowRect(hwnd, out var r) ? (r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : default;

        public static int Pid(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            return (int)pid;
        }

        private static bool Visible(IntPtr hwnd) => (GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & WS_VISIBLE) != 0;

        private static string ClassName(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            return GetClassNameW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        private static string Title(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            return GetWindowTextW(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        public static (IntPtr Window, int Pid)? LargestWindow(IReadOnlyCollection<int> pids)
        {
            IntPtr best = IntPtr.Zero;
            int bestPid = 0;
            long bestArea = 0;
            EnumWindows((hwnd, _) =>
            {
                int pid = Pid(hwnd);
                if (!pids.Contains(pid) || !Visible(hwnd) || IsToolWindow(hwnd) || IsCloaked(hwnd)) return true;
                var r = Rect(hwnd);
                long area = (long)r.W * r.H;
                if (area > bestArea) { bestArea = area; best = hwnd; bestPid = pid; }
                return true;
            }, IntPtr.Zero);
            return best == IntPtr.Zero ? null : (best, bestPid);
        }

        private static bool IsToolWindow(IntPtr hwnd) => (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0;

        private static bool IsCloaked(IntPtr hwnd) => DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

        public static int? PidByTitle(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            int? found = null;
            long bestArea = -1;
            EnumWindows((hwnd, _) =>
            {
                if (!Visible(hwnd) || !string.Equals(Title(hwnd).Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
                var r = Rect(hwnd);
                long area = (long)r.W * r.H;
                if (area > bestArea) { bestArea = area; found = Pid(hwnd); }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal) { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        public static List<(int Pid, string Title, (int X, int Y, int W, int H) Rect)> AppWindows()
        {
            var list = new List<(int, string, (int, int, int, int))>();
            EnumWindows((hwnd, _) =>
            {
                if (!Visible(hwnd) || IsIconic(hwnd) || IsToolWindow(hwnd) || IsCloaked(hwnd) || ShellClasses.Contains(ClassName(hwnd))) return true;
                if (Title(hwnd) is not { Length: > 0 } title) return true;
                var r = Rect(hwnd);
                if (r.W >= 160 && r.H >= 100) list.Add((Pid(hwnd), title, r));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static List<(int Pid, string Title)> TitledWindows()
        {
            var list = new List<(int, string)>();
            EnumWindows((hwnd, _) =>
            {
                if (Visible(hwnd) && Title(hwnd) is { Length: > 0 } title) list.Add((Pid(hwnd), title));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        public static void Restore(IntPtr hwnd)
        {
            const int SW_RESTORE = 9;
            if (IsIconic(hwnd) || IsZoomed(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        }

        public static void RemoveBorders(IntPtr hwnd)
        {
            long style = GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64() & ~(WS_THICKFRAME | WS_DLGFRAME | WS_BORDER);
            SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));
            long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & ~(WS_EX_DLGMODALFRAME | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE | WS_EX_STATICEDGE);
            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING | SWP_FRAMECHANGED);
        }

        public static void HideUwpTitleBar(IntPtr hwnd)
        {
            if (ClassName(hwnd) != "ApplicationFrameWindow") return;
            EnumChildWindows(hwnd, (child, _) =>
            {
                if (ClassName(child) == "ApplicationFrameTitleBarWindow") ShowWindow(child, 0);
                return true;
            }, IntPtr.Zero);
        }

        public static int? UwpContentOffset(IntPtr hwnd)
        {
            if (ClassName(hwnd) != "ApplicationFrameWindow") return null;
            int frameTop = Rect(hwnd).Y;
            int? coreTop = null;
            EnumChildWindows(hwnd, (child, _) =>
            {
                if (ClassName(child) != "Windows.UI.Core.CoreWindow") return true;
                coreTop = Rect(child).Y;
                return false;
            }, IntPtr.Zero);
            return coreTop is { } top && top - frameTop > 0 ? top - frameTop : null;
        }
    }
}
