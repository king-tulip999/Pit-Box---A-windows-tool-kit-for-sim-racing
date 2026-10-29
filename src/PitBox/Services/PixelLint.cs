using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PitBox.Services;

internal static class PixelLint
{
    private const double OffGrid = 0.05;
    private const double MaxCut = 2.5;

    internal sealed record Finding(string Rule, string Where, string Element, string Detail)
    {
        public override string ToString() => $"pixel {Rule} [{Where}] {Element} {Detail}";
    }

    public static List<Finding> Inspect(Visual root, string where)
    {
        var found = new List<Finding>();
        double scale = VisualTreeHelper.GetDpi(root).DpiScaleX;
        Walk(root, root, scale, new Rect(-1e6, -1e6, 2e6, 2e6), "", where, found);
        return found.DistinctBy(f => (f.Rule, f.Element)).ToList();
    }

    private static void Walk(Visual node, Visual root, double scale, Rect clip, string clipper, string where, List<Finding> found)
    {
        if (node is UIElement { IsVisible: false } or UIElement { Opacity: < 0.05 }) return;
        if (node is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } fe && Device(fe, root, scale) is { } box)
        {
            if (Edges(fe, scale) is { } edges && box.IntersectsWith(clip))
                Check(fe, box, edges, clip, clipper, where, found);
            if (fe.ClipToBounds && Narrows(ref clip, box))
                clipper = Owner(fe);
            if (LayoutInformation.GetLayoutClip(fe) is { } layoutClip && Device(fe, root, scale, layoutClip.Bounds) is { } clipBox && Narrows(ref clip, clipBox))
                clipper = Owner(fe) + " layout";
            if (clip.IsEmpty) return;
        }
        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++)
            if (VisualTreeHelper.GetChild(node, i) is Visual child)
                Walk(child, root, scale, clip, clipper, where, found);
    }

    private static Rect? Device(FrameworkElement fe, Visual root, double scale, Rect? local = null)
    {
        try
        {
            var r = fe.TransformToAncestor(root).TransformBounds(local ?? new Rect(0, 0, fe.ActualWidth, fe.ActualHeight));
            return new Rect(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);
        }
        catch (InvalidOperationException) { return null; }
    }

    private static Thickness? Edges(FrameworkElement fe, double scale)
    {
        switch (fe)
        {
            case Border b when Visible(b.BorderBrush) && b.BorderThickness is { } t && (t.Left > 0 || t.Top > 0 || t.Right > 0 || t.Bottom > 0):
                return t;
            case Border b when Visible(b.Background) && IsRule(fe, scale):
                return new Thickness(1);
            case Shape s when Visible(s.Stroke) && s.StrokeThickness > 0 && s is Rectangle:
                return new Thickness(1);
            case Shape s when Visible(s.Fill) && IsRule(fe, scale):
                return new Thickness(1);
        }
        return null;
    }

    private static bool IsRule(FrameworkElement fe, double scale) => Math.Min(fe.ActualWidth, fe.ActualHeight) * scale <= MaxCut;

    private static bool Visible(Brush? brush) => brush switch
    {
        null => false,
        SolidColorBrush s => s.Color.A > 0 && brush.Opacity > 0,
        _ => brush.Opacity > 0,
    };

    private static void Check(FrameworkElement fe, Rect box, Thickness edges, Rect clip, string clipper, string where, List<Finding> found)
    {
        bool snapped = fe is Controls.Tile;
        var off = new List<string>();
        if (!snapped)
        {
            if (edges.Left > 0 && OffPixel(box.Left)) off.Add($"left {box.Left:0.##}");
            if (edges.Top > 0 && OffPixel(box.Top)) off.Add($"top {box.Top:0.##}");
            if (edges.Right > 0 && OffPixel(box.Right)) off.Add($"right {box.Right:0.##}");
            if (edges.Bottom > 0 && OffPixel(box.Bottom)) off.Add($"bottom {box.Bottom:0.##}");
        }
        if (off.Count > 0)
            found.Add(new Finding("blur", where, Describe(fe), "edge between device pixels: " + string.Join(", ", off)));

        var cut = new List<string>();
        if (edges.Left > 0 && Cut(clip.Left - box.Left)) cut.Add($"left by {clip.Left - box.Left:0.#}");
        if (edges.Top > 0 && Cut(clip.Top - box.Top)) cut.Add($"top by {clip.Top - box.Top:0.#}");
        if (edges.Right > 0 && Cut(box.Right - clip.Right)) cut.Add($"right by {box.Right - clip.Right:0.#}");
        if (edges.Bottom > 0 && Cut(box.Bottom - clip.Bottom)) cut.Add($"bottom by {box.Bottom - clip.Bottom:0.#}");
        if (cut.Count > 0)
            found.Add(new Finding("cut", where, Describe(fe), "edge clipped by " + clipper + ": " + string.Join(", ", cut) + $" px (edge box {box.Left:0.##},{box.Top:0.##}–{box.Right:0.##},{box.Bottom:0.##}, visible {clip.Left:0.##},{clip.Top:0.##}–{clip.Right:0.##},{clip.Bottom:0.##})"));
    }

    private static bool Narrows(ref Rect clip, Rect by)
    {
        var next = Rect.Intersect(clip, by);
        if (next == clip) return false;
        clip = next;
        return true;
    }

    private static string Owner(FrameworkElement fe) =>
        $"{fe.GetType().Name}{(fe.Name.Length > 0 ? "#" + fe.Name : "")} ({fe.ActualWidth:0.###}x{fe.ActualHeight:0.###} DIP, wants {fe.DesiredSize.Width:0.###}x{fe.DesiredSize.Height:0.###})";

    private static bool OffPixel(double v) => Math.Abs(v - Math.Round(v)) > OffGrid;

    private static bool Cut(double over) => over > OffGrid && over <= MaxCut;

    private static string Describe(FrameworkElement fe)
    {
        var path = new List<string>();
        DependencyObject? at = fe;
        for (int i = 0; i < 4 && at is FrameworkElement e; i++, at = VisualTreeHelper.GetParent(e))
            path.Add((e.Name.Length > 0 ? $"{e.GetType().Name}#{e.Name}" : e.GetType().Name));
        string text = FirstText(fe, 0) ?? (fe.TemplatedParent as FrameworkElement is { } owner ? FirstText(owner, 0) : null) ?? "";
        return string.Join(" < ", path) + (text.Length > 0 ? $" “{(text.Length > 30 ? text[..30] : text)}”" : "");
    }

    private static string? FirstText(DependencyObject d, int depth)
    {
        if (d is TextBlock { Text.Length: > 0 } tb) return tb.Text;
        if (depth > 8) return null;
        int count = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < count; i++)
            if (FirstText(VisualTreeHelper.GetChild(d, i), depth + 1) is { } t)
                return t;
        return null;
    }
}
