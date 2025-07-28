using System.Runtime.CompilerServices;
using System.Text;

[InterpolatedStringHandler]
public readonly ref struct LatexInterpolatedStringHandler
{
    private readonly StringBuilder _builder;

    public LatexInterpolatedStringHandler(int literalLength, int formattedCount)
    {
        _builder = new StringBuilder(literalLength);
    }

    public void AppendLiteral(string value)
    {
        // Literal parts of the string are assumed to be trusted LaTeX
        _builder.Append(value);
    }

    public void AppendFormatted<T>(T value)
    {
        if (value is IFormattable formattable)
        {
            AppendFormatted(formattable, null);
        }
        else
        {
            _builder.Append(EscapeLatex(value?.ToString() ?? string.Empty));
        }
    }

    public void AppendFormatted<T>(T value, string? format) where T : IFormattable
    {
        string formatted = value.ToString(format, System.Globalization.CultureInfo.InvariantCulture);
        _builder.Append(EscapeLatex(formatted));
    }

    public void AppendFormatted(string? value)
    {
        _builder.Append(EscapeLatex(value ?? string.Empty));
    }

    public void AppendFormatted(string? value, int alignment)
    {
        string escaped = EscapeLatex(value ?? string.Empty);
        _builder.Append(escaped.PadLeft(alignment));
    }

    public void AppendFormatted(string? value, int alignment, string? format)
    {
        // Format is not used here, since string doesn't use it
        string escaped = EscapeLatex(value ?? string.Empty);
        _builder.Append(escaped.PadLeft(alignment));
    }

    public string GetFormattedText() => _builder.ToString();

    public override string ToString() => _builder.ToString();

    private static string EscapeLatex(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char c in input)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\textbackslash{}"); break;
                case '{':  sb.Append(@"\{"); break;
                case '}':  sb.Append(@"\}"); break;
                case '#':  sb.Append(@"\#"); break;
                case '$':  sb.Append(@"\$"); break;
                case '%':  sb.Append(@"\%"); break;
                case '&':  sb.Append(@"\&"); break;
                case '_':  sb.Append(@"\_"); break;
                case '^':  sb.Append(@"\^{}"); break;
                case '~':  sb.Append(@"\~{}"); break;
                default:   sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
