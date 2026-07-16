using System.Collections.Immutable;

namespace FindJobHelper.Core.Tests;

public sealed class SelectionDebugReportTests
{
    [Fact]
    public async Task FrozenDatasetSelectionComparisonReport()
    {
        var runs = await SelectionDebugReport.RunAll(CancellationToken.None);
        var report = SelectionDebugReport.ToMarkdown(runs);

        AssertSelectionInvariants(runs);
        Assert.Contains("budget minimum/maximum vs actual", report);
        Assert.Contains("over +", report);

        await Verify(report);
    }

    private static void AssertSelectionInvariants(
        ImmutableArray<SelectionDebugRun> runs)
    {
        foreach (var run in runs)
        {
            foreach (var eventGroup in run.Result.Diagnostics.Items
                .GroupBy(x => new
                {
                    x.Section,
                    x.Event,
                }))
            {
                var selected = eventGroup
                    .Select(x => x.Item)
                    .ToArray();
                var selectedSet = new HashSet<ExperienceListItem>();

                foreach (var item in selected)
                {
                    Assert.True(
                        selectedSet.Add(item),
                        $"Duplicate selected item in {run.Scenario}/{run.Preset}/{eventGroup.Key.Event.Title}.");
                }

                var selectedIndexes = selected
                    .Select((item, index) => (item, index))
                    .ToDictionary(x => x.item, x => x.index);

                foreach (var trace in eventGroup)
                {
                    foreach (var dependency in TransitiveDependencies(trace.Item))
                    {
                        Assert.True(
                            selectedIndexes.TryGetValue(dependency, out var dependencyIndex),
                            $"Missing dependency for {run.Scenario}/{run.Preset}/{eventGroup.Key.Event.Title}: {dependency.Text}.");
                        Assert.True(
                            dependencyIndex < selectedIndexes[trace.Item],
                            $"Dependency order violation in {run.Scenario}/{run.Preset}/{eventGroup.Key.Event.Title}: {dependency.Text} must appear before {trace.Item.Text}.");
                    }

                    foreach (var predecessor in trace.Item.After)
                    {
                        if (predecessor is null ||
                            !selectedIndexes.TryGetValue(predecessor, out var predecessorIndex))
                        {
                            continue;
                        }

                        Assert.True(
                            predecessorIndex < selectedIndexes[trace.Item],
                            $"Relative order violation in {run.Scenario}/{run.Preset}/{eventGroup.Key.Event.Title}: {predecessor.Text} must appear before {trace.Item.Text}.");
                    }
                }
            }
        }
    }

    private static IEnumerable<ExperienceListItem> TransitiveDependencies(
        ExperienceListItem item)
    {
        var seen = new HashSet<ExperienceListItem>();
        var stack = new Stack<ExperienceListItem>();
        Push(item);

        while (stack.Count > 0)
        {
            var dependency = stack.Pop();
            if (!seen.Add(dependency))
            {
                continue;
            }

            yield return dependency;
            Push(dependency);
        }

        void Push(ExperienceListItem parent)
        {
            foreach (var dependency in parent.DependsOn)
            {
                if (dependency is null)
                {
                    continue;
                }

                stack.Push(dependency);
            }
        }
    }
}
