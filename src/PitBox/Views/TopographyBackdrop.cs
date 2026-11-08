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
    public const string LineKey = "TextMuted";

    private static BitmapSource? _picture;
    private static int _pictureWidth;
    private static Color _pictureLine;
    private static double _pictureAlpha = -1;
    private static readonly object PictureGate = new();

    public static void Preload(Config.WindowBounds? saved) => LoadInBackground(StartWidth(saved), null);

    private static void LoadInBackground(int width, Action? done)
    {
        var thread = new Thread(() =>
        {
            lock (PictureGate)
                if (_picture is null || _pictureWidth != width) Decode(width);
            done?.Invoke();
            System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread)?.InvokeShutdown();
        }) { IsBackground = true, Name = "Pit-Box backdrop" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static int StartWidth(Config.WindowBounds? saved)
    {
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        if (primary is not { Bounds.Width: > 0 } screen || SystemParameters.PrimaryScreenWidth <= 0) return BackdropLook.MaxDecodeWidth;
        if (saved is { Maximized: true }) return BackdropLook.DecodeWidthFor(screen.Bounds.Width, screen.Bounds.Height);
        double toPixels = screen.Bounds.Width / SystemParameters.PrimaryScreenWidth;
        double width = saved is { Width: > 0 } ? Math.Max(saved.Width, MainWindow.MinWindowWidth) : 1472;
        double height = saved is { Height: > 0 } ? Math.Max(saved.Height, MainWindow.MinWindowHeight) : 820;
        return BackdropLook.DecodeWidthFor(width * toPixels, height * toPixels);
    }

    private readonly Image _image = new() { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Threading.DispatcherTimer _resized = new(System.Windows.Threading.DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
    private int _width;

    public TopographyBackdrop()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_image);
        _resized.Tick += (_, _) => { _resized.Stop(); Refit(); };
        SizeChanged += (_, _) => { _resized.Stop(); _resized.Start(); };
        Loaded += (_, _) => { ThemeManager.Changed -= Apply; ThemeManager.Changed += Apply; _width = WantedWidth(); Apply(); };
        Unloaded += (_, _) => { ThemeManager.Changed -= Apply; _resized.Stop(); };
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _resized.Stop();
        _resized.Start();
    }

    public static Color Bg => ThemeManager.ColorOf("Bg", Colors.Black);

    public static Color Line => ThemeManager.ColorOf(LineKey, Colors.Gray);

    private int WantedWidth()
    {
        if (Window.GetWindow(this) is not { ActualWidth: > 0 } window || PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
            return BackdropLook.MaxDecodeWidth;
        var pixels = target.TransformToDevice.Transform(new Point(window.ActualWidth, window.ActualHeight));
        return BackdropLook.DecodeWidthFor(pixels.X, pixels.Y);
    }

    private void Refit()
    {
        if (!IsLoaded) return;
        int wanted = WantedWidth();
        if (wanted == _width) return;
        _width = wanted;
        var ui = Dispatcher;
        LoadInBackground(wanted, () => ui.BeginInvoke(new Action(() => { if (IsLoaded && _width == wanted) Apply(); })));
    }

    private void Apply()
    {
        var bg = Bg;
        var line = Line;
        double alpha = BackdropLook.LineAlpha(line, bg);
        var fill = new SolidColorBrush(bg);
        fill.Freeze();
        Background = fill;
        _image.Source = Picture(_width > 0 ? _width : BackdropLook.MaxDecodeWidth, line, alpha);
    }

    private static BitmapSource? Picture(int width, Color line, double alpha)
    {
        lock (PictureGate)
        {
            if ((_picture is null || _pictureWidth != width) && !Decode(width)) return null;
            if (_picture is { } picture && (_pictureLine != line || _pictureAlpha != alpha))
            {
                _picture = Indexed(picture, line, alpha);
                (_pictureLine, _pictureAlpha) = (line, alpha);
            }
            return _picture;
        }
    }

    private static BitmapSource Indexed(BitmapSource source, Color line, double alpha)
    {
        var colors = new Color[256];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = Color.FromArgb((byte)Math.Round(i * alpha), line.R, line.G, line.B);
        int width = source.PixelWidth, height = source.PixelHeight;
        var bytes = new byte[width * height];
        source.CopyPixels(bytes, width, 0);
        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Indexed8, new BitmapPalette(colors), bytes, width);
        picture.Freeze();
        return picture;
    }

    private static bool Decode(int width)
    {
        try
        {
            var source = new BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri("pack://application:,,,/Assets/BackdropLines.png");
            source.DecodePixelWidth = width;
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.EndInit();
            var gray = new FormatConvertedBitmap(source, PixelFormats.Gray8, null, 0);
            gray.Freeze();
            _picture = Indexed(gray, Colors.Black, 0);
            (_pictureWidth, _pictureLine, _pictureAlpha) = (width, Colors.Black, 0);
            return true;
        }
        catch (Exception ex)
        {
            App.LogError(ex);
            return false;
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
