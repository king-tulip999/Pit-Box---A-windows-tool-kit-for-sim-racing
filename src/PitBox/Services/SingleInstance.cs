namespace PitBox.Services;

internal static class SingleInstance
{
    private const string MutexName = @"Local\PitBox.SingleInstance";
    private const string ShowEventName = @"Local\PitBox.ShowWindow";

    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;

    public static bool TryAcquire()
    {
        if (Mutex.TryOpenExisting(Config.LegacyName.AppMutex, out var old))
        {
            old.Dispose();
            return false;
        }
        _mutex = new Mutex(true, MutexName, out bool created);
        if (!created)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        return true;
    }

    public static void SignalExisting()
    {
        foreach (var name in new[] { ShowEventName, Config.LegacyName.ShowEvent })
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(name);
                ev.Set();
            }
            catch
            {
            }
        }
    }

    public static void Listen(Action show)
    {
        var showEvent = _showEvent ??= new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var thread = new Thread(() =>
        {
            while (showEvent.WaitOne())
                show();
        })
        { IsBackground = true, Name = "Pit-Box single instance" };
        thread.Start();
    }

    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
    }
}
