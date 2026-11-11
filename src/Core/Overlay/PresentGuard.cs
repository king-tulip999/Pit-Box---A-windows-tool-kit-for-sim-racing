using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Core.Overlay;

internal sealed class PresentGuard
{
    internal const int Chunk = 64 * 1024;

    private byte[]? _last;
    private Point _at;
    private byte _opacity;
    private bool _valid;

    public void Reset() => _valid = false;

    public void Release()
    {
        _valid = false;
        _last = null;
    }

    public bool Same(IntPtr bits, int length, Point at, byte opacity)
    {
        if (bits == IntPtr.Zero || length <= 0)
        {
            _valid = false;
            return false;
        }
        if (_last?.Length != length)
        {
            _last = new byte[length];
            _valid = false;
        }
        var now = MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AddByteOffset(ref Unsafe.NullRef<byte>(), bits), length);
        int from = 0;
        if (_valid && at == _at && opacity == _opacity)
        {
            while (from < length)
            {
                int n = Math.Min(Chunk, length - from);
                if (!now.Slice(from, n).SequenceEqual(_last.AsSpan(from, n))) break;
                from += n;
            }
            if (from >= length) return true;
        }
        now[from..].CopyTo(_last.AsSpan(from));
        _at = at;
        _opacity = opacity;
        _valid = true;
        return false;
    }
}
