using System.Drawing;
using System.Runtime.InteropServices;

namespace PitBox.Overlay;

internal sealed class PresentGuard
{
    internal const int Chunk = 64 * 1024;

    private byte[]? _last, _chunk;
    private Point _at;
    private byte _opacity;
    private bool _valid;

    public void Reset() => _valid = false;

    public void Release()
    {
        _valid = false;
        _last = _chunk = null;
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
        int from = 0;
        if (_valid && at == _at && opacity == _opacity)
        {
            if (_chunk is null || _chunk.Length < Math.Min(Chunk, length)) _chunk = new byte[Math.Min(Chunk, length)];
            while (from < length)
            {
                int n = Math.Min(_chunk.Length, length - from);
                Marshal.Copy(bits + from, _chunk, 0, n);
                if (!_chunk.AsSpan(0, n).SequenceEqual(_last.AsSpan(from, n))) break;
                from += n;
            }
            if (from >= length) return true;
        }
        Marshal.Copy(bits + from, _last, from, length - from);
        _at = at;
        _opacity = opacity;
        _valid = true;
        return false;
    }
}
