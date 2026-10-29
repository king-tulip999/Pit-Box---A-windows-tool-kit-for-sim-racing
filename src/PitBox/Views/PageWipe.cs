using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PitBox.Views;

internal sealed class PageWipe : FrameworkElement
{
    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(PageWipe),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(PageWipe),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty EdgeProperty = DependencyProperty.Register(nameof(Edge), typeof(Brush), typeof(PageWipe),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Edge { get => (Brush?)GetValue(EdgeProperty); set => SetValue(EdgeProperty, value); }

    private const double Band = 150, Stripe = 4, Gap = 6, Lean = 0.33, RunMs = 520;

    public PageWipe()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        SetResourceReference(FillProperty, "Bg");
        SetResourceReference(EdgeProperty, "Accent");
    }

    public void Play()
    {
        if (!Motion.Flourish || ActualWidth <= 0) return;
        BeginAnimation(ProgressProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(RunMs)) { EasingFunction = Motion.Ease, FillBehavior = FillBehavior.Stop });
    }

    protected override void OnRender(DrawingContext dc)
    {
        double p = Progress, w = ActualWidth, h = ActualHeight;
        if (p <= 0 || p >= 1 || Fill is null || Edge is null || w <= 0 || h <= 0) return;
        double lean = h * Lean, travel = w + Band + lean;
        double left = -Band - lean + p * travel;
        Quad(dc, Fill, left + Stripe + Gap, Band - 2 * (Stripe + Gap), lean, h);
        Quad(dc, Edge, left, Stripe, lean, h);
        Quad(dc, Edge, left + Band - Stripe, Stripe, lean, h);
    }

    private static void Quad(DrawingContext dc, Brush brush, double x, double width, double lean, double h)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(x + lean, 0), true, true);
            g.LineTo(new Point(x + lean + width, 0), false, false);
            g.LineTo(new Point(x + width, h), false, false);
            g.LineTo(new Point(x, h), false, false);
        }
        geo.Freeze();
        dc.DrawGeometry(brush, null, geo);
    }
}
