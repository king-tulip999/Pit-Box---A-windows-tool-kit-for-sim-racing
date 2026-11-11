using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Core.Theme;

namespace Core.Views;

internal static class TextFit
{
    private static FormattedText Format(TextBlock tb, string text) =>
        new(text, CultureInfo.CurrentUICulture, tb.FlowDirection, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
            tb.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(tb).PixelsPerDip);

    public static double Width(TextBlock tb, string text)
    {
        var key = new WidthKey(text, new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, tb.FlowDirection,
            VisualTreeHelper.GetDpi(tb).PixelsPerDip, CultureInfo.CurrentUICulture);
        lock (Widths)
            if (Widths.TryGetValue(key, out double known)) return known;
        double width = Format(tb, text).WidthIncludingTrailingWhitespace;
        lock (Widths)
        {
            if (Widths.Count >= MemoSize) Widths.Clear();
            Widths[key] = width;
        }
        return width;
    }

    private const int MemoSize = 4096;

    private readonly record struct WidthKey(string Text, Typeface Typeface, double Size, FlowDirection Flow, double Dip, CultureInfo Culture);

    private static readonly Dictionary<WidthKey, double> Widths = new();

    public static bool IsTrimmed(TextBlock tb)
    {
        if (tb.TextTrimming == TextTrimming.None || tb.Text.Length == 0 || tb.ActualWidth <= 0) return false;
        double width = tb.ActualWidth - tb.Padding.Left - tb.Padding.Right;
        if (tb.TextWrapping == TextWrapping.NoWrap) return Width(tb, tb.Text) > width + 0.5;
        var text = Format(tb, tb.Text);
        text.MaxTextWidth = Math.Max(1, width);
        if (!double.IsNaN(tb.LineHeight)) text.LineHeight = tb.LineHeight;
        return text.Height > tb.ActualHeight - tb.Padding.Top - tb.Padding.Bottom + 0.5;
    }

    private static readonly DependencyProperty TrimTipProperty =
        DependencyProperty.RegisterAttached("TrimTip", typeof(bool), typeof(TextFit), new PropertyMetadata(false));

    public static T TipWhenTrimmed<T>(T tb) where T : TextBlock
    {
        tb.SetValue(TrimTipProperty, true);
        tb.SizeChanged += (_, _) => Update(tb);
        tb.Loaded += (_, _) =>
        {
            if (!CheckedAsIs(tb)) Update(tb);
        };
        tb.ToolTipOpening += (_, e) =>
        {
            if (!Update(tb)) e.Handled = true;
        };
        return tb;
    }

    public static bool HasTrimTip(DependencyObject d) => (bool)d.GetValue(TrimTipProperty);

    private sealed record TrimCheck(double Width, double Height, string Text, double FontSize);

    private static readonly DependencyProperty TrimCheckedProperty =
        DependencyProperty.RegisterAttached("TrimChecked", typeof(TrimCheck), typeof(TextFit), new PropertyMetadata(null));

    private static bool CheckedAsIs(TextBlock tb) =>
        tb.GetValue(TrimCheckedProperty) is TrimCheck seen
        && seen.Width == tb.ActualWidth && seen.Height == tb.ActualHeight && seen.FontSize == tb.FontSize && string.Equals(seen.Text, tb.Text, StringComparison.Ordinal);

    private static bool Update(TextBlock tb)
    {
        tb.SetValue(TrimCheckedProperty, new TrimCheck(tb.ActualWidth, tb.ActualHeight, tb.Text, tb.FontSize));
        if (IsTrimmed(tb))
        {
            if (tb.ToolTip is not ToolTip { Content: string shown } || shown != tb.Text) tb.ToolTip = TextCase.VerbatimTip(tb.Text);
            return true;
        }
        if (tb.ToolTip is ToolTip) tb.ClearValue(FrameworkElement.ToolTipProperty);
        return false;
    }
}
