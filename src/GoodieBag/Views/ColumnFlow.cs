using System.Windows;
using System.Windows.Controls;

namespace GoodieBag.Views;

internal sealed class ColumnFlow : Panel
{
    public double MinColumnWidth { get; init; } = 254;
    public int MaxColumns { get; init; } = 4;
    public double ColumnGap { get; init; } = 10;

    private readonly List<double> _rowHeights = new();
    private int _rows = 1;
    private double _cell;

    private int Columns(double width) =>
        double.IsInfinity(width) || double.IsNaN(width) ? 1 : Math.Clamp((int)((width + ColumnGap) / (MinColumnWidth + ColumnGap)), 1, MaxColumns);

    private List<UIElement> Shown() => InternalChildren.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();

    protected override Size MeasureOverride(Size available)
    {
        var shown = Shown();
        int columns = Math.Min(Columns(available.Width), Math.Max(1, shown.Count));
        _rows = Math.Max(1, (int)Math.Ceiling(shown.Count / (double)columns));
        columns = Math.Max(1, (int)Math.Ceiling(shown.Count / (double)_rows));
        _cell = double.IsInfinity(available.Width) ? MinColumnWidth : (available.Width - ColumnGap * (columns - 1)) / columns;
        _rowHeights.Clear();
        for (int r = 0; r < _rows; r++) _rowHeights.Add(0);
        foreach (UIElement child in InternalChildren)
            if (child.Visibility == Visibility.Collapsed) child.Measure(new Size(0, 0));
        for (int i = 0; i < shown.Count; i++)
        {
            shown[i].Measure(new Size(_cell, double.PositiveInfinity));
            _rowHeights[i % _rows] = Math.Max(_rowHeights[i % _rows], shown[i].DesiredSize.Height);
        }
        double width = double.IsInfinity(available.Width) ? columns * _cell + ColumnGap * (columns - 1) : available.Width;
        return new Size(width, _rowHeights.Sum());
    }

    protected override Size ArrangeOverride(Size final)
    {
        var shown = Shown();
        foreach (UIElement child in InternalChildren)
            if (child.Visibility == Visibility.Collapsed) child.Arrange(new Rect(0, 0, 0, 0));
        for (int i = 0; i < shown.Count; i++)
        {
            int column = i / _rows, row = i % _rows;
            double y = 0;
            for (int r = 0; r < row && r < _rowHeights.Count; r++) y += _rowHeights[r];
            double height = row < _rowHeights.Count ? _rowHeights[row] : shown[i].DesiredSize.Height;
            shown[i].Arrange(new Rect(column * (_cell + ColumnGap), y, _cell, height));
        }
        return final;
    }
}
