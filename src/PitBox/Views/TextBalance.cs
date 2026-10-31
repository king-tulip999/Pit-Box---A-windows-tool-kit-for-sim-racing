using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PitBox.Views;

internal static class TextBalance
{
    public const int Steps = 12;
    public const double Slack = 1;
    public const int MaxPasses = 2;

    public static readonly DependencyProperty OffProperty =
        DependencyProperty.RegisterAttached("Off", typeof(bool), typeof(TextBalance), new PropertyMetadata(false));

    private static readonly DependencyProperty AddedProperty =
        DependencyProperty.RegisterAttached("Added", typeof(double), typeof(TextBalance), new PropertyMetadata(0.0));

    private static readonly DependencyProperty PassesProperty =
        DependencyProperty.RegisterAttached("Passes", typeof(int), typeof(TextBalance), new PropertyMetadata(0));

    public static void SetOff(DependencyObject d, bool value) => d.SetValue(OffProperty, value);

    public static bool GetOff(DependencyObject d) => (bool)d.GetValue(OffProperty);

    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(TextBlock), FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(OnSizeChanged));

    public static double BalancedWidth(Func<double, double> heightAt, double full)
    {
        if (full <= 0) return full;
        double height = heightAt(full);
        if (heightAt(double.PositiveInfinity) >= height - 0.5) return full;
        double low = 0, high = full;
        for (int i = 0; i < Steps; i++)
        {
            double mid = (low + high) / 2;
            if (heightAt(mid) <= height + 0.5) high = mid; else low = mid;
        }
        return Math.Min(full, Math.Ceiling(high) + Slack);
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not TextBlock tb || !e.WidthChanged && !e.HeightChanged) return;
        double added = (double)tb.GetValue(AddedProperty);
        if (tb.TextWrapping == TextWrapping.NoWrap || tb.TextTrimming != TextTrimming.None || tb.TextAlignment != TextAlignment.Left
            || tb.HorizontalAlignment != HorizontalAlignment.Stretch || GetOff(tb) || tb.Inlines.Count > 1 || string.IsNullOrEmpty(tb.Text))
        {
            if (added > 0) Apply(tb, added, 0);
            return;
        }
        var pad = tb.Padding;
        double slot = System.Windows.Controls.Primitives.LayoutInformation.GetLayoutSlot(tb).Width - tb.Margin.Left - tb.Margin.Right;
        slot = Math.Min(slot, double.IsNaN(tb.Width) ? tb.MaxWidth : tb.Width);
        if (added <= 0 && slot <= tb.DesiredSize.Width - tb.Margin.Left - tb.Margin.Right + 0.5) return;
        double full = slot - pad.Left - (pad.Right - added);
        if (full <= 0) return;
        var typeface = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
        double dip = VisualTreeHelper.GetDpi(tb).PixelsPerDip;
        var mode = TextOptions.GetTextFormattingMode(tb);
        var culture = CultureInfo.CurrentUICulture;
        string text = tb.Text;
        double HeightAt(double width)
        {
            var ft = new FormattedText(text, culture, tb.FlowDirection, typeface, tb.FontSize, Brushes.Black, null, mode, dip);
            if (!double.IsPositiveInfinity(width)) ft.MaxTextWidth = Math.Max(1, width);
            if (!double.IsNaN(tb.LineHeight)) ft.LineHeight = tb.LineHeight;
            return ft.Height;
        }
        int passes = (int)tb.GetValue(PassesProperty);
        if (passes >= MaxPasses) return;
        tb.SetValue(PassesProperty, passes + 1);
        if (passes == 0) tb.Dispatcher.BeginInvoke(() => tb.ClearValue(PassesProperty), System.Windows.Threading.DispatcherPriority.ContextIdle);
        double width = BalancedWidth(HeightAt, full);
        Apply(tb, added, Math.Max(0, Math.Floor(full - width)));
    }

    private static void Apply(TextBlock tb, double added, double wanted)
    {
        if (Math.Abs(added - wanted) < 1) return;
        var pad = tb.Padding;
        tb.Padding = new Thickness(pad.Left, pad.Top, pad.Right - added + wanted, pad.Bottom);
        tb.SetValue(AddedProperty, wanted);
    }
}
