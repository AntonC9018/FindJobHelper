using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class CvFailurePresenterTests
{
    [Fact]
    public void InstallationFailureUsesCompleteFactsAndGeneralDisposition()
    {
        var options = new LatexExecutionOptions(
            [new TexFileLatexRequirement(new("needspace.sty"))],
            "./scripts/setup-latex.sh");
        var failure = new IncompleteLatexInstallation(
            new TexFileLatexRequirement(new("needspace.sty")),
            LatexExecutionPhase.FinalRendering,
            diagnostic: "missing file",
            diagnosticDirectory: "/diagnostics",
            options);

        var presentation = CvFailurePresenter.Present((ICvRenderResult)failure);

        Assert.Same(options, failure.ExecutionOptions);
        Assert.Equal(CvFailureDisposition.General, presentation.Disposition);
        Assert.Contains("needspace.sty", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("./scripts/setup-latex.sh", presentation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderLayoutFailureUsesFactsAndValidationDisposition()
    {
        ICvRenderResult result = new EventOverflowFailure(
            Section: "Work Experience",
            Event: "Large event");

        var presentation = CvFailurePresenter.Present(result);

        Assert.Equal(CvFailureDisposition.Validation, presentation.Disposition);
        Assert.Equal(
            "CV event 'Large event' in section 'Work Experience' exceeds the usable height of a fresh page.",
            presentation.Message);
    }

    [Fact]
    public void SuccessNullAndUnsupportedImplementationsAreInvariantViolations()
    {
        ICvMeasurementResult successfulMeasurement = CvMeasurementSnapshot.CreateFrozen(
            experienceItems: new Dictionary<ExperienceItemId, LatexHeight>(),
            experienceHeadings: new Dictionary<ExperienceListId, LatexHeight>(),
            experienceChrome: new Dictionary<ExperienceListId, LatexHeight>(),
            currentPageCompleteSections: new Dictionary<Section, LatexHeight>(),
            currentPageSectionChrome: new Dictionary<Section, LatexHeight>(),
            freshPageSectionChrome: new Dictionary<Section, LatexHeight>(),
            currentPageSplitSectionStart: new Dictionary<Section, LatexHeight>(),
            freshPageSplitSectionStart: new Dictionary<Section, LatexHeight>(),
            splitSectionEnd: LatexHeight.Zero,
            freshPageContinuation: LatexHeight.Zero,
            currentPageExplicitStaticSections: new Dictionary<Section, LatexHeight>(),
            freshPageExplicitStaticSections: new Dictionary<Section, LatexHeight>(),
            documentHeader: LatexHeight.Zero,
            documentFooter: LatexHeight.Zero,
            usablePageHeight: LatexHeight.Zero);
        ICvRenderResult successfulRender = new GeneratedCvArtifacts("cv.pdf");

        Assert.Throws<ArgumentNullException>(() => CvFailurePresenter.Present((ICvMeasurementResult)null!));
        Assert.Throws<ArgumentNullException>(() => CvFailurePresenter.Present((ICvRenderResult)null!));
        Assert.Contains(
            typeof(UnsupportedMeasurementResult).FullName!,
            Assert.Throws<InvalidOperationException>(
                () => CvFailurePresenter.Present(new UnsupportedMeasurementResult())).Message,
            StringComparison.Ordinal);
        Assert.Contains(
            typeof(UnsupportedRenderResult).FullName!,
            Assert.Throws<InvalidOperationException>(
                () => CvFailurePresenter.Present(new UnsupportedRenderResult())).Message,
            StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => CvFailurePresenter.Present(successfulMeasurement));
        Assert.Throws<InvalidOperationException>(() => CvFailurePresenter.Present(successfulRender));
    }

    private sealed class UnsupportedMeasurementResult : ICvMeasurementResult;
    private sealed class UnsupportedRenderResult : ICvRenderResult;
}
