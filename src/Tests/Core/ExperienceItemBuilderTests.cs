using FindJobHelper.CVGeneration;
using MainCli;

namespace FindJobHelper.Core.Tests;

public sealed class ExperienceItemBuilderTests
{
    [Fact]
    public void Builder_NamedGroupsReuseHandlesAndPreserveFlatDeclarationOrder()
    {
        var databaseBuilder = new ExperienceDatabaseBuilder();
        var place = databaseBuilder.Place("test");
        ExperienceItemGroupBuilder? firstTeaching = null;
        ExperienceItemGroupBuilder? secondTeaching = null;
        ExperienceItemGroupBuilder? automation = null;
        databaseBuilder.Job(experience =>
        {
            experience.Title("job");
            experience.Place(place);
            experience.DateRange(DateRange.Completed(new(2024), new(2025)));

            firstTeaching = experience.Group("teaching");
            automation = experience.Group("automation");
            secondTeaching = experience.Group("teaching");
            firstTeaching.Item(x => x.Text($"teaching one"));
            automation.Item(x => x.Text($"automation"));
            secondTeaching.Item(x => x.Text($"teaching two"));
            experience.Item(x => x.Text($"ungrouped"));
        });

        Assert.Same(firstTeaching, secondTeaching);
        Assert.NotSame(firstTeaching, automation);
        var list = Assert.Single(databaseBuilder.Build().Experiences);
        Assert.Equal(
            ["teaching one", "automation", "teaching two", "ungrouped"],
            list.Items.Select(item => item.Text.ToString()));
        Assert.Equal(["teaching", "automation"], list.ItemGroups.Select(group => group.Id));
        Assert.Same(list.Items[0], list.ItemGroups[0].Items[0]);
        Assert.Same(list.Items[2], list.ItemGroups[0].Items[1]);
        Assert.Same(list.Items[1], Assert.Single(list.ItemGroups[1].Items));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Builder_InvalidGroupIdsThrow(string? id)
    {
        var databaseBuilder = new ExperienceDatabaseBuilder();
        var place = databaseBuilder.Place("test");
        Assert.Throws<ArgumentException>(() => databaseBuilder.Job(experience =>
        {
            experience.Title("job");
            experience.Place(place);
            experience.DateRange(DateRange.Completed(new(2024), new(2025)));
            experience.Group(id!);
        }));
    }

    [Fact]
    public void Builder_EmptyNamedGroupThrows()
    {
        var databaseBuilder = new ExperienceDatabaseBuilder();
        var place = databaseBuilder.Place("test");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            databaseBuilder.Job(experience =>
            {
                experience.Title("job");
                experience.Place(place);
                experience.DateRange(DateRange.Completed(new(2024), new(2025)));
                experience.Group("empty");
            }));

        Assert.Contains("empty", exception.Message, StringComparison.Ordinal);
        Assert.Contains("at least one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UniversityLecturer_HasExpectedNamedGroupMembership()
    {
        var (tags, _) = TagsDatabaseFactory.Create();
        var list = Assert.Single(
            ExperienceDatabaseFactory.Create(tags).Experiences,
            experience => experience.Title.Value == "University Lecturer");

        Assert.Equal(7, list.Items.Length);
        var teaching = Assert.Single(list.ItemGroups, group => group.Id == "teaching");
        var automation = Assert.Single(
            list.ItemGroups,
            group => group.Id == "university-automation");

        Assert.Collection(
            teaching.Items,
            item => Assert.Same(list.Items[0], item),
            item => Assert.Same(list.Items[5], item));
        Assert.Collection(
            automation.Items,
            item => Assert.Same(list.Items[1], item),
            item => Assert.Same(list.Items[2], item),
            item => Assert.Same(list.Items[3], item),
            item => Assert.Same(list.Items[4], item),
            item => Assert.Same(list.Items[6], item));
    }

    [Fact]
    public void Builder_ComposesRequirementAndOrderRulesWithRequestedDsl()
    {
        var databaseBuilder = new ExperienceDatabaseBuilder();
        var place = databaseBuilder.Place("test");
        databaseBuilder.Job(experience =>
        {
            experience.Title("job");
            experience.Place(place);
            experience.DateRange(DateRange.Completed(new(2024), new(2025)));

            var intro = experience.Item(x => x.Text($"intro"));
            experience.Item(x =>
            {
                x.Text($"conditional");
                x.Required().BeforeAny().Required().BeforeAny();
                x.Order.After(intro);
            });
            experience.Item(x =>
            {
                x.Text($"always");
                x.Required().Always().Required().Always();
            });
        });

        var items = Assert.Single(databaseBuilder.Build().Experiences).Items;
        var conditional = items[1];
        var always = items[2];

        Assert.Equal(ItemRequirement.IfAny, conditional.Required);
        Assert.Equal(ItemMove.ToFront, conditional.Order.Move);
        Assert.Same(items[0], Assert.Single(conditional.Order.After));
        Assert.Equal(ItemRequirement.Always, always.Required);
    }

    [Fact]
    public void Builder_TerminalMethodsReturnTheirOwningBuilders()
    {
        var item = new ExperienceItemBuilder();
        var other = new ExperienceItemBuilder();

        Assert.Same(item, item.Required().IfAny());
        Assert.Same(item, item.Required().BeforeAny());
        Assert.Same(item.Order, item.Order.After(other));
        Assert.Same(item.Order, item.Order.Move().ToFront());
    }

    [Theory]
    [InlineData(ItemRequirement.IfAny, ItemRequirement.Always)]
    [InlineData(ItemRequirement.Always, ItemRequirement.IfAny)]
    public void Builder_ConflictingRequirementModesThrowClearConfigurationException(
        ItemRequirement first,
        ItemRequirement second)
    {
        var item = new ExperienceItemBuilder();
        Configure(item, first);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            Configure(item, second));

        Assert.Contains(first.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains(second.ToString(), exception.Message, StringComparison.Ordinal);
        Assert.Contains("cannot", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void Configure(
        ExperienceItemBuilder item,
        ItemRequirement requirement)
    {
        if (requirement == ItemRequirement.IfAny)
        {
            item.Required().IfAny();
        }
        else
        {
            item.Required().Always();
        }
    }
}
