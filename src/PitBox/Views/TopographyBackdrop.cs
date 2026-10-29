using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PitBox.Services;
using PitBox.Theme;

namespace PitBox.Views;

internal sealed class TopographyBackdrop : Grid
{
    public const int DecodeWidth = 2560;
    public const string LineKey = "TextMuted";

    private static byte[]? _mask;
    private static int _maskWidth, _maskHeight;

    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly BackdropTracers _tracers = new();

    public TopographyBackdrop()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_image);
        Children.Add(_tracers);
        Loaded += (_, _) => { ThemeManager.Changed -= Apply; ThemeManager.Changed += Apply; Apply(); };
        Unloaded += (_, _) => ThemeManager.Changed -= Apply;
    }

    public static Color Bg => ThemeManager.Current.BackdropBg ?? ThemeManager.ColorOf("Bg", Colors.Black);

    public static Color Line => ThemeManager.Current.BackdropLine ?? ThemeManager.ColorOf(LineKey, Colors.Gray);

    private void Apply()
    {
        var bg = Bg;
        var line = Line;
        double alpha = ThemeManager.Current.BackdropLine is null ? BackdropLook.LineAlpha(line, bg) : 1;
        var fill = new SolidColorBrush(bg);
        fill.Freeze();
        Background = fill;
        _image.Source = Picture(line, alpha);
        _tracers.Repaint();
    }

    private static ImageSource? Picture(Color line, double alpha)
    {
        if (!LoadMask()) return null;
        var colors = new Color[256];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.FromArgb((byte)Math.Round(i * alpha), line.R, line.G, line.B);
        var picture = BitmapSource.Create(_maskWidth, _maskHeight, 96, 96, PixelFormats.Indexed8, new BitmapPalette(colors), _mask, _maskWidth);
        picture.Freeze();
        return picture;
    }

    private static bool LoadMask()
    {
        if (_mask is not null) return true;
        try
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri("pack://application:,,,/Assets/BackdropLines.png");
            source.DecodePixelWidth = DecodeWidth;
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.EndInit();
            var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            _maskWidth = gray.PixelWidth;
            _maskHeight = gray.PixelHeight;
            var bytes = new byte[_maskWidth * _maskHeight];
            gray.CopyPixels(bytes, _maskWidth, 0);
            _mask = bytes;
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
        }
    }
}

internal sealed class BackdropTracers : FrameworkElement, IFrameStep
{
    public const int MaxTracers = 7;
    public const int Shades = 10;
    public const double FrameMs = 1000.0 / 30;
    public const double MinSpeed = 0.14, MaxSpeed = 0.32;
    public const double MinTail = 110, MaxTail = 460;
    public const double SegmentLength = 12;
    public const double MinRun = 300, MaxRun = 2200;
    public const double MinGapMs = 350, MaxGapMs = 1100;
    public const double FadeInMs = 400;
    public const double Thickness = 1.6;
    public const double Strength = 0.6;
    public const double AccentShare = 0.5;

    private static readonly string[] ColorKeys = { "Accent", "Slate", "Gold", "Sage" };

    private static (Size Picture, BackdropPath[] Paths)? _plan;

    private readonly DrawingVisual _visual = new();
    private readonly Tracer[] _tracers = new Tracer[MaxTracers];
    private readonly Pen[][] _pens = new Pen[ColorKeys.Length][];
    private Matrix _toScreen = Matrix.Identity;
    private Window? _window;
    private bool _running;
    private double _last, _nextSpawn;

    private sealed class Tracer
    {
        public bool Live;
        public BackdropPath Path = null!;
        public double Start, Direction, Run, Travelled, Age, Speed, Tail;
        public int Color;
    }

    public BackdropTracers()
    {
        for (int i = 0; i < _tracers.Length; i++) _tracers[i] = new Tracer();
        AddVisualChild(_visual);
        IsHitTestVisible = false;
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
        SizeChanged += (_, _) => Fit();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _visual;

    private bool CanRun => Motion.Flourish && IsLoaded && IsVisible && Motion.WindowActive && _window?.WindowState != WindowState.Minimized && Plan() is not null;

    private void OnWindowState(object? sender, EventArgs e) => Update();

    public void Repaint()
    {
        var accent = ThemeManager.ColorOf("Accent", Colors.DeepPink);
        for (int c = 0; c < ColorKeys.Length; c++)
        {
            var color = ThemeManager.ColorOf(ColorKeys[c], accent);
            var shades = new Pen[Shades];
            for (int i = 0; i < Shades; i++)
            {
                double a = Strength * (i + 1) / Shades;
                var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Round(a * 255), color.R, color.G, color.B)), Thickness)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                pen.Freeze();
                shades[i] = pen;
            }
            _pens[c] = shades;
        }
    }

    private static (Size Picture, BackdropPath[] Paths)? Plan()
    {
        if (_plan is not null) return _plan;
        try
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/BackdropPaths.bin")).Stream;
            var plan = BackdropPath.Read(stream);
            if (plan.Paths.Length > 0) _plan = plan;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
        }
        return _plan;
    }

    private void Fit()
    {
        if (Plan() is { } plan) _toScreen = BackdropLook.Cover(RenderSize, plan.Picture);
    }

    public void Update()
    {
        if (CanRun && !_running)
        {
            if (_pens[0] is null) Repaint();
            Fit();
            _running = true;
            _last = FrameDriver.Now;
            _nextSpawn = _last + 200;
            FrameDriver.Run(this);
        }
        else if (!CanRun && _running) Stop();
    }

    private void Stop()
    {
        _running = false;
        FrameDriver.Stop(this);
        foreach (var t in _tracers) t.Live = false;
        using (_visual.RenderOpen()) { }
    }

    public bool Step(double now)
    {
        if (!_running) return false;
        double dt = now - _last;
        if (dt < FrameMs) return true;
        _last = now;
        dt = Math.Min(dt, 100);
        if (now >= _nextSpawn)
        {
            Spawn();
            _nextSpawn = now + MinGapMs + Random.Shared.NextDouble() * (MaxGapMs - MinGapMs);
        }
        using var dc = _visual.RenderOpen();
        foreach (var t in _tracers)
        {
            if (!t.Live) continue;
            t.Age += dt;
            t.Travelled += dt * t.Speed;
            if (t.Travelled - t.Tail >= t.Run)
            {
                t.Live = false;
                continue;
            }
            Draw(dc, t);
        }
        return true;
    }

    private void Spawn()
    {
        Tracer? free = null;
        foreach (var t in _tracers)
            if (!t.Live) { free = t; break; }
        if (free is null || Plan() is not { } plan) return;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var path = plan.Paths[Random.Shared.Next(plan.Paths.Length)];
            double start = Random.Shared.NextDouble() * path.Total;
            double ahead = path.Total - start;
            double direction = ahead >= start ? 1 : -1;
            double room = direction > 0 ? ahead : start;
            double run = Math.Min(room, Between(MinRun, MaxRun));
            if (run < MinRun) continue;
            free.Path = path;
            free.Start = start;
            free.Direction = direction;
            free.Run = run;
            free.Travelled = 0;
            free.Age = 0;
            free.Speed = Between(MinSpeed, MaxSpeed);
            free.Tail = Math.Min(run, Between(MinTail, MaxTail));
            free.Color = Random.Shared.NextDouble() < AccentShare ? 0 : 1 + Random.Shared.Next(ColorKeys.Length - 1);
            free.Live = true;
            return;
        }
    }

    private static double Between(double low, double high) => low + Random.Shared.NextDouble() * (high - low);

    private void Draw(DrawingContext dc, Tracer t)
    {
        double head = Math.Min(t.Travelled, t.Run);
        double end = Math.Max(0, t.Travelled - t.Tail);
        if (head <= end) return;
        double fadeIn = Math.Min(1, t.Age / FadeInMs);
        var previous = _toScreen.Transform(t.Path.At(t.Start + t.Direction * end));
        for (double s = end; s < head;)
        {
            double next = Math.Min(head, s + SegmentLength);
            var point = _toScreen.Transform(t.Path.At(t.Start + t.Direction * next));
            double along = (next - (t.Travelled - t.Tail)) / t.Tail;
            int shade = (int)Math.Clamp(Math.Round(along * fadeIn * (Shades - 1)), 0, Shades - 1);
            dc.DrawLine(_pens[t.Color][shade], previous, point);
            previous = point;
            s = next;
        }
    }
}

internal sealed class GlassPane : Border
{
    public const double BlurRadius = 14;

    private readonly Rectangle _frost = new() { IsHitTestVisible = false };
    private readonly VisualBrush _brush;
    private readonly FrameworkElement _source;
    private Rect _seen;

    public GlassPane(FrameworkElement source)
    {
        _source = source;
        IsHitTestVisible = false;
        ClipToBounds = true;
        _brush = new VisualBrush(source) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        _frost.Fill = _brush;
        _frost.Margin = new Thickness(-BlurRadius);
        if (PcClass.Effects) _frost.Effect = new BlurEffect { Radius = BlurRadius, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };
        Child = _frost;
        LayoutUpdated += (_, _) => Follow();
    }

    private void Follow()
    {
        if (!IsVisible || _frost.ActualWidth <= 0 || !_source.IsVisible || PresentationSource.FromVisual(_frost) is null) return;
        Rect at;
        try { at = _frost.TransformToVisual(_source).TransformBounds(new Rect(_frost.RenderSize)); }
        catch (InvalidOperationException) { return; }
        if (at == _seen) return;
        _seen = at;
        _brush.Viewbox = at;
    }
}
