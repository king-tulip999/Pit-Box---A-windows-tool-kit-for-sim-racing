using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GoodieBag.Theme;

[Flags]
public enum Sides
{
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
    All = Left | Top | Right | Bottom,
}

public static class Hairline
{
    public const string WidthKey = "Hairline";

    public static double Scale { get; private set; } = 1;

    public static double Width => Pixels(1, Scale);

    public static double Pixels(double dips, double scale) =>
        dips <= 0 || scale <= 0 ? 0 : Math.Max(1, Math.Floor(dips * scale + 1e-6)) / scale;

    public static Thickness Edge(Thickness dips, double scaleX, double scaleY) =>
        new(Pixels(dips.Left, scaleX), Pixels(dips.Top, scaleY), Pixels(dips.Right, scaleX), Pixels(dips.Bottom, scaleY));

    public static Thickness WholePixels(Thickness dips, double scaleX, double scaleY) =>
        new(WholePixels(dips.Left, scaleX), WholePixels(dips.Top, scaleY), WholePixels(dips.Right, scaleX), WholePixels(dips.Bottom, scaleY));

    public static double WholePixels(double dips, double scale) => scale <= 0 || dips <= 0 ? dips : Math.Floor(dips * scale + 1e-6) / scale;

    public static double ScaleOf(Visual visual)
    {
        if (Services.ScreenshotRunner.DrawsAt96) return 1;
        try { return VisualTreeHelper.GetDpi(visual).DpiScaleX; }
        catch (InvalidOperationException) { return Scale; }
    }

    public static void Use(double scale)
    {
        if (Services.ScreenshotRunner.DrawsAt96) scale = 1;
        if (scale <= 0) return;
        Scale = scale;
        Install();
    }

    public static void Install()
    {
        if (Application.Current?.Resources is not { } res) return;
        double w = Pixels(1, Scale);
        if (res[WidthKey] is double now && now == w) return;
        res[WidthKey] = w;
        foreach (var sides in AllCombinations)
            res[Key(sides)] = Edges(sides, w);
    }

    public static Thickness Edges(Sides sides = Sides.All) => Edges(sides, Pixels(1, Scale));

    private static Thickness Edges(Sides sides, double w) => new(
        sides.HasFlag(Sides.Left) ? w : 0, sides.HasFlag(Sides.Top) ? w : 0, sides.HasFlag(Sides.Right) ? w : 0, sides.HasFlag(Sides.Bottom) ? w : 0);

    private static readonly Sides[] AllCombinations = Enumerable.Range(1, 15).Select(i => (Sides)i).ToArray();

    public static string Key(Sides sides) => sides == Sides.All ? "HairlineEdge" : "HairlineEdge" + sides.ToString().Replace(", ", "");

    public static T Frame<T>(T border, Sides sides = Sides.All) where T : Border
    {
        border.SetResourceReference(Border.BorderThicknessProperty, Key(sides));
        return border;
    }

    public static void Frame(Control control, Sides sides = Sides.All) =>
        control.SetResourceReference(Control.BorderThicknessProperty, Key(sides));

    public static T Rule<T>(T element, Orientation orientation = Orientation.Horizontal) where T : FrameworkElement
    {
        element.SetResourceReference(orientation == Orientation.Horizontal ? FrameworkElement.HeightProperty : FrameworkElement.WidthProperty, WidthKey);
        return element;
    }

    public static System.Windows.Media.Pen Pen(Brush brush, Visual visual, double dips = 1) => Pen(brush, ScaleOf(visual), dips);

    public static System.Windows.Media.Pen Pen(Brush brush, double scale, double dips = 1)
    {
        var pen = new System.Windows.Media.Pen(brush, Pixels(dips, scale));
        pen.Freeze();
        return pen;
    }

    public static double Snap(double at, double scale, double dips = 1)
    {
        double px = Math.Max(1, Math.Floor(dips * scale + 1e-6));
        double start = Nearest(at * scale) - Math.Floor(px / 2);
        return (start + px / 2) / scale;
    }

    public static Rect SnapBox(Rect box, double scale, double dips = 1)
    {
        double half = Pixels(dips, scale) / 2;
        double l = Nearest(box.Left * scale) / scale + half, t = Nearest(box.Top * scale) / scale + half;
        double r = Nearest(box.Right * scale) / scale - half, b = Nearest(box.Bottom * scale) / scale - half;
        return new Rect(l, t, Math.Max(0, r - l), Math.Max(0, b - t));
    }

    private static double Nearest(double devicePixels) => Math.Floor(devicePixels + 0.5);
}
