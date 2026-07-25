using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class ExperienceItemSerializationTests
{
    [Fact]
    public async Task Serialization_PreservesNamedGroupIdsAndMemberIdentity()
    {
        var first = Item("first");
        var second = Item("second");
        var database = Database(first, second);
        var list = Assert.Single(database.Experiences);
        database = WithExperiences(database, [
            new ExperienceList
            {
                Title = list.Title,
                Place = list.Place,
                DateRange = list.DateRange,
                Type = list.Type,
                Items = list.Items,
                ItemGroups =
                [
                    new ExperienceItemGroup { Id = "named", Items = [first, second] },
                ],
            },
        ]);

        var json = await Serialize(database);
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var roundTripped = await ExperienceDatabaseSerializer.Deserialize(
            input,
            CancellationToken.None);
        var roundTrippedList = Assert.Single(roundTripped.Experiences);
        var group = Assert.Single(roundTrippedList.ItemGroups);

        Assert.Equal("named", group.Id);
        Assert.Same(roundTrippedList.Items[0], group.Items[0]);
        Assert.Same(roundTrippedList.Items[1], group.Items[1]);
    }

    [Fact]
    public async Task Serialization_UsesNestedOrderStringEnumsAndPreservesReferences()
    {
        var predecessor = Item("predecessor");
        var ordered = new ExperienceListItem
        {
            Text = Text("ordered"),
            DependsOn = [],
            Required = ItemRequirement.Always,
            Order = new()
            {
                Move = ItemMove.ToFront,
                After = [predecessor],
            },
        };
        var database = Database(predecessor, ordered);

        var json = await Serialize(database);

        Assert.Contains("\"Required\": \"Always\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Move\": \"ToFront\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Order\": {", json, StringComparison.Ordinal);

        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var roundTripped = await ExperienceDatabaseSerializer.Deserialize(
            input,
            CancellationToken.None);
        var items = Assert.Single(roundTripped.Experiences).Items;

        Assert.Equal(ItemRequirement.Always, items[1].Required);
        Assert.Equal(ItemMove.ToFront, items[1].Order.Move);
        Assert.Same(items[0], Assert.Single(items[1].Order.After));
    }

    [Fact]
    public async Task Deserialization_RejectsLegacyTopLevelAfterEvenWithCurrentOrder()
    {
        var json = await Serialize(Database(Item("item")));
        var root = JsonNode.Parse(json)!.AsObject();
        var item = root[nameof(ExperienceDatabase.Experiences)]!
            .AsArray()[0]!
            .AsObject()[nameof(ExperienceList.Items)]!
            .AsArray()[0]!
            .AsObject();
        item["After"] = new JsonArray();

        await using var input = new MemoryStream(
            Encoding.UTF8.GetBytes(root.ToJsonString()));

        var exception = await Assert.ThrowsAsync<JsonException>(() =>
            ExperienceDatabaseSerializer.Deserialize(input, CancellationToken.None));

        Assert.Contains("After", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Serialization_PreservesFormatAgnosticRootNodes()
    {
        var itemText = new StyledText
        {
            Text = "styled",
            Style = StyleFlags.Bold,
        };
        var description = new Href
        {
            Url = new Uri("https://example.test/description"),
            Text = new PlainText { Text = "link" },
        };
        var database = new ExperienceDatabase
        {
            AllPlaces = [],
            Experiences =
            [
                new ExperienceList
                {
                    Title = "test",
                    Place = new("test"),
                    DateRange = DateRange.Completed(new(2024), new(2025)),
                    Type = ExperienceType.Job,
                    Description = description,
                    Items =
                    [
                        new ExperienceListItem
                        {
                            Text = itemText,
                            DependsOn = [],
                            Required = ItemRequirement.None,
                            Order = new(),
                        },
                    ],
                },
            ],
        };

        var json = await Serialize(database);
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var roundTripped = await ExperienceDatabaseSerializer.Deserialize(
            input,
            CancellationToken.None);
        var list = Assert.Single(roundTripped.Experiences);

        Assert.IsType<Href>(list.Description);
        Assert.IsType<StyledText>(Assert.Single(list.Items).Text);
    }

    private static async Task<string> Serialize(ExperienceDatabase database)
    {
        using var output = new MemoryStream();
        await database.Serialize(output, CancellationToken.None);
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static ExperienceDatabase Database(
        params ExperienceListItem[] items)
    {
        return new()
        {
            AllPlaces = [],
            Experiences =
            [
                new()
                {
                    Title = "test",
                    Place = new("test"),
                    DateRange = DateRange.Completed(new(2024), new(2025)),
                    Type = ExperienceType.Job,
                    Items = items.ToImmutableArray(),
                },
            ],
        };
    }

    private static ExperienceListItem Item(string text)
    {
        return new()
        {
            Text = Text(text),
            DependsOn = [],
            Required = ItemRequirement.None,
            Order = new(),
        };
    }

    private static RichText Text(string text)
    {
        return RichText.Create($"{new PlainText { Text = text }}");
    }

    private static ExperienceDatabase WithExperiences(
        ExperienceDatabase database,
        ImmutableArray<ExperienceList> experiences)
    {
        return new ExperienceDatabase
        {
            AllPlaces = database.AllPlaces,
            Experiences = experiences,
        };
    }
}
