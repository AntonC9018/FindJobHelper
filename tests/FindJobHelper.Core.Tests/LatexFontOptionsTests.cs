using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexFontOptionsTests
{
    [Fact]
    public void Default_UsesExpectedFontFamilies()
    {
        Assert.Equal(
            ["Liberation Serif", "Liberation Sans", "Liberation Mono"],
            LatexFontOptions.Default.Families.Values.Select(static family => family.Value));
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
        Assert.Throws<ArgumentNullException>(() => new LatexFontOptions(default));
        Assert.Throws<ArgumentException>(() => new LatexFontOptions(new([family, null!, family])));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void Options_RequireOneFamilyPerRole(int count)
    {
        var families = Enumerable.Repeat(new LatexFontFamilyName("Valid Family"), count).ToArray();
        Assert.Throws<ArgumentException>(() => new LatexFontRoleArray<LatexFontFamilyName>(families));
    }
}
