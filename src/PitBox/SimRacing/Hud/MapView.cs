using System.Drawing;

namespace PitBox.SimRacing.Hud;

internal readonly record struct MapView(PointF Screen, PointF Centre, float Scale, float Angle)
{
    public PointF ToScreen(PointF m)
    {
        float dx = m.X - Centre.X, dy = m.Y - Centre.Y;
        float cos = MathF.Cos(Angle), sin = MathF.Sin(Angle);
        return new PointF(Screen.X + Scale * (cos * dx - sin * dy), Screen.Y + Scale * (sin * dx + cos * dy));
    }

    public static float FitScale(RectangleF extent, SizeF room, bool turning)
    {
        if (extent.Width <= 0 || extent.Height <= 0) return 0.0001f;
        if (turning)
        {
            float across = MathF.Sqrt(extent.Width * extent.Width + extent.Height * extent.Height);
            return Math.Max(0.0001f, Math.Min(room.Width, room.Height) / across);
        }
        return Math.Max(0.0001f, Math.Min(room.Width / extent.Width, room.Height / extent.Height));
    }

    public static float UpAngle(PointF heading) => Wrap(-MathF.PI / 2 - MathF.Atan2(heading.Y, heading.X));

    public static float Ease(float from, float to, double seconds, double settle)
    {
        if (settle <= 0 || seconds <= 0) return seconds <= 0 ? from : Wrap(to);
        float t = (float)(1 - Math.Exp(-seconds / settle));
        return Wrap(from + Wrap(to - from) * t);
    }

    public static float Wrap(float a)
    {
        a %= 2 * MathF.PI;
        if (a > MathF.PI) a -= 2 * MathF.PI;
        else if (a <= -MathF.PI) a += 2 * MathF.PI;
        return a;
    }
}
