using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PitBox.Views;

namespace PitBox.Controls;

public class Lamp : Border
{
    public const double RiseMs = 260, FallMs = 340;
    private const double FillMs = 150, DrawMs = 280, TickDash = 10;

    public static readonly DependencyProperty OnProperty = DependencyProperty.Register(
        nameof(On), typeof(bool?), typeof(Lamp), new PropertyMetadata(false, (d, _) => ((Lamp)d).Paint(animate: true)));

    public static readonly DependencyProperty HotProperty = DependencyProperty.Register(
        nameof(Hot), typeof(bool), typeof(Lamp), new PropertyMetadata(false, (d, _) => ((Lamp)d).Paint(animate: false)));

    private readonly Border _lit;
    private readonly Path _tick;
    private bool _shown;
    private const string LitKey = "Accent";

    public Lamp()
    {
        Width = 18;
        Height = 18;
        BorderThickness = new Thickness(2);
        SnapsToDevicePixels = true;
        _tick = new Path
        {
            Stretch = Stretch.Uniform,
            StrokeThickness = 2.2,
            StrokeStartLineCap = PenLineCap.Flat,
            StrokeEndLineCap = PenLineCap.Square,
            StrokeLineJoin = PenLineJoin.Miter,
            StrokeDashArray = new DoubleCollection { TickDash, TickDash },
            StrokeDashCap = PenLineCap.Flat,
            Margin = new Thickness(1.5, 2, 1.5, 2),
        };
        _tick.SetResourceReference(Path.DataProperty, "I.Tick");
        _tick.SetResourceReference(Shape.StrokeProperty, "OnAccent");
        _lit = new Border { Child = _tick };
        _lit.SetResourceReference(BackgroundProperty, LitKey);
        Child = _lit;
        _lit.Opacity = 0;
        _tick.StrokeDashOffset = TickDash;
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

    private void Paint(bool animate)
    {
        bool on = On == true;
        SetResourceReference(BorderBrushProperty, on ? LitKey : Hot ? "TextMuted" : "LineStrong");
        if (on == _shown) return;
        _shown = on;
        if (animate && IsLoaded)
        {
            Motion.To(_lit, OpacityProperty, on ? 1 : 0, FillMs, ease: Motion.Smooth);
            if (on)
            {
                Settle(_tick, Shape.StrokeDashOffsetProperty, TickDash);
                Motion.To(_tick, Shape.StrokeDashOffsetProperty, 0, DrawMs, ease: Motion.Smooth);
            }
            return;
        }
        Settle(_lit, OpacityProperty, on ? 1 : 0);
        Settle(_tick, Shape.StrokeDashOffsetProperty, on ? 0 : TickDash);
    }

    private static void Settle(UIElement target, DependencyProperty property, double value)
    {
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
    }
}
