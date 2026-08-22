using System.Globalization;

namespace FindJobHelper.Core.Helper;

internal static class DiagnosticFormatting
{
    public static FormattedScore FormatScore(float value) =>
        new(value);

    public static SignedFormattedScore FormatSignedScore(float value) =>
        new(value);
}

internal readonly record struct FormattedScore(float Value) : ISpanFormattable
{
    private const string DefaultNumericFormat = "0.###";

    public override string ToString() =>
        ToString(format: null, formatProvider: null);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = formatProvider;
        var numericFormat = string.IsNullOrEmpty(format)
            ? DefaultNumericFormat
            : format;
        return Value.ToString(
            numericFormat,
            CultureInfo.InvariantCulture);
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        _ = provider;
        var numericFormat = format.IsEmpty
            ? DefaultNumericFormat.AsSpan()
            : format;
        return Value.TryFormat(
            destination,
            out charsWritten,
            numericFormat,
            CultureInfo.InvariantCulture);
    }
}

internal readonly record struct SignedFormattedScore(float Value) : ISpanFormattable
{
    private const string NumericFormat = "+0.###;-0.###;+0";

    public override string ToString() =>
        ToString(format: null, formatProvider: null);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = format;
        _ = formatProvider;
        return Value.ToString(NumericFormat, CultureInfo.InvariantCulture);
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        _ = format;
        _ = provider;
        return Value.TryFormat(
            destination,
            out charsWritten,
            NumericFormat,
            CultureInfo.InvariantCulture);
    }
}
