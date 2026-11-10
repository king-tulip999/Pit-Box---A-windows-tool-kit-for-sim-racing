using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Controls;
using PitBox.Services;
using PitBox.SessionStats;
using PitBox.SimRacing.Race;
using PitBox.Theme;

namespace PitBox.Views;

internal sealed class Slant : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(Slant),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    private readonly double[] _points;

    public Slant(string brush, params double[] points)
    {
        _points = points;
        IsHitTestVisible = false;
        SetResourceReference(FillProperty, brush);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Fill is null || _points.Length < 6) return;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(_points[0] * ActualWidth, _points[1] * ActualHeight), true, true);
            for (int i = 2; i + 1 < _points.Length; i += 2) g.LineTo(new Point(_points[i] * ActualWidth, _points[i + 1] * ActualHeight), true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(Fill, null, geo);
    }
}

internal sealed class LapStrip : FrameworkElement
{
    public static readonly DependencyProperty BarProperty = Brush(nameof(Bar)), BestProperty = Brush(nameof(Best)), DirtyProperty = Brush(nameof(Dirty)), BaseProperty = Brush(nameof(Base)),
        LabelProperty = Brush(nameof(Label)), TickProperty = Brush(nameof(Tick));
    public static readonly DependencyProperty GrowProperty = DependencyProperty.Register(nameof(Grow), typeof(double), typeof(LapStrip),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Brush(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(LapStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Bar { get => (Brush?)GetValue(BarProperty); set => SetValue(BarProperty, value); }
    public Brush? Best { get => (Brush?)GetValue(BestProperty); set => SetValue(BestProperty, value); }
    public Brush? Dirty { get => (Brush?)GetValue(DirtyProperty); set => SetValue(DirtyProperty, value); }
    public Brush? Base { get => (Brush?)GetValue(BaseProperty); set => SetValue(BaseProperty, value); }
    public Brush? Label { get => (Brush?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public Brush? Tick { get => (Brush?)GetValue(TickProperty); set => SetValue(TickProperty, value); }
    public double Grow { get => (double)GetValue(GrowProperty); set => SetValue(GrowProperty, value); }

    private const double GrowSpread = 8, LabelSize = 11.5, LabelGap = 7, TickGap = 3;

    public const double CleanOpacity = 0.82, DirtyOpacity = 0.6;

    private readonly IReadOnlyList<(double Time, bool Clean)> _laps;
    private readonly double _lo, _hi;
    private readonly bool _scale;
    private FormattedText? _fastest, _slowest;
    private FormattedText[] _ticks = Array.Empty<FormattedText>();
    private int[] _tickLaps = Array.Empty<int>();
    private Brush? _labelsFor, _ticksFor;
    private double _labelsDip;

    public const double ScaleTop = 1.05;

    public LapStrip(IReadOnlyList<(double Time, bool Clean)> laps, string place, bool scale = false)
    {
        _laps = laps;
        _scale = scale;
        _lo = laps.Count > 0 ? laps.Min(l => l.Time) : 0;
        _hi = laps.Count > 0 ? Math.Min(laps.Max(l => l.Time), _lo * ScaleTop) : 1;
        SetResourceReference(BarProperty, place);
        SetResourceReference(BestProperty, "Heading");
        SetResourceReference(DirtyProperty, "Danger");
        SetResourceReference(BaseProperty, "Line");
        SetResourceReference(LabelProperty, "TextMuted");
        SetResourceReference(TickProperty, "TextFaint");
        SnapsToDevicePixels = true;
        ToolTip = "Lap times: taller = faster";
    }

    public static string ScaleLabel(double seconds)
    {
        long tenths = (long)Math.Round(Math.Max(0, seconds) * 10);
        return $"{tenths / 600}:{tenths % 600 / 10:00}.{tenths % 10}";
    }

    public static int[] LapTicks(int count)
    {
        if (count <= 0) return Array.Empty<int>();
        int step = count <= 30 ? 5 : count <= 60 ? 10 : count <= 150 ? 20 : 50;
        var ticks = new List<int> { 1 };
        for (int lap = step; lap <= count; lap += step) ticks.Add(lap);
        return ticks.ToArray();
    }

    private void BuildLabels(double dip)
    {
        if (_fastest is not null && ReferenceEquals(_labelsFor, Label) && ReferenceEquals(_ticksFor, Tick) && _labelsDip == dip) return;
        var family = TryFindResource("RaceFont") as FontFamily ?? new FontFamily("Segoe UI");
        var face = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        FormattedText Text(string s, Brush? brush) => new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, LabelSize, brush ?? Brushes.Gray, dip);
        _fastest = Text(ScaleLabel(_lo), Label);
        _slowest = Text(ScaleLabel(_hi), Label);
        _tickLaps = LapTicks(_laps.Count);
        _ticks = _tickLaps.Select(lap => Text(lap.ToString(CultureInfo.CurrentCulture), Tick)).ToArray();
        _labelsFor = Label;
        _ticksFor = Tick;
        _labelsDip = dip;
    }

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        double w = ActualWidth, h = ActualHeight;
        if (_laps.Count == 0 || w <= 0 || h <= 0) return;
        double px = Hairline.ScaleOf(this);
        double left = 0, top0 = 0, bottom = h;
        if (_scale)
        {
            BuildLabels(VisualTreeHelper.GetDpi(this).PixelsPerDip);
            double half = _fastest!.Height / 2;
            left = Math.Ceiling(Math.Max(_fastest.Width, _slowest!.Width) + LabelGap);
            top0 = Math.Ceiling(half);
            bottom = h - Math.Ceiling(_ticks.Length > 0 ? _ticks[0].Height + TickGap : 0);
            if (bottom - top0 <= 4 || w - left <= 4) return;
            dc.DrawText(_fastest, new Point(left - LabelGap - _fastest.Width, top0 - half));
            dc.DrawText(_slowest, new Point(left - LabelGap - _slowest.Width, bottom - half));
        }
        double plotW = w - left, plotH = bottom - top0;
        double gap = _laps.Count * 2 < plotW ? 1 : 0;
        double step = plotW / _laps.Count;
        double range = Math.Max(0.001, _hi - _lo);
        for (int i = 0; i < _laps.Count; i++)
        {
            var (time, clean) = _laps[i];
            double share = time >= _hi ? 0.08 : 0.08 + 0.92 * (_hi - time) / range;
            if (Grow < 1) share *= FrameDriver.EaseOut((Grow * (_laps.Count + GrowSpread) - i) / GrowSpread);
            double x0 = Hairline.Snap(left + i * step, px), x1 = Hairline.Snap(left + (i + 1) * step - gap, px);
            double top = Hairline.Snap(bottom - share * plotH, px);
            var fill = time == _lo ? Best : clean ? Bar : Dirty;
            dc.PushOpacity(time == _lo ? 1 : clean ? CleanOpacity : DirtyOpacity);
            if (top < bottom) dc.DrawRectangle(fill, null, new Rect(x0, top, Math.Max(px, x1 - x0), bottom - top));
            dc.Pop();
        }
        dc.DrawRectangle(Base, null, new Rect(left, Hairline.Snap(bottom, px) - Hairline.Pixels(1, px), plotW, Hairline.Pixels(1, px)));
        if (!_scale) return;
        for (int k = 0; k < _ticks.Length; k++)
        {
            double centre = left + (_tickLaps[k] - 0.5) * step - gap / 2;
            double x = Math.Clamp(centre - _ticks[k].Width / 2, left, w - _ticks[k].Width);
            dc.DrawText(_ticks[k], new Point(x, bottom + TickGap));
        }
    }
}

internal sealed class SegmentMeter : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = Brush(nameof(Fill)), TrackProperty = Brush(nameof(Track)), WarnProperty = Brush(nameof(Warn)), HotProperty = Brush(nameof(Hot));

    private static DependencyProperty Brush(string name) => DependencyProperty.Register(name, typeof(Brush), typeof(SegmentMeter),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Track { get => (Brush?)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public Brush? Warn { get => (Brush?)GetValue(WarnProperty); set => SetValue(WarnProperty, value); }
    public Brush? Hot { get => (Brush?)GetValue(HotProperty); set => SetValue(HotProperty, value); }
    public bool Tiered { get; init; }
    public bool Smooth { get; init; }
    public double CellWidth { get; init; } = 11;

    public const double WarnFrom = 0.65, HotFrom = 0.85;

    public SegmentMeter(string fill = "Accent")
    {
        Height = 9;
        SnapsToDevicePixels = true;
        SetResourceReference(FillProperty, fill);
        SetResourceReference(TrackProperty, "MeterTrack");
        SetResourceReference(WarnProperty, "Warn");
        SetResourceReference(HotProperty, "Danger");
    }


    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        if (Smooth)
        {
            double level = Math.Clamp(double.IsNaN(Value) ? 0 : Value, 0, 1);
            if (Track is { } track) dc.DrawRectangle(track, null, new Rect(0, 0, w, h));
            var fill = Tiered && level > HotFrom ? Hot : Tiered && level > WarnFrom ? Warn : Fill;
            if (fill is not null && level > 0) dc.DrawRectangle(fill, null, new Rect(0, 0, Math.Round(w * level), h));
            return;
        }
        double skew = h * 0.4, gap = 3;
        int count = Math.Max(4, (int)((w - skew + gap) / (CellWidth + gap)));
        double cell = (w - skew - gap * (count - 1)) / count;
        int on = (int)Math.Round(Math.Clamp(double.IsNaN(Value) ? 0 : Value, 0, 1) * count);
        for (int i = 0; i < count; i++)
        {
            double x = i * (cell + gap), at = (i + 1.0) / count;
            var brush = i >= on ? Track : Tiered && at > HotFrom ? Hot : Tiered && at > WarnFrom ? Warn : Fill;
            if (brush is null) continue;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(x + skew, 0), true, true);
                g.LineTo(new Point(x + skew + cell, 0), false, false);
                g.LineTo(new Point(x + cell, h), false, false);
                g.LineTo(new Point(x, h), false, false);
            }
            geo.Freeze();
            dc.DrawGeometry(brush, null, geo);
        }
    }
}

internal sealed class Backdrop : Grid
{
    public const int Shapes = 6;

    public Slant Main { get; }

    public Backdrop(int shape, string place = "Place4", string? main = null)
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Main = new Slant(main ?? place + "Faint", Shape(shape)) { Opacity = Strength };
        Children.Add(Main);
    }

    public const double Strength = 0.55;

    private static double[] Shape(int shape) => (((shape % Shapes) + Shapes) % Shapes) switch
    {
        0 => new[] { 0, 0, 0.62, 0, 0.4, 1, 0, 1.0 },
        1 => new[] { 0, 0.74, 1, 0.34, 1, 0.5, 0, 0.9 },
        2 => new[] { 0.68, 0, 0.8, 0, 0.62, 1, 0.5, 1.0 },
        3 => new[] { 1, 0.3, 1, 1, 0.5, 1.0 },
        4 => new[] { 0.5, 0, 1, 0, 1, 1, 0.75, 1.0 },
        _ => new[] { 0.38, 0, 1, 0, 1, 0.42, 0.3, 0.42 },
    };
}

internal sealed class Stripe : Grid
{
    public FrameworkElement? Car { get; init; }
    public Slant[] Bands { get; init; } = Array.Empty<Slant>();

    private const double ClearGap = 24, FadeLength = 140;

    public void KeepClearOf(FrameworkElement text)
    {
        double shownFor = -1, widthFor = -1;
        void Refresh()
        {
            double clear = text.Margin.Left + text.ActualWidth + ClearGap, width = ActualWidth;
            if (width <= 0 || (clear == shownFor && width == widthFor)) return;
            shownFor = clear;
            widthFor = width;
            double from = Math.Clamp(clear / width, 0, 1), to = Math.Clamp((clear + FadeLength) / width, from, 1);
            var mask = new LinearGradientBrush(new GradientStopCollection { new(Colors.Transparent, from), new(Colors.Black, to) }, new Point(0, 0.5), new Point(1, 0.5));
            mask.Freeze();
            foreach (var band in Bands) band.OpacityMask = mask;
        }
        SizeChanged += (_, _) => Refresh();
        text.SizeChanged += (_, _) => Refresh();
    }
}

internal sealed record RaceCardData(SessionSummary Session, IReadOnlyList<(double Time, bool Clean)> Laps, double? Consistency, IReadOnlyList<Award> Awards, string Driver,
    SessionConditions? Conditions = null, bool Imperial = false);

internal static class RaceParts
{
    public static string Place(SessionSummary s) => RaceLook.Key(s.Type == "Race" && s.Finished ? s.ShownPos : null);

    public static bool Raced(SessionSummary s) => s.Type == "Race" && s.Finished && s.FinishPos is > 0;

    public static TextBlock Big(string text, double size, string brush, bool heavy = true)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size,
            FontWeight = heavy ? FontWeights.ExtraBold : FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center,
        };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        TextCase.Verbatim(t);
        return t;
    }

    public static TextBlock BigFigure(string text, double size, string brush)
    {
        var t = Big(text, size, brush);
        FigureLines(t, size);
        return t;
    }

    public static TextBlock LiveFigure(string text, double size, string brush, bool heavy = true)
    {
        var t = Big(text, size, brush, heavy);
        FigureLines(t, size);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    public static void FigureLines(DependencyObject target, double size)
    {
        TextBlock.SetLineStackingStrategy(target, LineStackingStrategy.BlockLineHeight);
        TextBlock.SetLineHeight(target, FigureLineHeight(size));
    }

    public static double FigureLineHeight(double size) => Math.Round(size * 1.08);

    public static Border FigureMark(TextBlock figure, double width, string brush, double gap)
    {
        var mark = new Border { Width = width, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        mark.SetResourceReference(Border.BackgroundProperty, brush);
        void Place()
        {
            var family = figure.FontFamily;
            double size = figure.FontSize, line = figure.LineHeight;
            double baseline = double.IsNaN(line) || family.LineSpacing <= 0 ? family.Baseline * size : line * family.Baseline / family.LineSpacing;
            double caps = new Typeface(family, figure.FontStyle, figure.FontWeight, figure.FontStretch).TryGetGlyphTypeface(out var glyphs) ? glyphs.CapsHeight * size : size * 0.7;
            mark.Height = Math.Max(1, Math.Round(caps));
            mark.Margin = new Thickness(0, Math.Max(0, Math.Round(baseline - caps)), gap, 0);
        }
        figure.Loaded += (_, _) => Place();
        Place();
        DockPanel.SetDock(mark, Dock.Left);
        return mark;
    }

    public static TextBlock Value(string text, double size = 15, string brush = "Heading")
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.FontFamilyProperty, "RaceFont");
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        System.Windows.Documents.Typography.SetNumeralAlignment(t, FontNumeralAlignment.Tabular);
        return t;
    }

    public static string GainBrush(int gained) => gained > 0 ? "SageDeep" : gained < 0 ? "CoralDeep" : "TextMuted";

    public static string Signed(int n) => n > 0 ? "+" + n.ToString(CultureInfo.CurrentCulture) : n < 0 ? "−" + (-n).ToString(CultureInfo.CurrentCulture) : "0";

    public static Icon Glyph(string icon, double size, string brush)
    {
        var glyph = new Icon { Data = Ui.Res<Geometry>(icon), Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
        glyph.SetResourceReference(Control.ForegroundProperty, brush);
        return glyph;
    }

    public static FrameworkElement Ribbon(Award award)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 4, 11, 4) };
        row.Children.Add(Glyph(RaceResult.Icon(award), 13, "OnAccent"));
        var name = Big(RaceResult.Name(award), 13.5, "OnAccent", heavy: false);
        name.Margin = new Thickness(5, 0, 0, 0);
        row.Children.Add(name);
        var grid = new Grid { ToolTip = RaceResult.Why(award), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        grid.Children.Add(new Slant("Accent", 0.1, 0, 1, 0, 1, 1, 0, 1));
        grid.Children.Add(row);
        return grid;
    }

    public static Border Chip(FrameworkElement content, string background = "SurfaceAlt", string edge = "Line", string? tip = null)
    {
        var chip = Hairline.Frame(new Border { Padding = new Thickness(7, 0, 7, 0), Height = Ui.ChipHeight, Child = content, ToolTip = tip, Margin = new Thickness(0, 0, 5, 5) });
        chip.SetResourceReference(Border.BackgroundProperty, background);
        chip.SetResourceReference(Border.BorderBrushProperty, edge);
        content.VerticalAlignment = VerticalAlignment.Center;
        return chip;
    }

    public static Border AwardChip(Award award, string place)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Glyph(RaceResult.Icon(award), 12, place + "Text"));
        row.Children.Add(new TextBlock { Text = RaceResult.Name(award), Style = Ui.Res<Style>("Caption"), Margin = new Thickness(5, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }
            .Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, "Heading")));
        return Chip(row, place + "Soft", place + "Edge", RaceResult.Why(award));
    }

    public static Border AwardSquare(Award award, string place)
    {
        var square = Hairline.Frame(new Border { Width = Ui.ChipHeight, Height = Ui.ChipHeight, Child = Glyph(RaceResult.Icon(award), 13, place + "Text"), Margin = new Thickness(3, 0, 0, 0),
            ToolTip = $"{RaceResult.Name(award)}: {RaceResult.Why(award)}" });
        ((FrameworkElement)square.Child).HorizontalAlignment = HorizontalAlignment.Center;
        square.SetResourceReference(Border.BackgroundProperty, place + "Soft");
        square.SetResourceReference(Border.BorderBrushProperty, place + "Edge");
        return square;
    }

    public static Border Labeled(string label, string value, string? tip = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Big(label, 12.5, "TextMuted", heavy: false));
        row.Children.Add(ChipDivider());
        row.Children.Add(new TextBlock { Text = value, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center }
            .Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, "Text")));
        return Chip(row, tip: tip);
    }

    private static Border ChipDivider()
    {
        var line = Hairline.Rule(new Border { Height = 12, Margin = new Thickness(7, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }, Orientation.Vertical);
        line.SetResourceReference(Border.BackgroundProperty, "LineStrong");
        return line;
    }

    public static IEnumerable<Border> Ratings(SessionSummary s)
    {
        if (!s.Official)
        {
            yield return Labeled("Hosted", "Ratings unchanged", "League and hosted races don't change iRating or safety rating");
            yield break;
        }
        yield return Rating("iR", s.IrStart is { } ir && s.IrDelta is { } d ? ir + d : null, s.IrDelta, v => Math.Round(v).ToString("N0", CultureInfo.CurrentCulture),
            "iRating after the race, and the change. Known once your next session in this category starts");
        yield return Rating("SR", s.SrStart is { } sr && s.SrDelta is { } sd ? sr + sd : null, s.SrDelta, v => v.ToString("0.00", CultureInfo.CurrentCulture),
            "Safety rating after the race, and the change. Known once your next session in this category starts");
    }

    private static Border Rating(string label, double? value, double? delta, Func<double, string> format, string tip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Big(label, 12.5, "TextMuted", heavy: false));
        row.Children.Add(ChipDivider());
        var shown = new TextBlock { Text = value is { } v ? format(v) : RaceFormat.Missing, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
        shown.SetResourceReference(TextBlock.ForegroundProperty, value is null ? "TextFaint" : "Text");
        row.Children.Add(shown);
        if (value is not null && delta is { } d)
        {
            string brush = d > 0 ? "SageDeep" : d < 0 ? "CoralDeep" : "TextMuted";
            string sign = d > 0 ? "+" : d < 0 ? "−" : "";
            row.Children.Add(new TextBlock { Text = sign + format(Math.Abs(d)), Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) }
                .Also(t => t.SetResourceReference(TextBlock.ForegroundProperty, brush)));
        }
        return Chip(row, tip: tip);
    }

    public const string ConsistencyTip = "70% how even your clean laps were (laps more than 7% off your middle lap time left out), 30% the share of clean laps";

    public static FrameworkElement Tile(string value, string label, string bar, string valueBrush = "Heading", double size = 30, string? tip = null, bool centred = false)
    {
        var stack = new StackPanel { Margin = new Thickness(16, 12, 12, 12) };
        var number = LiveFigure(value, size, valueBrush);
        number.HorizontalAlignment = HorizontalAlignment.Left;
        var figureRow = new DockPanel { LastChildFill = true, HorizontalAlignment = centred ? HorizontalAlignment.Center : HorizontalAlignment.Stretch };
        if (!centred) figureRow.Children.Add(FigureMark(number, 3, bar, 9));
        figureRow.Children.Add(number);
        stack.Children.Add(figureRow);
        stack.Children.Add(new TextBlock
        {
            Text = label, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap,
            Margin = centred ? new Thickness(0, 2, 0, 0) : new Thickness(12, 2, 0, 0), TextAlignment = centred ? TextAlignment.Center : TextAlignment.Left,
        });
        if (centred)
        {
            stack.Margin = new Thickness(12, 12, 12, 8);
            stack.VerticalAlignment = VerticalAlignment.Center;
        }
        var cell = Hairline.Frame(new Border { Child = stack, ToolTip = tip }, Sides.Left);
        cell.SetResourceReference(Border.BorderBrushProperty, "Line");
        return cell;
    }

    public static Grid Tiles(int columns, params FrameworkElement?[] tiles)
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = columns };
        foreach (var t in tiles.OfType<FrameworkElement>()) grid.Children.Add(t);
        var host = new Grid();
        host.Children.Add(grid);
        var rule = Hairline.Rule(new Border { VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false });
        rule.SetResourceReference(Border.BackgroundProperty, "Line");
        host.Children.Add(rule);
        return host;
    }

    public const double StripeFade = 0.55;

    public static Stripe CarOnStripe(string? car, string place, double height, double carHeight, double carWidth, double lift = 0.13, double carArea = 0)
    {
        var picture = CarPictures.Picture(car, carHeight, carWidth);
        var band = new Slant(place + "Band", 0, 0.55 - lift, 1, 0.23 - lift, 1, 0.59 - lift, 0, 0.91 - lift);
        var edge = new Slant(place, 0, 0.94 - lift, 1, 0.62 - lift, 1, 0.66 - lift, 0, 0.98 - lift) { Opacity = 0.75 };
        var zone = new Stripe { Height = height, ClipToBounds = true, Car = picture, Bands = new[] { band, edge } };
        if (carArea > 0)
        {
            band.OpacityMask = edge.OpacityMask = FadeIn;
        }
        zone.Children.Add(band);
        zone.Children.Add(edge);
        if (picture is not null)
        {
            picture.HorizontalAlignment = carArea > 0 ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            picture.VerticalAlignment = carArea > 0 ? VerticalAlignment.Center : VerticalAlignment.Top;
            if (carArea > 0) picture.Margin = new Thickness(0, 0, Math.Max(0, (carArea - carWidth) / 2), 0);
            zone.Children.Add(picture);
        }
        return zone;
    }

    private static readonly Brush FadeIn = Frozen(new LinearGradientBrush(
        new GradientStopCollection { new(Colors.Transparent, 0), new(Colors.Black, StripeFade) }, new Point(0, 0.5), new Point(1, 0.5)));

    private static T Frozen<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    public static FrameworkElement Position(int finish, string place, double size, string label, string? tier = null, string? sub = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        var number = Big("P" + finish.ToString(CultureInfo.CurrentCulture), size, place + "Text");
        number.VerticalAlignment = VerticalAlignment.Top;
        number.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        number.LineHeight = size * 0.86;
        row.Children.Add(number);
        var side = new StackPanel { Margin = new Thickness(size * 0.06, size * 0.05, 0, 0) };
        side.Children.Add(Big(label, Math.Max(11.5, size * 0.1), "TextMuted", heavy: false));
        if (tier is not null) side.Children.Add(Big(tier, Math.Max(14, size * 0.2), place + "Text").Also(t => t.Margin = new Thickness(0, size * 0.04, 0, 0)));
        if (sub is not null) side.Children.Add(Big(sub, Math.Max(11.5, size * 0.1), "TextMuted", heavy: false));
        row.Children.Add(side);
        return row;
    }

    public static FrameworkElement CentredPosition(int finish, string place, double size, string label, string? tier = null, string? sub = null)
    {
        var row = (StackPanel)Position(finish, place, size, label, tier, sub);
        row.VerticalAlignment = VerticalAlignment.Center;
        var number = (TextBlock)row.Children[0];
        var shift = new TranslateTransform();
        row.RenderTransform = shift;
        void Centre()
        {
            var ink = VisualTreeHelper.GetContentBounds(number);
            if (ink.IsEmpty) return;
            double offset = Math.Round(number.ActualHeight / 2 - (ink.Top + ink.Height / 2));
            if (shift.Y != offset) shift.Y = offset;
        }
        number.SizeChanged += (_, _) => number.Dispatcher.BeginInvoke(Centre, System.Windows.Threading.DispatcherPriority.Loaded);
        return row;
    }

    public static FrameworkElement Climb(SessionSummary s, double size)
    {
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        if (s.StartPos is not { } start || s.FinishPos is not { } finish) return stack;
        int gained = start - finish;
        stack.Children.Add(Big(Signed(gained), size, GainBrush(gained)).Also(t => t.HorizontalAlignment = HorizontalAlignment.Right));
        var path = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, ToolTip = "Started → finished" };
        path.Children.Add(Big("P" + start, 12.5, "TextMuted", heavy: false));
        path.Children.Add(Glyph("I.ArrowRight", 11, "TextMuted").Also(g => g.Margin = new Thickness(4, 0, 4, 0)));
        path.Children.Add(Big("P" + finish, 12.5, "TextMuted", heavy: false));
        stack.Children.Add(path);
        return stack;
    }

    public static FrameworkElement Card(RaceCardData d, string label = "Finished", Sides edges = Sides.All)
    {
        var s = d.Session;
        string place = Place(s);
        int finish = s.ShownPos ?? 0;
        var stack = new StackPanel();

        var hero = new Grid { Height = 146 };
        hero.Children.Add(new Slant(place + "Wash", 0, 0, 1, 0, 1, 0.55, 0, 1));
        hero.Children.Add(new Slant(place + "Faint", 0, 0, 0.62, 0, 0.3, 1, 0, 1));
        var position = Position(finish, place, 112, label, s.HasTier ? s.Tier : null, s.ClassPlace);
        position.Margin = new Thickness(16, 22, 0, 0);
        position.HorizontalAlignment = HorizontalAlignment.Left;
        hero.Children.Add(position);
        var climb = Climb(s, 28);
        climb.Margin = new Thickness(0, 14, 16, 0);
        hero.Children.Add(climb);
        var cls = Big(s.CarClass, 12.5, "TextMuted", heavy: false);
        cls.HorizontalAlignment = HorizontalAlignment.Right;
        cls.VerticalAlignment = VerticalAlignment.Bottom;
        cls.Margin = new Thickness(0, 0, 16, 14);
        hero.Children.Add(cls);
        stack.Children.Add(hero);

        var car = new Grid { Margin = new Thickness(0, -16, 0, 0) };
        var stripe = CarOnStripe(s.Car, place, 104, 96, 250, lift: 0);
        car.Children.Add(stripe);
        stack.Children.Add(car);

        var who = new StackPanel { Margin = new Thickness(16, 8, 16, 0) };
        if (d.Driver.Length > 0)
        {
            var driver = Big(d.Driver, 26, "Heading", heavy: false);
            driver.TextTrimming = TextTrimming.CharacterEllipsis;
            who.Children.Add(TextFit.TipWhenTrimmed(driver));
        }
        who.Children.Add(Muted(s.Car));
        who.Children.Add(Muted(s.Place));
        who.Children.Add(Muted(Dates.Day(s.StartUtc.ToLocalTime())));
        stack.Children.Add(who);

        var chips = new WrapPanel { Margin = new Thickness(16, 10, 11, 0) };
        foreach (var chip in Ratings(s)) chips.Children.Add(chip);
        if (s.Incidents is { } inc) chips.Children.Add(Labeled("Inc", inc + "x", "Incidents"));
        foreach (var award in d.Awards) chips.Children.Add(AwardChip(award, place));
        stack.Children.Add(chips);

        var rows = new StackPanel { Margin = new Thickness(16, 6, 16, 0) };
        Row(rows, "Best lap", Value(s.Best is { } best ? StatFormat.LapTime(best) : RaceFormat.Missing));
        if (AverageClean(d.Laps) is { } average) Row(rows, "Average clean lap", Value(StatFormat.LapTime(average)));
        if (d.Consistency is { } grade) Row(rows, "Consistency", Value(grade.ToString("0.0", CultureInfo.CurrentCulture) + "%"), ConsistencyTip);
        Row(rows, "Laps", WithNote(s.Laps.ToString(CultureInfo.CurrentCulture), RaceFormat.Count(s.PitStops, "stop")));
        Row(rows, "Drive time", Value(StatFormat.Duration(s.DriveS)));
        stack.Children.Add(rows);

        LapStrip? lapStrip = null;
        if (d.Laps.Count > 1)
        {
            var strip = new StackPanel { Margin = new Thickness(16, 8, 16, 0) };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
            int clean = d.Laps.Count(l => l.Clean);
            var share = new TextBlock { Text = $"{100.0 * clean / d.Laps.Count:0}% clean", Style = Ui.Res<Style>("Caption") };
            DockPanel.SetDock(share, Dock.Right);
            head.Children.Add(share);
            head.Children.Add(new TextBlock { Text = "Lap times", Style = Ui.Res<Style>("Caption") });
            strip.Children.Add(head);
            lapStrip = new LapStrip(d.Laps, place, scale: true) { Height = 64 };
            strip.Children.Add(lapStrip);
            strip.Children.Add(LapKey(place));
            stack.Children.Add(strip);
        }
        if (d.Conditions is { } conditions && ConditionsBlock(s, conditions, d.Imperial) is { } block) stack.Children.Add(block);
        else stack.Children.Add(new Border { Height = d.Laps.Count > 1 ? 14 : 12 });

        var card = Hairline.Frame(new PitBox.Controls.Tile { Child = stack, ClipToBounds = true }, edges);
        card.SetResourceReference(Border.BackgroundProperty, "Surface");
        card.SetResourceReference(Border.BorderBrushProperty, "CardEdge");
        if (edges == Sides.All)
        {
            card.SetResourceReference(Border.CornerRadiusProperty, "R.Card");
            PitBox.Controls.Tile.SetLifted(card, true);
        }
        RaceArrival.When(card, position, climb, stripe, lapStrip);
        return card;
    }

    private static void Row(Panel rows, string label, FrameworkElement value, string? tip = null)
    {
        var line = new DockPanel { Height = 30, ToolTip = tip, Background = Brushes.Transparent };
        value.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(value, Dock.Right);
        line.Children.Add(value);
        line.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center });
        var rule = Hairline.Frame(new Border { Child = line }, Sides.Top);
        rule.SetResourceReference(Border.BorderBrushProperty, "Line");
        rows.Children.Add(rule);
    }

    private static FrameworkElement WithNote(string value, string note)
    {
        if (note.Length == 0) return Value(value);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Value(value));
        row.Children.Add(new TextBlock { Text = note, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private static FrameworkElement LapKey(string place)
    {
        var key = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
        var hint = new TextBlock { Text = "Taller = faster", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFaint");
        DockPanel.SetDock(hint, Dock.Right);
        key.Children.Add(hint);
        var items = new StackPanel { Orientation = Orientation.Horizontal };
        void Item(string brush, double opacity, string label)
        {
            var swatch = new Border { Width = 8, Height = 8, Opacity = opacity, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            swatch.SetResourceReference(Border.BackgroundProperty, brush);
            items.Children.Add(swatch);
            items.Children.Add(new TextBlock { Text = label, Style = Ui.Res<Style>("Caption"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center });
        }
        Item(place, LapStrip.CleanOpacity, "Clean");
        Item("Danger", LapStrip.DirtyOpacity, "Not clean");
        Item("Heading", 1, "Best");
        key.Children.Add(items);
        return key;
    }

    private static FrameworkElement? ConditionsBlock(SessionSummary s, SessionConditions c, bool imperial)
    {
        var rows = new StackPanel();
        if (ConditionsText.Surface(c.WetnessMax, c.RainMax, c.RainS, s.DriveS) is { } surface)
            Row(rows, "Surface", WithNote(surface.Value, surface.Note), "The wettest the track got, and how much of the race had rain or a track declared wet");
        if (ConditionsText.Temp(c.TrackTempMin, c.TrackTempMax, null, imperial) is { } track) Row(rows, "Track temperature", Value(track), "Lowest–highest while you drove");
        if (ConditionsText.Temp(c.AirTempMin, c.AirTempMax, c.AirTemp, imperial) is { } air) Row(rows, "Air temperature", Value(air), "Lowest–highest while you drove");
        if (ConditionsText.Humidity(c.HumidityMin, c.HumidityMax, c.Humidity) is { } humidity) Row(rows, "Humidity", Value(humidity), "Lowest–highest relative humidity while you drove");
        if (ConditionsText.Wind(c.WindMin, c.WindMax, c.WindKph, imperial) is { } wind)
            Row(rows, "Wind", WithNote(wind, c.WindDir is { } dir ? ConditionsText.Compass(dir) : ""), "Lowest–highest wind speed while you drove, and where it came from at the start");
        if (c.Rubber.Length > 0) Row(rows, "Rubber", Value(ConditionsText.Rubber(c.Rubber)), "Rubber on the racing line at the start");

        string sky = c.Skies is { } skies and >= 0 and < 4 ? ConditionsText.SkyNames[skies] : "";
        string? simTime = c.SimStart.Length > 0 ? Pages.StatsSessionView.SimTime(c.SimStart) : null;
        if (rows.Children.Count == 0 && sky.Length == 0) return null;

        var block = new StackPanel { Margin = new Thickness(16, 12, 16, 14) };
        var head = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
        if (c.Dynamic)
        {
            var dynamic = new TextBlock { Text = "Dynamic weather", Style = Ui.Res<Style>("Caption"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dynamic, Dock.Right);
            head.Children.Add(dynamic);
        }
        head.Children.Add(new TextBlock { Text = "Conditions", Style = Ui.Res<Style>("Eyebrow"), VerticalAlignment = VerticalAlignment.Center });
        var rule = Hairline.Frame(new Border { Child = head }, Sides.Top);
        rule.SetResourceReference(Border.BorderBrushProperty, "Line");
        block.Children.Add(rule);

        if (sky.Length > 0)
        {
            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
            var glyph = Glyph(c.Skies == 0 ? "I.Sun" : "I.Cloud", 26, "AccentText");
            glyph.Margin = new Thickness(0, 0, 10, 0);
            line.Children.Add(glyph);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            words.Children.Add(Big(sky, 17, "Heading", heavy: false));
            if (simTime is not null) words.Children.Add(Muted($"Race start {simTime} sim time"));
            line.Children.Add(words);
            block.Children.Add(line);
        }
        block.Children.Add(rows);
        return block;
    }

    public static double? AverageClean(IReadOnlyList<(double Time, bool Clean)> laps)
    {
        if (laps.Count == 0) return null;
        var sorted = laps.Select(l => l.Time).OrderBy(t => t).ToList();
        double median = sorted[sorted.Count / 2];
        var pace = laps.Where(l => l.Clean && l.Time <= median * RaceResult.PaceWindow).Select(l => l.Time).ToList();
        return pace.Count > 0 ? pace.Average() : null;
    }

    private static TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, Style = Ui.Res<Style>("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return t;
    }

    private static T Also<T>(this T item, Action<T> act)
    {
        act(item);
        return item;
    }
}
