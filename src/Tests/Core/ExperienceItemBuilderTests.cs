using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class ExperienceItemBuilderTests
{
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
