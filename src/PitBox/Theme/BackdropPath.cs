using System.IO;
using System.Windows;
using System.Windows.Media;

namespace PitBox.Theme;

internal static class BackdropLook
{
    public const double LineContrast = 13.86;

    public static double Luminance(Color c) => 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

    public static double LineAlpha(Color line, Color bg)
    {
        double gap = Math.Abs(Luminance(line) - Luminance(bg));
        return gap <= 0 ? 0 : Math.Clamp(LineContrast / gap, 0.04, 1);
    }

    public static Matrix Cover(Size area, Size picture)
    {
        double s = Math.Max(area.Width / picture.Width, area.Height / picture.Height);
        var m = Matrix.Identity;
        m.Scale(s, s);
        m.Translate((area.Width - picture.Width * s) / 2, (area.Height - picture.Height * s) / 2);
        return m;
    }
}

internal sealed class BackdropPath
{
    public Point[] Points { get; }
    public double[] Lengths { get; }
    public double Total => Lengths[^1];

    public BackdropPath(Point[] points)
    {
        Points = points;
        Lengths = new double[points.Length];
        for (int i = 1; i < points.Length; i++) Lengths[i] = Lengths[i - 1] + (points[i] - points[i - 1]).Length;
    }

    public Point At(double s)
    {
        if (s <= 0) return Points[0];
        if (s >= Total) return Points[^1];
        int i = Array.BinarySearch(Lengths, s);
        if (i >= 0) return Points[i];
        i = ~i;
        double span = Lengths[i] - Lengths[i - 1];
        double t = span <= 0 ? 0 : (s - Lengths[i - 1]) / span;
        var a = Points[i - 1];
        var b = Points[i];
        return new Point(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    public static (Size Picture, BackdropPath[] Paths) Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        int width = reader.ReadUInt16(), height = reader.ReadUInt16();
        int count = (int)reader.ReadUInt32();
        var paths = new List<BackdropPath>(count);
        for (int c = 0; c < count; c++)
        {
            int n = reader.ReadUInt16();
            var points = new Point[n];
            for (int i = 0; i < n; i++) points[i] = new Point(reader.ReadUInt16(), reader.ReadUInt16());
            if (n >= 2) paths.Add(new BackdropPath(points));
        }
        return (new Size(width, height), paths.ToArray());
    }
}
