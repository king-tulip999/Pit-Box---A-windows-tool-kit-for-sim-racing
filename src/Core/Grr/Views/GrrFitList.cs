using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Theme;

namespace Core.Grr.Views;

internal sealed class GrrFitList : Panel
{
    public static readonly DependencyProperty RailProperty = DependencyProperty.Register(nameof(Rail), typeof(Brush), typeof(GrrFitList),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Rail
    {
        get => (Brush?)GetValue(RailProperty);
        set => SetValue(RailProperty, value);
    }

    public int Anchor { get; init; }
    public int MinRows { get; init; } = 3;
    public bool ShowAll { get; init; }
    public double RailX { get; init; } = 12;
    public double DotY { get; init; } = 13.5;

    private int _from, _to;
    private readonly List<double> _tops = new();

    public GrrFitList()
    {
        ClipToBounds = true;
        SetResourceReference(RailProperty, "LineStrong");
    }

    internal static (int From, int To) Window(int count, int anchor, int slots)
    {
        if (count <= 0 || slots <= 0) return (0, 0);
        slots = Math.Min(slots, count);
        anchor = anchor < 0 || anchor >= count ? count - 1 : anchor;
        int before = slots >= 5 ? 2 : slots >= 2 ? 1 : 0;
        int from = Math.Clamp(anchor - before, 0, count - slots);
        return (from, from + slots);
    }

    private double HeightOf(int from, int to)
    {
        double h = 0;
        for (int i = from; i < to; i++) h += InternalChildren[i].DesiredSize.Height;
        return h;
    }

    protected override Size MeasureOverride(Size available)
    {
        double width = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(available.Width, double.PositiveInfinity));
            width = Math.Max(width, child.DesiredSize.Width);
        }
        int count = InternalChildren.Count;
        var (from, to) = ShowAll ? (0, count) : Window(count, Anchor, MinRows);
        return new Size(width, HeightOf(from, to));
    }

    protected override Size ArrangeOverride(Size final)
    {
        int count = InternalChildren.Count;
        int slots = count;
        if (!ShowAll)
        {
            slots = Math.Min(MinRows, count);
            while (slots < count)
            {
                var (f, t) = Window(count, Anchor, slots + 1);
                if (HeightOf(f, t) > final.Height + 0.5) break;
                slots++;
            }
        }
        (_from, _to) = Window(count, Anchor, slots);
        _tops.Clear();
        double y = 0;
        for (int i = 0; i < count; i++)
        {
            var child = InternalChildren[i];
            bool shown = i >= _from && i < _to;
            var visibility = shown ? Visibility.Visible : Visibility.Hidden;
            if (child.Visibility != visibility) child.Visibility = visibility;
            if (!shown)
            {
                child.Arrange(new Rect(0, 0, final.Width, 0));
                continue;
            }
            _tops.Add(y);
            child.Arrange(new Rect(0, y, final.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
        InvalidateVisual();
        return final;
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Rail is not { } brush || _tops.Count < 2) return;
        double scale = Hairline.ScaleOf(this);
        double width = Hairline.Pixels(1, scale);
        double x = Hairline.Snap(RailX, scale) - width / 2;
        dc.DrawRectangle(brush, null, new Rect(x, _tops[0] + DotY, width, _tops[^1] - _tops[0]));
    }
}
