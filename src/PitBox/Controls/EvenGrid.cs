using System.Windows;
using System.Windows.Controls;

namespace PitBox.Controls;

internal sealed class EvenGrid : Panel
{
    public double MinItemWidth { get; set; } = 190;
    public double ColumnGap { get; set; } = 6;
    public double RowGap { get; set; } = 6;

    private int _columns = 1;
    private double[] _rowHeights = Array.Empty<double>();

    public static int ColumnsFor(double width, double minItem, double gap, int count) =>
        double.IsInfinity(width) || width <= 0 ? Math.Max(1, count) : Math.Clamp((int)Math.Floor((width + gap) / (minItem + gap)), 1, Math.Max(1, count));

    private double ItemWidth(double width) => _columns == 1 ? width : (width - ColumnGap * (_columns - 1)) / _columns;

    protected override Size MeasureOverride(Size available)
    {
        int n = InternalChildren.Count;
        _columns = ColumnsFor(available.Width, MinItemWidth, ColumnGap, n);
        double item = double.IsInfinity(available.Width) ? MinItemWidth : ItemWidth(available.Width);
        int rows = (n + _columns - 1) / _columns;
        _rowHeights = new double[rows];
        for (int i = 0; i < n; i++)
        {
            var child = InternalChildren[i];
            child.Measure(new Size(item, double.PositiveInfinity));
            _rowHeights[i / _columns] = Math.Max(_rowHeights[i / _columns], child.DesiredSize.Height);
        }
        double height = _rowHeights.Sum() + RowGap * Math.Max(0, rows - 1);
        double width = double.IsInfinity(available.Width) ? item * _columns + ColumnGap * (_columns - 1) : available.Width;
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double item = ItemWidth(final.Width), y = 0;
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int row = i / _columns, col = i % _columns;
            if (row >= _rowHeights.Length) break;
            if (col == 0 && row > 0) y += _rowHeights[row - 1] + RowGap;
            InternalChildren[i].Arrange(new Rect(col * (item + ColumnGap), y, item, _rowHeights[row]));
        }
        return final;
    }
}
