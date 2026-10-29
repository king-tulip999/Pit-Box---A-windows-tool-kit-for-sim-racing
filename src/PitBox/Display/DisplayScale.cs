using System.Runtime.InteropServices;

namespace PitBox.Display;

internal static class DisplayScale
{
    private const uint MonitorDefaultToNull = 0;
    private const int EffectiveDpi = 0;
    private const double BaseDpi = 96;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    public static int? Percent(MonitorInfo monitor)
    {
        if (!monitor.Active || monitor.Width <= 0 || monitor.Height <= 0) return null;
        try
        {
            var handle = MonitorFromPoint(new Point { X = monitor.X + monitor.Width / 2, Y = monitor.Y + monitor.Height / 2 }, MonitorDefaultToNull);
            if (handle == IntPtr.Zero || GetDpiForMonitor(handle, EffectiveDpi, out uint dpi, out _) != 0 || dpi == 0) return null;
            return (int)Math.Round(dpi / BaseDpi * 100);
        }
        catch (DllNotFoundException) { return null; }
        catch (EntryPointNotFoundException) { return null; }
    }
}
