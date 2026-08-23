using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexFontOptionsTests
{
    [Fact]
    public void Default_UsesExpectedFontFamilies()
    {
        var families = new LatexFontRoleArray<LatexFontFamilyName>(
            main: new("Liberation Serif"),
            sans: new("Liberation Sans"),
            monospace: new("Liberation Mono"));
        var scales = new LatexFontRoleArray<LatexFontScale?>(
            main: null,
            sans: null,
            monospace: new(0.92));

        Assert.Equal(
            new LatexFontOptions(
                families: families,
                scales: scales),
            LatexFontOptions.Default);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.92)]
    [InlineData(1)]
    [InlineData(100)]
    public void FontScale_AcceptsPositiveFiniteValues(double value)
    {
        var scale = new LatexFontScale(value);

        Assert.Equal(value, scale.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FontScale_RejectsValuesThatAreNotPositiveAndFinite(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LatexFontScale(value));
    }

    [Fact]
    public void FontScale_FormatsWithInvariantDecimalNotation()
    {
        Assert.Equal("0.92", new LatexFontScale(0.92).ToString());
    }

    [Fact]
    public void ConfigurationRenderer_AppliesOnlyConfiguredScales()
    {
        var options = new LatexFontOptions(
            families: LatexFontOptions.Default.Families,
            scales: new(
                main: new(1.1),
                sans: null,
                monospace: new(0.875)));

        var lines = LatexFontConfigurationRenderer.Render(options).Split('\n');
        string[] expectedLines =
        [
            @"\setmainfont[Scale=1.1]{Liberation Serif}",
            @"\setsansfont{Liberation Sans}",
            @"\setmonofont[Scale=0.875]{Liberation Mono}",
        ];

        Assert.Equal(expectedLines, lines);
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
        var families = new LatexFontRoleArray<LatexFontFamilyName>(
            main: family,
            sans: null!,
            monospace: family);
        Assert.Throws<ArgumentException>(() => new LatexFontOptions(
            families: families,
            scales: LatexFontOptions.Default.Scales));
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
    public void RoleArray_CreateVisitsEveryRoleInOrder()
    {
        var visited = new List<LatexFontRole>();
        var values = LatexFontRoleArray<LatexFontRole>.Create(role =>
        {
            visited.Add(role);
            return role;
        });

        Assert.Equal(LatexFontRoles.All, visited.ToArray());
        Assert.Equal(LatexFontRoles.All, values.ToArray());
    }

    [Fact]
    public void RoleArray_EnumeratesInRoleOrder()
    {
        var values = new LatexFontRoleArray<int>(main: 1, sans: 2, monospace: 3);

        Assert.Equal([1, 2, 3], values);
    }
}
