using System.Windows;
using System.Windows.Controls;

namespace Core.Controls;

internal sealed class FitColumns : Panel
{
    public int MaxColumns { get; set; } = 2;

    private int _columns = 1;
    private double _column;
    private double[] _rowHeights = Array.Empty<double>();

    public static int ColumnsFor(double width, double column, int max, int count)
    {
        int most = Math.Clamp(Math.Min(max, count), 1, int.MaxValue);
        if (double.IsInfinity(width) || column <= 0) return most;
        return Math.Clamp((int)Math.Floor(width / column), 1, most);
    }

    protected override Size MeasureOverride(Size available)
    {
        int n = InternalChildren.Count;
        double widest = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, child.DesiredSize.Width);
        }
        _columns = ColumnsFor(available.Width, widest, MaxColumns, n);
        _column = double.IsInfinity(available.Width) ? widest : Math.Min(widest, available.Width / _columns);
        if (_column < widest)
            foreach (UIElement child in InternalChildren) child.Measure(new Size(_column, double.PositiveInfinity));
        int rows = (n + _columns - 1) / _columns;
        _rowHeights = new double[rows];
        for (int i = 0; i < n; i++)
            _rowHeights[i / _columns] = Math.Max(_rowHeights[i / _columns], InternalChildren[i].DesiredSize.Height);
        return new Size(_column * _columns, _rowHeights.Sum());
    }

    protected override Size ArrangeOverride(Size final)
    {
        double y = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int row = i / _columns, col = i % _columns;
            if (row >= _rowHeights.Length) break;
            if (col == 0 && row > 0) y += _rowHeights[row - 1];
            InternalChildren[i].Arrange(new Rect(col * _column, y, _column, _rowHeights[row]));
        }
        return final;
    }
}
