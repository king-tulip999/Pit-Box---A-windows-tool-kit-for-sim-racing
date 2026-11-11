using System.Windows;
using System.Windows.Media;
using Core.Theme;

namespace Core.Views;

internal static class Dividers
{
    public static Color? AccentOf(FrameworkElement element) => (element.TryFindResource("Accent") as SolidColorBrush)?.Color;

    public static void Draw(DrawingContext dc, double left, double right, double y, bool fromStart = false, Color? accent = null)
    {
        if (right - left < 40) return;
        double c = Center(y);
        var line = new LineGeometry(new Point(left, c), new Point(right, c));
        line.Freeze();
        dc.PushOpacityMask(Fade(fromStart));
        Stroke(dc, line, accent);
        dc.Pop();
    }

    public const double HaloWidth = 11;

    private static readonly double[] HaloBands = [11, 9, 7, 5, 3];

    internal static double Center(double v) => Math.Floor(v) + Hairline.Width / 2;

    internal static void Stroke(DrawingContext dc, Geometry path, Color? accent)
    {
        if (Pens(accent) is not var (halo, core)) return;
        foreach (var pen in halo) dc.DrawGeometry(null, pen, path);
        dc.DrawGeometry(null, core, path);
    }

    private static (Pen[] Halo, Pen Core)? _pens;
    private static Color _pensAccent;
    private static double _pensScale;
    private static ThemeSpec? _pensSpec;

    private static (Pen[] Halo, Pen Core)? Pens(Color? wanted)
    {
        if ((wanted ?? GlobalAccent()) is not { } accent) return null;
        var spec = ThemeManager.Current;
        if (_pens is { } cached && _pensAccent == accent && _pensScale == Hairline.Scale && ReferenceEquals(_pensSpec, spec)) return cached;
        double glow = Math.Min(0.95, spec.GlowStrength * (spec.Dark ? 0.22 : 0.16)), line = spec.Dark ? 0.7 : 0.85;
        double layer = 1 - Math.Pow(1 - glow, 1.0 / HaloBands.Length);
        Color A(double alpha) => Color.FromArgb((byte)Math.Round(255 * alpha), accent.R, accent.G, accent.B);
        var halo = HaloBands.Select(width => Frozen(new Pen(new SolidColorBrush(A(layer)), width))).ToArray();
        var core = Frozen(new Pen(new SolidColorBrush(A(line)), Hairline.Width));
        (_pensAccent, _pensScale, _pensSpec) = (accent, Hairline.Scale, spec);
        _pens = (halo, core);
        return _pens;
    }

    private static Pen Frozen(Pen pen)
    {
        pen.StartLineCap = pen.EndLineCap = PenLineCap.Flat;
        pen.LineJoin = PenLineJoin.Round;
        pen.Freeze();
        return pen;
    }

    private static Color? GlobalAccent() => Ui.Res<Brush>("Accent") is SolidColorBrush { Color: var accent } ? accent : null;

    private static Brush? _fadeIn, _fadeOut;

    private static Brush Fade(bool fromStart)
    {
        if ((fromStart ? _fadeOut : _fadeIn) is { } made) return made;
        var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
        if (fromStart)
        {
            fade.GradientStops.Add(new GradientStop(Colors.Black, 0));
            fade.GradientStops.Add(new GradientStop(Colors.Black, 0.25));
            fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        }
        else
        {
            fade.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            fade.GradientStops.Add(new GradientStop(Colors.Black, 0.18));
            fade.GradientStops.Add(new GradientStop(Colors.Black, 0.82));
            fade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        }
        fade.Freeze();
        if (fromStart) _fadeOut = fade;
        else _fadeIn = fade;
        return fade;
    }
}

internal sealed class DividerLine : FrameworkElement
{
    public DividerLine()
    {
        IsHitTestVisible = false;
        Height = 9;
    }

    public bool FromStart { get; set; }

    protected override void OnRender(DrawingContext dc) =>
        Dividers.Draw(dc, 0, ActualWidth, Math.Floor(ActualHeight / 2), FromStart, Dividers.AccentOf(this));
}
