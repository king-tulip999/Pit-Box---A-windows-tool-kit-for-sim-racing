using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PitBox.Views;

internal sealed class ThemeWipe : IFrameStep
{
    private const double WipeMs = 560, Lean = 0.3;

    private readonly Grid _host;
    private readonly Image _shot;
    private readonly double _start;

    private ThemeWipe(Grid host, Image shot)
    {
        _host = host;
        _shot = shot;
        _start = FrameDriver.Now;
    }

    private static ThemeWipe? _running;

    public static void Begin(Grid host, FrameworkElement content)
    {
        if (!Motion.Flourish || !content.IsVisible || content.ActualWidth < 1 || content.ActualHeight < 1) return;
        _running?.End();
        var dpi = VisualTreeHelper.GetDpi(content);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(content);
        bitmap.Freeze();
        var shot = new Image { Source = bitmap, Stretch = Stretch.Fill, Width = content.ActualWidth, Height = content.ActualHeight, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        Panel.SetZIndex(shot, 1);
        host.Children.Add(shot);
        var wipe = new ThemeWipe(host, shot);
        _running = wipe;
        wipe.Step(wipe._start);
        FrameDriver.Run(wipe);
    }

    public bool Step(double now)
    {
        double t = (now - _start) / WipeMs;
        if (t >= 1)
        {
            End();
            return false;
        }
        double w = _shot.Width, h = _shot.Height, lean = h * Lean;
        double x = -lean + FrameDriver.EaseOut(t) * (w + lean);
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
        return true;
    }

    private void End()
    {
        FrameDriver.Stop(this);
        _host.Children.Remove(_shot);
        if (_running == this) _running = null;
    }
}
