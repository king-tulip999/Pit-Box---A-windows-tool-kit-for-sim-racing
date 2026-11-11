using System.Windows;
using System.Windows.Media;

namespace Core.Theme;

internal static class CardShadow
{
    public const int Layers = 9;
    public const double Drop = 0.6, Rise = 3;

    private static readonly Brush Layer = Frozen(new SolidColorBrush(Color.FromArgb(12, 0, 0, 0)));

    public static DrawingGroup Build(double width, double height, double radius)
    {
        var card = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
        card.Freeze();
        var group = new DrawingGroup();
        for (int k = 1; k <= Layers; k++)
        {
            var spread = Spread(k);
            var outer = new Rect(-spread.Side, spread.Top, width + 2 * spread.Side, height - spread.Top + spread.Bottom);
            if (outer.Width <= 0 || outer.Height <= 0) continue;
            double r = radius + spread.Side;
            var ring = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(outer, r, r), card);
            ring.Freeze();
            group.Children.Add(new GeometryDrawing(Layer, null, ring));
        }
        group.Freeze();
        return group;
    }

    public static (double Side, double Top, double Bottom) Spread(int layer)
    {
        double side = layer * 0.85;
        double top = Math.Min(Rise, layer * 0.5) - side * 0.35 + Rise * 0.5;
        return (side, top, layer * (1 + Drop));
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
