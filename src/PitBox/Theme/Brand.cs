using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PitBox.Theme;

internal static class Brand
{
    public const string Name = "Pit-Box";
    public const string Wordmark = WordmarkStart + WordmarkJoin + WordmarkEnd;
    public const string TrayKey = "Brand";

    private const double Grid = 24;
    private const double CapRatio = 0.7;

    public static readonly Typeface Typeface = new(ThemeManager.Family("./Assets/Fonts/Condensed/BarlowCondensed-ExtraBoldItalic.ttf#Barlow Condensed"), FontStyles.Italic, FontWeights.ExtraBold, FontStretches.Normal);

    public const string WordmarkStart = "PIT";
    public const string WordmarkJoin = "-";
    public const string WordmarkEnd = "BOX";

    public static readonly Typeface WordmarkFace = new(ThemeManager.Family("./Assets/Fonts/Condensed/#Barlow Condensed"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static LinearGradientBrush JoinBrush(Color from, Color to)
    {
        var brush = new LinearGradientBrush(from, to, new Point(0, 0.5), new Point(1, 0.5));
        brush.Freeze();
        return brush;
    }

    private static Geometry? _letters;

    public static Geometry Letters => _letters ??= BuildLetters();

    private static Geometry BuildLetters()
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        group.Children.Add(Word("PIT", 6.8, 7.2, 16.4));
        group.Children.Add(Word("BOX", 17.2, 7.2, 16.4));
        group.Freeze();
        return group;
    }

    private static Geometry Word(string text, double centreY, double cap, double width)
    {
        var probe = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface, 100, Brushes.Black, 1.0);
        var shape = probe.BuildGeometry(new Point(0, 0));
        double sy = cap / CapRatio / 100, sx = width / probe.WidthIncludingTrailingWhitespace;
        var m = Matrix.Identity;
        m.Scale(sx, sy);
        m.Translate(Grid / 2 - width / 2, centreY + cap / 2 - probe.Baseline * sy);
        shape.Transform = new MatrixTransform(m);
        return shape;
    }

    public static void Draw(DrawingContext dc, Rect tile, Color ink, double pixelsPerDip)
    {
        double unit = tile.Width / Grid;
        dc.PushTransform(new TranslateTransform(tile.X, tile.Y));
        dc.PushTransform(new ScaleTransform(unit, unit));
        dc.DrawGeometry(Solid(ink), null, Letters);
        dc.Pop();
        dc.Pop();

        double px = 1 / pixelsPerDip;
        double size = tile.Width * pixelsPerDip;
        double height = Math.Max(1, Math.Round(1.2 * size / Grid));
        double top = Math.Round((tile.Y + tile.Height / 2) * pixelsPerDip - height / 2);
        double left = Math.Round((tile.X + 3.6 * unit) * pixelsPerDip), right = Math.Round((tile.X + 20.4 * unit) * pixelsPerDip);
        dc.DrawRectangle(Fade(ink), null, new Rect(left * px, top * px, (right - left) * px, height * px));
    }

    private static SolidColorBrush Solid(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Fade(Color c)
    {
        var clear = Color.FromArgb(0, c.R, c.G, c.B);
        var brush = new LinearGradientBrush(new GradientStopCollection
        {
            new(clear, 0), new(c, 0.35), new(c, 0.65), new(clear, 1),
        }, new Point(0, 0.5), new Point(1, 0.5));
        brush.Freeze();
        return brush;
    }
}
