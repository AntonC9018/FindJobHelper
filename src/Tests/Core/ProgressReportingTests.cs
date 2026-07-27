using System.Reflection;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class ProgressReportingTests
{
    [Fact]
    public void WeightedAggregation_ClampsAndRemainsMonotonic()
    {
        var plan = new CvGenerationProgressPlan([
            new(CvGenerationTask.ComputingHeights, "Computing heights", 1),
            new(CvGenerationTask.MatchingExperiences, "Matching experiences", 3),
        ]);
        var aggregator = new WeightedProgressAggregator(plan);

        var heights = aggregator.Update(
            CvGenerationTask.ComputingHeights,
            new(CompletedWorkUnits: 150, TotalWorkUnits: 100));
        var regressedHeights = aggregator.Update(
            CvGenerationTask.ComputingHeights,
            new(CompletedWorkUnits: -20, TotalWorkUnits: 100));
        var matching = aggregator.Update(
            CvGenerationTask.MatchingExperiences,
            new(CompletedWorkUnits: 1, TotalWorkUnits: 2));
        var regressedMatching = aggregator.Update(
            CvGenerationTask.MatchingExperiences,
            new(CompletedWorkUnits: 0, TotalWorkUnits: 2));

        Assert.Equal(100, heights.TaskPercentage);
        Assert.Equal(25, heights.OverallPercentage);
        Assert.Equal(100, regressedHeights.TaskPercentage);
        Assert.Equal(25, regressedHeights.OverallPercentage);
        Assert.Equal(50, matching.TaskPercentage);
        Assert.Equal(62.5, matching.OverallPercentage);
        Assert.Equal(50, regressedMatching.TaskPercentage);
        Assert.Equal(62.5, regressedMatching.OverallPercentage);
    }

    [Fact]
    public void WeightedAggregation_ZeroWorkCompletesWithoutDivisionByZero()
    {
        var plan = new CvGenerationProgressPlan([
            new(CvGenerationTask.ComputingHeights, "Computing heights", 0),
        ]);
        var aggregator = new WeightedProgressAggregator(plan);

        var update = aggregator.Update(
            CvGenerationTask.ComputingHeights,
            new(CompletedWorkUnits: 0, TotalWorkUnits: 0));

        Assert.Equal(100, update.TaskPercentage);
        Assert.Equal(100, update.OverallPercentage);
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
