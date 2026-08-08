using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexFailureClassifierTests
{
    private static readonly LatexExecutionOptions Options = new(
        [
            new ExecutableLatexRequirement("xelatex"),
            new ExecutableLatexRequirement("latexmk"),
            new TexFileLatexRequirement("needspace.sty"),
            new FontLatexRequirement("Liberation Serif"),
            new BabelLanguageLatexRequirement("romanian"),
        ],
        "./scripts/setup-latex.sh");

    [Theory]
    [InlineData("xelatex", LatexExecutionPhase.HeightMeasurement)]
    [InlineData("latexmk", LatexExecutionPhase.FinalRendering)]
    public void LaunchFailureIdentifiesDeclaredExecutable(
        string executable,
        LatexExecutionPhase phase)
    {
        var result = LatexFailureClassifier.ClassifyLaunchFailure(
            executable,
            phase,
            new FileNotFoundException($"Cannot start {executable}."),
            "/diagnostics",
            Options);

        var failure = Assert.IsType<IncompleteLatexInstallation>(result);
        var requirement = Assert.IsType<ExecutableLatexRequirement>(
            failure.MissingRequirement.Value);
        Assert.Equal(executable, requirement.Name);
        Assert.Equal(phase, failure.Phase);
        Assert.Equal("/diagnostics", failure.DiagnosticDirectory);
        Assert.EndsWith(
            "Make sure all LaTeX dependencies are installed, run ./scripts/setup-latex.sh, then retry.",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "! LaTeX Error: File `needspace.sty' not found.",
        typeof(TexFileLatexRequirement))]
    [InlineData(
        "Package fontspec Error:\nThe font \"Liberation Serif\" cannot be found.",
        typeof(FontLatexRequirement))]
    [InlineData(
        "! Package babel Error: Unknown option 'romanian'.",
        typeof(BabelLanguageLatexRequirement))]
    public void LogClassificationRecognizesDeclaredResource(
        string log,
        Type expectedRequirementType)
    {
        foreach (var phase in Enum.GetValues<LatexExecutionPhase>())
        {
            var failure = LatexFailureClassifier.ClassifyLog(
                log,
                phase,
                "/diagnostics",
                Options);

            Assert.NotNull(failure);
            Assert.IsType(expectedRequirementType, failure.MissingRequirement.Value);
            Assert.False(string.IsNullOrWhiteSpace(failure.Diagnostic));
        }
    }

    [Theory]
    [InlineData("! Undefined control sequence.\n! LaTeX Error: File `needspace.sty' not found.")]
    [InlineData("! FJH_EVENT_PAGE_OVERFLOW: WorkExperience / 1\n! LaTeX Error: File `needspace.sty' not found.")]
    [InlineData("! An unknown catastrophic TeX failure.")]
    [InlineData("Package fontspec Warning: Font shape unavailable; substituting.")]
    public void EarlierMalformedOverflowUnknownAndWarningsAreNotSetupFailures(string log)
    {
        Assert.Null(LatexFailureClassifier.ClassifyLog(
            log,
            LatexExecutionPhase.FinalRendering,
            "/diagnostics",
            Options));
    }

    [Fact]
    public void FirstCausalMissingRequirementWinsOverLaterOverflow()
    {
        var failure = LatexFailureClassifier.ClassifyLog(
            "! LaTeX Error: File `needspace.sty' not found.\n! FJH_EVENT_PAGE_OVERFLOW: WorkExperience / 1",
            LatexExecutionPhase.FinalRendering,
            "/diagnostics",
            Options);

        Assert.IsType<TexFileLatexRequirement>(failure!.MissingRequirement.Value);
    }

    [Fact]
    public void UndeclaredResourceIsNotASetupFailure()
    {
        Assert.Null(LatexFailureClassifier.ClassifyLog(
            "! LaTeX Error: File `undeclared.sty' not found.",
            LatexExecutionPhase.HeightMeasurement,
            "/diagnostics",
            Options));
    }

    [Fact]
    public void MessageWithoutHintUsesTheShortGuidance()
    {
        var options = new LatexExecutionOptions([
            new TexFileLatexRequirement("needspace.sty"),
        ]);
        var failure = LatexFailureClassifier.ClassifyLog(
            "! LaTeX Error: File `needspace.sty' not found.",
            LatexExecutionPhase.HeightMeasurement,
            "/diagnostics",
            options);

        Assert.EndsWith(
            "Make sure all LaTeX dependencies are installed, then retry.",
            failure!.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyNestedUnionIsRejectedAtConsumptionBoundary()
    {
        LatexRequirement requirement = default;

        Assert.Throws<InvalidOperationException>(() => Describe(requirement));

        static string Describe(LatexRequirement value) => value switch
        {
            ExecutableLatexRequirement executable => executable.Name,
            TexFileLatexRequirement file => file.FileName,
            FontLatexRequirement font => font.FamilyName,
            BabelLanguageLatexRequirement language => language.LanguageName,
            _ => throw new InvalidOperationException("The union is empty."),
        };
    }

    [Fact]
    public async Task MeasurementLaunchFailureRetainsItsDiagnosticDirectory()
    {
        var runner = new XeLatexMeasurementRunner(
            new LatexExecutablePaths("unused-latexmk", "missing-xelatex.exe"));
        var request = new LatexMeasurementRequest(
            new MeasurementCorrelationId(1),
            new LatexMeasurementCacheKey(
                1,
                LatexMeasurementKind.ExperienceItem,
                new string('0', 64)),
            "text",
            LatexMeasurementMode.Box);
        var result = await runner.MeasureAsync(
            "template.tex",
            [request],
            NoOpProgressReporter.Instance,
            new LatexExecutionOptions(
                [new ExecutableLatexRequirement("missing-xelatex")],
                "setup"),
            CancellationToken.None);

        var failure = Assert.IsType<IncompleteLatexInstallation>(result.Value);
        try
        {
            Assert.True(Directory.Exists(failure.DiagnosticDirectory));
            Assert.True(File.Exists(Path.Combine(failure.DiagnosticDirectory, "measurement.tex")));
        }
        finally
        {
            Directory.Delete(failure.DiagnosticDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledMeasurementDeletesItsWorkingDirectory()
    {
        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fjh-cancel-cleanup-{Guid.NewGuid():N}");
        var runner = new XeLatexMeasurementRunner(
            new LatexExecutablePaths("unused-latexmk", "unused-xelatex"),
            () => workingDirectory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.MeasureAsync(
            "template.tex",
            [new LatexMeasurementRequest(
                new MeasurementCorrelationId(1),
                new LatexMeasurementCacheKey(
                    1,
                    LatexMeasurementKind.ExperienceItem,
                    new string('0', 64)),
                "text",
                LatexMeasurementMode.Box)],
            NoOpProgressReporter.Instance,
            Options,
            cancellation.Token));

        Assert.False(Directory.Exists(workingDirectory));
    }
}
