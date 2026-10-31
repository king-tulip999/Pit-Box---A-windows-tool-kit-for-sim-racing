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
