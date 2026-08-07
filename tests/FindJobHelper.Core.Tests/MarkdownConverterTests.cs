using FindJobHelper.Core.Helper;

namespace FindJobHelper.Core.Tests;

public sealed class MarkdownConverterTests
{
    [Fact]
    public void EscapesStructuralCharactersInPlainText()
    {
        var text = new PlainText
        {
            Text = @"\`*_{}[]()<>#+-.!|",
        };

        Assert.Equal(
            @"\\\`\*\_\{\}\[\]\(\)\<\>\#\+\-\.\!\|",
            text.ToMarkdownString());
    }

    [Fact]
    public void RendersBoldAndItalic()
    {
        Assert.Equal(
            "**bold**",
            RichTextFactory.Styled("bold", StyleFlags.Bold).ToMarkdownString());
        Assert.Equal(
            "*italic*",
            RichTextFactory.Styled("italic", StyleFlags.Italic).ToMarkdownString());
    }

    [Fact]
    public void DoesNotEscapeMarkdownPunctuationInsideCode()
    {
        Assert.Equal(
            "`code *_[]()`",
            RichTextFactory.Code("code *_[]()").ToMarkdownString());
    }

    [Fact]
    public void UsesFenceLongerThanBacktickRunsInsideCode()
    {
        Assert.Equal(
            "```a``b```",
            RichTextFactory.Code("a``b").ToMarkdownString());
        Assert.Equal(
            "`` `edge` ``",
            RichTextFactory.Code("`edge`").ToMarkdownString());
    }

    [Fact]
    public void PreservesCombinedCodeAndEmphasis()
    {
        var text = RichTextFactory.Styled(
            "a*b",
            StyleFlags.Bold | StyleFlags.Italic | StyleFlags.Code);

        Assert.Equal("***`a*b`***", text.ToMarkdownString());
    }

    [Fact]
    public void WrapsAbsoluteLinkDestinationInAngleBrackets()
    {
        var link = RichTextFactory.Href(
            "https://example.test/path_(one)?first=1&second=two",
            "label");

        Assert.Equal(
            "[label](<https://example.test/path_(one)?first=1&second=two>)",
            link.ToMarkdownString());
    }

    [Fact]
    public void PreservesStylesInLinkLabels()
    {
        var link = RichTextFactory.Href(
            "https://example.test",
            RichTextFactory.Bold("styled label"));

        Assert.Equal(
            "[**styled label**](<https://example.test/>)",
            link.ToMarkdownString());
    }

    [Fact]
    public void CompositeNodesAreEscapedExactlyOnce()
    {
        var text = new RichText
        {
            Items =
            [
                new PlainText { Text = "a*b " },
                RichTextFactory.Bold("c_d"),
            ],
        };

        Assert.Equal(@"a\*b **c\_d**", text.ToMarkdownString());
    }
}
