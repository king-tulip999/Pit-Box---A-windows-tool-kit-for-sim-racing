using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using Core.Theme;

namespace Core.Controls;

public sealed class EchoTitle : FrameworkElement
{
    public const double BodySize = 28, BleedTop = 14, BleedBottom = 8, TopCrop = 0.08, StrokeWidth = 1.25, StrokeOpacity = 0.55;

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(EchoTitle), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Reshape));

    public static readonly DependencyProperty FamilyProperty = DependencyProperty.Register(
        nameof(Family), typeof(FontFamily), typeof(EchoTitle), new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Reshape));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(EchoTitle), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender, Restroke));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily Family
    {
        get => (FontFamily)GetValue(FamilyProperty);
        set => SetValue(FamilyProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private Geometry? _ink;
    private Pen? _pen;
    private double _width, _height, _reserve;

    private static void Reshape(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var title = (EchoTitle)d;
        title._ink = null;
        if (e.Property == TextProperty) AutomationProperties.SetName(title, (string)e.NewValue);
    }

    private static void Restroke(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((EchoTitle)d)._pen = null;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _ink = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Shape()
    {
        if (_ink is not null) return;
        var typeface = new Typeface(Family, FontStyles.Normal, FontWeights.ExtraBold, FontStretches.Normal);
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var body = Formatted(TextCase.Capitals(Text), typeface, BodySize, dip);
        _height = Math.Ceiling(body.Height);
        _reserve = Math.Ceiling(body.Width);
        double caps = typeface.TryGetGlyphTypeface(out var glyphs) && glyphs.CapsHeight > 0 ? glyphs.CapsHeight : 0.7;
        double band = _height + BleedTop + BleedBottom;
        var text = Formatted(TextCase.Capitals(Text), typeface, band / (1 - TopCrop) / caps, dip);
        double baseline = _height + BleedBottom - 1;
        var ink = text.BuildGeometry(new Point(0, baseline - text.Baseline));
        var bounds = ink.Bounds;
        if (bounds.IsEmpty)
        {
            _ink = Geometry.Empty;
            _width = 0;
            return;
        }
        ink.Transform = new TranslateTransform(StrokeWidth / 2 - bounds.Left, 0);
        ink.Freeze();
        _ink = ink;
        _width = Math.Ceiling(bounds.Width + StrokeWidth);
    }

    private static FormattedText Formatted(string text, Typeface typeface, double size, double dip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.Black, dip);

    protected override Size MeasureOverride(Size available)
    {
        Shape();
        return new Size(Math.Min(_reserve, available.Width), _height);
    }

    protected override Geometry GetLayoutClip(Size layoutSlotSize) =>
        new RectangleGeometry(new Rect(0, -BleedTop, Math.Max(_width, RenderSize.Width), RenderSize.Height + BleedTop + BleedBottom));

    protected override void OnRender(DrawingContext dc)
    {
        Shape();
        if (_ink is null || _ink.IsEmpty()) return;
        if (_pen is null)
        {
            var color = Stroke is SolidColorBrush solid ? solid.Color : Colors.White;
            var brush = new SolidColorBrush(color) { Opacity = StrokeOpacity };
            brush.Freeze();
            _pen = new Pen(brush, StrokeWidth) { LineJoin = PenLineJoin.Round };
            _pen.Freeze();
        }
        dc.DrawGeometry(null, _pen, _ink);
    }
}
