using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace PitBox.SimRacing.Hud;

internal static class HudArt
{
    private const string Assets = "pack://application:,,,/PitBox;component/Assets/";
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Image?> Cars = new(StringComparer.OrdinalIgnoreCase);

    private static byte[]? Read(string path)
    {
        var info = System.Windows.Application.GetResourceStream(new Uri(Assets + path));
        if (info?.Stream is not { } stream) return null;
        using (stream)
        using (var copy = new MemoryStream())
        {
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }

    public static Image? Car(string? car)
    {
        if (CarArt.KeyOf(car) is not { } key) return null;
        lock (Gate)
        {
            if (_liveryVersion != LiveryPictures.Version)
            {
                Cars.Clear();
                _liveryVersion = LiveryPictures.Version;
            }
            if (Cars.TryGetValue(key, out var known)) return known;
            Image? image = null;
            try
            {
                if ((Painted(key) ?? Read($"Cars/{key}.png")) is { } bytes)
                {
                    using var stream = new MemoryStream(bytes);
                    using var loaded = Image.FromStream(stream);
                    image = new Bitmap(loaded);
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or UriFormatException)
            {
                App.LogError(ex);
            }
            return Cars[key] = image;
        }
    }

    private static int _liveryVersion;

    private static byte[]? Painted(string key)
    {
        if (LiveryPictures.FileFor(key) is not { } file) return null;
        try { return File.ReadAllBytes(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
