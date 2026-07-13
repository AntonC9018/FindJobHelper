using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexEscapedStringTests
{
    [Fact]
    public void EscapesLatexSpecialCharacters()
    {
        var value = new LatexEscapedString(@"plain\{}#$%&_^~");

        Assert.Equal(@"plain\textbackslash{}\{\}\#\$\%\&\_\^{}\~{}", value.ToString());
    }

    [Fact]
    public void TryFormatWritesDirectlyToDestination()
    {
        var value = new LatexEscapedString(@"a&b");
        Span<char> destination = stackalloc char[4];

        var success = value.TryFormat(destination, out var charsWritten, default, null);

        Assert.True(success);
        Assert.Equal(4, charsWritten);
        Assert.True(destination.SequenceEqual(@"a\&b"));
    }

    [Fact]
    public void TryFormatReturnsFalseWhenDestinationIsTooSmall()
    {
        var value = new LatexEscapedString(@"a&b");
        Span<char> destination = stackalloc char[3];

        var success = value.TryFormat(destination, out var charsWritten, default, null);

        Assert.False(success);
        Assert.Equal(0, charsWritten);
    }
}
