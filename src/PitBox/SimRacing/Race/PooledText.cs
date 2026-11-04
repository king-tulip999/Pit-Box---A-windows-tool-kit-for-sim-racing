using System.Globalization;
using System.Text;

namespace PitBox.SimRacing.Race;

internal static class PooledText
{
    private const int Capacity = 4096;
    public const int Room = 256;

    [ThreadStatic] private static Dictionary<string, string>? _pool;

    public static string Of(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty) return "";
        var pool = _pool ??= new Dictionary<string, string>(StringComparer.Ordinal);
        var lookup = pool.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(text, out var known)) return known;
        if (pool.Count >= Capacity) pool.Clear();
        string made = text.ToString();
        pool[made] = made;
        return made;
    }

    public static string Concat(string a, string b)
    {
        var t = new PooledBuilder(stackalloc char[Room]);
        t.Add(a);
        t.Add(b);
        return t.Done();
    }

    public static string Concat(string a, string b, string c)
    {
        var t = new PooledBuilder(stackalloc char[Room]);
        t.Add(a);
        t.Add(b);
        t.Add(c);
        return t.Done();
    }

    public static string Join(string separator, List<string> parts)
    {
        var t = new PooledBuilder(stackalloc char[Room]);
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0) t.Add(separator);
            t.Add(parts[i]);
        }
        return t.Done();
    }

    public static string Number(string prefix, long n, string suffix = "")
    {
        var t = new PooledBuilder(stackalloc char[Room]);
        t.Add(prefix);
        t.Add(n);
        t.Add(suffix);
        return t.Done();
    }

    public static string Number(string prefix, long a, string middle, long b, string suffix = "")
    {
        var t = new PooledBuilder(stackalloc char[Room]);
        t.Add(prefix);
        t.Add(a);
        t.Add(middle);
        t.Add(b);
        t.Add(suffix);
        return t.Done();
    }

    public static string Number(string prefix, double value, string format, IFormatProvider provider, string suffix = "")
    {
        var t = new PooledBuilder(stackalloc char[Room]);
        t.Add(prefix);
        t.Add(value, format, provider);
        t.Add(suffix);
        return t.Done();
    }
}

internal ref struct PooledBuilder
{
    private readonly Span<char> _buffer;
    private int _at;
    private StringBuilder? _spill;

    public PooledBuilder(Span<char> buffer)
    {
        _buffer = buffer;
        _at = 0;
        _spill = null;
    }

    public void Add(string text) => AddSpan(text.AsSpan());

    public void AddSpan(ReadOnlySpan<char> text)
    {
        if (_spill is null && text.TryCopyTo(_buffer[_at..])) { _at += text.Length; return; }
        Spill().Append(text);
    }

    public void Add(char c)
    {
        if (_spill is null && _at < _buffer.Length) { _buffer[_at++] = c; return; }
        Spill().Append(c);
    }

    public void Add(long n)
    {
        if (_spill is null && n.TryFormat(_buffer[_at..], out int written, default, CultureInfo.CurrentCulture)) { _at += written; return; }
        Spill().Append(n.ToString(CultureInfo.CurrentCulture));
    }

    public void Add(long n, string format, IFormatProvider provider)
    {
        if (_spill is null && n.TryFormat(_buffer[_at..], out int written, format, provider)) { _at += written; return; }
        Spill().Append(n.ToString(format, provider));
    }

    public void Add(double value, string format, IFormatProvider provider)
    {
        if (_spill is null && value.TryFormat(_buffer[_at..], out int written, format, provider)) { _at += written; return; }
        Spill().Append(value.ToString(format, provider));
    }

    private StringBuilder Spill() => _spill ??= new StringBuilder().Append(_buffer[.._at]);

    public readonly string Done() => _spill is { } spill ? spill.ToString() : PooledText.Of(_buffer[.._at]);
}
