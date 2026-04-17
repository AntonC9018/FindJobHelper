using System.Collections.Immutable;

using static RichTextFactory;

public sealed class RichTextFactory
{
    public static StyledText Styled(string text, StyleFlags flags)
    {
        return new()
        {
            Text = text,
            Style = flags,
        };
    }

    public static StyledText Italic(string text)
    {
        return Styled(text, StyleFlags.Italic);
    }

    public static StyledText Bold(string text)
    {
        return Styled(text, StyleFlags.Bold);
    }

    public static StyledText Code(string text)
    {
        return Styled(text, StyleFlags.Code);
    }

    public static Href Href(string url, RichText text)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"The given url '{url}' must be a valid url");
        }
        return new()
        {
            Url = uri,
            Text = text,
        };
    }
}

public interface IRichTextElement
{
}

[Flags]
public enum StyleFlags
{
    Italic = 1 << 0,
    Bold = 1 << 1,
    Code = 1 << 2,
}

public sealed class StyledText : IRichTextElement
{
    public required string Text { get; init; }
    public required StyleFlags Style { get; init; }

    public override string ToString() => Text;
}

public sealed class Href : IRichTextElement
{
    public required Uri Url { get; init; }
    public required IRichTextElement Text { get; init; }

    public override string ToString() => Text.ToString()!;
}

public sealed class RichText : IRichTextElement
{
    public required ImmutableArray<IRichTextElement> Items { get; init; }

    public override string ToString() => string.Join(" ", Items);
}

public sealed class PlainText : IRichTextElement
{
    public required string Text { get; init; }

    public override string ToString() => Text;
}

public static class ExampleUsage
{
    // Use an interpolated string handler here.
    private static RichText T(RichText rt)
    {
        return rt;
    }

    public static void Test()
    {
        _ = T($"""
        Hello this is plain text,
        Here's a link: {Href("https://example.com", $"{Italic("Hello")} world"))}.
        Here's some text: {Styled("", StyleFlags.Bold | StyleFlags.Italic)} {Bold("bold")} {Code("code")}
        """);
    }
}
