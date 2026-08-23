using FindJobHelper.CVGeneration;

public sealed class LatexFontConfigurationResolverTests
{
    public static IEnumerable<object?[]> PrecedenceCases =>
    [
        [
            new Case(
                Flags: Values(
                    mainFamily: "Flag Serif",
                    sansFamily: "Flag Sans",
                    monoFamily: "Flag Mono",
                    mainScale: "1.1",
                    sansScale: "1.2",
                    monoScale: "0.8"),
                Environments: Values(
                    mainFamily: "Env Serif",
                    sansFamily: "Env Sans",
                    monoFamily: "Env Mono",
                    mainScale: "invalid",
                    sansScale: "invalid",
                    monoScale: "invalid"),
                ExpectedOptions: Options(
                    mainFamily: "Flag Serif",
                    sansFamily: "Flag Sans",
                    monoFamily: "Flag Mono",
                    mainScale: 1.1,
                    sansScale: 1.2,
                    monoScale: 0.8),
                ExpectedManuallySpecified: new(main: true, sans: true, monospace: true)),
        ],
        [
            new Case(
                Flags: Values(),
                Environments: Values(
                    mainFamily: "Env Serif",
                    sansFamily: "Env Sans",
                    monoFamily: "Env Mono",
                    mainScale: "0.9",
                    sansScale: "1.05",
                    monoScale: "0.75"),
                ExpectedOptions: Options(
                    mainFamily: "Env Serif",
                    sansFamily: "Env Sans",
                    monoFamily: "Env Mono",
                    mainScale: 0.9,
                    sansScale: 1.05,
                    monoScale: 0.75),
                ExpectedManuallySpecified: new(main: true, sans: true, monospace: true)),
        ],
        [
            new Case(
                Flags: Values(),
                Environments: Values(),
                ExpectedOptions: LatexFontOptions.Default,
                ExpectedManuallySpecified: new(main: false, sans: false, monospace: false)),
        ],
    ];

    [Theory]
    [MemberData(nameof(PrecedenceCases))]
    public void Resolve_AppliesIndependentFlagEnvironmentAndDefaultPrecedence(object caseValue)
    {
        var testCase = Assert.IsType<Case>(caseValue);
        var result = LatexFontConfigurationResolver.Resolve(
            flags: testCase.Flags,
            environments: testCase.Environments);

        Assert.Equal(testCase.ExpectedOptions, result.Options);
        Assert.Equal(testCase.ExpectedManuallySpecified, result.ManuallySpecified);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-number")]
    [InlineData("0")]
    [InlineData("-0.01")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Resolve_RejectsInvalidScaleFlags(string value)
    {
        var flags = Values(mainScale: value);

        var exception = Assert.Throws<LatexFontConfigurationException>(() =>
            LatexFontConfigurationResolver.Resolve(
                flags: flags,
                environments: Values()));

        Assert.StartsWith("--main-font-size", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsInvalidScaleEnvironmentValuesWithTheirSourceName()
    {
        var environments = Values(sansScale: "0");

        var exception = Assert.Throws<LatexFontConfigurationException>(() =>
            LatexFontConfigurationResolver.Resolve(
                flags: Values(),
                environments: environments));

        Assert.StartsWith("CV_SANS_FONT_SIZE", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RejectsPresentBlankFamilyBeforeEnvironmentFallback()
    {
        var flags = Values(mainFamily: " ");
        var environments = Values(mainFamily: "Env Serif");

        var exception = Assert.Throws<LatexFontConfigurationException>(() =>
            LatexFontConfigurationResolver.Resolve(
                flags: flags,
                environments: environments));

        Assert.StartsWith("--main-font", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaleSettings_ExposeMatchingEnvironmentVariableNames()
    {
        Assert.Equal("CV_MAIN_FONT_SIZE", LatexFontConfigurationResolver.ScaleSettings.Main.EnvironmentVariable);
        Assert.Equal("CV_SANS_FONT_SIZE", LatexFontConfigurationResolver.ScaleSettings.Sans.EnvironmentVariable);
        Assert.Equal("CV_MONO_FONT_SIZE", LatexFontConfigurationResolver.ScaleSettings.Monospace.EnvironmentVariable);
    }

    private static LatexFontConfigurationValues Values(
        string? mainFamily = null,
        string? sansFamily = null,
        string? monoFamily = null,
        string? mainScale = null,
        string? sansScale = null,
        string? monoScale = null)
    {
        var families = new LatexFontRoleArray<string?>(
            main: mainFamily,
            sans: sansFamily,
            monospace: monoFamily);
        var scales = new LatexFontRoleArray<string?>(
            main: mainScale,
            sans: sansScale,
            monospace: monoScale);
        return new(Families: families, Scales: scales);
    }

    private static LatexFontOptions Options(
        string mainFamily,
        string sansFamily,
        string monoFamily,
        double? mainScale,
        double? sansScale,
        double? monoScale)
    {
        var families = new LatexFontRoleArray<LatexFontFamilyName>(
            main: new(mainFamily),
            sans: new(sansFamily),
            monospace: new(monoFamily));
        var scales = new LatexFontRoleArray<LatexFontScale?>(
            main: CreateScale(mainScale),
            sans: CreateScale(sansScale),
            monospace: CreateScale(monoScale));
        return new(
            families: families,
            scales: scales);
    }

    private static LatexFontScale? CreateScale(double? value)
    {
        if (value is null)
        {
            return null;
        }

        return new(value.Value);
    }

    private sealed record Case(
        LatexFontConfigurationValues Flags,
        LatexFontConfigurationValues Environments,
        LatexFontOptions ExpectedOptions,
        LatexFontRoleArray<bool> ExpectedManuallySpecified);
}
