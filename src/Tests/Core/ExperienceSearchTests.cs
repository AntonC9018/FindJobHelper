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
    public void Search_DefaultsMissingBudgetsToUnboundedRange()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(WorkKey, _ => true);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("first", (tag, 10)),
                Item("second", (tag, 9))),
        ]);
        var budget = Assert.Single(result.Diagnostics.Budgets);

        Assert.Equal(new[] { "first", "second" }, Texts(result.Get(WorkKey)));
        Assert.Equal(0, budget.RequestedMinimum);
        Assert.Equal(int.MaxValue, budget.RequestedMaximum);
    }

    [Fact]
    public void Search_UsesSharedMmrAcrossPredicates()
    {
        var tagA = new Tag("a");
        var tagB = new Tag("b");

        var builder = NewBuilder(WeightedTags.Create([
            (tagA, 1),
            (tagB, 1),
        ]));
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

        var builder = NewBuilder(WeightedTags.Create([
            (tagA, 1),
            (tagB, 1),
            (tagC, 1),
        ]));
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
    public void Search_AppliesTotalItemBudgetOption()
    {
        var tagA = new Tag("a");
        var tagB = new Tag("b");
        var builder = NewBuilder(WeightedTags.Create([
            (tagA, 1),
            (tagB, 1),
        ]));
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
                opts.TotalItemBudget = 2;
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
        var builder = NewBuilder(WeightedTags.Create([
            (tagA, 1),
            (tagB, 1),
        ]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 2);

        var result = builder.Build().Run([
            Experience("first", ExperienceType.Job, 2025, Item("first", (tagA, 10))),
            Experience("second", ExperienceType.Job, 2024, Item("second", (tagB, 9))),
        ]);

        Assert.Equal(new[] { "first", "second" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_CanRankItemsGloballyWithoutPreservingEveryList()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 1,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            ProjectKey,
            e => e.Type == ExperienceType.Project,
            opts =>
            {
                opts.TotalItemBudget = 2;
                opts.PreserveOneItemPerList = false;
            });

        var result = builder.Build().Run([
            Experience(
                "strong",
                ExperienceType.Project,
                2025,
                Item("strongest", (tag, 10)),
                Item("second strongest", (tag, 9))),
            Experience(
                "weak",
                ExperienceType.Project,
                2024,
                Item("weak", (tag, 1))),
        ]);

        Assert.Equal(
            new[] { "strongest", "second strongest" },
            Texts(result.Get(ProjectKey)));
    }

    [Fact]
    public void Search_SkipsNonSeedZeroScoreCandidatesAfterMinimumIsMet()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 2);

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
    public void Search_UnconstrainedRunStopsAtNonPositiveMmrScore()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0.9f,
            SaturationQuota: 1,
            SaturationPenalty: 1));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("seed", (tag, 10)),
                Item("negative", (tag, 9))),
        ]);

        Assert.Equal(new[] { "seed" }, Texts(result.Get(WorkKey)));
        var budget = Assert.Single(result.Diagnostics.Budgets);
        Assert.Equal(int.MaxValue, budget.RequestedMaximum);
        Assert.Equal(1, budget.ActualCount);
    }

    [Fact]
    public void Search_UsesZeroScoreCandidateToMeetMinimum()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
                opts.TotalItemBudget = 2;
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
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
                opts.TotalItemBudget = 2;
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
    public void Search_RejectsMinimumBudgetAboveTotal()
    {
        var builder = NewBuilder(WeightedTags.Create([
            (new Tag("a"), 1),
        ]));
        builder.Configure(
            WorkKey,
            _ => true,
            opts =>
            {
                opts.MinTotalItemBudget = 2;
                opts.TotalItemBudget = 1;
            });

        var exception = Assert.Throws<ArgumentException>(() => builder.Build());

        Assert.Contains("must not exceed", exception.Message);
    }

    [Fact]
    public void SearchBuilder_UsesConfiguredBudgets()
    {
        var tag = new Tag("a");
        var tags = WeightedTags.Create([(tag, 1)]);
        var builder = NewBuilder(tags);
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            static _ => true,
            options =>
            {
                options.MinTotalItemBudget = 2;
                options.TotalItemBudget = 2;
                options.ScoreLowerBound = 0;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("seed", (tag, 10)),
                Item("minimum", (tag, 9))),
        ]).Get(WorkKey);

        Assert.Equal(new[] { "seed", "minimum" }, Texts(result));
    }

    [Fact]
    public void SearchBuilder_DefaultsMissingBudgetsToUnboundedRange()
    {
        var tag = new Tag("a");
        var tags = WeightedTags.Create([(tag, 1)]);
        var builder = NewBuilder(tags);
        builder.Configure(WorkKey, static _ => true);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("first", (tag, 10)),
                Item("second", (tag, 9))),
        ]);
        var budget = Assert.Single(result.Diagnostics.Budgets);

        Assert.Equal(new[] { "first", "second" }, Texts(result.Get(WorkKey)));
        Assert.Equal(0, budget.RequestedMinimum);
        Assert.Equal(int.MaxValue, budget.RequestedMaximum);
    }

    [Fact]
    public void Search_AppliesLowerBoundPerPredicate()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
        var builder = NewBuilder(WeightedTags.Create([
            (tagA, 0.5f),
            (tagB, 2f),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.ScoreLowerBound = 0;
            });

        var sourceItem = Item(
            "work-a",
            (tagA, 8),
            (tagB, 3),
            (unmatched, 10));
        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                sourceItem),
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
        Assert.Same(sourceItem.Text, subItem.Text);
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
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
        Assert.Equal(9, Assert.Single(subItems[1].DebugTagScores).Score);
        Assert.Equal(
            9,
            Assert.Single(subItems[1].DebugRequirementCoverage).Score);
        var breakdown = Assert.IsType<MmrScoreBreakdown>(
            subItems[1].DebugMmrScoreBreakdown);
        Assert.InRange(
            breakdown.NormalizedMmrScore,
            0.209f,
            0.211f);
    }

    [Fact]
    public void Search_RecencyBoostFavorsNewerRemainingCandidate()
    {
        var tag = new Tag("a");
        var newer = Experience(
            "newer",
            ExperienceType.Job,
            2024,
            Item("newer-seed", (tag, 10)),
            Item("newer-extra", (tag, 8)));
        var older = Experience(
            "older",
            ExperienceType.Job,
            2019,
            Item("older-seed", (tag, 10)),
            Item("older-extra", (tag, 9)));

        var withoutBoost = Run(recencyBoost: 0);
        var withBoost = Run(recencyBoost: 0.5f);

        Assert.Equal(
            new[] { "newer-seed", "older-seed", "older-extra" },
            Texts(withoutBoost.Get(WorkKey)));
        Assert.Equal(
            new[] { "newer-seed", "newer-extra", "older-seed" },
            Texts(withBoost.Get(WorkKey)));

        SearchResult Run(float recencyBoost)
        {
            var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
            builder.Mmr(new MmrOptions(
                RelevanceWeight: 1,
                SaturationQuota: 1,
                SaturationPenalty: 0));
            builder.Configure(
                WorkKey,
                e => e.Type == ExperienceType.Job,
                options =>
                {
                    options.TotalItemBudget = 3;
                    options.RecencyBoost = recencyBoost;
                });
            return builder.Build().Run([older, newer]);
        }
    }

    [Fact]
    public void Search_RecencyBoostInterpolatesLinearlyBetweenSectionDates()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 1,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            options =>
            {
                options.TotalItemBudget = 3;
                options.RecencyBoost = 0.5f;
            });

        var result = builder.Build().Run([
            Experience("oldest", ExperienceType.Job, 2019, Item("oldest", (tag, 10))),
            Experience("middle", ExperienceType.Job, 2024, Item("middle", (tag, 10))),
            Experience("newest", ExperienceType.Job, 2029, Item("newest", (tag, 10))),
        ]);

        var oldest = Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "oldest");
        var middle = Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "middle");
        var newest = Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "newest");
        Assert.Equal(10, oldest.DebugScore);
        Assert.InRange(middle.DebugScore, 12.5f, 12.501f);
        Assert.Equal(15, newest.DebugScore);
        Assert.Equal(10, Assert.Single(newest.DebugTagScores).Score);
    }

    [Fact]
    public void Search_RecencyBoostDoesNothingWithoutDateSpread()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 1,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            options =>
            {
                options.TotalItemBudget = 2;
                options.RecencyBoost = 10;
            });

        var result = builder.Build().Run([
            Experience("first", ExperienceType.Job, 2024, Item("first", (tag, 10))),
            Experience("second", ExperienceType.Job, 2024, Item("second", (tag, 10))),
        ]);

        Assert.All(result.Diagnostics.Items, item => Assert.Equal(10, item.DebugScore));
    }

    [Fact]
    public void Search_RecencyBoostDoesNotAffectScoreLowerBound()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            options =>
            {
                options.TotalItemBudget = 1;
                options.ScoreLowerBound = 5;
                options.RecencyBoost = 10;
            });

        var result = builder.Build().Run([
            Experience("older", ExperienceType.Job, 2019, Item("eligible", (tag, 5))),
            Experience("newer", ExperienceType.Job, 2024, Item("below threshold", (tag, 4))),
        ]);

        Assert.Equal(new[] { "eligible" }, Texts(result.Get(WorkKey)));
        Assert.Equal(5, Assert.Single(result.Diagnostics.Items).DebugScore);
    }

    [Fact]
    public void Search_RecencyBoostUsesIndependentSectionDateRanges()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 1,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            options =>
            {
                options.TotalItemBudget = 2;
                options.RecencyBoost = 0.5f;
            });
        builder.Configure(
            ProjectKey,
            e => e.Type == ExperienceType.Project,
            options =>
            {
                options.TotalItemBudget = 2;
                options.RecencyBoost = 1;
            });

        var result = builder.Build().Run([
            Experience("old work", ExperienceType.Job, 1999, Item("old work", (tag, 10))),
            Experience("new work", ExperienceType.Job, 2009, Item("new work", (tag, 10))),
            Experience("old project", ExperienceType.Project, 2019, Item("old project", (tag, 10))),
            Experience("new project", ExperienceType.Project, 2020, Item("new project", (tag, 10))),
        ]);

        Assert.Equal(
            10,
            Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "old work").DebugScore);
        Assert.Equal(
            15,
            Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "new work").DebugScore);
        Assert.Equal(
            10,
            Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "old project").DebugScore);
        Assert.Equal(
            20,
            Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "new project").DebugScore);
    }

    [Fact]
    public void Search_RecencyBoostTreatsOngoingExperienceAsEndingToday()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 1,
            SaturationQuota: 1,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            options =>
            {
                options.TotalItemBudget = 2;
                options.RecencyBoost = 0.5f;
            });
        var search = builder.Build();

        var result = search.Run(
            [
                Experience(
                    "completed",
                    ExperienceType.Job,
                    DateRange.Completed(new(2019), new(2020)),
                    Item("completed", (tag, 10))),
                Experience(
                    "ongoing",
                    ExperienceType.Job,
                    DateRange.Ongoing(new(2024, 6)),
                    Item("ongoing", (tag, 10))),
            ],
            new DateOnly(2025, 7, 1));

        Assert.Equal(
            10,
            Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "completed").DebugScore);
        Assert.Equal(
            15,
            Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "ongoing").DebugScore);
    }

    [Fact]
    public void Search_RecencyBoostOnlyScalesMmrRelevance()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0.9f,
            SaturationQuota: 1,
            SaturationPenalty: 0.2f));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            options =>
            {
                options.TotalItemBudget = 2;
                options.RecencyBoost = 0.5f;
            });

        var result = builder.Build().Run([
            Experience("older", ExperienceType.Job, 2019, Item("older", (tag, 10))),
            Experience("newer", ExperienceType.Job, 2024, Item("newer", (tag, 10))),
        ]);

        Assert.Equal(
            15,
            Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "newer").DebugScore);
        Assert.InRange(
            Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "older").DebugScore,
            4.999f,
            5.001f);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Search_RejectsInvalidRecencyBoost(float recencyBoost)
    {
        var builder = NewBuilder(WeightedTags.Empty);
        builder.Configure(
            WorkKey,
            _ => true,
            options => options.RecencyBoost = recencyBoost);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Build());

        Assert.Equal("RecencyBoost", exception.ParamName);
    }

    [Fact]
    public void Search_IncludeEmptyListsRetainsHeadingsAndSuppressesEmptyBodyMetadata()
    {
        var matching = new Tag("matching");
        var unrelated = new Tag("unrelated");
        var builder = NewBuilder(WeightedTags.Create([
            (matching, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.ScoreLowerBound = 5;
                opts.IncludeEmptyLists = true;
            });
        builder.Configure(
            ProjectKey,
            e => e.Type == ExperienceType.Project,
            opts => opts.TotalItemBudget = 1);

        var newestUnmatchedJob = ExperienceWithMetadata(
            "newest unmatched job",
            ExperienceType.Job,
            2025,
            Item("unmatched", (unrelated, 10)));
        var olderMatchedJob = ExperienceWithMetadata(
            "older matched job",
            ExperienceType.Job,
            2024,
            Item("matched", (matching, 10)));
        var unmatchedProject = ExperienceWithMetadata(
            "unmatched project",
            ExperienceType.Project,
            2026,
            Item("project", (unrelated, 10)));

        var result = builder.Build().Run([
            olderMatchedJob,
            unmatchedProject,
            newestUnmatchedJob,
        ]);

        var work = result.Get(WorkKey);
        Assert.Equal(
            new[] { "newest unmatched job", "older matched job" },
            work.Select(static item => item.Title.Value));
        Assert.Empty(work[0].SubItems);
        Assert.Null(work[0].Text);
        Assert.Empty(work[0].Urls);
        Assert.Equal(new[] { "matched" }, Texts([work[1]]));
        Assert.NotNull(work[1].Text);
        Assert.Equal(
            new[] { "https://example.test/experience" },
            work[1].Urls.Select(static url => url.Value));
        Assert.Empty(result.Get(ProjectKey));

        var workBudget = Assert.Single(result.Diagnostics.Budgets.Where(x => x.Section == WorkKey));
        Assert.Equal(1, workBudget.ActualCount);
        Assert.Equal(0, workBudget.RemainingMaximumBudget);
        Assert.Single(result.Diagnostics.Items);
    }

    [Fact]
    public void Search_IncludeEmptyListsStillEmitsHeadingsAtZeroItemBudget()
    {
        var tag = new Tag("matching");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 0;
                opts.IncludeEmptyLists = true;
            });

        var result = builder.Build().Run([
            Experience("work", ExperienceType.Job, 2025, Item("point", (tag, 10))),
        ]);

        var work = Assert.Single(result.Get(WorkKey));
        Assert.Empty(work.SubItems);
        var budget = Assert.Single(result.Diagnostics.Budgets);
        Assert.Equal(0, budget.ActualCount);
        Assert.Equal(0, budget.RemainingMaximumBudget);
    }

    [Fact]
    public void Search_KnownKeyWithNoCandidatesReturnsEmpty()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
        var builder = NewBuilder(WeightedTags.Create([
            (new Tag("a"), 1),
        ]));
        builder.Configure(WorkKey, _ => true);
        builder.Configure(WorkKey, _ => false);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Search_MultiplePredicateMatchThrows()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
        var dependent = ItemDependingOn("dependent", [dependency], (tag, 10));
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.RecencyBoost = 0.5f;
            });

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
        var dependent = ItemDependingOn("dependent", [dependency], (tag, 10));
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
        var dependent = ItemDependingOn("dependent", [dependency], (tag, 10));
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
    public void Search_IfAnyIsScopedToTheSameExperience()
    {
        var tag = new Tag("match");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "matched",
                ExperienceType.Job,
                2025,
                Item("selected", (tag, 10)),
                RequiredItem("same-list conditional", ItemRequirement.IfAny)),
            Experience(
                "unmatched",
                ExperienceType.Job,
                2024,
                RequiredItem("other-list conditional", ItemRequirement.IfAny)),
        ]);

        var events = result.Get(WorkKey);
        Assert.Equal("matched", Assert.Single(events).Title.Value);
        Assert.Equal(
            new[] { "selected", "same-list conditional" },
            Texts(events));
    }

    [Fact]
    public void Search_EmptyHeadingDoesNotTriggerOnlyIfAnyItem()
    {
        var tag = new Tag("match");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 1;
                opts.IncludeEmptyLists = true;
            });

        var result = builder.Build().Run([
            Experience(
                "heading only",
                ExperienceType.Job,
                2025,
                RequiredItem("conditional", ItemRequirement.IfAny)),
        ]);

        Assert.Empty(Assert.Single(result.Get(WorkKey)).SubItems);
        Assert.Empty(result.Diagnostics.Items);
    }

    [Fact]
    public void Search_SelectingSoleIfAnyItemNormallyIsAllowed()
    {
        var tag = new Tag("match");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                RequiredItem(
                    "conditional match",
                    ItemRequirement.IfAny,
                    (tag, 10))),
        ]);

        Assert.Equal(new[] { "conditional match" }, Texts(result.Get(WorkKey)));
        Assert.Equal(
            SelectionItemReason.Direct,
            Assert.Single(result.Diagnostics.Items).Reason);
    }

    [Fact]
    public void Search_SelectingOneIfAnyItemTriggersOtherIfAnySiblings()
    {
        var tag = new Tag("match");
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                RequiredItem(
                    "matched conditional",
                    ItemRequirement.IfAny,
                    (tag, 10)),
                RequiredItem(
                    "unmatched conditional",
                    ItemRequirement.IfAny)),
        ]);

        Assert.Equal(
            new[] { "matched conditional", "unmatched conditional" },
            Texts(result.Get(WorkKey)));
        Assert.Equal(
            new[]
            {
                SelectionItemReason.Direct,
                SelectionItemReason.RequiredIfAny,
            },
            result.Diagnostics.Items.Select(trace => trace.Reason).ToArray());
    }

    [Fact]
    public void Search_IfAnyClosureIncludesMultipleItemsAndDependenciesOnce()
    {
        var tag = new Tag("match");
        var dependency = Item("shared dependency");
        var first = RequiredItemDependingOn(
            "first conditional",
            ItemRequirement.IfAny,
            [dependency],
            (tag, 1));
        var second = RequiredItemDependingOn(
            "second conditional",
            ItemRequirement.IfAny,
            [dependency]);
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
                first,
                second,
                Item("selected", (tag, 10))),
        ]);

        var texts = Texts(result.Get(WorkKey));
        Assert.Equal(4, texts.Length);
        Assert.Equal(4, texts.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("selected", texts);
        Assert.Contains("shared dependency", texts);
        Assert.Contains("first conditional", texts);
        Assert.Contains("second conditional", texts);

        var reasons = result.Diagnostics.Items.ToDictionary(
            trace => trace.Item.Text.ToString()!,
            trace => trace.Reason);
        Assert.Equal(SelectionItemReason.Direct, reasons["selected"]);
        Assert.Equal(SelectionItemReason.Dependency, reasons["shared dependency"]);
        Assert.Equal(SelectionItemReason.RequiredIfAny, reasons["first conditional"]);
        Assert.Equal(SelectionItemReason.RequiredIfAny, reasons["second conditional"]);

        var budget = Assert.Single(result.Diagnostics.Budgets);
        Assert.Equal(1, budget.RequestedMaximum);
        Assert.Equal(4, budget.ActualCount);
        Assert.Equal(-3, budget.RemainingMaximumBudget);
    }

    [Fact]
    public void Search_AlwaysWorksWithoutTagMatchAndTriggersIfAny()
    {
        var queryTag = new Tag("query");
        var builder = NewBuilder(WeightedTags.Create([
            (queryTag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                RequiredItem("always", ItemRequirement.Always),
                RequiredItem("conditional", ItemRequirement.IfAny)),
        ]);

        Assert.Equal(
            new[] { "always", "conditional" },
            Texts(result.Get(WorkKey)));
        Assert.Equal(
            new[]
            {
                SelectionItemReason.RequiredAlways,
                SelectionItemReason.RequiredIfAny,
            },
            result.Diagnostics.Items.Select(x => x.Reason).ToArray());

        var budget = Assert.Single(result.Diagnostics.Budgets);
        Assert.Equal(1, budget.RequestedMaximum);
        Assert.Equal(2, budget.ActualCount);
        Assert.Equal(-1, budget.RemainingMaximumBudget);
    }

    [Fact]
    public void Search_ZeroBudgetGroupSuppressesAlwaysItems()
    {
        var matchingTag = new Tag("matching");
        var builder = NewBuilder(WeightedTags.Create([
            (matchingTag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 0);

        var result = builder.Build().Run([
            Experience(
                "disabled",
                ExperienceType.Job,
                2025,
                RequiredItem("always", ItemRequirement.Always)),
        ]);

        Assert.Empty(result.Get(WorkKey));
    }

    [Fact]
    public void Search_ThesisTagHasNoImplicitRequirementBehavior()
    {
        var matchingTag = new Tag("matching");
        var thesisTag = new Tag("Thesis");
        var builder = NewBuilder(WeightedTags.Create([
            (matchingTag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("selected", (matchingTag, 10)),
                Item("ordinary thesis tag", (thesisTag, 10))),
        ]);

        Assert.Equal(new[] { "selected" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_RequiredContentIsRegisteredWithMmr()
    {
        var repeatedTag = new Tag("repeated");
        var diverseTag = new Tag("diverse");
        var builder = NewBuilder(WeightedTags.Create([
            (repeatedTag, 1),
            (diverseTag, 1),
        ]));
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 0.9f,
            SaturationQuota: 1,
            SaturationPenalty: 0.5f));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 2);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                RequiredItem(
                    "always",
                    ItemRequirement.Always,
                    (repeatedTag, 10)),
                Item("redundant", (repeatedTag, 9)),
                Item("diverse", (diverseTag, 5))),
        ]);

        Assert.Equal(
            new[] { "always", "diverse" },
            Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_DependencyCycleThrows()
    {
        var tag = new Tag("a");
        var first = Item("first", (tag, 10));
        var second = ItemDependingOn("second", [first], (tag, 9));
        SetDependencies(first, second);
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
        Assert.Contains("Cycle detected in DependsOn", exception.Message);
    }

    [Fact]
    public void Search_SharedDependenciesAreIncludedOnce()
    {
        var tag = new Tag("a");
        var dependency = Item("dependency");
        var first = ItemDependingOn("first", [dependency], (tag, 10));
        var second = ItemDependingOn("second", [dependency], (tag, 9));
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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
        var dependent = ItemDependingOn("dependent", [dependency], (tag, 10));
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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

    [Fact]
    public void Search_AfterDoesNotSelectUnmatchedPredecessor()
    {
        var tag = new Tag("a");
        var predecessor = Item("predecessor");
        var ordered = ItemAfter("ordered", [predecessor], (tag, 10));
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                ordered,
                predecessor),
        ]);

        Assert.Equal(new[] { "ordered" }, Texts(result.Get(WorkKey)));
        Assert.Equal(SelectionItemReason.Direct, Assert.Single(result.Diagnostics.Items).Reason);
        Assert.Equal(1, Assert.Single(result.Diagnostics.Budgets).ActualCount);
    }

    [Fact]
    public void Search_AfterOrdersPredecessorWhenBothAreSelected()
    {
        var tag = new Tag("a");
        var predecessor = Item("predecessor", (tag, 9));
        var ordered = ItemAfter("ordered", [predecessor], (tag, 10));
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 2);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                ordered,
                predecessor),
        ]);

        Assert.Equal(new[] { "predecessor", "ordered" }, Texts(result.Get(WorkKey)));
        Assert.All(
            result.Diagnostics.Items,
            trace => Assert.Equal(SelectionItemReason.Direct, trace.Reason));
    }

    [Fact]
    public void Search_AfterSupportsTransitiveOrderingAmongSelectedItems()
    {
        var tag = new Tag("a");
        var first = Item("first", (tag, 8));
        var second = ItemAfter("second", [first], (tag, 9));
        var third = ItemAfter("third", [second], (tag, 10));
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 3);

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                third,
                second,
                first),
        ]);

        Assert.Equal(new[] { "first", "second", "third" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_AfterIsTransitiveThroughUnselectedItems()
    {
        var tag = new Tag("a");
        var third = Item("third", (tag, 8));
        var second = ItemAfter("second", [third]);
        var first = ItemAfter("first", [second], (tag, 10));
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts =>
            {
                opts.TotalItemBudget = 2;
                opts.ScoreLowerBound = 1;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                first,
                second,
                third),
        ]);

        Assert.Equal(new[] { "third", "first" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Search_OrderOnlyCycleThrows()
    {
        var tag = new Tag("a");
        var first = Item("first", (tag, 10));
        var second = ItemAfter("second", [first], (tag, 9));
        SetAfter(first, second);
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
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

        Assert.Contains("Cycle detected in ordering relationships", exception.Message);
    }

    [Fact]
    public void Search_MixedDependencyAndOrderingCycleThrows()
    {
        var tag = new Tag("a");
        var first = Item("first", (tag, 10));
        var second = ItemAfter("second", [first]);
        SetDependencies(first, second);
        var builder = NewBuilder(WeightedTags.Create([
            (tag, 1),
        ]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            opts => opts.TotalItemBudget = 1);

        var search = builder.Build();
        var exception = Assert.Throws<InvalidOperationException>(() => search.Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                first,
                second),
        ]));

        Assert.Contains("Cycle detected in ordering relationships", exception.Message);
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
            .Select(x => x.Text.ToString()!)
            .ToArray();
    }

    private static ExperienceList Experience(
        string title,
        ExperienceType type,
        int year,
        params ExperienceListItem[] items)
    {
        return Experience(
            title,
            type,
            DateRange.Completed(new(Year: year), new(Year: year + 1)),
            items);
    }

    private static ExperienceList Experience(
        string title,
        ExperienceType type,
        DateRange dateRange,
        params ExperienceListItem[] items)
    {
        return new()
        {
            Title = title,
            Place = new("test"),
            DateRange = dateRange,
            Items = items.ToImmutableArray(),
            Type = type,
        };
    }

    private static ExperienceList ExperienceWithMetadata(
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
            Description = new PlainText { Text = "description" },
            Items = items.ToImmutableArray(),
            Type = type,
            Urls = ["https://example.test/experience"],
        };
    }

    private static ExperienceListItem Item(
        string text,
        params (Tag Tag, int Score)[] tags)
    {
        return ItemDependingOn(text, [], tags);
    }

    private static ExperienceListItem ItemDependingOn(
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
            DependsOn = dependencies.ToImmutableArray(),
        };
    }

    private static ExperienceListItem RequiredItem(
        string text,
        ItemRequirement requirement,
        params (Tag Tag, int Score)[] tags)
    {
        return RequiredItemDependingOn(text, requirement, [], tags);
    }

    private static ExperienceListItem RequiredItemDependingOn(
        string text,
        ItemRequirement requirement,
        ExperienceListItem[] dependencies,
        params (Tag Tag, int Score)[] tags)
    {
        return new()
        {
            Text = RichText.Create($"{new PlainText { Text = text }}"),
            Tags = tags
                .Select(x => new TagReference(x.Tag, x.Score))
                .ToImmutableArray(),
            DependsOn = dependencies.ToImmutableArray(),
            Required = requirement,
        };
    }

    private static ExperienceListItem ItemAfter(
        string text,
        ExperienceListItem[] predecessors,
        params (Tag Tag, int Score)[] tags)
    {
        var item = Item(text, tags);
        typeof(ExperienceListItem)
            .GetProperty(nameof(ExperienceListItem.Order))!
            .SetValue(item, new ItemOrder
            {
                After = predecessors.ToImmutableArray(),
            });
        return item;
    }

    private static void SetDependencies(
        ExperienceListItem item,
        params ExperienceListItem[] dependencies)
    {
        typeof(ExperienceListItem)
            .GetProperty(nameof(ExperienceListItem.DependsOn))!
            .SetValue(item, dependencies.ToImmutableArray());
    }

    private static void SetAfter(
        ExperienceListItem item,
        params ExperienceListItem[] predecessors)
    {
        typeof(ExperienceListItem)
            .GetProperty(nameof(ExperienceListItem.Order))!
            .SetValue(item, new ItemOrder
            {
                After = predecessors.ToImmutableArray(),
            });
    }
}
