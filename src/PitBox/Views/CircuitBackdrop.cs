using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PitBox.Services;
using PitBox.Theme;

namespace PitBox.Views;

internal sealed class CircuitBackdrop : Grid
{
    private static event Action? CarsChanged;

    public static void RefreshCars() => CarsChanged?.Invoke();

    private readonly CircuitPlanLayer _plan = new();
    private readonly CircuitCarsLayer _cars = new();

    public CircuitBackdrop()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Children.Add(_plan);
        Children.Add(_cars);
        Loaded += (_, _) =>
        {
            CarsChanged -= _cars.Update;
            CarsChanged += _cars.Update;
        };
        Unloaded += (_, _) => CarsChanged -= _cars.Update;
    }
}

internal sealed class CircuitPlanLayer : FrameworkElement
{
    public static readonly DependencyProperty DrawingProperty = DependencyProperty.Register(nameof(Drawing), typeof(Drawing), typeof(CircuitPlanLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush HeaderFade = CircuitArt.HeaderFade();

    public Drawing? Drawing { get => (Drawing?)GetValue(DrawingProperty); set => SetValue(DrawingProperty, value); }

    public CircuitPlanLayer()
    {
        IsHitTestVisible = false;
        CacheMode = new BitmapCache();
        SetResourceReference(DrawingProperty, "CircuitDrawing");
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Drawing is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        dc.PushOpacityMask(HeaderFade);
        dc.PushOpacityMask(CircuitArt.Fade(RenderSize));
        dc.PushTransform(new MatrixTransform(CircuitArt.Cover(RenderSize)));
        dc.DrawDrawing(Drawing);
        dc.Pop();
        dc.Pop();
        dc.Pop();
    }
}

internal sealed class CircuitCarsLayer : FrameworkElement, IFrameStep
{
    public static readonly DependencyProperty LookProperty = DependencyProperty.Register(nameof(Look), typeof(CircuitCarLook), typeof(CircuitCarsLayer),
        new FrameworkPropertyMetadata(null, (d, _) => ((CircuitCarsLayer)d).Paint()));

    public CircuitCarLook? Look { get => (CircuitCarLook?)GetValue(LookProperty); set => SetValue(LookProperty, value); }

    private const double FrameMs = 1000.0 / 30;

    private static readonly double[] Starts = { 0, 260, 600, 950, 1300 };
    private static readonly double[] Paces = { 1, 0.97, 0.95, 0.985, 0.96 };

    private readonly VisualCollection _visuals;
    private readonly Car[] _cars;
    private readonly double[] _at = (double[])Starts.Clone();
    private Window? _window;
    private bool _running;
    private double _last;

    private sealed class Car
    {
        public readonly ContainerVisual Root = new();
        public readonly DrawingVisual Trail = new();
        public readonly DrawingVisual Body = new();
        public readonly ScaleTransform Stretch = new();
        public readonly ScaleTransform Size = new();
        public readonly RotateTransform Turn = new();
        public readonly TranslateTransform Move = new();

        public Car()
        {
            Trail.Transform = Stretch;
            Root.Children.Add(Trail);
            Root.Children.Add(Body);
            var group = new TransformGroup();
            group.Children.Add(Size);
            group.Children.Add(Turn);
            group.Children.Add(Move);
            Root.Transform = group;
            Root.Opacity = 0;
        }
    }

    public CircuitCarsLayer()
    {
        _visuals = new VisualCollection(this);
        _cars = new Car[Starts.Length];
        for (int i = 0; i < _cars.Length; i++)
        {
            _cars[i] = new Car();
            _visuals.Add(_cars[i].Root);
        }
        IsHitTestVisible = false;
        SetResourceReference(LookProperty, "CircuitCars");
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            if (_window is not null)
            {
                _window.StateChanged -= OnWindowState;
                _window.StateChanged += OnWindowState;
            }
            Motion.WindowActiveChanged -= Update;
            Motion.WindowActiveChanged += Update;
            Update();
        };
        Unloaded += (_, _) =>
        {
            if (_window is not null) _window.StateChanged -= OnWindowState;
            Motion.WindowActiveChanged -= Update;
            Stop();
        };
        IsVisibleChanged += (_, _) => Update();
        SizeChanged += (_, _) => Place();
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    private static bool Shown => (AppServices.Current?.Config.Ui.TrackCars ?? true) && Motion.Flourish;

    private bool CanRun => Shown && IsLoaded && IsVisible && Motion.WindowActive && _window?.WindowState != WindowState.Minimized;

    private void OnWindowState(object? sender, EventArgs e) => Update();

    public void Update()
    {
        if (CanRun && !_running)
        {
            _running = true;
            _last = FrameDriver.Now;
            FrameDriver.Run(this);
        }
        else if (!CanRun && _running) Stop();
        Place();
    }

    private void Stop()
    {
        _running = false;
        FrameDriver.Stop(this);
    }

    public bool Step(double now)
    {
        if (!_running) return false;
        double dt = now - _last;
        if (dt < FrameMs) return true;
        _last = now;
        dt = Math.Min(dt, 100);
        var plan = CircuitPlan.HairpinAndEsses;
        for (int i = 0; i < _at.Length; i++)
        {
            _at[i] += dt * plan.SpeedAt(_at[i]) * Paces[i];
            if (_at[i] > plan.RaceLine.Total) _at[i] -= plan.RaceLine.Total;
        }
        Place();
        return true;
    }

    private void Paint()
    {
        var look = Look;
        for (int i = 0; i < _cars.Length; i++)
        {
            using (var dc = _cars[i].Trail.RenderOpen())
                if (look is not null)
                    for (int j = 0; j < CircuitCarLook.TrailParts; j++)
                    {
                        double a = -CircuitCarLook.TrailLength * j / CircuitCarLook.TrailParts, b = -CircuitCarLook.TrailLength * (j + 1) / CircuitCarLook.TrailParts;
                        dc.DrawLine(look.Trails[i][j], new Point(a, 0), new Point(b, 0));
                    }
            using (var dc = _cars[i].Body.RenderOpen())
                if (look is not null)
                    dc.DrawLine(look.Bodies[i], new Point(-CircuitCarLook.BodyLength / 2, 0), new Point(CircuitCarLook.BodyLength / 2, 0));
        }
        Place();
    }

    private void Place()
    {
        bool shown = Shown && Look is not null && ActualWidth > 0 && ActualHeight > 0;
        var plan = CircuitPlan.HairpinAndEsses;
        var cover = shown ? CircuitArt.Cover(RenderSize) : Matrix.Identity;
        for (int i = 0; i < _cars.Length; i++)
        {
            var car = _cars[i];
            if (!shown)
            {
                car.Root.Opacity = 0;
                continue;
            }
            int at = plan.RaceLine.IndexAt(Math.Clamp(_at[i], 0, plan.RaceLine.Total));
            var p = cover.Transform(plan.RaceLine.Points[at]);
            var t = plan.RaceLine.Tangents[at];
            double speed = plan.Speeds[at] * Paces[i];
            car.Size.ScaleX = car.Size.ScaleY = cover.M11;
            car.Stretch.ScaleX = (8 + 20 * speed / CircuitPlan.TopSpeed) / CircuitCarLook.TrailLength;
            car.Turn.Angle = Math.Atan2(t.Y, t.X) * 180 / Math.PI;
            car.Move.X = p.X;
            car.Move.Y = p.Y;
            car.Root.Opacity = CircuitArt.CarFade(RenderSize, p);
        }
    }
}
