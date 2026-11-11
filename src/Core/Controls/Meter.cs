using System.Windows;
using System.Windows.Media;

using Core.Theme;

namespace Core.Controls;
public sealed class Meter : FrameworkElement
{
    public const double Pad = 2;
    private const double Edge = 1.5;
    private const double TieredWarnFrom = 0.75;

    static Meter()
    {
        HeightProperty.OverrideMetadata(typeof(Meter), new FrameworkPropertyMetadata(double.NaN, null,
            (_, v) => v is double h && !double.IsNaN(h) && !double.IsInfinity(h) ? h + 2 * Pad : v));
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(Meter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush Fill { get; set; } = ThemeManager.Brush("Mono", Brushes.SteelBlue);

    public bool Tiered { get; set; }

    public int Steps { get; set; }

    public bool StepsUseFill { get; set; }

    public Meter()
    {
        Height = 5;
    }

    private Brush? _penBrush;
    private Pen? _pen;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 2 * Pad || h <= 2 * Pad) return;
        var track = ThemeManager.Brush("MeterTrack", Brushes.LightGray);
        double value = Math.Clamp(Value, 0, 1);
        Brush edge = Steps > 0
            ? StepsUseFill ? Fill : ThemeManager.StepBrush()
            : Tiered ? (value < TieredWarnFrom ? ThemeManager.Brush("LineStrong", Brushes.Gray) : ThemeManager.RampBrush(value)) : Fill;
        double r = Math.Min(h / 2, ThemeManager.Round(3).TopLeft), ri = Math.Max(0, Math.Min((h - 2 * Pad) / 2, r - Pad / 2));
        var inner = new Rect(Pad, Pad, w - 2 * Pad, h - 2 * Pad);

        if (_pen is null || !ReferenceEquals(_penBrush, edge))
        {
            _penBrush = edge;
            _pen = new Pen(edge, Edge);
            _pen.Freeze();
        }
        dc.DrawRoundedRectangle(track, _pen, new Rect(Edge / 2, Edge / 2, w - Edge, h - Edge), r, r);

        if (Steps > 0)
        {
            double gap = Math.Clamp(inner.Width / Steps * 0.25, 1, 4);
            double seg = (inner.Width - gap * (Steps - 1)) / Steps, done = value * Steps;
            for (int i = 0; i < Steps; i++)
            {
                double part = Math.Clamp(done - i, 0, 1);
                if (part <= 0.02) continue;
                var box = new Rect(inner.X + i * (seg + gap), inner.Y, Math.Max(0, Math.Min(seg, Math.Max(inner.Height, seg * part))), inner.Height);
                var brush = StepsUseFill ? Fill : ThemeManager.StepBrush();
                dc.DrawRoundedRectangle(brush, null, box, Math.Min(ri, seg / 2), Math.Min(ri, seg / 2));
            }
            return;
        }

        double fw = value * inner.Width;
        if (fw <= 0.5) return;
        double end = Math.Max(inner.Height, fw);
        var fill = ThemeManager.Brush("MeterFill", Brushes.SteelBlue);
        dc.DrawRoundedRectangle(fill, null, new Rect(inner.X, inner.Y, end, inner.Height), ri, ri);
        if (ThemeManager.MeterHead is { } head && end >= 8)
            dc.DrawRectangle(head, null, new Rect(inner.X + end - 3, inner.Y, 3, inner.Height));
    }
}
