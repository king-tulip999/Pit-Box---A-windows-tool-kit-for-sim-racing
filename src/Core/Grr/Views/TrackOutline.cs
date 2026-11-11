using System.Windows;
using System.Windows.Media;
using Core.SimRacing.TrackMaps;
using Core.Views;

namespace Core.Grr.Views;

internal sealed class TrackOutline : FrameworkElement
{
    private const double Pad = 6, Under = 6, Line = 2.5, Tick = 11;
    private readonly Pen _under = Pen("LineStrong", Under), _line = Pen("Accent", Line), _tick = Pen("Heading", 2);
    private TrackMap? _map;

    public TrackOutline()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    public TrackMap? Map
    {
        get => _map;
        set
        {
            _map = value;
            InvalidateVisual();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var line = _map?.Centerline;
        if (line is not { Length: > 1 } || ActualWidth < 4 * Pad || ActualHeight < 4 * Pad) return;
        float minX = line.Min(p => p.X), maxX = line.Max(p => p.X), minY = line.Min(p => p.Y), maxY = line.Max(p => p.Y);
        double scale = Math.Min((ActualWidth - 2 * Pad) / Math.Max(1, maxX - minX), (ActualHeight - 2 * Pad) / Math.Max(1, maxY - minY));
        double ox = (ActualWidth - (maxX - minX) * scale) / 2, oy = (ActualHeight - (maxY - minY) * scale) / 2;
        Point At(System.Drawing.PointF p) => new(ox + (p.X - minX) * scale, oy + (p.Y - minY) * scale);

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(At(line[0]), false, true);
            for (int i = 1; i < line.Length; i++) g.LineTo(At(line[i]), true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(null, _under, geo);
        dc.DrawGeometry(null, _line, geo);

        if (_map!.StartLine is { } start)
        {
            Point a = At(start.A), b = At(start.B);
            Vector along = b - a;
            if (along.Length < 0.01) return;
            along.Normalize();
            Point mid = a + (b - a) / 2;
            dc.DrawLine(_tick, mid - along * Tick / 2, mid + along * Tick / 2);
        }
    }

    private static Pen Pen(string brush, double width)
    {
        var pen = new Pen(Ui.Res<Brush>(brush), width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}
