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

    public TopographyBackdrop()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Children.Add(_image);
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
