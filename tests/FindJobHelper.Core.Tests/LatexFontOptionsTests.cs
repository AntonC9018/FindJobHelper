using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexFontOptionsTests
{
    [Fact]
    public void Default_UsesExpectedFontFamilies()
    {
        Assert.Equal(
            new LatexFontOptions(new(
                main: new("Liberation Serif"),
                sans: new("Liberation Sans"),
                monospace: new("Liberation Mono"))),
            LatexFontOptions.Default);
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
        Assert.Throws<ArgumentException>(() => new LatexFontOptions(new(
            main: family,
            sans: null!,
            monospace: family)));
    }

    [Fact]
    public void RoleArray_IndexerRejectsUnknownRole()
    {
        var families = LatexFontOptions.Default.Families;
        Assert.Throws<ArgumentOutOfRangeException>(() => families[(LatexFontRole)123]);
    }

    [Fact]
    public void RoleArray_MapPreservesRoles()
    {
        var values = new LatexFontRoleArray<int>(main: 1, sans: 2, monospace: 3);

        var mapped = values.Map(static value => value.ToString());

        Assert.Equal("1", mapped.Main);
        Assert.Equal("2", mapped.Sans);
        Assert.Equal("3", mapped.Monospace);
    }

    [Fact]
    public void RoleArray_EnumeratesInRoleOrder()
    {
        var values = new LatexFontRoleArray<int>(main: 1, sans: 2, monospace: 3);

        Assert.Equal([1, 2, 3], values);
    }
}
