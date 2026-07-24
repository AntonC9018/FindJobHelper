using System.Collections.Immutable;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;
using MainCli;

namespace FindJobHelper.Core.Tests;

public sealed class ExperienceListSorterTests
{
    [Fact]
    public void AllEvents_OrderAfterOrdersItemsWithoutChangingSelection()
    {
        var predecessor = Item(Text("predecessor"));
        var ordered = new ExperienceListItem
        {
            Text = RichText.Create($"{Text("ordered")}"),
            Order = new()
            {
                After = [predecessor],
            },
        };
        var list = new ExperienceList
        {
            Title = "test",
            Place = new("test"),
            DateRange = DateRange.Completed(new(Year: 2024), new(Year: 2025)),
            Items = [ordered, predecessor],
            Type = ExperienceType.Job,
        };

        var texts = Assert.Single(new[] { list }.AllEvents())
            .SubItems
            .Select(x => x.String.ToString())
            .ToArray();

        Assert.Equal(new[] { "predecessor", "ordered" }, texts);
    }

    [Fact]
    public void AllEvents_SelectedFrontItemAppearsFirst()
    {
        var ordinary = Item(Text("ordinary"));
        var front = FrontItem(Text("front"));
        var list = List(ordinary, front);

        var texts = Assert.Single(new[] { list }.AllEvents())
            .SubItems
            .Select(x => x.String.ToString())
            .ToArray();

        Assert.Equal(new[] { "front", "ordinary" }, texts);
    }

    [Fact]
    public void AllEvents_ExplicitRelationshipCanReorderFrontItems()
    {
        var secondFront = FrontItem(Text("second front"));
        var firstFront = new ExperienceListItem
        {
            Text = RichText.Create($"{Text("first front")}"),
            Order = new()
            {
                Move = ItemMove.ToFront,
                After = [secondFront],
            },
        };
        var list = List(
            firstFront,
            secondFront,
            Item(Text("ordinary")));

        var texts = Assert.Single(new[] { list }.AllEvents())
            .SubItems
            .Select(x => x.String.ToString())
            .ToArray();

        Assert.Equal(
            new[] { "second front", "first front", "ordinary" },
            texts);
    }

    [Fact]
    public void Search_UnmatchedFrontItemRemainsUnselected()
    {
        var tag = new Tag("match");

        var texts = SelectTexts(
            tags: new WeightedTags { [tag] = 1 },
            budget: 1,
            scoreLowerBound: 0,
            mmr: MmrOptions.Default,
            items:
            [
                FrontItem(Text("front")),
                Item(Text("selected"), (tag, 10)),
            ]);

        Assert.Equal(new[] { "selected" }, texts);
    }

    [Fact]
    public void Search_MultipleFrontItemsFormStableDeclarationOrderPrefix()
    {
        var tag = new Tag("match");

        var texts = SelectTexts(
            tags: new WeightedTags { [tag] = 1 },
            budget: 3,
            scoreLowerBound: 0,
            mmr: new(
                RelevanceWeight: 1,
                SaturationQuota: 1,
                SaturationPenalty: 0),
            items:
            [
                FrontItem(Text("front-first"), (tag, 8)),
                Item(Text("ordinary"), (tag, 10)),
                FrontItem(Text("front-second"), (tag, 9)),
            ]);

        Assert.Equal(
            new[] { "front-first", "front-second", "ordinary" },
            texts);
    }

    [Fact]
    public void Search_ContradictoryFrontAndAfterRelationshipsThrowCycle()
    {
        var tag = new Tag("match");
        var ordinary = Item(Text("ordinary"), (tag, 10));
        var front = new ExperienceListItem
        {
            Text = RichText.Create($"{Text("front")}"),
            Tags = [new(tag, 9)],
            Order = new()
            {
                Move = ItemMove.ToFront,
                After = [ordinary],
            },
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            SelectTexts(
                tags: new WeightedTags { [tag] = 1 },
                budget: 2,
                scoreLowerBound: 0,
                mmr: MmrOptions.Default,
                items: [ordinary, front]));

        Assert.Contains(
            "Cycle detected in ordering relationships",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SelectEvents_WithPureRelevanceKeepsSimilarHighScoringItems()
    {
        var selectedText = SelectTexts(
            budget: 3,
            mmr: new(
                RelevanceWeight: 1f,
                SaturationQuota: 1,
                SaturationPenalty: 0f));

        // With pure relevance, MMR ignores redundancy and saturation, so the
        // three highest raw scores all come from the same tag cluster.
        Assert.Equal(
            new[] { "repeated-best", "repeated-second", "repeated-third" },
            selectedText);
    }

    [Fact]
    public void SelectEvents_WithQuotaOneDiversifiesImmediately()
    {
        var selectedText = SelectTexts(
            budget: 2,
            mmr: new(
                RelevanceWeight: 0.9f,
                SaturationQuota: 1,
                SaturationPenalty: 0.5f));

        // Quota 1 makes the second "a" item saturated. Even though it has a
        // higher raw score than "different", the different tag should win.
        Assert.Equal(new[] { "repeated-best", "different" }, selectedText);
    }

    [Fact]
    public void SelectEvents_WithQuotaTwoAllowsOneRepeatThenDiversifies()
    {
        var selectedText = SelectTexts(
            budget: 3,
            mmr: new(
                RelevanceWeight: 0.9f,
                SaturationQuota: 2,
                SaturationPenalty: 0.5f));

        // Quota 2 allows one repeated "a" item. The third repeated item is the
        // first one over quota, so selection switches to the different tag.
        Assert.Equal(
            new[] { "repeated-best", "repeated-second", "different" },
            selectedText);
    }

    [Fact]
    public void SelectEvents_DoesNotDiversifyToItemsBelowLowerBound()
    {
        var selectedText = SelectTexts(
            budget: 2,
            scoreLowerBound: 6,
            mmr: new(
                RelevanceWeight: 0.9f,
                SaturationQuota: 1,
                SaturationPenalty: 0.5f));

        // ScoreLowerBound filters out "different" before MMR runs because its
        // raw score is 5, so the selector must keep the next eligible repeat.
        Assert.Equal(new[] { "repeated-best", "repeated-second" }, selectedText);
    }

    [Fact]
    public void SelectEvents_MatchesItemsRelatedToQueryThroughAnotherTag()
    {
        var query = new Tag("query");
        var related = new Tag("related");
        var tags = BuildTagsDatabase(tags =>
        {
            tags.Query.IsIncludedIn(tags.Bridge).By(0.8f).WhichIsIncludedInIt().By(0.8f);
            tags.Bridge.IsIncludedIn(tags.Related).By(0.8f).WhichIsIncludedInIt().By(0.8f);
        });

        var selectedText = SelectTexts(
            tags: tags.Weighted([(query, 1f)]),
            budget: 1,
            scoreLowerBound: 5,
            mmr: MmrOptions.Default,
            items: [
                Item(Text("related-item"), (related, 10)),
            ]);

        // TagsDatabase.Weighted is the parameter source that expands "query"
        // through "bridge" into "related", so this non-exact item still passes.
        Assert.Equal(new[] { "related-item" }, selectedText);
    }

    [Fact]
    public void SelectEvents_CanSelectItemThatAlsoOverlapsSelectedTags()
    {
        var selectedTag = new Tag("selected");
        var newTag = new Tag("new");

        var selectedText = SelectTexts(
            tags: new WeightedTags
            {
                [selectedTag] = 1,
                [newTag] = 1,
            },
            budget: 2,
            scoreLowerBound: 0,
            mmr: new(
                RelevanceWeight: 0.6f,
                SaturationQuota: 1,
                SaturationPenalty: 0.2f),
            items: [
                Item(Text("selected-best"), (selectedTag, 10)),
                Item(Text("overlap-plus-new"), (selectedTag, 1), (newTag, 8)),
                Item(Text("only-new-weaker"), (newTag, 4)),
            ]);

        // "overlap-plus-new" shares an already-selected tag, so redundancy and
        // saturation lower its MMR score. They do not exclude it, and its high
        // new-tag score still beats the weaker clean alternative.
        Assert.Equal(new[] { "selected-best", "overlap-plus-new" }, selectedText);
    }

    private static string[] SelectTexts(
        int budget,
        MmrOptions mmr,
        float scoreLowerBound = 0)
    {
        var tagA = new Tag("a");
        var tagB = new Tag("b");

        var repeatedBest = Item(Text("repeated-best"), (tagA, 10));
        var repeatedSecond = Item(Text("repeated-second"), (tagA, 9));
        var repeatedThird = Item(Text("repeated-third"), (tagA, 8));
        var different = Item(Text("different"), (tagB, 5));

        return SelectTexts(
            tags: new WeightedTags
            {
                [tagA] = 1,
                [tagB] = 1,
            },
            budget: budget,
            scoreLowerBound: scoreLowerBound,
            mmr: mmr,
            items: [
                repeatedBest,
                repeatedSecond,
                repeatedThird,
                different,
            ]);
    }

    private static string[] SelectTexts(
        WeightedTags tags,
        int budget,
        float scoreLowerBound,
        MmrOptions mmr,
        params ExperienceListItem[] items)
    {
        var list = List(items);

        var key = new ExperienceKey("Default");
        var builder = new SearchBuilder();
        builder.Tags(tags);
        builder.Mmr(mmr);
        builder.Configure(
            key,
            static _ => true,
            options =>
            {
                options.TotalItemBudget = budget;
                options.ScoreLowerBound = scoreLowerBound;
            });
        var events = builder.Build().Run([list]).Get(key);

        return Assert.Single(events)
            .SubItems
            .Select(x => x.String.ToString())
            .ToArray();
    }

    private static TagsDatabase BuildTagsDatabase(Action<TestTagBuilders> configure)
    {
        var builder = new TagsDatabaseBuilder();
        var tags = new TestTagBuilders(
            builder.Tag("query"),
            builder.Tag("bridge"),
            builder.Tag("related"));

        configure(tags);
        var result = builder.Build();
        Assert.Empty(result.Errors ?? []);
        return result.Database!;
    }

    private readonly record struct TestTagBuilders(
        TagBuilder Query,
        TagBuilder Bridge,
        TagBuilder Related);

    private static ExperienceListItem Item(
        IRichTextNode text,
        params (Tag Tag, int Score)[] tags)
    {
        var tagReferences = tags
            .Select(x => new TagReference(x.Tag, x.Score))
            .ToImmutableArray();

        return new()
        {
            Text = RichText.Create($"{text}"),
            Tags = tagReferences,
        };
    }

    private static ExperienceListItem FrontItem(
        IRichTextNode text,
        params (Tag Tag, int Score)[] tags)
    {
        var tagReferences = tags
            .Select(x => new TagReference(x.Tag, x.Score))
            .ToImmutableArray();

        return new()
        {
            Text = RichText.Create($"{text}"),
            Tags = tagReferences,
            Order = new()
            {
                Move = ItemMove.ToFront,
            },
        };
    }

    private static ExperienceList List(
        params ExperienceListItem[] items)
    {
        return new()
        {
            Title = "test",
            Place = new("test"),
            DateRange = DateRange.Completed(new(Year: 2024), new(Year: 2025)),
            Items = items.ToImmutableArray(),
            Type = ExperienceType.Job,
        };
    }

    private static IRichTextNode Text(string text)
    {
        return new PlainText
        {
            Text = text,
        };
    }
}
