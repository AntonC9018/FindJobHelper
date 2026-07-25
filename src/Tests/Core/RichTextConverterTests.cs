using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class RichTextConverterTests
{
    [Fact]
    public void ToLatexString_EscapesRootPlainText()
    {
        IRichTextNode text = new PlainText
        {
            Text = @"\ { } # $ % & _ ^ ~",
        };

        var rendered = text.ToLatexString();

        Assert.Equal(
            @"\textbackslash{} \{ \} \# \$ \% \& \_ \^{} \~{}",
            rendered);
    }

    [Fact]
    public void ToLatexString_EscapesRootStyledTextInsideNestedFormatting()
    {
        IRichTextNode text = new StyledText
        {
            Text = "styled_&",
            Style = StyleFlags.Bold | StyleFlags.Code | StyleFlags.Italic,
        };

        var rendered = text.ToLatexString();

        Assert.Equal(
            @"\textbf{\texttt{\textit{styled\_\&}}}",
            rendered);
    }

    [Fact]
    public void ToLatexString_EscapesRootHrefUrlAndStyledLabelIndependently()
    {
        IRichTextNode text = new Href
        {
            Url = new Uri("https://example.test/a_b?x=1&y=2#frag%25"),
            Text = new RichText
            {
                Items =
                [
                    new PlainText { Text = "label_ " },
                    new StyledText
                    {
                        Text = "styled&",
                        Style = StyleFlags.Italic,
                    },
                ],
            },
        };

        var rendered = text.ToLatexString();

        Assert.Equal(
            @"\href{https://example.test/a\_b?x=1\&y=2\#frag\%25}{label\_ \textit{styled\&}}",
            rendered);
    }

    [Fact]
    public void ToLatexString_ConcatenatesCompositeNodesWithoutDoubleEscaping()
    {
        IRichTextNode text = new RichText
        {
            Items =
            [
                new PlainText { Text = "left&" },
                new StyledText
                {
                    Text = "middle_",
                    Style = StyleFlags.Bold,
                },
                new PlainText { Text = "right%" },
            ],
        };

        var rendered = text.ToLatexString();

        Assert.Equal(@"left\&\textbf{middle\_}right\%", rendered);
        Assert.DoesNotContain(@"\textbackslash{}\&", rendered, StringComparison.Ordinal);
    }
}
