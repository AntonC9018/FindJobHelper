using FindJobHelper.CVGeneration;
using FindJobHelper.Generation;
using Spectre.Console.Testing;

namespace MainCli.Tests;

public sealed class CvGenerationProgressDisplayTests
{
    [Fact]
    public async Task InteractiveDisplay_RendersReusableCurrentAndOverallRowsAtOneHundredPercent()
    {
        using var console = new TestConsole();
        console.Interactive().Width(120);
        var display = new InteractiveCvGenerationProgressDisplay(console);
        var plan = new CvGenerationProgressPlan([
            new(
                CvGenerationModule.ComputingHeights,
                "Computing heights"),
            new(
                CvGenerationModule.MatchingExperiences,
                "Matching experiences"),
        ]);

        await display.RunAsync(
            plan,
            context =>
            {
                context.BeginModule(CvGenerationModule.ComputingHeights);
                context.Reporter(CvGenerationModule.ComputingHeights)
                    .Report(new(1, 1));
                context.BeginModule(CvGenerationModule.MatchingExperiences);
                context.Reporter(CvGenerationModule.MatchingExperiences)
                    .Report(new(1, 1));
                return Task.FromResult(0);
            },
            CancellationToken.None);

        Assert.Contains("Overall", console.Output, StringComparison.Ordinal);
        Assert.Contains("Current task:", console.Output, StringComparison.Ordinal);
        Assert.Contains("Matching experiences", console.Output, StringComparison.Ordinal);
        var renderedLines = console.Lines.ToArray();
        var overallDescriptionLine = Array.FindLastIndex(
            renderedLines,
            static line => line.TrimEnd() == "Overall");
        var currentDescriptionLine = Array.FindLastIndex(renderedLines, static line =>
            line.Contains("Current task:", StringComparison.Ordinal));
        Assert.True(overallDescriptionLine > 0, console.Output);
        Assert.True(
            currentDescriptionLine > overallDescriptionLine,
            console.Output);
        Assert.DoesNotContain(
            "Overall",
            renderedLines[overallDescriptionLine - 1],
            StringComparison.Ordinal);
        Assert.Contains(
            "100%",
            renderedLines[overallDescriptionLine - 1],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Current task:",
            renderedLines[currentDescriptionLine - 1],
            StringComparison.Ordinal);
        Assert.Contains(
            "100%",
            renderedLines[currentDescriptionLine - 1],
            StringComparison.Ordinal);
        Assert.Equal(
            renderedLines[overallDescriptionLine - 1]
                .Count(static character => character == '━'),
            renderedLines[currentDescriptionLine - 1]
                .Count(static character => character == '━'));
    }
}
