using System.Collections.Immutable;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class RequiredTagProvenanceTests
{
    private static readonly ExperienceKey WorkKey = new("Work");

    [Fact]
    public void Match_DirectBoostAppliesOnlyToExactTagsAndFullAliases()
    {
        var builder = new TagsDatabaseBuilder();
        var direct = builder.Tag("Direct", "Alias");
        var partial = builder.Tag("Partial");
        var oneWay = builder.Tag("One Way");
        var middle = builder.Tag("Middle");
        var transitive = builder.Tag("Transitive");
        direct.IsIncludedIn(partial)
            .By(0.6f)
            .WhichIsIncludedInIt()
            .By(0.2f);
        direct.IsIncludedIn(oneWay)
            .Fully()
            .WhichIsIncludedInIt()
            .By(0.2f);
        direct.IsIncludedIn(middle)
            .By(0.9f)
            .WhichIsIncludedInIt()
            .By(0.1f);
        middle.IsIncludedIn(transitive)
            .By(0.9f)
            .WhichIsIncludedInIt()
            .By(0.1f);
        var query = Build(builder).Weighted([("Direct", 1)]);

        AssertMatch("Direct", expectedBase: 10, expectedDirect: 10, expectedBonus: 5);
        AssertMatch("Alias", expectedBase: 10, expectedDirect: 10, expectedBonus: 5);
        AssertMatch("Partial", expectedBase: 6, expectedDirect: 0, expectedBonus: 0);
        AssertMatch("One Way", expectedBase: 10, expectedDirect: 0, expectedBonus: 0);
        AssertMatch("Transitive", expectedBase: 8, expectedDirect: 0, expectedBonus: 0);

        Assert.True(query.TryGetValue(new("Direct"), out var directProjection));
        Assert.True(query.TryGetValue(new("Alias"), out var aliasProjection));
        Assert.True(query.TryGetValue(new("Partial"), out var partialProjection));
        Assert.True(query.TryGetValue(new("One Way"), out var oneWayProjection));
        Assert.True(query.TryGetValue(new("Transitive"), out var transitiveProjection));
        Assert.True(Assert.Single(directProjection.Origins).IsDirect);
        Assert.True(Assert.Single(aliasProjection.Origins).IsDirect);
        Assert.False(Assert.Single(partialProjection.Origins).IsDirect);
        Assert.False(Assert.Single(oneWayProjection.Origins).IsDirect);
        Assert.False(Assert.Single(transitiveProjection.Origins).IsDirect);

        void AssertMatch(
            string tag,
            float expectedBase,
            float expectedDirect,
            float expectedBonus)
        {
            var scored = query.Match([new(new(tag), 10)], directMatchBoost: 0.5f);
            var match = Assert.Single(scored.Matches);
            Assert.Equal(expectedBase, match.BaseContribution, tolerance: 0.0001f);
            Assert.Equal(expectedDirect, match.DirectContribution, tolerance: 0.0001f);
            Assert.Equal(expectedBonus, match.DirectMatchBonus, tolerance: 0.0001f);
            Assert.Equal(
                expectedBase + expectedBonus,
                match.RelevanceContribution,
                tolerance: 0.0001f);
        }
    }

    [Fact]
    public void Match_UsesWeakerDirectContributionWhenIndirectMatchIsStronger()
    {
        var builder = new TagsDatabaseBuilder();
        var indirect = builder.Tag("Indirect");
        var target = builder.Tag("Target");
        indirect.IsIncludedIn(target)
            .Fully()
            .WhichIsIncludedInIt()
            .By(0.1f);
        var query = Build(builder).Weighted([
            ("Indirect", 1),
            ("Target", 0.4f),
        ]);

        var scored = query.Match(
            [new(new("Target"), 10)],
            directMatchBoost: 0.5f);
        var match = Assert.Single(scored.Matches);

        Assert.Equal(1, match.Projection.MaximumCoefficient);
        Assert.Equal(0.4f, match.Projection.MaximumDirectCoefficient);
        Assert.Equal(10, match.BaseContribution);
        Assert.Equal(4, match.DirectContribution);
        Assert.Equal(2, match.DirectMatchBonus);
        Assert.Equal(12, match.RelevanceContribution);
        Assert.Equal(10, scored.BaseRelevance);
        Assert.Equal(2, scored.DirectMatchBonus);
        Assert.Equal(12, scored.Sum);
        Assert.Equal(14, scored.RequirementCoverage.Values.Sum());
    }

    [Fact]
    public void Match_AddsPerTagBonusesAndDoesNotBoostNonPositiveContributions()
    {
        var first = new Tag("First");
        var second = new Tag("Second");
        var query = WeightedTags.Create([
            (first, 1),
            (second, 0.5f),
        ]);

        var positive = query.Match([
            new(first, 10),
            new(second, 8),
        ], directMatchBoost: 0.5f);
        var negative = query.Match([
            new(first, -10),
        ], directMatchBoost: 0.5f);

        Assert.Equal(14, positive.BaseRelevance);
        Assert.Equal(7, positive.DirectMatchBonus);
        Assert.Equal(21, positive.Sum);
        Assert.Equal(0, Assert.Single(negative.Matches).DirectMatchBonus);
        Assert.Equal(-10, negative.Sum);
    }

    [Fact]
    public void Match_BoostsDoNotAlterRequirementCoverage()
    {
        var builder = new TagsDatabaseBuilder();
        var exact = builder.Tag("Exact");
        var indirect = builder.Tag("Indirect");
        exact.IsIncludedIn(indirect)
            .By(0.5f)
            .WhichIsIncludedInIt()
            .By(0.1f);
        var query = Build(builder).Weighted([("Exact", 1)]);
        var tags = ImmutableArray.Create(
            new TagReference(new("Exact"), 4),
            new TagReference(new("Indirect"), 6));

        var unboosted = query.Match(tags);
        var boosted = query.Match(tags, directMatchBoost: 2);

        Assert.Equal(
            unboosted.RequirementCoverage.OrderBy(static x => x.Key.Name),
            boosted.RequirementCoverage.OrderBy(static x => x.Key.Name));
        Assert.Equal(
            unboosted.RequirementGroupCoverage.OrderBy(
                static x => x.Key.CanonicalTag.Name),
            boosted.RequirementGroupCoverage.OrderBy(
                static x => x.Key.CanonicalTag.Name));
        Assert.Equal(7, unboosted.Sum);
        Assert.Equal(15, boosted.Sum);
    }

    [Fact]
    public void Weighted_AliasesUseOneCanonicalGroupAndMaximumWeight()
    {
        var builder = new TagsDatabaseBuilder();
        var dotnet = builder.Tag(".NET", "DotNet", "C#");
        var unity = builder.Tag("Unity");
        dotnet.IsIncludedIn(unity)
            .By(0.5f)
            .WhichIsIncludedInIt()
            .By(0.2f);
        var database = Build(builder);
        var equalWeights = database.Weighted([
            ("C#", 1.5f),
            (".NET", 1.5f),
        ]);

        var group = Assert.Single(equalWeights.RequiredTagGroups);
        Assert.Equal(".NET", group.CanonicalTag.Name);
        Assert.Equal(1.5f, group.MaximumWeight);
        Assert.Equal(
            new[] { "C#", ".NET" },
            group.ConfiguredTags.Select(x => x.Tag.Name));
        AssertProjection(equalWeights, ".NET", 1.5f, group);
        AssertProjection(equalWeights, "C#", 1.5f, group);
        AssertProjection(equalWeights, "Unity", 0.75f, group);

        var matches = equalWeights.Match([
            new(new(".NET"), 2),
            new(new("Unity"), 4),
        ]);

        Assert.Equal(6, matches.Sum);
        Assert.Equal(6, Assert.Single(matches.RequirementCoverage).Value);
        Assert.All(
            matches.Matches,
            match => Assert.Same(
                group,
                Assert.Single(match.Projection.Origins).RequiredTagGroup));

        var unequalWeights = database.Weighted([
            ("C#", 1),
            (".NET", 2),
        ]);
        Assert.Equal(
            2,
            Assert.Single(unequalWeights.RequiredTagGroups).MaximumWeight);
        AssertProjection(
            unequalWeights,
            "Unity",
            expectedMaximum: 1,
            Assert.Single(unequalWeights.RequiredTagGroups));
    }

    [Fact]
    public void Match_KeepsUnequalOriginsWhileRawUsesMaximum()
    {
        var (database, first, second, target) = UnequalOriginDatabase();
        var query = database.Weighted([
            (first.Name, 1),
            (second.Name, 1),
        ]);

        Assert.True(query.TryGetValue(target, out var projection));
        Assert.Equal(0.8f, projection.MaximumCoefficient, tolerance: 0.0001f);
        Assert.Equal(
            new[] { 0.8f, 0.6f },
            projection.Origins
                .Select(x => x.Coefficient)
                .OrderDescending()
                .ToArray());

        var matches = query.Match([new(target, 10)]);
        Assert.Equal(8, matches.Sum);
        Assert.Equal(
            new[] { 8f, 6f },
            matches.RequirementCoverage.Values
                .OrderDescending()
                .ToArray());

        var result = Run(
            query,
            minimum: 3,
            maximum: 3,
            mmr: new(
                RelevanceWeight: 1,
                SaturationQuota: 10,
                SaturationPenalty: 0),
            Item("shared target", (target, 20)),
            Item("first exact", (first, 8)),
            Item("second exact", (second, 7)));

        var firstExact = Trace(result, "first exact");
        var secondExact = Trace(result, "second exact");
        Assert.Equal(
            0.8f,
            firstExact.ScoreBreakdown.MaximumCosineSimilarity,
            tolerance: 0.0001f);
        Assert.Equal(
            0.6f,
            secondExact.ScoreBreakdown.MaximumCosineSimilarity,
            tolerance: 0.0001f);
    }

    [Fact]
    public void Weighted_TransitiveTargetsRetainOnlyTheExplicitOrigin()
    {
        var builder = new TagsDatabaseBuilder();
        var a = builder.Tag("A");
        var b = builder.Tag("B");
        var c = builder.Tag("C");
        var d = builder.Tag("D");
        var alternate = builder.Tag("Alternate");
        Link(a, b, 0.9f);
        Link(b, c, 0.9f);
        Link(c, d, 0.9f);
        Link(a, alternate, 0.85f);
        Link(alternate, d, 0.85f);
        var database = Build(builder);
        var query = database.Weighted([("A", 1)]);
        var group = Assert.Single(query.RequiredTagGroups);

        AssertProjection(query, "B", 0.9f, group);
        AssertProjection(query, "C", 0.8f, group);
        var dProjection = AssertProjection(query, "D", 0.7f, group);
        Assert.Single(dProjection.Origins);

        foreach (var target in new[] { new Tag("B"), new Tag("C"), new Tag("D") })
        {
            var coverage = query.Match([new(target, 10)]).RequirementCoverage;
            var requirement = Assert.Single(coverage);
            Assert.Equal(group.CanonicalTag, requirement.Key);
            Tag[] unexpectedTags = [new("B"), new("C"), new("Alternate")];
            Assert.DoesNotContain(coverage.Keys, unexpectedTags.Contains);
        }
    }

    [Theory]
    [InlineData("Unity", 0.5f)]
    [InlineData("Game Programming", 0.4f)]
    public void IndirectCandidateCannotEscapeCanonicalSaturation(
        string indirectTagName,
        float overlap)
    {
        var builder = new TagsDatabaseBuilder();
        var dotnet = builder.Tag(".NET", "C#");
        var indirect = builder.Tag(indirectTagName);
        var diverse = builder.Tag("Playwright");
        dotnet.IsIncludedIn(indirect)
            .By(overlap)
            .WhichIsIncludedInIt()
            .By(0.1f);
        var database = Build(builder);
        var query = database.Weighted([
            (".NET", 1),
            ("Playwright", 1),
        ]);

        var result = Run(
            query,
            minimum: 3,
            maximum: 3,
            mmr: new(
                RelevanceWeight: 0.9f,
                SaturationQuota: 1,
                SaturationPenalty: 0.5f),
            Item("exact", (new(".NET"), 10)),
            Item("indirect", (new(indirectTagName), 10)),
            Item("diverse", (new("Playwright"), 4)));

        var exact = Trace(result, "exact");
        var indirectTrace = Trace(result, "indirect");
        var diverseTrace = Trace(result, "diverse");
        Assert.True(
            exact.ScoreBreakdown.SelectionOrdinal
            < diverseTrace.ScoreBreakdown.SelectionOrdinal);
        Assert.True(
            diverseTrace.ScoreBreakdown.SelectionOrdinal
            < indirectTrace.ScoreBreakdown.SelectionOrdinal);
        Assert.Equal(
            1,
            indirectTrace.ScoreBreakdown.Saturation,
            tolerance: 0.0001f);
        Assert.Equal(
            ".NET",
            Assert.Single(indirectTrace.Matches.RequirementCoverage)
                .Key.Name);
    }

    [Fact]
    public void PureRelevanceAndLowerBoundContinueUsingMaximumRawRelevance()
    {
        var (database, first, second, target) = UnequalOriginDatabase();
        var query = database.Weighted([
            (first.Name, 1),
            (second.Name, 1),
        ]);
        var pureRelevance = new MmrOptions(
            RelevanceWeight: 1,
            SaturationQuota: 1,
            SaturationPenalty: 0);

        var selected = Run(
            query,
            minimum: 0,
            maximum: 1,
            mmr: pureRelevance,
            Item("shared coverage", (target, 10)),
            Item("higher raw", (first, 9)));

        Assert.Equal(
            "higher raw",
            Assert.Single(
                Assert.Single(selected.Get(WorkKey)).SubItems)
                .Text.ToString());
        Assert.Equal(8, query.Match([new(target, 10)]).Sum);
        Assert.Equal(
            14,
            query.Match([new(target, 10)])
                .RequirementCoverage.Values.Sum());

        var thresholded = Run(
            query,
            minimum: 0,
            maximum: 1,
            mmr: pureRelevance,
            scoreLowerBound: 8.5f,
            Item("shared coverage", (target, 10)));
        Assert.Empty(thresholded.Get(WorkKey));
    }

    [Fact]
    public void RequiredAndDependencyItemsRegisterCoverageBeforeRanking()
    {
        var (database, dotnet, unity, game, diverse) =
            IndirectCoverageDatabase();
        var query = database.Weighted([
            (dotnet.Name, 1),
            (diverse.Name, 1),
        ]);
        var mmr = new MmrOptions(
            RelevanceWeight: 0.9f,
            SaturationQuota: 1,
            SaturationPenalty: 0.5f);

        var required = Item(
            "required indirect",
            ItemRequirement.Always,
            [],
            (unity, 10));
        var requiredResult = Run(
            query,
            minimum: 0,
            maximum: 2,
            mmr: mmr,
            required,
            Item("redundant indirect", (game, 10)),
            Item("diverse", (diverse, 3)));
        Assert.Equal(
            new[] { "required indirect", "diverse" },
            Texts(requiredResult));

        var dependency = Item("dependency indirect", (unity, 10));
        var dependent = Item(
            "dependent",
            ItemRequirement.None,
            [dependency],
            (diverse, 10));
        var dependencyResult = Run(
            query,
            minimum: 3,
            maximum: 3,
            mmr: mmr,
            dependent,
            Item("after dependency", (game, 10)),
            dependency);
        var dependencyTrace = Trace(
            dependencyResult,
            "dependency indirect");
        var laterTrace = Trace(dependencyResult, "after dependency");
        Assert.Equal(SelectionItemReason.Dependency, dependencyTrace.Reason);
        Assert.True(
            dependencyTrace.ScoreBreakdown.SelectionOrdinal
            < laterTrace.ScoreBreakdown.SelectionOrdinal);
        Assert.True(laterTrace.ScoreBreakdown.Saturation > 0);
    }

    [Fact]
    public void MinimumFilledItemsRegisterCoverageBeforeTheNextMinimum()
    {
        var (database, dotnet, unity, game, _) =
            IndirectCoverageDatabase();
        var query = database.Weighted([(dotnet.Name, 1)]);

        var result = Run(
            query,
            minimum: 3,
            maximum: 3,
            mmr: new(
                RelevanceWeight: 0.9f,
                SaturationQuota: 1,
                SaturationPenalty: 1),
            Item("exact", (dotnet, 10)),
            Item("first minimum", (unity, 10)),
            Item("second minimum", (game, 10)));

        var firstMinimum = Trace(result, "first minimum");
        var secondMinimum = Trace(result, "second minimum");
        Assert.Equal(
            1,
            firstMinimum.ScoreBreakdown.Saturation,
            tolerance: 0.0001f);
        Assert.Equal(
            2,
            secondMinimum.ScoreBreakdown.Saturation,
            tolerance: 0.0001f);
        Assert.True(secondMinimum.DebugScore < 0);
        Assert.Equal(
            secondMinimum.ScoreBreakdown.NormalizedMmrScore,
            secondMinimum.DebugScore);
    }

    private static (
        TagsDatabase Database,
        Tag First,
        Tag Second,
        Tag Target) UnequalOriginDatabase()
    {
        var builder = new TagsDatabaseBuilder();
        var first = builder.Tag("First");
        var second = builder.Tag("Second");
        var target = builder.Tag("Target");
        first.IsIncludedIn(target)
            .By(0.8f)
            .WhichIsIncludedInIt()
            .By(0.1f);
        second.IsIncludedIn(target)
            .By(0.6f)
            .WhichIsIncludedInIt()
            .By(0.1f);
        return (
            Build(builder),
            new(first.Name),
            new(second.Name),
            new(target.Name));
    }

    private static (
        TagsDatabase Database,
        Tag DotNet,
        Tag Unity,
        Tag Game,
        Tag Diverse) IndirectCoverageDatabase()
    {
        var builder = new TagsDatabaseBuilder();
        var dotnet = builder.Tag(".NET", "C#");
        var unity = builder.Tag("Unity");
        var game = builder.Tag("Game Programming");
        var diverse = builder.Tag("Playwright");
        dotnet.IsIncludedIn(unity)
            .By(0.5f)
            .WhichIsIncludedInIt()
            .By(0.1f);
        dotnet.IsIncludedIn(game)
            .By(0.4f)
            .WhichIsIncludedInIt()
            .By(0.1f);
        return (
            Build(builder),
            new(dotnet.Name),
            new(unity.Name),
            new(game.Name),
            new(diverse.Name));
    }

    private static void Link(
        TagBuilder left,
        TagBuilder right,
        float overlap)
    {
        left.IsIncludedIn(right)
            .By(overlap)
            .WhichIsIncludedInIt()
            .By(overlap);
    }

    private static TagsDatabase Build(TagsDatabaseBuilder builder)
    {
        var result = builder.Build();
        Assert.Empty(result.Errors ?? []);
        return result.Database!;
    }

    private static WeightedTagProjection AssertProjection(
        WeightedTags query,
        string target,
        float expectedMaximum,
        RequiredTagGroup expectedGroup)
    {
        Assert.True(query.TryGetValue(new(target), out var projection));
        Assert.Equal(
            expectedMaximum,
            projection.MaximumCoefficient,
            tolerance: 0.0001f);
        Assert.Contains(
            projection.Origins,
            origin => ReferenceEquals(
                expectedGroup,
                origin.RequiredTagGroup));
        return projection;
    }

    private static SearchResult Run(
        WeightedTags query,
        int minimum,
        int maximum,
        MmrOptions mmr,
        params ExperienceListItem[] items)
    {
        return Run(
            query,
            minimum,
            maximum,
            mmr,
            scoreLowerBound: 0,
            items);
    }

    private static SearchResult Run(
        WeightedTags query,
        int minimum,
        int maximum,
        MmrOptions mmr,
        float scoreLowerBound,
        params ExperienceListItem[] items)
    {
        var builder = new SearchBuilder();
        builder.Tags(query);
        builder.Mmr(mmr);
        builder.Configure(
            WorkKey,
            static _ => true,
            options =>
            {
                options.MinItemBudget = minimum;
                options.ItemBudget = maximum;
                options.ScoreLowerBound = scoreLowerBound;
            });
        return builder.Build().Run([
            new()
            {
                Title = "work",
                Place = new("test"),
                DateRange = DateRange.Completed(new(2025), new(2026)),
                Type = ExperienceType.Job,
                Items = items.ToImmutableArray(),
            },
        ], NoOpProgressReporter.Instance);
    }

    private static SelectionItemTrace Trace(
        SearchResult result,
        string text)
    {
        return Assert.Single(
            result.Diagnostics.Items,
            x => x.Item.Text.ToString() == text);
    }

    private static string[] Texts(SearchResult result)
    {
        return result.Get(WorkKey)
            .SelectMany(x => x.SubItems)
            .Select(x => x.Text.ToString()!)
            .ToArray();
    }

    private static ExperienceListItem Item(
        string text,
        params (Tag Tag, int Score)[] tags)
    {
        return Item(text, ItemRequirement.None, [], tags);
    }

    private static ExperienceListItem Item(
        string text,
        ItemRequirement required,
        ImmutableArray<ExperienceListItem> dependencies,
        params (Tag Tag, int Score)[] tags)
    {
        return new()
        {
            Text = new PlainText { Text = text },
            Required = required,
            DependsOn = dependencies,
            Tags = tags
                .Select(x => new TagReference(x.Tag, x.Score))
                .ToImmutableArray(),
        };
    }
}
