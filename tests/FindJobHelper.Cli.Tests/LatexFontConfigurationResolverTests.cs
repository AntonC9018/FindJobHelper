using FindJobHelper.CVGeneration;

public sealed class LatexFontConfigurationResolverTests
{
    public static IEnumerable<object?[]> Cases =>
    [
        [
            new LatexFontRoleArray<string?>(main: "Flag Serif", sans: "Flag Sans", monospace: "Flag Mono"),
            new LatexFontRoleArray<string?>(main: "Env Serif", sans: "Env Sans", monospace: "Env Mono"),
            new LatexFontOptions(new(main: new("Flag Serif"), sans: new("Flag Sans"), monospace: new("Flag Mono"))),
            new LatexFontRoleArray<bool>(main: true, sans: true, monospace: true),
            false,
        ],
        [
            new LatexFontRoleArray<string?>(main: null, sans: null, monospace: null),
            new LatexFontRoleArray<string?>(main: "Env Serif", sans: "Env Sans", monospace: "Env Mono"),
            new LatexFontOptions(new(main: new("Env Serif"), sans: new("Env Sans"), monospace: new("Env Mono"))),
            new LatexFontRoleArray<bool>(main: true, sans: true, monospace: true),
            false,
        ],
        [
            new LatexFontRoleArray<string?>(main: null, sans: null, monospace: null),
            new LatexFontRoleArray<string?>(main: null, sans: null, monospace: null),
            LatexFontOptions.Default,
            new LatexFontRoleArray<bool>(main: false, sans: false, monospace: false),
            false,
        ],
        [
            new LatexFontRoleArray<string?>(main: " ", sans: null, monospace: null),
            new LatexFontRoleArray<string?>(main: "Env Serif", sans: null, monospace: null),
            null,
            null,
            true,
        ],
        [
            new LatexFontRoleArray<string?>(main: null, sans: null, monospace: null),
            new LatexFontRoleArray<string?>(main: null, sans: "", monospace: null),
            null,
            null,
            true,
        ],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolve_AppliesIndependentPrecedenceAndRejectsPresentBlankValues(
        LatexFontRoleArray<string?> flags,
        LatexFontRoleArray<string?> environments,
        LatexFontOptions? expectedOptions,
        LatexFontRoleArray<bool>? expectedManuallySpecified,
        bool throws)
    {
        ResolvedLatexFontConfiguration Resolve() => LatexFontConfigurationResolver.Resolve(
            flags: flags,
            environments: environments);

        if (throws)
        {
            Assert.Throws<LatexFontConfigurationException>(Resolve);
            return;
        }

        var result = Resolve();
        Assert.Equal(expectedOptions, result.Options);
        Assert.Equal(expectedManuallySpecified, result.ManuallySpecified);
    }
}
