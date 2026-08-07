using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexConverterTests
{
    [Fact]
    public void EscapesEveryLatexSpecialCharacter()
    {
        Assert.Equal(
            @"plain\textbackslash{}\{\}\#\$\%\&\_\^{}\~{}",
            LatexConverter.ToLatexString(@"plain\{}#$%&_^~").ToString());
    }

    [Fact]
    public void ConvertsRegularAndNullableStrings()
    {
        Assert.Equal(@"a\&b", LatexConverter.ToLatexString(new RegularString("a&b")).ToString());
        Assert.Equal(@"a\_b", LatexConverter.ToLatexString(new NullableRegularString("a_b")).ToString());
        Assert.Equal(string.Empty, LatexConverter.ToLatexString(NullableRegularString.Null).ToString());
    }

    [Fact]
    public void EscapedValueFormatsDirectlyIntoDestination()
    {
        var value = LatexConverter.ToLatexString(@"a&b");
        Span<char> destination = stackalloc char[4];

        var success = value.TryFormat(destination, out var charsWritten, default, null);

        Assert.True(success);
        Assert.Equal(4, charsWritten);
        Assert.True(destination.SequenceEqual(@"a\&b"));
    }

    [Fact]
    public void RegularStringsReturnUnescapedSourceText()
    {
        const string source = @"plain\{}#$%&_^~";

        Assert.Equal(source, new RegularString(source).ToString());
        Assert.Equal(source, new NullableRegularString(source).ToString());
        Assert.Equal(string.Empty, NullableRegularString.Null.ToString());
    }
}
