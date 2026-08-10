using FindJobHelper.CVGeneration;

public sealed class LatexFontConfigurationResolverTests
{
    public static IEnumerable<object?[]> Cases =>
    [
        [
            new Case(
                Flags: new(main: "Flag Serif", sans: "Flag Sans", monospace: "Flag Mono"),
                Environments: new(main: "Env Serif", sans: "Env Sans", monospace: "Env Mono"),
                ExpectedOptions: new(new(main: new("Flag Serif"), sans: new("Flag Sans"), monospace: new("Flag Mono"))),
                ExpectedManuallySpecified: new(main: true, sans: true, monospace: true)),
        ],
        [
            new Case(
                Flags: new(main: null, sans: null, monospace: null),
                Environments: new(main: "Env Serif", sans: "Env Sans", monospace: "Env Mono"),
                ExpectedOptions: new(new(main: new("Env Serif"), sans: new("Env Sans"), monospace: new("Env Mono"))),
                ExpectedManuallySpecified: new(main: true, sans: true, monospace: true)),
        ],
        [
            new Case(
                Flags: new(main: null, sans: null, monospace: null),
                Environments: new(main: null, sans: null, monospace: null),
                ExpectedOptions: LatexFontOptions.Default,
                ExpectedManuallySpecified: new(main: false, sans: false, monospace: false)),
        ],
        [
            new Case(
                Flags: new(main: " ", sans: null, monospace: null),
                Environments: new(main: "Env Serif", sans: null, monospace: null),
                Throws: true),
        ],
        [
            new Case(
                Flags: new(main: null, sans: null, monospace: null),
                Environments: new(main: null, sans: "", monospace: null),
                Throws: true),
        ],
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Resolve_AppliesIndependentPrecedenceAndRejectsPresentBlankValues(Case testCase)
    {
        ResolvedLatexFontConfiguration Resolve() => LatexFontConfigurationResolver.Resolve(
            flags: testCase.Flags,
            environments: testCase.Environments);

        if (testCase.Throws)
        {
            Assert.Throws<LatexFontConfigurationException>(Resolve);
            return;
        }

        var result = Resolve();
        Assert.Equal(testCase.ExpectedOptions, result.Options);
        Assert.Equal(testCase.ExpectedManuallySpecified, result.ManuallySpecified);
    }

    public sealed record Case(
        LatexFontRoleArray<string?> Flags,
        LatexFontRoleArray<string?> Environments,
        LatexFontOptions? ExpectedOptions = null,
        LatexFontRoleArray<bool>? ExpectedManuallySpecified = null,
        bool Throws = false);
}
