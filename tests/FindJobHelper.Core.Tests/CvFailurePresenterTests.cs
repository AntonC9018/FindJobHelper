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
            "missing file",
            "/diagnostics",
            options);

        var presentation = CvFailurePresenter.Present(new CvRenderResult(failure));

        Assert.Same(options, failure.ExecutionOptions);
        Assert.Equal(CvFailureDisposition.General, presentation.Disposition);
        Assert.Contains("needspace.sty", presentation.Message, StringComparison.Ordinal);
        Assert.Contains("./scripts/setup-latex.sh", presentation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderLayoutFailureUsesFactsAndValidationDisposition()
    {
        CvRenderResult result = new RenderLayoutFailure(
            new EventOverflowFailure("Work Experience", "Large event"));

        var presentation = CvFailurePresenter.Present(result);

        Assert.Equal(CvFailureDisposition.Validation, presentation.Disposition);
        Assert.Equal(
            "CV event 'Large event' in section 'Work Experience' exceeds the usable height of a fresh page.",
            presentation.Message);
    }

    [Fact]
    public void SuccessAndDefaultUnionsAreInvariantViolations()
    {
        CvMeasurementResult emptyMeasurement = default;
        CvRenderResult emptyRender = default;
        CvMeasurementResult successfulMeasurement = CvMeasurementSnapshot.CreateFrozen(
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
        CvRenderResult successfulRender = new GeneratedCvArtifacts("cv.pdf");

        Assert.Throws<InvalidOperationException>(() => CvFailurePresenter.Present(emptyMeasurement));
        Assert.Throws<InvalidOperationException>(() => CvFailurePresenter.Present(emptyRender));
        Assert.Throws<InvalidOperationException>(() => CvFailurePresenter.Present(successfulMeasurement));
        Assert.Throws<InvalidOperationException>(() => CvFailurePresenter.Present(successfulRender));
    }
}
