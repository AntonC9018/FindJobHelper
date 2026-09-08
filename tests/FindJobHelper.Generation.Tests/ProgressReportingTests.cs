using FindJobHelper.Generation;

namespace FindJobHelper.Generation.Tests;

public sealed class ProgressReportingTests
{
    [Theory]
    [InlineData(1, 1, 100, 25)]
    [InlineData(1, 2, 50, 12.5)]
    [InlineData(0, 0, 100, 25)]
    public void EqualShareAggregation_ReportsModuleAndOverallShares(
        double completed,
        double total,
        double expectedModule,
        double expectedOverall)
    {
        var aggregator = new EqualShareProgressAggregator(CreatePdfPlan());

        var update = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(completed, total));

        Assert.Equal(expectedModule, update.ModulePercentage);
        Assert.Equal(expectedOverall, update.OverallPercentage);
    }

    [Fact]
    public void EqualShareAggregation_CombinesCompletedAndPartialModules()
    {
        var aggregator = new EqualShareProgressAggregator(CreatePdfPlan());
        aggregator.Update(CvGenerationModule.ComputingHeights, new(1, 1));
        aggregator.Update(CvGenerationModule.MatchingExperiences, new(1, 1));

        var update = aggregator.Update(
            CvGenerationModule.CreatingTexFile,
            new(1, 2));

        Assert.Equal(50, update.ModulePercentage);
        Assert.Equal(62.5, update.OverallPercentage);
    }

    [Fact]
    public void EqualShareAggregation_ClampsAndRemainsMonotonic()
    {
        var aggregator = new EqualShareProgressAggregator(CreatePdfPlan());
        var negative = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(-20, 100));
        var half = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(50, 100));
        var regressive = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(25, 100));
        var excessive = aggregator.Update(
            CvGenerationModule.ComputingHeights,
            new(150, 100));

        Assert.Equal(0, negative.ModulePercentage);
        Assert.Equal(50, half.ModulePercentage);
        Assert.Equal(50, regressive.ModulePercentage);
        Assert.Equal(100, excessive.ModulePercentage);
    }

    private static CvGenerationProgressPlan CreatePdfPlan() =>
        new([
            new(CvGenerationModule.ComputingHeights, "Computing heights"),
            new(CvGenerationModule.MatchingExperiences, "Matching experiences"),
            new(CvGenerationModule.CreatingTexFile, "Creating TeX file"),
            new(CvGenerationModule.RenderingPdf, "Rendering PDF"),
        ]);
}
