using System.Windows;
using System.Windows.Media;

namespace PitBox.Theme;

internal static class Brand
{
    public const string Name = "Pit-Box";
    public const string Wordmark = WordmarkStart + WordmarkGap + WordmarkEnd;
    public const string TrayKey = "Brand";

    private const double Grid = 24;

    public const string WordmarkStart = "PIT";
    public const string WordmarkGap = "_";
    public const string WordmarkEnd = "BOX";

    public static readonly Typeface WordmarkFace = new(ThemeManager.Family(RaceLook.FontPath), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private const string InkPath =
        "M12.642,6.413 7.033,6.428 6.742,6.465 6.544,6.516 6.319,6.605 6.165,6.683 5.830,6.903 5.591,7.110 5.347,7.370 5.114,7.701 4.901,8.072 4.687,8.508 "
        + "4.609,8.733 4.584,8.945 4.605,9.050 4.652,9.141 4.746,9.209 4.877,9.249 5.142,9.248 5.750,9.197 8.766,9.180 8.845,9.163 9.652,9.163 9.732,9.145 "
        + "10.869,9.145 10.935,9.162 11.491,9.163 11.636,9.182 11.848,9.256 11.960,9.341 12.045,9.460 12.100,9.606 12.123,9.765 12.116,9.884 12.062,10.082 "
        + "11.966,10.280 11.821,10.472 11.583,10.666 11.345,10.790 11.041,10.864 10.869,10.882 5.750,10.882 5.671,10.900 5.486,10.900 5.076,10.970 4.745,11.080 "
        + "4.335,11.270 3.994,11.484 3.572,11.841 3.319,12.145 3.145,12.423 2.914,12.913 2.051,14.989 1.505,16.246 1.280,16.815 1.221,17.040 1.228,17.264 "
        + "1.266,17.366 1.372,17.477 1.517,17.535 1.650,17.554 3.263,17.554 3.528,17.499 3.726,17.376 3.813,17.291 3.897,17.172 4.113,16.749 5.116,14.447 "
        + "5.353,14.079 5.591,13.860 5.750,13.762 5.975,13.672 6.319,13.616 10.327,13.616 10.406,13.599 10.684,13.599 11.200,13.530 11.729,13.409 12.033,13.303 "
        + "12.377,13.140 12.694,12.941 13.047,12.661 13.343,12.371 13.620,12.037 14.028,11.431 14.256,10.995 14.361,10.743 14.609,9.976 14.702,9.566 14.748,9.249 "
        + "14.765,8.746 14.749,8.521 14.692,8.164 14.566,7.754 14.418,7.450 14.231,7.185 13.951,6.912 13.802,6.802 13.554,6.662 13.210,6.519 13.012,6.464Z "
        + "M0,3.778 17.654,3.778 17.280,5.218 0,5.218Z";

    private const string AccentPath =
        "M15.000,6.418 14.956,6.466 14.966,6.537 15.301,7.040 15.396,7.251 15.517,7.648 15.584,8.111 15.593,8.402 15.554,9.050 15.591,9.154 15.671,9.180 "
        + "19.215,9.182 19.401,9.258 19.544,9.421 19.627,9.698 19.625,9.963 19.551,10.241 19.387,10.553 19.215,10.760 19.123,10.838 18.951,10.946 18.819,11.001 "
        + "18.607,11.037 15.300,11.037 15.234,11.058 15.181,11.117 15.025,11.497 14.791,11.947 14.498,12.397 14.173,12.807 14.149,12.901 14.176,12.928 "
        + "14.242,12.946 18.210,12.946 18.303,12.965 18.422,13.025 18.514,13.113 18.590,13.243 18.648,13.442 18.651,13.640 18.625,13.839 18.492,14.182 "
        + "18.303,14.453 18.131,14.613 17.906,14.745 17.641,14.802 10.433,14.807 10.357,14.844 10.299,14.910 8.597,17.331 8.515,17.476 8.516,17.529 8.543,17.569 "
        + "18.250,17.575 18.329,17.555 18.475,17.553 18.858,17.465 19.123,17.360 19.586,17.087 19.875,16.854 20.134,16.590 20.551,16.053 20.806,15.585 "
        + "21.009,15.069 22.614,8.878 22.661,8.614 22.686,8.191 22.668,7.939 22.564,7.490 22.403,7.110 22.208,6.855 21.940,6.625 21.702,6.502 21.371,6.413Z "
        + "M7.094,18.782 24,18.782 24,20.222 6.720,20.222Z";

    private static readonly Dictionary<int, string[]> PixelSizes = new()
    {
        [16] =
        [
            "................",
            "................",
            "PPPPPPPPPPP.....",
            "................",
            "..PPPPP..BBBBBB.",
            "..PPPPPP.BBBBBBB",
            "......PP......BB",
            "..PPPPP......BB.",
            "..PPPP....BBBBB.",
            "..PP.........BB.",
            ".PP..........BB.",
            "PP..BBBBBBBBBB..",
            "PP..BBBBBBBBB...",
            "................",
            ".....BBBBBBBBBBB",
            "................",
        ],
        [20] =
        [
            "....................",
            "....................",
            "....................",
            "PPPPPPPPPPPPPP......",
            "....................",
            "...PPPPPP..BBBBBBBB.",
            "..PPPPPPP.BBBBBBBBB.",
            "..PPPPPPP.BBBBBBBBB.",
            "......PPP.......BBB.",
            "......PPP.......BBB.",
            "..PPPPPP...BBBBBBB..",
            "..PPPPPP.......BBB..",
            "..PPPPP........BBB..",
            "..PPP..BBBBBBBBBBB..",
            "PPP..BBBBBBBBBBBB...",
            "PPP..BBBBBBBBBBB....",
            "....................",
            "......BBBBBBBBBBBBBB",
            "....................",
            "....................",
        ],
        [24] =
        [
            "........................",
            "........................",
            "........................",
            "PPPPPPPPPPPPPPPPP.......",
            "........................",
            "........................",
            "...PPPPPPPP..BBBBBBBBBB.",
            "...PPPPPPPPP.BBBBBBBBBBB",
            "...PPPPPPPPP.BBBBBBBBBBB",
            "........PPP.........BBB.",
            "........PPP.........BBB.",
            "...PPPPPPPP....BBBBBBBB.",
            "...PPPPPPPP....BBBBBBBB.",
            "..PPPPPPP..........BBB..",
            "...PPP.............BBB..",
            "..PPP..............BBB..",
            "..PPP..BBBBBBBBBBBBBBB..",
            "PPP..BBBBBBBBBBBBBBBB...",
            "PPP..BBBBBBBBBBBBBBB....",
            "........................",
            "........................",
            ".......BBBBBBBBBBBBBBBBB",
            "........................",
            "........................",
        ],
        [32] =
        [
            "................................",
            "................................",
            "................................",
            "................................",
            "PPPPPPPPPPPPPPPPPPPPPPP.........",
            "PPPPPPPPPPPPPPPPPPPPPP..........",
            "................................",
            "................................",
            "....PPPPPPPPPP....BBBBBBBBBBBBB.",
            "....PPPPPPPPPPPP..BBBBBBBBBBBBBB",
            "....PPPPPPPPPPPP..BBBBBBBBBBBBBB",
            "...PPPPPPPPPPPP..BBBBBBBBBBBBBB.",
            "...........PPPP............BBBB.",
            "...........PPPP............BBBB.",
            "...........PPPP............BBBB.",
            "...PPPPPPPPPPP......BBBBBBBBBB..",
            "...PPPPPPPPPPP......BBBBBBBBBB..",
            "...PPPPPPPPPPP......BBBBBBBBBB..",
            "...PPPPPPPPPP.............BBBB..",
            "...PPPP..................BBBB...",
            "..PPPP...................BBBB...",
            "..PPPP...BBBBBBBBBBBBBBBBBBBB...",
            ".PPPP...BBBBBBBBBBBBBBBBBBBBB...",
            "PPPP...BBBBBBBBBBBBBBBBBBBBB....",
            "PPPP...BBBBBBBBBBBBBBBBBBBB.....",
            "................................",
            "................................",
            "..........BBBBBBBBBBBBBBBBBBBBBB",
            "..........BBBBBBBBBBBBBBBBBBBBBB",
            "................................",
            "................................",
            "................................",
        ],
    };

    public static IReadOnlyDictionary<int, string[]> Pixels => PixelSizes;

    public static double? CrispSize(double maxHeight, double pixelsPerDip)
    {
        double best = PixelSizes.Keys.Select(px => px / pixelsPerDip).Where(dip => dip <= maxHeight).DefaultIfEmpty(0).Max();
        return best >= maxHeight * 0.75 ? best : null;
    }

    private static Geometry? _ink, _accent;

    public static Geometry InkShape => _ink ??= Parse(InkPath);

    public static Geometry AccentShape => _accent ??= Parse(AccentPath);

    private static Geometry Parse(string data)
    {
        var geometry = Geometry.Parse("F1 " + data);
        geometry.Freeze();
        return geometry;
    }

    public static void Draw(DrawingContext dc, Rect tile, Color ink, Color accent, double pixelsPerDip)
    {
        double device = tile.Width * pixelsPerDip;
        if (Math.Abs(device - Math.Round(device)) < 0.01 && PixelSizes.TryGetValue((int)Math.Round(device), out var rows))
        {
            DrawPixels(dc, tile, rows, Solid(ink), Solid(accent), 1 / pixelsPerDip);
            return;
        }
        double unit = tile.Width / Grid;
        dc.PushTransform(new TranslateTransform(tile.X, tile.Y));
        dc.PushTransform(new ScaleTransform(unit, unit));
        dc.DrawGeometry(Solid(ink), null, InkShape);
        dc.DrawGeometry(Solid(accent), null, AccentShape);
        dc.Pop();
        dc.Pop();
    }

    private static void DrawPixels(DrawingContext dc, Rect tile, string[] rows, Brush ink, Brush accent, double px)
    {
        double left = Math.Round(tile.X / px) * px, top = Math.Round(tile.Y / px) * px;
        var lines = Enumerable.Range(0, rows.Length + 1);
        dc.PushGuidelineSet(new GuidelineSet(lines.Select(i => left + i * px).ToArray(), lines.Select(i => top + i * px).ToArray()));
        for (int y = 0; y < rows.Length; y++)
        {
            string row = rows[y];
            for (int x = 0; x < row.Length;)
            {
                int end = x;
                while (end < row.Length && row[end] == row[x]) end++;
                if (row[x] != '.')
                    dc.DrawRectangle(row[x] == 'P' ? ink : accent, null, new Rect(left + x * px, top + y * px, (end - x) * px, px));
                x = end;
            }
        }
        dc.Pop();
    }

    private static SolidColorBrush Solid(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }
}
