using System.Windows;
using System.Windows.Controls;

namespace Core.Controls;

public sealed class MasonryPanel : Panel
{
    public double MinColumnWidth { get; set; } = 373;
    public int MaxColumns { get; set; } = 3;
    public double Gap { get; set; } = 13;
    public bool Stable { get; set; }
    public bool EvenBottoms { get; set; }
    public Func<int, int, int?>? ColumnOf { get; set; }

    private int ColumnFor(double[] heights, int index) =>
        ColumnOf?.Invoke(heights.Length, index) is int fixedColumn && fixedColumn >= 0 && fixedColumn < heights.Length ? fixedColumn
        : Stable ? index % heights.Length : Array.IndexOf(heights, heights.Min());

    private int Columns(double width) =>
        double.IsInfinity(width) ? 1 : Math.Clamp((int)((width + Gap) / (MinColumnWidth + Gap)), 1, Math.Max(1, MaxColumns));

    private double ColumnWidth(double width, int columns) => Math.Max(0, (width - Gap * (columns - 1)) / columns);

    protected override Size MeasureOverride(Size available)
    {
        int columns = Columns(available.Width);
        double w = double.IsInfinity(available.Width) ? MinColumnWidth : ColumnWidth(available.Width, columns);
        var heights = new double[columns];
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(w, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            int c = ColumnFor(heights, index++);
            heights[c] += child.DesiredSize.Height + Gap;
        }
        double height = Math.Max(0, heights.Max() - Gap);
        return new Size(double.IsInfinity(available.Width) ? w : available.Width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        int columns = Columns(final.Width);
        double w = ColumnWidth(final.Width, columns);
        var heights = new double[columns];
        var placed = new List<(UIElement Child, int Column, double Top)>();
        int index = 0;
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) { child.Arrange(new Rect()); continue; }
            int c = ColumnFor(heights, index++);
            placed.Add((child, c, heights[c]));
            heights[c] += child.DesiredSize.Height + Gap;
        }
        double bottom = heights.Max() - Gap;
        for (int i = 0; i < placed.Count; i++)
        {
            var (child, c, top) = placed[i];
            bool last = !placed.Skip(i + 1).Any(p => p.Column == c);
            double h = EvenBottoms && columns > 1 && last ? Math.Max(child.DesiredSize.Height, bottom - top) : child.DesiredSize.Height;
            double x0 = Math.Round(c * (w + Gap)), x1 = Math.Round(c * (w + Gap) + w);
            child.Arrange(new Rect(x0, top, Math.Max(0, x1 - x0), h));
        }
        return final;
    }
}
