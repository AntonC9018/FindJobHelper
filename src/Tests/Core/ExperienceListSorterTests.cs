using System.Collections.Immutable;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;
using MainCli;

namespace FindJobHelper.Core.Tests;

public sealed class ExperienceListSorterTests
{
    [Fact]
    public void AllEvents_AfterOrdersItemsWithoutChangingSelection()
    {
        var predecessor = Item(Text("predecessor"));
        var ordered = new ExperienceListItem
        {
            Text = RichText.Create($"{Text("ordered")}"),
            After = [predecessor],
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
    public void Factory_ExampleCo BetaAuthenticationIsOnlyOrderedAfterBackendIntroduction()
    {
        var (tags, _) = TagsDatabaseFactory.Create();
        var database = ExperienceDatabaseFactory.Create(tags);
        var backend = Assert.Single(database.Experiences.Where(x =>
            x.Title.Value == "Backend Developer" &&
            x.Place.Name.Value == "ExampleCo Beta"));
        var introduction = backend.Items[0];
        var authentication = Assert.Single(backend.Items.Where(x =>
            x.Tags.Any(tag =>
                tag.Tag.Name.Equals("Security", StringComparison.OrdinalIgnoreCase) &&
                tag.Score == 8)));

        Assert.Empty(authentication.DependsOn);
        Assert.Same(introduction, Assert.Single(authentication.After));
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
        var list = new ExperienceList
        {
            Title = "test",
            Place = new("test"),
            DateRange = DateRange.Completed(new(Year: 2024), new(Year: 2025)),
            Items = items.ToImmutableArray(),
            Type = ExperienceType.Job,
        };

        var events = new[] { list }.SelectEvents(new(
            Tags: tags,
            TotalItemBudget: budget,
            ScoreLowerBound: scoreLowerBound)
        {
            Mmr = mmr,
        });

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

    private static IRichTextNode Text(string text)
    {
        return new PlainText
        {
            Text = text,
        };
    }
}
