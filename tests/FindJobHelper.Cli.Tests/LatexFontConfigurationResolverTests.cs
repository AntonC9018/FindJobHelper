using FindJobHelper.CVGeneration;

public sealed class LatexFontConfigurationResolverTests
{
    public static IEnumerable<object?[]> Cases =>
    [
        ["Flag Serif", "Flag Sans", "Flag Mono", "Env Serif", "Env Sans", "Env Mono", "Flag Serif", "Flag Sans", "Flag Mono", ManuallySpecifiedLatexFontRoles.Main | ManuallySpecifiedLatexFontRoles.Sans | ManuallySpecifiedLatexFontRoles.Mono, false],
        [null, null, null, "Env Serif", "Env Sans", "Env Mono", "Env Serif", "Env Sans", "Env Mono", ManuallySpecifiedLatexFontRoles.Main | ManuallySpecifiedLatexFontRoles.Sans | ManuallySpecifiedLatexFontRoles.Mono, false],
        [null, null, null, null, null, null, "Liberation Serif", "Liberation Sans", "Latin Modern Mono", ManuallySpecifiedLatexFontRoles.None, false],
        [" ", null, null, "Env Serif", null, null, null, null, null, ManuallySpecifiedLatexFontRoles.None, true],
        [null, null, null, null, "", null, null, null, null, ManuallySpecifiedLatexFontRoles.None, true],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolve_AppliesIndependentPrecedenceAndRejectsPresentBlankValues(
        string? mainFlag,
        string? sansFlag,
        string? monoFlag,
        string? mainEnvironment,
        string? sansEnvironment,
        string? monoEnvironment,
        string? expectedMain,
        string? expectedSans,
        string? expectedMono,
        ManuallySpecifiedLatexFontRoles expectedRoles,
        bool throws)
    {
        ResolvedLatexFontConfiguration Resolve() => LatexFontConfigurationResolver.Resolve(
            mainFlag: mainFlag,
            sansFlag: sansFlag,
            monoFlag: monoFlag,
            mainEnvironment: mainEnvironment,
            sansEnvironment: sansEnvironment,
            monoEnvironment: monoEnvironment);

        if (throws)
        {
            Assert.Throws<LatexFontConfigurationException>(Resolve);
            return;
        }

        var result = Resolve();
        Assert.Equal(expectedMain, result.Options.MainFontFamily.Value);
        Assert.Equal(expectedSans, result.Options.SansFontFamily.Value);
        Assert.Equal(expectedMono, result.Options.MonoFontFamily.Value);
        Assert.Equal(expectedRoles, result.ManuallySpecifiedRoles);
    }
}
