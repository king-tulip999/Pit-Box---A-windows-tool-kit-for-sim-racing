using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Core.Views;

internal sealed class ThemeWipe : IFrameStep
{
    private const double WipeMs = 340, Lean = 0.3, ShotScale = 0.5;

    private readonly Grid _host;
    private readonly Image _shot;
    private double _start = double.NaN;
    private int _frames;

    private ThemeWipe(Grid host, Image shot)
    {
        _host = host;
        _shot = shot;
    }

    private static ThemeWipe? _running;

    public static bool Begin(Grid host, FrameworkElement content)
    {
        if (!Motion.Flourish || !content.IsVisible || content.ActualWidth < 1 || content.ActualHeight < 1) return false;
        _running?.End();
        var bitmap = FromScreen(content) ?? Rendered(content);
        var shot = new Image { Source = bitmap, Stretch = Stretch.Fill, Width = content.ActualWidth, Height = content.ActualHeight, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Panel.SetZIndex(shot, 1);
        host.Children.Add(shot);
        var wipe = new ThemeWipe(host, shot);
        _running = wipe;
        wipe.Clip(0);
        FrameDriver.Run(wipe);
        return true;
    }

    private static BitmapSource Rendered(FrameworkElement content)
    {
        var dpi = VisualTreeHelper.GetDpi(content);
        double zoom = WindowScale.Of(content) * ShotScale;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX * zoom), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY * zoom),
            dpi.PixelsPerInchX * zoom, dpi.PixelsPerInchY * zoom, PixelFormats.Pbgra32);
        bitmap.Render(content);
        bitmap.Freeze();
        return bitmap;
    }

    private static BitmapSource? FromScreen(FrameworkElement content)
    {
        if (Window.GetWindow(content) is not { IsActive: true, WindowState: not WindowState.Minimized } || PresentationSource.FromVisual(content) is null) return null;
        Point topLeft, bottomRight;
        try
        {
            topLeft = content.PointToScreen(new Point(0, 0));
            bottomRight = content.PointToScreen(new Point(content.ActualWidth, content.ActualHeight));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        int x = (int)Math.Round(topLeft.X), y = (int)Math.Round(topLeft.Y);
        int width = (int)Math.Round(bottomRight.X) - x, height = (int)Math.Round(bottomRight.Y) - y;
        var screen = System.Windows.Forms.SystemInformation.VirtualScreen;
        if (width < 1 || height < 1 || x < screen.Left || y < screen.Top || x + width > screen.Right || y + height > screen.Bottom) return null;
        IntPtr screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        IntPtr memoryDc = IntPtr.Zero, picture = IntPtr.Zero, previous = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            picture = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || picture == IntPtr.Zero) return null;
            previous = SelectObject(memoryDc, picture);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, SourceCopy)) return null;
            SelectObject(memoryDc, previous);
            previous = IntPtr.Zero;
            var bitmap = Imaging.CreateBitmapSourceFromHBitmap(picture, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (previous != IntPtr.Zero) SelectObject(memoryDc, previous);
            if (picture != IntPtr.Zero) DeleteObject(picture);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private const int SourceCopy = 0x00CC0020;

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, int operation);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr item);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    public bool Step(double now)
    {
        if (++_frames < 2) return true;
        if (double.IsNaN(_start)) _start = now;
        double t = (now - _start) / WipeMs;
        if (t >= 1)
        {
            End();
            return false;
        }
        Clip(FrameDriver.EaseOut(t));
        return true;
    }

    private void Clip(double progress)
    {
        double w = _shot.Width, h = _shot.Height, lean = h * Lean;
        double x = -lean + progress * (w + lean);
        var keep = new StreamGeometry();
        using (var g = keep.Open())
        {
            g.BeginFigure(new Point(x + lean, 0), true, true);
            g.LineTo(new Point(w + lean, 0), false, false);
            g.LineTo(new Point(w + lean, h), false, false);
            g.LineTo(new Point(x, h), false, false);
        }
        keep.Freeze();
        _shot.Clip = keep;
    }

    private void End()
    {
        FrameDriver.Stop(this);
        _host.Children.Remove(_shot);
        if (_running == this) _running = null;
    }
}
