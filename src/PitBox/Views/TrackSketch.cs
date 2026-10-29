using System.Windows;
using System.Windows.Media;
using PitBox.SimRacing.TrackMaps;

namespace PitBox.Views;

internal sealed class TrackSketch : FrameworkElement, IFrameStep
{
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(TrackSketch),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CarProperty = DependencyProperty.Register(nameof(Car), typeof(Brush), typeof(TrackSketch),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public Brush? Car { get => (Brush?)GetValue(CarProperty); set => SetValue(CarProperty, value); }

    private const int Points = 240;
    private const double DrawMs = 1300, LapMs = 9000;

    private readonly double _thickness;
    private readonly bool _lap;
    private Point[]? _loop;
    private double _drawn = 1, _carAt = -1, _start, _lapFrom;
    private bool _played, _driving;
    private Rect _bounds;
    private Matrix _fit;
    private Size _fitFor;
    private readonly DrawingVisual _car = new();
    private readonly TranslateTransform _carMove = new();
    private Brush? _carBrush;
    private Pen? _pen;
    private Brush? _penBrush;
    private Geometry? _full;
    private Size _fullFor;

    public bool PlayOnLoad { get; init; }
    public int? TrackId { get; init; }
    public string TrackName { get; init; } = "";

    public TrackSketch(string track, string config, string stroke, double thickness, bool lap = false)
    {
        _thickness = thickness;
        _lap = lap;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        SetResourceReference(StrokeProperty, stroke);
        SetResourceReference(CarProperty, "Heading");
        Visibility = Visibility.Hidden;
        _car.Transform = _carMove;
        AddVisualChild(_car);
        Loaded += (_, _) =>
        {
            Motion.WindowActiveChanged -= UpdateDriving;
            Motion.WindowActiveChanged += UpdateDriving;
            _ = LoadAsync(track, config);
        };
        Unloaded += (_, _) =>
        {
            Motion.WindowActiveChanged -= UpdateDriving;
            _driving = false;
            FrameDriver.Stop(this);
        };
        IsVisibleChanged += (_, _) => UpdateDriving();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _car;

    private async Task LoadAsync(string track, string config)
    {
        if (_loop is not null)
        {
            UpdateDriving();
            return;
        }
        TrackMap? map;
        try { map = await TrackMapLibrary.ForSession(TrackId, TrackName, track, config); }
        catch (Exception ex) { App.LogError(ex); map = null; }
        if (map is null || map.Centerline.Length < 3)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        _loop = TrackMap.Resample(map.Centerline, Points).Select(p => new Point(p.X, p.Y)).ToArray();
        _bounds = new Rect(new Point(_loop.Min(p => p.X), _loop.Min(p => p.Y)), new Point(_loop.Max(p => p.X), _loop.Max(p => p.Y)));
        _fitFor = Size.Empty;
        bool intro = PlayOnLoad && !_played && Motion.Flourish;
        if (intro) _drawn = 0;
        else if (_lap) _carAt = 0;
        Visibility = Visibility.Visible;
        InvalidateVisual();
        if (intro) Motion.AfterPageSettles(Dispatcher, Play);
        else UpdateDriving();
    }

    public void Play()
    {
        _played = true;
        if (_loop is null) return;
        if (!Motion.Flourish || !IsLoaded)
        {
            _drawn = 1;
            if (_lap && _carAt < 0) _carAt = 0;
            InvalidateVisual();
            UpdateDriving();
            return;
        }
        _drawn = 0;
        _carAt = -1;
        _driving = false;
        _start = FrameDriver.Now;
        FrameDriver.Run(this);
        InvalidateVisual();
    }

    private bool CanDrive => _lap && _loop is not null && _drawn >= 1 && Motion.Flourish && Motion.WindowActive && IsLoaded && IsVisible;

    private void UpdateDriving()
    {
        if (_drawn < 1 && _carAt < 0) return;
        if (CanDrive == _driving) return;
        _driving = !_driving;
        if (_driving)
        {
            _lapFrom = FrameDriver.Now - Math.Max(0, _carAt) * LapMs;
            FrameDriver.Run(this);
        }
        else FrameDriver.Stop(this);
    }

    public bool Step(double now)
    {
        if (_driving)
        {
            double lap = (now - _lapFrom) / LapMs;
            _carAt = lap - Math.Floor(lap);
            MoveCar();
            return true;
        }
        double t = now - _start;
        if (t < DrawMs)
        {
            double k = t / DrawMs;
            _drawn = k < 0.5 ? 4 * k * k * k : 1 - Math.Pow(-2 * k + 2, 3) / 2;
            InvalidateVisual();
            return true;
        }
        _drawn = 1;
        if (_lap) _carAt = 0;
        InvalidateVisual();
        if (!CanDrive) return false;
        _driving = true;
        _lapFrom = now;
        return true;
    }

    private void MoveCar()
    {
        if (_loop is null || _carAt < 0 || _fitFor != RenderSize) return;
        double at = _carAt * _loop.Length;
        int i = (int)at % _loop.Length;
        var a = _loop[i];
        var b = _loop[(i + 1) % _loop.Length];
        double u = at - Math.Floor(at);
        var p = _fit.Transform(new Point(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u));
        _carMove.X = p.X;
        _carMove.Y = p.Y;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_loop is null || Stroke is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        if (_pen is null || !ReferenceEquals(_penBrush, Stroke))
        {
            _penBrush = Stroke;
            _pen = new Pen(Stroke, _thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            _pen.Freeze();
            _full = null;
        }
        if (_fitFor != RenderSize)
        {
            _fit = Fit(RenderSize);
            _fitFor = RenderSize;
            _full = null;
        }
        var fit = _fit;
        if (_drawn >= 1)
        {
            if (_full is null || _fullFor != RenderSize)
            {
                _full = Build(fit, _loop.Length, closed: true);
                _fullFor = RenderSize;
            }
            dc.DrawGeometry(null, _pen, _full);
        }
        else if (_drawn > 0)
            dc.DrawGeometry(null, _pen, Build(fit, Math.Max(2, (int)Math.Round(_drawn * _loop.Length)), closed: false));
        bool carShown = _carAt >= 0 && Car is not null;
        if (carShown && !ReferenceEquals(_carBrush, Car))
        {
            _carBrush = Car;
            double r = _thickness * 1.1 + 1.5;
            using var cdc = _car.RenderOpen();
            cdc.DrawEllipse(Car, null, new Point(0, 0), r, r);
        }
        _car.Opacity = carShown ? 1 : 0;
        MoveCar();
    }

    private Matrix Fit(Size size)
    {
        double minX = _bounds.Left, minY = _bounds.Top, maxX = _bounds.Right, maxY = _bounds.Bottom;
        double pad = _thickness * 2 + 2;
        double scale = Math.Min((size.Width - 2 * pad) / Math.Max(1, maxX - minX), (size.Height - 2 * pad) / Math.Max(1, maxY - minY));
        var m = Matrix.Identity;
        m.Translate(-(minX + maxX) / 2, -(minY + maxY) / 2);
        m.Scale(scale, scale);
        m.Translate(size.Width / 2, size.Height / 2);
        return m;
    }

    private StreamGeometry Build(Matrix fit, int count, bool closed)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(fit.Transform(_loop![0]), false, closed);
            for (int i = 1; i < count; i++) g.LineTo(fit.Transform(_loop[i]), true, true);
        }
        geo.Freeze();
        return geo;
    }
}
