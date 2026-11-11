using System.Windows;
using System.Windows.Media;

namespace Core.Theme;

internal static class BackdropLook
{
    public const double LineContrast = 13.86;
    public const int MaxDecodeWidth = 2560;
    public const int DecodeStep = 320;
    public const double PictureAspect = 16.0 / 9.0;

    public static int DecodeWidthFor(double pixelWidth, double pixelHeight)
    {
        double needed = Math.Max(pixelWidth, pixelHeight * PictureAspect);
        if (!(needed > 0) || double.IsInfinity(needed)) return MaxDecodeWidth;
        int stepped = (int)Math.Ceiling(needed / DecodeStep) * DecodeStep;
        return Math.Clamp(stepped, DecodeStep, MaxDecodeWidth);
    }

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
