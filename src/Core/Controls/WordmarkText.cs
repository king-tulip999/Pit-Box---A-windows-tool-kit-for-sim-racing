using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using Core.Theme;

namespace Core.Controls;

public sealed class WordmarkText : FrameworkElement
{
    public const double Tracking = 0.24, InkScale = 0.8;

    public static readonly DependencyProperty InkHeightProperty = DependencyProperty.Register(
        nameof(InkHeight), typeof(double), typeof(WordmarkText), new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Reshape));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(WordmarkText), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, Reshape));

    public double InkHeight
    {
        get => (double)GetValue(InkHeightProperty);
        set => SetValue(InkHeightProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    private FormattedText[]? _letters;
    private Point[] _origins = [];
    private double _width;

    public WordmarkText() => AutomationProperties.SetName(this, Brand.Name);

    private static void Reshape(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WordmarkText)d)._letters = null;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _letters = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Shape()
    {
        if (_letters is not null) return;
        string word = Brand.Wordmark;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var probe = Formatted(word, Brand.WordmarkBody, 100, Brushes.Black, dip);
        double size = 100 * InkHeight * InkScale / probe.Extent, k = size / 100;
        double inkTop = (probe.Height + probe.OverhangAfter - probe.Extent) * k;
        double top = Snap((InkHeight - probe.Extent * k) / 2 - inkTop, dip);
        double gap = Tracking * size, x = 0;
        _letters = new FormattedText[word.Length];
        _origins = new Point[word.Length];
        for (int i = 0; i < word.Length; i++)
        {
            var letter = Formatted(word[i].ToString(), i == 0 ? Brand.WordmarkLead : Brand.WordmarkBody, size, Foreground, dip);
            _letters[i] = letter;
            _origins[i] = new Point(Snap(x, dip), top);
            x += letter.WidthIncludingTrailingWhitespace + (i < word.Length - 1 ? gap : 0);
        }
        _width = Math.Ceiling(x);
    }

    private static double Snap(double value, double dip) => Math.Round(value * dip) / dip;

    private static FormattedText Formatted(string text, Typeface typeface, double size, Brush brush, double dip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, dip);

    protected override Size MeasureOverride(Size available)
    {
        Shape();
        return new Size(_width, InkHeight);
    }

    protected override void OnRender(DrawingContext dc)
    {
        Shape();
        for (int i = 0; i < _letters!.Length; i++) dc.DrawText(_letters[i], _origins[i]);
    }
}
