using System.Drawing;
using System.Runtime.InteropServices;

namespace PitBox.Overlay;

internal sealed class DibSurface : IDisposable
{
    private IntPtr _oldObject, _dib;

    public IntPtr Dc { get; private set; }
    public IntPtr Bits { get; private set; }
    public Size Size { get; private set; }
    public Bitmap? Bitmap { get; private set; }
    public Graphics? Graphics { get; private set; }

    public Graphics Ensure(Size size)
    {
        if (Graphics is not null && size == Size) return Graphics;
        Dispose();
        IntPtr screenDc = GetDC(IntPtr.Zero);
        try
        {
            var info = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = size.Width,
                biHeight = -size.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0,
            };
            Dc = CreateCompatibleDC(screenDc);
            _dib = CreateDIBSection(screenDc, ref info, 0, out IntPtr bits, IntPtr.Zero, 0);
            if (Dc == IntPtr.Zero || _dib == IntPtr.Zero || bits == IntPtr.Zero)
                throw new OutOfMemoryException("The overlay's drawing surface couldn't be created.");
            _oldObject = SelectObject(Dc, _dib);
            Bits = bits;
            Bitmap = new Bitmap(size.Width, size.Height, size.Width * 4, System.Drawing.Imaging.PixelFormat.Format32bppPArgb, bits);
            Graphics = Graphics.FromImage(Bitmap);
            Size = size;
            return Graphics;
        }
        catch
        {
            Dispose();
            throw;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public void Dispose()
    {
        Graphics?.Dispose();
        Graphics = null;
        Bitmap?.Dispose();
        Bitmap = null;
        if (Dc != IntPtr.Zero && _oldObject != IntPtr.Zero) SelectObject(Dc, _oldObject);
        if (_dib != IntPtr.Zero) DeleteObject(_dib);
        if (Dc != IntPtr.Zero) DeleteDC(Dc);
        (Dc, _dib, _oldObject, Bits) = (IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        Size = Size.Empty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
