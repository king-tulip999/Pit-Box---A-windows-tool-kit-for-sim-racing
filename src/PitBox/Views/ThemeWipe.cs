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
    private double _start = double.NaN;
    private int _frames;

    private ThemeWipe(Grid host, Image shot)
    {
        _host = host;
        _shot = shot;
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
        wipe.Clip(0);
        FrameDriver.Run(wipe);
    }

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
