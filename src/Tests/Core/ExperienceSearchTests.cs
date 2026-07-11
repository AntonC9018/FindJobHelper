using System.Collections.Immutable;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

#pragma warning disable CS0618 // Legacy budget alias compatibility coverage.

public sealed class ExperienceSearchTests
{
    private static readonly ExperienceKey WorkKey = new("Work");
    private static readonly ExperienceKey ProjectKey = new("Project");

    [Fact]
    public void Search_UsesSharedMmrAcrossPredicates()
    {
        var tagA = new Tag("a");
        var tagB = new Tag("b");

        var builder = NewBuilder(new()
        {
            [tagA] = 1,
            [tagB] = 1,
        });
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0.9f,
            SaturationQuota: 1,
            SaturationPenalty: 0.5f));
        builder.ConfigureDefaults(opts =>
        {
            opts.TotalItemBudget = 1;
            opts.ScoreLowerBound = 0;
        });
        builder.Configure(WorkKey, e => e.Type == ExperienceType.Job);
        builder.Configure(ProjectKey, e => e.Type == ExperienceType.Project);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("work-a", (tagA, 10))),
            Experience(
                "project",
                ExperienceType.Project,
                2024,
                Item("project-a", (tagA, 9)),
                Item("project-b", (tagB, 5))),
        ]);

        Assert.Equal(new[] { "work-a" }, Texts(result.Get(WorkKey)));
        Assert.Equal(new[] { "project-b" }, Texts(result.Get(ProjectKey)));
    }

    [Fact]
    public void Search_AppliesBudgetsPerPredicate()
    {
        var tagA = new Tag("a");
        var tagB = new Tag("b");
        var tagC = new Tag("c");

        var builder = NewBuilder(new()
        {
            [tagA] = 1,
            [tagB] = 1,
            [tagC] = 1,
        });
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 1,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.ConfigureDefaults(opts =>
        {
            opts.TotalItemBudget = 1;
            opts.ScoreLowerBound = 0;
        });
        builder.Configure(WorkKey, e => e.Type == ExperienceType.Job);
        builder.Configure(
            ProjectKey,
            e => e.Type == ExperienceType.Project,
            opts => opts.TotalItemBudget = 2);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("work-a", (tagA, 10))),
            Experience(
                "project",
                ExperienceType.Project,
                2024,
                Item("project-b", (tagB, 9)),
                Item("project-c", (tagC, 8))),
        ]);

        Assert.Equal(new[] { "work-a" }, Texts(result.Get(WorkKey)));
        Assert.Equal(
            new[] { "project-b", "project-c" },
            Texts(result.Get(ProjectKey)));
    }

    [Fact]
    public void Search_AppliesMaximumBudgetOption()
    {
        var tagA = new Tag("a");
        var tagB = new Tag("b");
        var builder = NewBuilder(new()
        {
            [tagA] = 1,
            [tagB] = 1,
        });
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 1,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.MinTotalItemBudget = 0;
                opts.MaxTotalItemBudget = 2;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("first", (tagA, 10)),
                Item("second", (tagB, 9))),
        ]);

        Assert.Equal(new[] { "first", "second" }, Texts(result.Get(WorkKey)));
        var budget = Assert.Single(result.Diagnostics.Budgets);
        Assert.Equal(0, budget.RequestedMinimum);
        Assert.Equal(2, budget.RequestedMaximum);
        Assert.Equal(0, budget.RemainingMaximumBudget);
    }

    [Fact]
    public void Search_PreservesOneSeedPerListWhenMmrScoreIsZero()
    {
        var tagA = new Tag("a");
        var tagB = new Tag("b");
        var builder = NewBuilder(new()
        {
            [tagA] = 1,
            [tagB] = 1,
        });
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.MaxTotalItemBudget = 2);

        var result = builder.Build().Run([
            Experience("first", ExperienceType.Job, 2025, Item("first", (tagA, 10))),
            Experience("second", ExperienceType.Job, 2024, Item("second", (tagB, 9))),
        ]);

        Assert.Equal(new[] { "first", "second" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_SkipsNonSeedZeroScoreCandidatesAfterMinimumIsMet()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.MaxTotalItemBudget = 2);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("seed", (tag, 10)),
                Item("skipped", (tag, 9))),
        ]);

        Assert.Equal(new[] { "seed" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_UsesZeroScoreCandidateToMeetMinimum()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.MinTotalItemBudget = 2;
                opts.MaxTotalItemBudget = 2;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("seed", (tag, 10)),
                Item("minimum", (tag, 9))),
        ]);

        Assert.Equal(new[] { "seed", "minimum" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_DoesNotUseLowerBoundRejectedCandidateToMeetMinimum()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.MinTotalItemBudget = 2;
                opts.MaxTotalItemBudget = 2;
                opts.ScoreLowerBound = 5;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("seed", (tag, 10)),
                Item("rejected", (tag, 4))),
        ]);

        Assert.Equal(new[] { "seed" }, Texts(result.Get(WorkKey)));
        var budget = Assert.Single(result.Diagnostics.Budgets);
        Assert.Equal(2, budget.RequestedMinimum);
        Assert.Equal(1, budget.ActualCount);
    }

    [Fact]
    public void Search_RejectsMinimumBudgetAboveMaximum()
    {
        var builder = NewBuilder(new()
        {
            [new("a")] = 1,
        });
        builder.Configure(
            WorkKey,
            _ => true,
            opts =>
            {
                opts.MinTotalItemBudget = 2;
                opts.MaxTotalItemBudget = 1;
            });

        var exception = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.Contains("must not exceed", exception.Message);
    }

    [Fact]
    public void SelectEvents_UsesNewSearchParameterBudgets()
    {
        var tag = new Tag("a");
        var tags = new WeightedTags
        {
            [tag] = 1,
        };
        var parameters = new SearchParams(
            Tags: tags,
            MinTotalItemBudget: 2,
            MaxTotalItemBudget: 2,
            ScoreLowerBound: 0)
        {
            Mmr = new(
                RelevanceWeight: 0,
                SaturationQuota: 1,
                SaturationPenalty: 0),
        };

        var result = ExperienceSelectionEngine.SelectEvents([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("seed", (tag, 10)),
                Item("minimum", (tag, 9))),
        ], parameters);

        Assert.Equal(new[] { "seed", "minimum" }, Texts(result));
    }

    [Fact]
    public void Search_AppliesLowerBoundPerPredicate()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.ScoreLowerBound = 5;
            });
        builder.Configure(
            ProjectKey,
            e => e.Type == ExperienceType.Project,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.ScoreLowerBound = 6;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("work-a", (tag, 5))),
            Experience(
                "project",
                ExperienceType.Project,
                2024,
                Item("project-a", (tag, 5))),
        ]);

        Assert.Equal(new[] { "work-a" }, Texts(result.Get(WorkKey)));
        Assert.Empty(result.Get(ProjectKey));
    }

    [Fact]
    public void Search_ReportsMatchedDebugTagsAndTotals()
    {
        var tagA = new Tag("a");
        var tagB = new Tag("b");
        var unmatched = new Tag("unmatched");
        var builder = NewBuilder(new()
        {
            [tagA] = 0.5f,
            [tagB] = 2f,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.ScoreLowerBound = 0;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item(
                    "work-a",
                    (tagA, 8),
                    (tagB, 3),
                    (unmatched, 10))),
        ]);

        var @event = Assert.Single(result.Get(WorkKey));
        var subItem = Assert.Single(@event.SubItems);
        var eventDebugTags = @event.DebugTagScores
            .Select(x => (Tag: x.Tag.Value, x.Score))
            .ToArray();
        var debugTags = subItem.DebugTagScores
            .Select(x => (Tag: x.Tag.Value, x.Score))
            .ToArray();

        Assert.Equal(10f, @event.DebugScore);
        Assert.Equal(10f, subItem.DebugScore);
        Assert.Equal(
            new[] { (Tag: "b", Score: 6f), (Tag: "a", Score: 4f) },
            eventDebugTags);
        Assert.Equal(
            new[] { (Tag: "b", Score: 6f), (Tag: "a", Score: 4f) },
            debugTags);
    }

    [Fact]
    public void Search_DebugScoreReflectsMmrPenalties()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0.9f,
            SaturationQuota: 1,
            SaturationPenalty: 0.5f));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 2;
                opts.ScoreLowerBound = 0;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("first", (tag, 10)),
                Item("second", (tag, 9))),
        ]);

        var subItems = Assert.Single(result.Get(WorkKey)).SubItems;
        Assert.Equal(2, subItems.Length);
        Assert.Equal(10f, subItems[0].DebugScore);
        Assert.InRange(subItems[1].DebugScore, 2.33f, 2.34f);
        Assert.InRange(
            Assert.Single(subItems[1].DebugTagScores).Score,
            2.33f,
            2.34f);
    }

    [Fact]
    public void Search_KnownKeyWithNoCandidatesReturnsEmpty()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            ProjectKey,
            e => e.Type == ExperienceType.Project,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.ScoreLowerBound = 0;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("work-a", (tag, 10))),
        ]);

        Assert.Empty(result.Get(ProjectKey));
    }

    [Fact]
    public void Search_UnknownKeyThrows()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            ProjectKey,
            e => e.Type == ExperienceType.Project,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([]);

        Assert.Throws<KeyNotFoundException>(() => result.Get(new("Missing")));
    }

    [Fact]
    public void Search_DuplicateKeyThrows()
    {
        var builder = NewBuilder(new()
        {
            [new("a")] = 1,
        });
        builder.Configure(WorkKey, _ => true);
        builder.Configure(WorkKey, _ => false);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Search_MultiplePredicateMatchThrows()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            WorkKey,
            _ => true,
            opts => opts.TotalItemBudget = 1);
        builder.Configure(
            ProjectKey,
            _ => true,
            opts => opts.TotalItemBudget = 1);

        var search = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => search.Run([
            Experience(
                "ambiguous",
                ExperienceType.Job,
                2025,
                Item("work-a", (tag, 10))),
        ]));

        Assert.Contains(WorkKey.Value, exception.Message);
        Assert.Contains(ProjectKey.Value, exception.Message);
    }

    [Fact]
    public void Search_SelectedDependentIncludesUnmatchedDependency()
    {
        var tag = new Tag("a");
        var dependency = Item("dependency");
        var dependent = ItemAfter("dependent", [dependency], (tag, 10));
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                dependent,
                dependency),
        ]);

        Assert.Equal(new[] { "dependency", "dependent" }, Texts(result.Get(WorkKey)));
        Assert.Equal(
            new[]
            {
                SelectionItemReason.Dependency,
                SelectionItemReason.Direct,
            },
            result.Diagnostics.Items.Select(x => x.Reason).ToArray());
    }

    [Fact]
    public void Search_DependencyAppearsBeforeDependent()
    {
        var tag = new Tag("a");
        var dependency = Item("dependency", (tag, 1));
        var dependent = ItemAfter("dependent", [dependency], (tag, 10));
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                dependent,
                dependency),
        ]);

        Assert.Equal(new[] { "dependency", "dependent" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_DependencyClosureCanExceedBudget()
    {
        var tag = new Tag("a");
        var dependency = Item("dependency");
        var dependent = ItemAfter("dependent", [dependency], (tag, 10));
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                dependent,
                dependency),
        ]);

        var budget = Assert.Single(result.Diagnostics.Budgets);
        Assert.Equal(0, budget.RequestedMinimum);
        Assert.Equal(1, budget.RequestedMaximum);
        Assert.Equal(2, budget.ActualCount);
        Assert.Equal(-1, budget.RemainingMaximumBudget);
    }

    [Fact]
    public void Search_EnabledGroupAlwaysIncludesEveryThesisTenItem()
    {
        var matchingTag = new Tag("matching");
        var thesisTag = new Tag("thesis");
        var otherTag = new Tag("other");
        var builder = NewBuilder(new()
        {
            [matchingTag] = 1,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "master",
                ExperienceType.Job,
                2025,
                Item("selected", (matchingTag, 10)),
                Item("master thesis", (thesisTag, 10)),
                Item("lower-scored thesis", (thesisTag, 9)),
                Item("different tag", (otherTag, 10))),
            Experience(
                "bachelor",
                ExperienceType.Job,
                2024,
                Item("bachelor thesis", (thesisTag, 10))),
        ]);

        var events = result.Get(WorkKey);
        Assert.Equal(new[] { "master", "bachelor" }, events.Select(x => x.Title.ToString()));
        Assert.Equal(new[] { "master thesis", "bachelor thesis" }, Texts(events));
        Assert.Equal(
            new[]
            {
                SelectionItemReason.Direct,
                SelectionItemReason.Direct,
            },
            result.Diagnostics.Items.Select(x => x.Reason).ToArray());

        var budget = Assert.Single(result.Diagnostics.Budgets);
        Assert.Equal(1, budget.RequestedMaximum);
        Assert.Equal(2, budget.ActualCount);
        Assert.Equal(-1, budget.RemainingMaximumBudget);
    }

    [Fact]
    public void Search_ZeroBudgetGroupDoesNotIncludeThesisItems()
    {
        var matchingTag = new Tag("matching");
        var thesisTag = new Tag("thesis");
        var builder = NewBuilder(new()
        {
            [matchingTag] = 1,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 0);

        var result = builder.Build().Run([
            Experience(
                "disabled",
                ExperienceType.Job,
                2025,
                Item("thesis", (thesisTag, 10))),
        ]);

        Assert.Empty(result.Get(WorkKey));
    }

    [Fact]
    public void Search_DependencyCycleThrows()
    {
        var tag = new Tag("a");
        var first = Item("first", (tag, 10));
        var second = ItemAfter("second", [first], (tag, 9));
        SetDependencies(first, second);
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 2);

        var search = builder.Build();

        var exception = Assert.Throws<InvalidOperationException>(() => search.Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                first,
                second),
        ]));
        Assert.Contains("Cycle detected in MustBeAfter", exception.Message);
    }

    [Fact]
    public void Search_SharedDependenciesAreIncludedOnce()
    {
        var tag = new Tag("a");
        var dependency = Item("dependency");
        var first = ItemAfter("first", [dependency], (tag, 10));
        var second = ItemAfter("second", [dependency], (tag, 9));
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 3);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                first,
                second,
                dependency),
        ]);

        Assert.Equal(
            new[] { "dependency", "first", "second" },
            Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_LowerBoundDoesNotRemoveRequiredDependencies()
    {
        var tag = new Tag("a");
        var dependency = Item("dependency", (tag, 1));
        var dependent = ItemAfter("dependent", [dependency], (tag, 10));
        var builder = NewBuilder(new()
        {
            [tag] = 1,
        });
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.ScoreLowerBound = 5;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                dependency,
                dependent),
        ]);

        Assert.Equal(new[] { "dependency", "dependent" }, Texts(result.Get(WorkKey)));
    }

    private static SearchBuilder NewBuilder(WeightedTags tags)
    {
        var builder = new SearchBuilder();
        builder.Tags(tags);
        return builder;
    }

    private static string[] Texts(ImmutableArray<Event> events)
    {
        return events
            .SelectMany(e => e.SubItems)
            .Select(x => x.String.ToString())
            .ToArray();
    }

    private static ExperienceList Experience(
        string title,
        ExperienceType type,
        int year,
        params ExperienceListItem[] items)
    {
        return new()
        {
            Title = title,
            Place = new("test"),
            DateRange = DateRange.Completed(new(Year: year), new(Year: year + 1)),
            Items = items.ToImmutableArray(),
            Type = type,
        };
    }

    private static ExperienceListItem Item(
        string text,
        params (Tag Tag, int Score)[] tags)
    {
        return ItemAfter(text, [], tags);
    }

    private static ExperienceListItem ItemAfter(
        string text,
        ExperienceListItem[] dependencies,
        params (Tag Tag, int Score)[] tags)
    {
        return new()
        {
            Text = RichText.Create($"{new PlainText { Text = text }}"),
            Tags = tags
                .Select(x => new TagReference(x.Tag, x.Score))
                .ToImmutableArray(),
            MustBeAfter = dependencies.ToImmutableArray(),
        };
    }

    private static void SetDependencies(
        ExperienceListItem item,
        params ExperienceListItem[] dependencies)
    {
        typeof(ExperienceListItem)
            .GetProperty(nameof(ExperienceListItem.MustBeAfter))!
            .SetValue(item, dependencies.ToImmutableArray());
    }
}
