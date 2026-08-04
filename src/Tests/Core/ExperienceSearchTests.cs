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
    public void Search_ExclusionKeepsHigherRankedItemAndUsesUnrelatedFallback()
    {
        var tag = new Tag("a");
        var high = Item("high", (tag, 10));
        var conflicting = Item("conflicting", (tag, 9));
        var fallback = Item("fallback", (tag, 8));
        var list = Experience("work", ExperienceType.Job, 2025, high, conflicting, fallback);
        list = new ExperienceList
        {
            Title = list.Title,
            Place = list.Place,
            DateRange = list.DateRange,
            Type = list.Type,
            Items = list.Items,
            ItemExclusionSets = [new ExperienceItemExclusionSet { Items = [high, conflicting] }],
        };
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Mmr(new MmrOptions(1, 1, 0));
        builder.Configure(WorkKey, _ => true, options => options.TotalItemBudget = 2);

        var result = builder.Build().Run([list], NoOpProgressReporter.Instance);

        Assert.Equal(new[] { "high", "fallback" }, Texts(result.Get(WorkKey)));
        Assert.DoesNotContain(
            result.Diagnostics.Items,
            trace => ReferenceEquals(trace.Item, conflicting));
    }

    [Fact]
    public void Search_ThrowsWhenDependencyClosureContainsMutuallyExclusiveItems()
    {
        var tag = new Tag("a");
        var dependency = Item("dependency", (tag, 1));
        var candidate = ItemDependingOn("candidate", [dependency], (tag, 10));
        var list = Experience("work", ExperienceType.Job, 2025, dependency, candidate);
        list = new ExperienceList
        {
            Title = list.Title,
            Place = list.Place,
            DateRange = list.DateRange,
            Type = list.Type,
            Items = list.Items,
            ItemExclusionSets = [new ExperienceItemExclusionSet { Items = [dependency, candidate] }],
        };
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Configure(WorkKey, _ => true, options => options.TotalItemBudget = 2);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.Build().Run([list], NoOpProgressReporter.Instance));

        Assert.Contains("closure", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mutually exclusive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

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
        ], NoOpProgressReporter.Instance);
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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance).Get(WorkKey);

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
        ], NoOpProgressReporter.Instance);
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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

        var @event = Assert.Single(result.Get(WorkKey));
        var subItem = Assert.Single(@event.SubItems);
        var eventDebugTags = @event.DebugInfo.TagScores
            .Select(x => (Tag: x.Tag.Value, x.Score))
            .ToArray();
        var debugTags = subItem.DebugInfo.TagScores
            .Select(x => (Tag: x.Tag.Value, x.Score))
            .ToArray();

        Assert.Equal(0.72f, @event.DebugInfo.Score, tolerance: 0.0001f);
        Assert.Equal(0.72f, subItem.DebugInfo.Score, tolerance: 0.0001f);
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
        ], NoOpProgressReporter.Instance);

        var subItems = Assert.Single(result.Get(WorkKey)).SubItems;
        Assert.Equal(2, subItems.Length);
        Assert.Equal(0.9f, subItems[0].DebugInfo.Score, tolerance: 0.0001f);
        Assert.InRange(subItems[1].DebugInfo.Score, 0.209f, 0.211f);
        Assert.Equal(9, Assert.Single(subItems[1].DebugInfo.TagScores).Score);
        Assert.Equal(
            9,
            Assert.Single(subItems[1].DebugInfo.RequirementCoverage).Score);
        var breakdown = Assert.IsType<MmrScoreBreakdown>(
            subItems[1].DebugInfo.MmrScoreBreakdown);
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
            return builder.Build().Run([older, newer], NoOpProgressReporter.Instance);
        }
    }

    [Fact]
    public void Search_DirectBoostCanMoveCandidateAcrossScoreLowerBound()
    {
        var tag = new Tag("a");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Configure(
            WorkKey,
            _ => true,
            options =>
            {
                options.TotalItemBudget = 1;
                options.ScoreLowerBound = 5;
                options.DirectMatchBoost = 0.5f;
            });

        var result = builder.Build().Run([
            Experience("work", ExperienceType.Job, 2025, Item("boosted", (tag, 4))),
        ], NoOpProgressReporter.Instance);
        var trace = Assert.Single(result.Diagnostics.Items);

        Assert.Equal(new[] { "boosted" }, Texts(result.Get(WorkKey)));
        Assert.Equal(4, trace.ScoreBreakdown.BaseRelevance);
        Assert.Equal(2, trace.ScoreBreakdown.DirectMatchBonus);
        Assert.Equal(6, trace.ScoreBreakdown.RawRelevance);
    }

    [Fact]
    public void Search_ComposesDirectAndRecencyBonusesAdditivelyBeforeMmr()
    {
        var tagBuilder = new TagsDatabaseBuilder();
        var indirect = tagBuilder.Tag("Indirect");
        var target = tagBuilder.Tag("Target");
        indirect.IsIncludedIn(target)
            .Fully()
            .WhichIsIncludedInIt()
            .By(0.1f);
        var tagsResult = tagBuilder.Build();
        Assert.Empty(tagsResult.Errors ?? []);
        var query = tagsResult.Database!.Weighted([
            ("Indirect", 1),
            ("Target", 0.4f),
        ]);
        var builder = NewBuilder(query);
        builder.Mmr(new MmrOptions(
            RelevanceWeight: 1,
            SaturationQuota: 10,
            SaturationPenalty: 0));
        builder.Configure(
            WorkKey,
            _ => true,
            options =>
            {
                options.TotalItemBudget = 2;
                options.DirectMatchBoost = 0.5f;
                options.RecencyBoost = 0.25f;
            });

        var result = builder.Build().Run([
            Experience("older", ExperienceType.Job, 2019, Item("older", (new("Target"), 10))),
            Experience("newer", ExperienceType.Job, 2024, Item("newer", (new("Target"), 10))),
        ], NoOpProgressReporter.Instance);
        var breakdown = Assert.Single(
            result.Diagnostics.Items,
            x => x.Event.Title.Value == "newer").ScoreBreakdown;

        Assert.Equal(10, breakdown.BaseRelevance);
        Assert.Equal(2, breakdown.DirectMatchBonus);
        Assert.Equal(12, breakdown.RawRelevance);
        Assert.Equal(0.25f, breakdown.AppliedRecencyBoost);
        Assert.Equal(2.5f, breakdown.RecencyBonus);
        Assert.Equal(14.5f, breakdown.AdjustedPreMmrRelevance);
        Assert.Equal(1, breakdown.NormalizedMmrScore);
    }

    [Fact]
    public void Search_RequiredAndDependencyItemsUseResolvedSectionDirectBoost()
    {
        var tag = new Tag("a");
        var dependency = Item("dependency", (tag, 4));
        var dependent = ItemDependingOn(
            "dependent",
            [dependency],
            (tag, 10));
        var always = RequiredItem(
            "always",
            ItemRequirement.Always,
            (tag, 6));
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Configure(
            WorkKey,
            _ => true,
            options =>
            {
                options.TotalItemBudget = 3;
                options.DirectMatchBoost = 0.5f;
            });

        var result = builder.Build().Run([
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                always,
                dependent,
                dependency),
        ], NoOpProgressReporter.Instance);
        var byText = result.Diagnostics.Items.ToDictionary(
            x => x.Item.Text.ToString()!,
            x => x);

        Assert.Equal(3, byText.Count);
        Assert.Equal(3, byText["always"].ScoreBreakdown.DirectMatchBonus);
        Assert.Equal(5, byText["dependent"].ScoreBreakdown.DirectMatchBonus);
        Assert.Equal(2, byText["dependency"].ScoreBreakdown.DirectMatchBonus);
        Assert.Equal(SelectionItemReason.RequiredAlways, byText["always"].Reason);
        Assert.Equal(SelectionItemReason.Dependency, byText["dependency"].Reason);
    }

    [Fact]
    public void Search_BoostsDoNotChangeSimilaritySaturationOrCoverage()
    {
        var tag = new Tag("a");
        var experiences = new[]
        {
            Experience("newer", ExperienceType.Job, 2024, Item("first", (tag, 10))),
            Experience("older", ExperienceType.Job, 2019, Item("second", (tag, 9))),
        };

        var unboosted = Run(directMatchBoost: 0, recencyBoost: 0);
        var boosted = Run(directMatchBoost: 0.5f, recencyBoost: 0.5f);
        var unboostedSecond = Assert.Single(
            unboosted.Diagnostics.Items,
            x => x.Item.Text.ToString() == "second");
        var boostedSecond = Assert.Single(
            boosted.Diagnostics.Items,
            x => x.Item.Text.ToString() == "second");

        Assert.Equal(
            unboostedSecond.Matches.RequirementCoverage.OrderBy(
                static x => x.Key.Name),
            boostedSecond.Matches.RequirementCoverage.OrderBy(
                static x => x.Key.Name));
        Assert.Equal(
            unboostedSecond.ScoreBreakdown.MaximumCosineSimilarity,
            boostedSecond.ScoreBreakdown.MaximumCosineSimilarity);
        Assert.Equal(
            unboostedSecond.ScoreBreakdown.Saturation,
            boostedSecond.ScoreBreakdown.Saturation);

        SearchResult Run(float directMatchBoost, float recencyBoost)
        {
            var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
            builder.Mmr(new MmrOptions(
                RelevanceWeight: 0.9f,
                SaturationQuota: 1,
                SaturationPenalty: 0.2f));
            builder.Configure(
                WorkKey,
                _ => true,
                options =>
                {
                    options.TotalItemBudget = 2;
                    options.DirectMatchBoost = directMatchBoost;
                    options.RecencyBoost = recencyBoost;
                });
            return builder.Build().Run(
                experiences,
                NoOpProgressReporter.Instance);
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
        ], NoOpProgressReporter.Instance);

        var oldest = Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "oldest");
        var middle = Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "middle");
        var newest = Assert.Single(result.Diagnostics.Items, x => x.Event.Title.Value == "newest");
        Assert.Equal(10, oldest.ScoreBreakdown.AdjustedPreMmrRelevance);
        Assert.InRange(
            middle.ScoreBreakdown.AdjustedPreMmrRelevance,
            12.5f,
            12.501f);
        Assert.Equal(15, newest.ScoreBreakdown.AdjustedPreMmrRelevance);
        Assert.InRange(oldest.DebugScore, 0.666f, 0.667f);
        Assert.InRange(middle.DebugScore, 0.833f, 0.834f);
        Assert.Equal(1, newest.DebugScore);
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
        ], NoOpProgressReporter.Instance);

        Assert.All(result.Diagnostics.Items, item =>
        {
            Assert.Equal(0, item.ScoreBreakdown.RecencyBonus);
            Assert.Equal(10, item.ScoreBreakdown.AdjustedPreMmrRelevance);
            Assert.Equal(1, item.DebugScore);
        });
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
        ], NoOpProgressReporter.Instance);

        Assert.Equal(new[] { "eligible" }, Texts(result.Get(WorkKey)));
        var trace = Assert.Single(result.Diagnostics.Items);
        Assert.Equal(5, trace.ScoreBreakdown.BaseRelevance);
        Assert.Equal(0, trace.ScoreBreakdown.RecencyBonus);
        Assert.Equal(0.72f, trace.DebugScore, tolerance: 0.0001f);
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
        ], NoOpProgressReporter.Instance);

        AssertBreakdown("old work", adjusted: 10, normalizedRank: 0.5f);
        AssertBreakdown("new work", adjusted: 15, normalizedRank: 0.75f);
        AssertBreakdown("old project", adjusted: 10, normalizedRank: 0.5f);
        AssertBreakdown("new project", adjusted: 20, normalizedRank: 1);

        void AssertBreakdown(
            string title,
            float adjusted,
            float normalizedRank)
        {
            var trace = Assert.Single(
                result.Diagnostics.Items,
                x => x.Event.Title.Value == title);
            Assert.Equal(
                adjusted,
                trace.ScoreBreakdown.AdjustedPreMmrRelevance);
            Assert.Equal(
                normalizedRank,
                trace.DebugScore,
                tolerance: 0.0001f);
        }
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
            new DateOnly(2025, 7, 1),
            NoOpProgressReporter.Instance);

        var completed = Assert.Single(
            result.Diagnostics.Items,
            x => x.Event.Title.Value == "completed");
        var ongoing = Assert.Single(
            result.Diagnostics.Items,
            x => x.Event.Title.Value == "ongoing");
        Assert.Equal(10, completed.ScoreBreakdown.AdjustedPreMmrRelevance);
        Assert.Equal(15, ongoing.ScoreBreakdown.AdjustedPreMmrRelevance);
        Assert.InRange(completed.DebugScore, 0.666f, 0.667f);
        Assert.Equal(1, ongoing.DebugScore);
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
        ], NoOpProgressReporter.Instance);

        var newer = Assert.Single(
            result.Diagnostics.Items,
            x => x.Event.Title.Value == "newer");
        var older = Assert.Single(
            result.Diagnostics.Items,
            x => x.Event.Title.Value == "older");
        Assert.Equal(15, newer.ScoreBreakdown.AdjustedPreMmrRelevance);
        Assert.Equal(10, older.ScoreBreakdown.AdjustedPreMmrRelevance);
        Assert.Equal(10, Assert.Single(newer.Matches.RequirementCoverage).Value);
        Assert.Equal(10, Assert.Single(older.Matches.RequirementCoverage).Value);
        Assert.Equal(1, older.ScoreBreakdown.MaximumCosineSimilarity);
        Assert.Equal(1, older.ScoreBreakdown.Saturation);
        Assert.Equal(0.3f, older.DebugScore, tolerance: 0.0001f);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ScoreBoost_RejectsInvalidValue(float value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScoreBoost(value));

        Assert.Equal("value", exception.ParamName);
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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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

        var result = builder.Build().Run([], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance));

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance));
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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance);

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
        ], NoOpProgressReporter.Instance));

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
        ], NoOpProgressReporter.Instance));

        Assert.Contains("Cycle detected in ordering relationships", exception.Message);
    }

    [Fact]
    public void Search_ReportsScoringSelectionRequiredDependenciesAndAssembly()
    {
        var tag = new Tag("match");
        var dependency = Item("dependency");
        var candidate = ItemDependingOn(
            "candidate",
            [dependency],
            (tag, 10));
        var required = RequiredItem(
            "required",
            ItemRequirement.IfAny);
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Configure(
            WorkKey,
            e => e.Type == ExperienceType.Job,
            options => options.TotalItemBudget = 3);
        var progress = new ProgressTestReporter();

        var result = builder.Build().Run(
            [
                Experience(
                    "work",
                    ExperienceType.Job,
                    2025,
                    candidate,
                    dependency,
                    required),
            ],
            progress);

        Assert.Equal(
            new[] { "candidate", "dependency", "required" },
            Texts(result.Get(WorkKey)).Order().ToArray());
        Assert.Equal(new ProgressReport(7, 7, "Matching experiences"), progress.Last);
        Assert.Contains(
            progress.Reports,
            static report => report.Detail?.Contains(
                "scanned and scored",
                StringComparison.Ordinal) == true);
        Assert.Contains(
            progress.Reports,
            static report => report.Detail?.Contains(
                "required or dependent",
                StringComparison.Ordinal) == true);
        Assert.Contains(
            progress.Reports,
            static report => report.Detail?.Contains(
                "candidate selected",
                StringComparison.Ordinal) == true);
        Assert.True(IsMonotonic(progress.Reports));
    }

    [Fact]
    public void Search_NoCandidatesAndRejectedCandidates_StillCompleteRealWork()
    {
        var tag = new Tag("match");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Configure(
            WorkKey,
            _ => true,
            options =>
            {
                options.TotalItemBudget = 1;
                options.ScoreLowerBound = 20;
            });
        var rejectedProgress = new ProgressTestReporter();

        _ = builder.Build().Run(
            [
                Experience(
                    "work",
                    ExperienceType.Job,
                    2025,
                    Item("below threshold", (tag, 10))),
            ],
            rejectedProgress);

        Assert.Equal(3, rejectedProgress.Last.CompletedWorkUnits);
        Assert.Equal(3, rejectedProgress.Last.TotalWorkUnits);
        Assert.Contains(
            rejectedProgress.Reports,
            static report => report.Detail?.Contains(
                "rejected by score",
                StringComparison.Ordinal) == true);

        var emptyProgress = new ProgressTestReporter();
        _ = builder.Build().Run([], emptyProgress);
        Assert.Equal(new ProgressReport(1, 1, "Matching experiences"), emptyProgress.Last);
    }

    [Fact]
    public void Search_RealAndNoOpProgressProduceIdenticalResults()
    {
        var tag = new Tag("match");
        var builder = NewBuilder(WeightedTags.Create([(tag, 1)]));
        builder.Configure(
            WorkKey,
            _ => true,
            options => options.TotalItemBudget = 1);
        var search = builder.Build();
        var experiences = new[]
        {
            Experience(
                "work",
                ExperienceType.Job,
                2025,
                Item("first", (tag, 10)),
                Item("second", (tag, 9))),
        };

        var withProgress = search.Run(
            experiences,
            new ProgressTestReporter());
        var withNoOp = search.Run(
            experiences,
            NoOpProgressReporter.Instance);

        Assert.Equal(
            Texts(withNoOp.Get(WorkKey)),
            Texts(withProgress.Get(WorkKey)));
        Assert.Equal(
            withNoOp.Diagnostics.Items.Select(static item => item.DebugScore),
            withProgress.Diagnostics.Items.Select(static item => item.DebugScore));
        Assert.Equal(
            withNoOp.Diagnostics.Items.Select(static item => item.Reason),
            withProgress.Diagnostics.Items.Select(static item => item.Reason));
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

    private static bool IsMonotonic(
        IReadOnlyList<ProgressReport> reports)
    {
        double previous = 0;
        foreach (var report in reports)
        {
            var current = ProgressMath.Fraction(report);
            if (current < previous)
            {
                return false;
            }
            previous = current;
        }
        return true;
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
