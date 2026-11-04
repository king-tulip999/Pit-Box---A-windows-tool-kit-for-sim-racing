using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Views;

namespace PitBox.Controls;

public class Lamp : Border
{
    public const double RiseMs = 260, FallMs = 340;

    public static readonly DependencyProperty OnProperty = DependencyProperty.Register(
        nameof(On), typeof(bool?), typeof(Lamp), new PropertyMetadata(false, (d, _) => ((Lamp)d).Paint(animate: true)));

    public static readonly DependencyProperty HotProperty = DependencyProperty.Register(
        nameof(Hot), typeof(bool), typeof(Lamp), new PropertyMetadata(false, (d, _) => ((Lamp)d).Paint(animate: false)));

    private readonly Border _lit;
    private readonly ScaleTransform _rise = new(1, 0);
    private string _litKey = "Accent";

    public Lamp()
    {
        Width = 15;
        Height = 15;
        BorderThickness = new Thickness(1.5);
        SnapsToDevicePixels = true;
        var tick = new System.Windows.Shapes.Path
        {
            Stretch = Stretch.Uniform,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Square,
            StrokeEndLineCap = PenLineCap.Square,
            Margin = new Thickness(1.5),
        };
        tick.SetResourceReference(System.Windows.Shapes.Path.DataProperty, "I.Check");
        tick.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "OnAccent");
        _lit = new Border { RenderTransformOrigin = new Point(0.5, 1), RenderTransform = _rise, Child = tick };
        Child = _lit;
        Paint(animate: false);
    }

    public bool? On
    {
        get => (bool?)GetValue(OnProperty);
        set => SetValue(OnProperty, value);
    }

    public bool Hot
    {
        get => (bool)GetValue(HotProperty);
        set => SetValue(HotProperty, value);
    }

    public string LitKey
    {
        get => _litKey;
        set { _litKey = value; Paint(animate: false); }
    }

    private void Paint(bool animate)
    {
        bool on = On == true;
        SetResourceReference(BorderBrushProperty, on || Hot ? _litKey : "LineStrong");
        _lit.SetResourceReference(BackgroundProperty, _litKey);
        double to = on ? 1 : 0;
        if (animate && IsLoaded)
            Motion.To(_rise, ScaleTransform.ScaleYProperty, to, on ? RiseMs : FallMs, ease: Motion.Smooth);
        else
        {
            _rise.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _rise.ScaleY = to;
        }
    }
}
