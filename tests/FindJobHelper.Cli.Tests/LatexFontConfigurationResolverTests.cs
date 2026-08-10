using FindJobHelper.CVGeneration;

public sealed class LatexFontConfigurationResolverTests
{
    public static IEnumerable<object?[]> Cases =>
    [
        [new string?[] { "Flag Serif", "Flag Sans", "Flag Mono" }, new string?[] { "Env Serif", "Env Sans", "Env Mono" }, new[] { "Flag Serif", "Flag Sans", "Flag Mono" }, new[] { true, true, true }, false],
        [new string?[3], new string?[] { "Env Serif", "Env Sans", "Env Mono" }, new[] { "Env Serif", "Env Sans", "Env Mono" }, new[] { true, true, true }, false],
        [new string?[3], new string?[3], new[] { "Liberation Serif", "Liberation Sans", "Liberation Mono" }, new[] { false, false, false }, false],
        [new string?[] { " ", null, null }, new string?[] { "Env Serif", null, null }, Array.Empty<string>(), Array.Empty<bool>(), true],
        [new string?[3], new string?[] { null, "", null }, Array.Empty<string>(), Array.Empty<bool>(), true],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolve_AppliesIndependentPrecedenceAndRejectsPresentBlankValues(
        string?[] flags,
        string?[] environments,
        string[] expectedFamilies,
        bool[] expectedManuallySpecified,
        bool throws)
    {
        ResolvedLatexFontConfiguration Resolve() => LatexFontConfigurationResolver.Resolve(
            flags: new(flags),
            environments: new(environments));

        if (throws)
        {
            Assert.Throws<LatexFontConfigurationException>(Resolve);
            return;
        }

        var result = Resolve();
        Assert.Equal(expectedFamilies, result.Options.Families.Values.Select(static family => family.Value));
        Assert.Equal(expectedManuallySpecified, result.ManuallySpecified.Values);
    }
}
