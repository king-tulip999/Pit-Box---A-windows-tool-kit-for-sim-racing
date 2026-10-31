using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.DirectWrite;
using FontFamily = System.Drawing.FontFamily;
using FontStyle = System.Drawing.FontStyle;

namespace PitBox.Overlay;

internal static class OverlayFonts
{
    public const string Family = "Saira";
    private const string Folder = "pack://application:,,,/PitBox;component/Assets/Fonts/Saira/";
    private static readonly string[] TextFiles = { "Saira-Regular.ttf", "Saira-SemiBold.ttf", "Saira-Bold.ttf" };
    private const string DisplayFile = "Condensed/SairaCondensed-ExtraBold.ttf";

    private static readonly object Gate = new();
    private static List<byte[]>? _text;
    private static byte[]? _displayBytes;
    private static PrivateFontCollection? _gdi;
    private static FontFamily? _regular, _semibold, _display;
    private static bool _gdiTried;
    private static IDWriteInMemoryFontFileLoader? _loader;

    public static FontFamily Regular { get { LoadGdi(); return _regular ?? new FontFamily("Segoe UI"); } }

    public static FontFamily Semibold { get { LoadGdi(); return _semibold ?? new FontFamily("Segoe UI Semibold"); } }

    public static (FontFamily Family, FontStyle Style) Display
    {
        get
        {
            LoadGdi();
            return _display is not null ? (_display, FontStyle.Regular) : (FontFamily.GenericSansSerif, FontStyle.Bold);
        }
    }

    private static void LoadBytes()
    {
        if (_text is not null) return;
        _text = TextFiles.Select(Read).OfType<byte[]>().ToList();
        _displayBytes = Read(DisplayFile);
    }

    private static void LoadGdi()
    {
        lock (Gate)
        {
            if (_gdiTried) return;
            _gdiTried = true;
            LoadBytes();
            try
            {
                _gdi = new PrivateFontCollection();
                foreach (var bytes in _text!.Append(_displayBytes).OfType<byte[]>())
                {
                    var memory = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, memory, bytes.Length);
                    _gdi.AddMemoryFont(memory, bytes.Length);
                }
                _regular = _gdi.Families.FirstOrDefault(f => f.Name == Family);
                _semibold = _gdi.Families.FirstOrDefault(f => f.Name == Family + " SemiBold");
                _display = _gdi.Families.FirstOrDefault(f => f.Name.StartsWith(Family + " Condensed", StringComparison.Ordinal));
            }
            catch (Exception ex) when (ex is ArgumentException or ExternalException or IOException)
            {
                App.LogError(ex);
            }
        }
    }

    public static IDWriteFontCollection? Collection(IDWriteFactory factory)
    {
        lock (Gate)
        {
            LoadBytes();
            if (_text!.Count == 0) return null;
            try
            {
                using var modern = factory.QueryInterfaceOrNull<IDWriteFactory5>();
                if (modern is null) return null;
                _loader ??= Registered(modern);
                using var builder = modern.CreateFontSetBuilder();
                foreach (var bytes in _text)
                {
                    var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                    try
                    {
                        using var file = _loader.CreateInMemoryFontFileReference(modern, pinned.AddrOfPinnedObject(), (uint)bytes.Length, null);
                        builder.AddFontFile(file);
                    }
                    finally
                    {
                        pinned.Free();
                    }
                }
                using var set = builder.CreateFontSet();
                return modern.CreateFontCollectionFromFontSet(set);
            }
            catch (SharpGen.Runtime.SharpGenException ex)
            {
                App.LogError(ex);
                return null;
            }
        }
    }

    private static IDWriteInMemoryFontFileLoader Registered(IDWriteFactory5 factory)
    {
        var loader = factory.CreateInMemoryFontFileLoader();
        factory.RegisterFontFileLoader(loader);
        return loader;
    }

    private static byte[]? Read(string path)
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri(Folder + path));
            if (info?.Stream is not { } stream) return null;
            using (stream)
            using (var copy = new MemoryStream())
            {
                stream.CopyTo(copy);
                return copy.ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or InvalidOperationException)
        {
            App.LogError(ex);
            return null;
        }
    }
}
