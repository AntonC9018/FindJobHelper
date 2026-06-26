using System.Collections.Immutable;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class ExperienceListSorterTests
{
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

    private static string[] SelectTexts(
        int budget,
        MmrOptions mmr)
    {
        var tagA = new Tag("a");
        var tagB = new Tag("b");

        var repeatedBest = Item(Text("repeated-best"), (tagA, 10));
        var repeatedSecond = Item(Text("repeated-second"), (tagA, 9));
        var repeatedThird = Item(Text("repeated-third"), (tagA, 8));
        var different = Item(Text("different"), (tagB, 5));

        var list = new ExperienceList
        {
            Title = "test",
            Place = new("test"),
            DateRange = DateRange.Completed(new(Year: 2024), new(Year: 2025)),
            IsJob = true,
            Items = [
                repeatedBest,
                repeatedSecond,
                repeatedThird,
                different,
            ],
        };

        var events = new[] { list }.SelectEvents(new(
            Tags: new WeightedTags
            {
                [tagA] = 1,
                [tagB] = 1,
            },
            TotalItemBudget: budget,
            ScoreLowerBound: 0)
        {
            Mmr = mmr,
        });

        return Assert.Single(events)
            .SubItems
            .Select(x => x.String.ToString())
            .ToArray();
    }

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
