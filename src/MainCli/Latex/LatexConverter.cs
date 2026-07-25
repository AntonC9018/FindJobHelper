using System.Text;
using FindJobHelper.Core.Helper;

namespace FindJobHelper.CVGeneration;

internal static class LatexConverter
{
    internal static LatexEscapedString ToLatexString(string value)
        => new(value);

    internal static LatexEscapedString ToLatexString(RegularString value)
        => ToLatexString(value.Value);

    internal static LatexEscapedString ToLatexString(NullableRegularString value)
        => ToLatexString(value.Value ?? string.Empty);

    internal static string ToLatexString(this IRichTextNode richText)
    {
        ArgumentNullException.ThrowIfNull(richText);
        var visitor = VisitationMap.CreateVisitor();
        visitor.AddOutput();
        visitor.Visit(richText);
        return visitor.GetOutput().ToString();
    }

    private static readonly RichTextVisitationMap VisitationMap = RichTextVisitorDefaults.CreateBuilder()
        .Override<Href>(next => (node, context) =>
        {
            var output = context.GetOutput();
            output.Append(@"\href{");
            output.Append($"{ToLatexString(node.Url.ToString())}");
            output.Append("}{");
            next(node, context);
            output.Append('}');
        })
        .Override<StyledText>(next => (node, context) =>
        {
            var output = context.GetOutput();
            var closingBraces = 0;
            foreach (var style in new (StyleFlags Flag, string Command)[]
                     {
                         (StyleFlags.Bold, "textbf"),
                         (StyleFlags.Code, "texttt"),
                         (StyleFlags.Italic, "textit"),
                     })
            {
                if (node.Style.HasFlag(style.Flag))
                {
                    output.Append('\\').Append(style.Command).Append('{');
                    closingBraces++;
                }
            }

            output.Append($"{ToLatexString(node.Text)}");
            next(node, context);
            output.Append('}', closingBraces);
        })
        .Override<PlainText>(next => (node, context) =>
        {
            context.GetOutput().Append($"{ToLatexString(node.Text)}");
            next(node, context);
        })
        .Default<RichText>()
        .Build();
}

internal readonly record struct LatexEscapedString(string Value) : ISpanFormattable
{
    public override string ToString() => $"{this}";

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = format;
        _ = formatProvider;
        return ToString();
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        _ = format;
        _ = provider;
        var position = 0;
        foreach (var ch in Value)
        {
            var escaped = ch switch
            {
                '\\' => @"\textbackslash{}",
                '{' => @"\{",
                '}' => @"\}",
                '#' => @"\#",
                '$' => @"\$",
                '%' => @"\%",
                '&' => @"\&",
                '_' => @"\_",
                '^' => @"\^{}",
                '~' => @"\~{}",
                _ => null,
            };

            if (escaped is null)
            {
                if (position == destination.Length)
                {
                    charsWritten = 0;
                    return false;
                }

                destination[position++] = ch;
            }
            else
            {
                if (!escaped.AsSpan().TryCopyTo(destination[position..]))
                {
                    charsWritten = 0;
                    return false;
                }

                position += escaped.Length;
            }
        }

        charsWritten = position;
        return true;
    }
}
