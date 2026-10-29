using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PitBox.SimRacing;

namespace PitBox.Views;

internal static class CarPictures
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static int _liveryVersion;

    public static ImageSource? Of(string key)
    {
        if (_liveryVersion != LiveryPictures.Version)
        {
            Cache.Clear();
            _liveryVersion = LiveryPictures.Version;
        }
        if (Cache.TryGetValue(key, out var known)) return known;
        var source = (LiveryPictures.FileFor(key) is { } painted ? Load(new Uri(painted), fresh: true) : null)
            ?? Load(new Uri($"pack://application:,,,/PitBox;component/Assets/Cars/{key}.png"), fresh: false);
        Cache[key] = source;
        return source;
    }

    private static ImageSource? Load(Uri uri, bool fresh)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = uri;
            image.CacheOption = BitmapCacheOption.OnLoad;
            if (fresh) image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ImageSource? For(string? car) => CarArt.KeyOf(car) is { } key ? Of(key) : null;

    public static Image? Picture(string? car, double height, double maxWidth = double.PositiveInfinity)
    {
        if (For(car) is not { } source) return null;
        var image = new Image
        {
            Source = source, Height = height, MaxWidth = maxWidth, Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    public const double SlotAspect = 3.0;

    public static Image? Slotted(string? car, double height)
    {
        if (Picture(car, height) is not { } picture) return null;
        picture.Width = height * SlotAspect;
        return picture;
    }

    public static FrameworkElement Beside(FrameworkElement content, string? car, double height, double gap = 8, bool slot = true)
    {
        if ((slot ? Slotted(car, height) : Picture(car, height, height * 4)) is not { } picture) return content;
        picture.Margin = new Thickness(0, 0, gap, 0);
        content.VerticalAlignment = VerticalAlignment.Center;
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(picture, Dock.Left);
        row.Children.Add(picture);
        row.Children.Add(content);
        return row;
    }
}
