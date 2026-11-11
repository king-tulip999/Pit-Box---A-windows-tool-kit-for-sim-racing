using System.Windows;
using System.Windows.Media;

namespace Core.Theme;

internal static class Brand
{
    public static string Name => AppIdentity.Name;
    public static string Wordmark => AppIdentity.IsTecnica ? "TECNICA" : "VELOCITA";
    public const string TrayKey = "Brand";

    private const double Grid = 24;

    public static readonly Typeface WordmarkLead = new(ThemeManager.Family(ThemeSpec.Saira), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static readonly Typeface WordmarkBody = new(ThemeManager.Family(ThemeSpec.Saira), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public const string TecnicaInk = "M7.49,3.97 7.7,5 7.49,6.03 6.91,6.91 6.03,7.49 5,7.7 3.97,7.49 3.09,6.91 2.51,6.03 2.3,5 2.51,3.97 3.09,3.09 3.97,2.51 5,2.3 6.03,2.51 6.91,3.09ZM13.91,10.09 14.49,10.97 14.7,12 14.49,13.03 13.91,13.91 13.03,14.49 12,14.7 10.97,14.49 10.09,13.91 9.51,13.03 9.3,12 9.51,10.97 10.09,10.09 10.97,9.51 12,9.3 13.03,9.51ZM20.91,17.09 21.49,17.97 21.7,19 21.49,20.03 20.91,20.91 20.03,21.49 19,21.7 17.97,21.49 17.09,20.91 16.51,20.03 16.3,19 16.51,17.97 17.09,17.09 17.97,16.51 19,16.3 20.03,16.51Z";

    public const string TecnicaAccent = "M13.91,3.09 14.49,3.97 14.7,5 14.49,6.03 13.91,6.91 13.03,7.49 12,7.7 10.97,7.49 10.09,6.91 9.51,6.03 9.3,5 9.51,3.97 10.09,3.09 10.97,2.51 12,2.3 13.03,2.51ZM20.03,2.51 20.91,3.09 21.49,3.97 21.7,5 21.49,6.03 20.91,6.91 20.03,7.49 19,7.7 17.97,7.49 17.09,6.91 16.51,6.03 16.3,5 16.51,3.97 17.09,3.09 17.97,2.51 19,2.3ZM6.03,9.51 6.91,10.09 7.49,10.97 7.7,12 7.49,13.03 6.91,13.91 6.03,14.49 5,14.7 3.97,14.49 3.09,13.91 2.51,13.03 2.3,12 2.51,10.97 3.09,10.09 3.97,9.51 5,9.3ZM20.91,10.09 21.49,10.97 21.7,12 21.49,13.03 20.91,13.91 20.03,14.49 19,14.7 17.97,14.49 17.09,13.91 16.51,13.03 16.3,12 16.51,10.97 17.09,10.09 17.97,9.51 19,9.3 20.03,9.51ZM6.03,16.51 6.91,17.09 7.49,17.97 7.7,19 7.49,20.03 6.91,20.91 6.03,21.49 5,21.7 3.97,21.49 3.09,20.91 2.51,20.03 2.3,19 2.51,17.97 3.09,17.09 3.97,16.51 5,16.3ZM13.03,16.51 13.91,17.09 14.49,17.97 14.7,19 14.49,20.03 13.91,20.91 13.03,21.49 12,21.7 10.97,21.49 10.09,20.91 9.51,20.03 9.3,19 9.51,17.97 10.09,17.09 10.97,16.51 12,16.3Z";

    public const string VelocitaInk = "";

    public const string VelocitaAccent = "M10.66,8.11 11.76,9.3 14.53,12.77 15.93,14.19 16.92,14.87 17.41,15.09 17.91,15.23 18.39,15.27 18.85,15.22 19.31,15.05 19.77,14.74 20.2,14.28 20.56,13.64 20.81,12.86 20.89,12 20.81,11.14 20.56,10.36 20.2,9.72 19.77,9.26 19.31,8.95 18.85,8.78 18.39,8.73 17.91,8.77 17.41,8.91 16.42,9.44 15.93,9.81 14.95,10.75 13.08,8.38 14.06,7.46 15.43,6.53 16.29,6.13 17.24,5.84 18.28,5.73 19.39,5.83 20.51,6.2 20.83,6.36 21.85,7.09 22.72,8.09 23.37,9.3 23.77,10.68 23.89,12.15 23.77,13.32 23.37,14.7 22.72,15.91 21.85,16.91 21.58,17.14 20.83,17.64 19.72,18.09 18.28,18.27 17.24,18.16 16.29,17.87 15.6,17.56 14.79,17.08 14.06,16.54 13.34,15.89 12.24,14.7 9.47,11.23 8.07,9.81 7.08,9.13 6.59,8.91 6.09,8.77 5.61,8.73 5.15,8.78 4.69,8.95 4.23,9.26 3.8,9.72 3.44,10.36 3.19,11.14 3.11,12 3.19,12.86 3.44,13.64 3.8,14.28 4.23,14.74 4.69,15.05 5.15,15.22 5.61,15.27 6.09,15.23 6.59,15.09 7.58,14.56 8.07,14.19 9.05,13.25 10.92,15.62 9.94,16.54 8.57,17.47 7.5,17.95 6.5,18.2 5.41,18.27 4.28,18.09 3.17,17.64 2.15,16.91 1.28,15.91 0.63,14.7 0.23,13.32 0.11,11.85 0.23,10.68 0.63,9.3 1.28,8.09 2.15,7.09 3.17,6.36 4.28,5.91 5.41,5.73 6.5,5.8 7.5,6.05 8.57,6.53 9.34,7.01Z";

    private static readonly int[] CrispSizes = { 16, 20, 24, 32 };

    private static Dictionary<int, string[]>? _pixelSizes;

    private static Dictionary<int, string[]> PixelSizes => _pixelSizes ??= CrispSizes.ToDictionary(size => size, Sample);

    private static string[] Sample(int size)
    {
        var rows = new string[size];
        var row = new char[size];
        double unit = Grid / size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var centre = new Point((x + 0.5) * unit, (y + 0.5) * unit);
                row[x] = InkShape.FillContains(centre) ? 'P' : AccentShape.FillContains(centre) ? 'B' : '.';
            }
            rows[y] = new string(row);
        }
        return rows;
    }

    public static IReadOnlyDictionary<int, string[]> Pixels => PixelSizes;

    public static double? CrispSize(double maxHeight, double pixelsPerDip)
    {
        double best = PixelSizes.Keys.Select(px => px / pixelsPerDip).Where(dip => dip <= maxHeight).DefaultIfEmpty(0).Max();
        return best >= maxHeight * 0.75 ? best : null;
    }

    private static Geometry? _ink, _accent;

    public static Geometry InkShape => _ink ??= Parse(AppIdentity.IsTecnica ? TecnicaInk : VelocitaInk);

    public static Geometry AccentShape => _accent ??= Parse(AppIdentity.IsTecnica ? TecnicaAccent : VelocitaAccent);

    public static Geometry Parse(string data)
    {
        if (data.Length == 0) return Geometry.Empty;
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
