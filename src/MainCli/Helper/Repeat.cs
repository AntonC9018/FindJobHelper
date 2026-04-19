using System.Diagnostics;

namespace FindJobHelper.Core.Helper;

public readonly struct Repeat : ISpanFormattable
{
    private readonly int _count;
    private readonly string _str;

    public Repeat(string str, int count)
    {
        _count = count;
        _str = str;
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = format;
        _ = formatProvider;
        return $"{this}";
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        _ = provider;
        _ = format;
        charsWritten = 0;

        var w = new WriteHelper(destination, ref charsWritten);
        var totalLen = _count * _str.Length;
        if (!w.CanAppend(totalLen))
        {
            return false;
        }
        for (int i = 0; i < _count; i++)
        {
            var t = w.Append(_str);
            Debug.Assert(t);
        }
        return true;
    }
}
