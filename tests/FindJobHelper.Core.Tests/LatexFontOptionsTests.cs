using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexFontOptionsTests
{
    [Fact]
    public void Default_UsesExpectedFontFamilies()
    {
        Assert.Equal("Liberation Serif", LatexFontOptions.Default.MainFontFamily.Value);
        Assert.Equal("Liberation Sans", LatexFontOptions.Default.SansFontFamily.Value);
        Assert.Equal("Latin Modern Mono", LatexFontOptions.Default.MonoFontFamily.Value);
    }

    [Theory]
    [InlineData("Noto Sans")]
    [InlineData("IBM Plex Mono")]
    [InlineData("思源黑體")]
    [InlineData("ПТ Сериф")]
    [InlineData("أميري")]
    public void Constructor_AcceptsFontFamilyNames(string value)
    {
        var familyName = new LatexFontFamilyName(value);
        Assert.Equal(value, familyName.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Font\nName")]
    [InlineData("Font\rName")]
    [InlineData("Font\tName")]
    [InlineData("Font#Name")]
    [InlineData("Font$Name")]
    [InlineData("Font%Name")]
    [InlineData("Font&Name")]
    [InlineData("Font\\Name")]
    [InlineData("Font^Name")]
    [InlineData("Font_Name")]
    [InlineData("Font{Name}")]
    [InlineData("Font~Name")]
    [InlineData("fonts/Font Name")]
    [InlineData("FontName.otf")]
    [InlineData("FontName.TTF")]
    [InlineData("FontName.ttc")]
    public void Constructor_RejectsUnsafeOrFileStyleInputs(string value)
    {
        Assert.Throws<ArgumentException>(() => new LatexFontFamilyName(value));
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new LatexFontFamilyName(null!));
    }

    [Fact]
    public void Options_RejectNullFamilies()
    {
        var family = new LatexFontFamilyName("Valid Family");
        Assert.Throws<ArgumentNullException>(() => new LatexFontOptions(null!, family, family));
        Assert.Throws<ArgumentNullException>(() => new LatexFontOptions(family, null!, family));
        Assert.Throws<ArgumentNullException>(() => new LatexFontOptions(family, family, null!));
    }
}
