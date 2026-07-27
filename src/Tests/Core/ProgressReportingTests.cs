using System.Reflection;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class ProgressReportingTests
{
    [Fact]
    public void EqualShareAggregation_CompletedModuleContributesOneShare()
    {
        var aggregator = new EqualShareProgressAggregator(CreatePdfPlan());

        var update = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(CompletedWorkUnits: 1, TotalWorkUnits: 1));

        Assert.Equal(100, update.ModulePercentage);
        Assert.Equal(25, update.OverallPercentage);
    }

    [Fact]
    public void EqualShareAggregation_HalfCompleteModuleContributesHalfItsShare()
    {
        var aggregator = new EqualShareProgressAggregator(CreatePdfPlan());

        var update = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(CompletedWorkUnits: 1, TotalWorkUnits: 2));

        Assert.Equal(50, update.ModulePercentage);
        Assert.Equal(12.5, update.OverallPercentage);
    }

    [Fact]
    public void EqualShareAggregation_CombinesCompletedAndPartialModules()
    {
        var aggregator = new EqualShareProgressAggregator(CreatePdfPlan());

        aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(CompletedWorkUnits: 1, TotalWorkUnits: 1));
        aggregator.Update(
            CvGenerationModule.MatchingExperiences,
            new(CompletedWorkUnits: 1, TotalWorkUnits: 1));
        var update = aggregator.Update(
            CvGenerationModule.CreatingTexFile,
            new(CompletedWorkUnits: 1, TotalWorkUnits: 2));

        Assert.Equal(50, update.ModulePercentage);
        Assert.Equal(62.5, update.OverallPercentage);
    }

    [Fact]
    public void EqualShareAggregation_ClampsAndRemainsMonotonic()
    {
        var aggregator = new EqualShareProgressAggregator(CreatePdfPlan());

        var negative = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(CompletedWorkUnits: -20, TotalWorkUnits: 100));
        var half = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(CompletedWorkUnits: 50, TotalWorkUnits: 100));
        var regressive = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(CompletedWorkUnits: 25, TotalWorkUnits: 100));
        var nonFiniteCompleted = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(
                CompletedWorkUnits: double.PositiveInfinity,
                TotalWorkUnits: 100));
        var excessive = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(CompletedWorkUnits: 150, TotalWorkUnits: 100));
        var nonFiniteTotal = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(
                CompletedWorkUnits: 1,
                TotalWorkUnits: double.NaN));

        Assert.Equal(0, negative.ModulePercentage);
        Assert.Equal(0, negative.OverallPercentage);
        Assert.Equal(50, half.ModulePercentage);
        Assert.Equal(12.5, half.OverallPercentage);
        Assert.Equal(50, regressive.ModulePercentage);
        Assert.Equal(12.5, regressive.OverallPercentage);
        Assert.Equal(50, nonFiniteCompleted.ModulePercentage);
        Assert.Equal(12.5, nonFiniteCompleted.OverallPercentage);
        Assert.Equal(100, excessive.ModulePercentage);
        Assert.Equal(25, excessive.OverallPercentage);
        Assert.Equal(100, nonFiniteTotal.ModulePercentage);
        Assert.Equal(25, nonFiniteTotal.OverallPercentage);
    }

    [Fact]
    public void EqualShareAggregation_ZeroTotalCompletesOnlyItsModuleShare()
    {
        var aggregator = new EqualShareProgressAggregator(CreatePdfPlan());

        var update = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(CompletedWorkUnits: 0, TotalWorkUnits: 0));

        Assert.Equal(100, update.ModulePercentage);
        Assert.Equal(25, update.OverallPercentage);
    }

    [Theory]
    [InlineData(-1, 10, 0)]
    [InlineData(5, 10, 0.5)]
    [InlineData(20, 10, 1)]
    [InlineData(0, 0, 1)]
    public void ProgressFraction_Clamps(
        double completed,
        double total,
        double expected)
    {
        Assert.Equal(
            expected,
            ProgressMath.Fraction(new(completed, total)));
    }

    [Fact]
    public void GenerationApis_RequireProgressArguments()
    {
        Assert.All(
            typeof(ExperienceSearch)
                .GetMethods(
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance)
                .Where(static method => method.Name == nameof(ExperienceSearch.Run)),
            static method => Assert.Contains(
                method.GetParameters(),
                static parameter =>
                    parameter.ParameterType == typeof(IProgressReporter)));

        AssertProgressParameter(
            typeof(LatexMeasurementService),
            nameof(LatexMeasurementService.MeasureAsync));
        AssertProgressParameter(
            typeof(CvTemplate),
            nameof(CvTemplate.Generate),
            typeof(LatexProgressReporters));
        AssertProgressParameter(
            typeof(CvMarkdownRenderer),
            "Render");
        AssertProgressParameter(
            typeof(ILatexMeasurementRunner),
            nameof(ILatexMeasurementRunner.MeasureAsync));
    }

    [Fact]
    public void NoOpReporter_IsReusableSingleton()
    {
        Assert.Same(
            NoOpProgressReporter.Instance,
            NoOpProgressReporter.Instance);

        NoOpProgressReporter.Instance.Report(new(1, 1, "done"));
    }

    private static CvGenerationProgressPlan CreatePdfPlan() =>
        new([
            new(
                CvGenerationModule.ComputingHeights,
                "Computing heights"),
            new(
                CvGenerationModule.MatchingExperiences,
                "Matching experiences"),
            new(
                CvGenerationModule.CreatingTexFile,
                "Creating TeX file"),
            new(
                CvGenerationModule.RenderingPdf,
                "Rendering PDF"),
        ]);

    private static void AssertProgressParameter(
        Type type,
        string methodName,
        Type? progressType = null)
    {
        var methods = type
            .GetMethods(
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.Instance)
            .Where(method => method.Name == methodName)
            .ToArray();

        Assert.NotEmpty(methods);
        Assert.All(
            methods,
            method => Assert.Contains(
                method.GetParameters(),
                parameter => parameter.ParameterType
                    == (progressType ?? typeof(IProgressReporter))));
    }
}
