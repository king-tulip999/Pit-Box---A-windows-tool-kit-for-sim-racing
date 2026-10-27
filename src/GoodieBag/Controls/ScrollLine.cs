using System.Windows;
using System.Windows.Media;

namespace GoodieBag.Controls;

public sealed class ScrollLine : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ScrollLine),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TaperProperty = DependencyProperty.Register(nameof(Taper), typeof(double), typeof(ScrollLine),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FadeProperty = DependencyProperty.Register(nameof(Fade), typeof(double), typeof(ScrollLine),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double Taper
    {
        get => (double)GetValue(TaperProperty);
        set => SetValue(TaperProperty, value);
    }

    public double Fade
    {
        get => (double)GetValue(FadeProperty);
        set => SetValue(FadeProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (Fill is null || w <= 0 || h <= 0) return;
        bool across = w > h;
        double length = across ? w : h, thick = across ? h : w;
        double point = Math.Min(Taper, length / 2), fade = Math.Min(Fade, length / 2);

        Point At(double along, double side) => across ? new Point(along, side) : new Point(side, along);

        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(At(0, thick / 2), true, true);
            g.LineTo(At(point, thick), false, false);
            g.LineTo(At(length - point, thick), false, false);
            g.LineTo(At(length, thick / 2), false, false);
            g.LineTo(At(length - point, 0), false, false);
            g.LineTo(At(point, 0), false, false);
        }
        shape.Freeze();

        if (fade > 0)
        {
            var mask = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = At(0, 0), EndPoint = At(length, 0) };
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            mask.GradientStops.Add(new GradientStop(Colors.Black, fade / length));
            mask.GradientStops.Add(new GradientStop(Colors.Black, 1 - fade / length));
            mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            mask.Freeze();
            dc.PushOpacityMask(mask);
            dc.DrawGeometry(Fill, null, shape);
            dc.Pop();
        }
        else dc.DrawGeometry(Fill, null, shape);
    }
}
