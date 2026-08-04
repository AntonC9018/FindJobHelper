using System.Collections.Immutable;

namespace FindJobHelper.Core.Tests;

public sealed class SelectionDebugReportTests
{
    [Fact]
    public void MarkdownTableCell_UsesSpanFormattingForLineBreaksAndPipes()
    {
        var cell = new SelectionDebugReport.MarkdownTableCell(
            "first\r\nsecond\nthird|value");
        Span<char> tooSmall = stackalloc char[1];

        var formatted = cell.TryFormat(
            tooSmall,
            out var charsWritten,
            format: default,
            provider: null);

        Assert.False(formatted);
        Assert.Equal(0, charsWritten);
        Assert.Equal("first<br>second<br>third\\|value", $"{cell}");
    }

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

                var encounteredNonFront = false;
                foreach (var item in selected)
                {
                    if (item.Order.Move == ItemMove.ToFront)
                    {
                        Assert.False(
                            encounteredNonFront,
                            $"Front-prefix violation in {run.Scenario}/{run.Preset}/{eventGroup.Key.Event.Title}: {item.Text} appeared after an ordinary item.");
                    }
                    else
                    {
                        encounteredNonFront = true;
                    }
                }

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

                    foreach (var predecessor in TransitiveOrderingPredecessors(trace.Item))
                    {
                        if (!selectedIndexes.TryGetValue(predecessor, out var predecessorIndex))
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

    private static IEnumerable<ExperienceListItem> TransitiveOrderingPredecessors(
        ExperienceListItem item)
    {
        var seen = new HashSet<ExperienceListItem>();
        var stack = new Stack<ExperienceListItem>();
        Push(item);

        while (stack.Count > 0)
        {
            var predecessor = stack.Pop();
            if (!seen.Add(predecessor))
            {
                continue;
            }

            yield return predecessor;
            Push(predecessor);
        }

        void Push(ExperienceListItem parent)
        {
            foreach (var predecessor in parent.DependsOn.Concat(parent.Order.After))
            {
                if (predecessor is not null)
                {
                    stack.Push(predecessor);
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
