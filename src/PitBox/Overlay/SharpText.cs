using System.Drawing;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using Color = System.Drawing.Color;
using Size = System.Drawing.Size;

namespace PitBox.Overlay;

internal enum TextAlign { Left, Center, Right }

internal sealed class SharpText : IDisposable
{
    public const string Family = "Bahnschrift";

    private readonly record struct Run(string Text, float Size, int Weight, Color Color, RectangleF Box, TextAlign Align);

    private static ID2D1Factory? _d2d;
    private static IDWriteFactory? _dwrite;
    private static IDWriteRenderingParams? _rendering;
    private static bool _failed;
    private static readonly Dictionary<(float, int, TextAlign), IDWriteTextFormat> Formats = new();
    private static readonly Dictionary<(string, float, int), float> Widths = new();

    private readonly List<Run> _runs = new();
    private ID2D1DCRenderTarget? _target;
    private ID2D1SolidColorBrush? _brush;

    public static bool Available => Factories();

    private static bool Factories()
    {
        if (_dwrite is not null) return true;
        if (_failed) return false;
        try
        {
            _d2d = D2D1.D2D1CreateFactory<ID2D1Factory>(Vortice.Direct2D1.FactoryType.SingleThreaded);
            _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>(Vortice.DirectWrite.FactoryType.Shared);
            _rendering = _dwrite.CreateCustomRenderingParams(1.8f, 1f, 0f, PixelGeometry.Flat, RenderingMode.NaturalSymmetric);
            return true;
        }
        catch (Exception ex) when (ex is SharpGen.Runtime.SharpGenException or DllNotFoundException or EntryPointNotFoundException)
        {
            _failed = true;
            App.LogError(ex);
            return false;
        }
    }

    private static IDWriteTextFormat Format(float size, int weight, TextAlign align)
    {
        var key = (MathF.Round(size * 4) / 4, weight, align);
        if (Formats.TryGetValue(key, out var f)) return f;
        f = _dwrite!.CreateTextFormat(Family, (FontWeight)weight, Vortice.DirectWrite.FontStyle.Normal, FontStretch.Normal, key.Item1);
        f.WordWrapping = WordWrapping.NoWrap;
        f.ParagraphAlignment = ParagraphAlignment.Near;
        f.TextAlignment = align switch { TextAlign.Center => TextAlignment.Center, TextAlign.Right => TextAlignment.Trailing, _ => TextAlignment.Leading };
        return Formats[key] = f;
    }

    public static float Measure(string text, float size, int weight)
    {
        if (text.Length == 0 || !Factories()) return -1;
        var key = (text, size, weight);
        if (Widths.TryGetValue(key, out var w)) return w;
        if (Widths.Count >= 4096) Widths.Clear();
        using var layout = _dwrite!.CreateTextLayout(text, Format(size, weight, TextAlign.Left), float.MaxValue, float.MaxValue);
        return Widths[key] = layout.Metrics.WidthIncludingTrailingWhitespace;
    }

    public void Clear() => _runs.Clear();

    public const int Regular = 400, Semibold = 600, Bold = 700;

    public void Add(string text, float size, int weight, Color color, RectangleF box, TextAlign align)
    {
        if (text.Length > 0 && color.A > 0 && box.Width > 0) _runs.Add(new Run(text, size, weight, color, box, align));
    }

    public bool Render(IntPtr dc, Size size)
    {
        if (_runs.Count == 0) return true;
        if (dc == IntPtr.Zero || !Factories()) return false;
        try
        {
            if (_target is null)
            {
                var props = new RenderTargetProperties(RenderTargetType.Software, new Vortice.DCommon.PixelFormat(Vortice.DXGI.Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96, RenderTargetUsage.None, Vortice.Direct2D1.FeatureLevel.Default);
                _target = _d2d!.CreateDCRenderTarget(props);
                _target.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
                _target.TextRenderingParams = _rendering;
                _brush = _target.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
            }
            GdiFlush();
            _target.BindDC(dc, new Vortice.RawRect(0, 0, size.Width, size.Height));
            _target.BeginDraw();
            foreach (var r in _runs)
            {
                float a = r.Color.A / 255f;
                _brush!.Color = new Color4(r.Color.R / 255f, r.Color.G / 255f, r.Color.B / 255f, a);
                _target.DrawText(r.Text, Format(r.Size, r.Weight, r.Align), new Rect(r.Box.X, r.Box.Y, r.Box.Width, r.Box.Height), _brush);
            }
            _target.EndDraw();
            GdiFlush();
            return true;
        }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            App.LogError(ex);
            DisposeTarget();
            return false;
        }
        finally
        {
            _runs.Clear();
        }
    }

    private void DisposeTarget()
    {
        _brush?.Dispose();
        _brush = null;
        _target?.Dispose();
        _target = null;
    }

    public void Dispose() => DisposeTarget();

    [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool GdiFlush();
}
