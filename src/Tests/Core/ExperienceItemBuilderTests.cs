using FindJobHelper.Core.Helper;
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

    [Fact]
    public void Builder_PreservesDirectRichTextNodesAndBuildsInterpolatedText()
    {
        var directDescription = new Href
        {
            Url = new Uri("https://example.test/description"),
            Text = new PlainText { Text = "description" },
        };
        var directItemText = new StyledText
        {
            Text = "direct",
            Style = StyleFlags.Bold,
        };
        var databaseBuilder = new ExperienceDatabaseBuilder();
        var place = databaseBuilder.Place("test");
        databaseBuilder.Job(experience =>
        {
            experience.Title("job");
            experience.Place(place);
            experience.DateRange(DateRange.Completed(new(2024), new(2025)));
            experience.Description(directDescription);
            experience.Item(item => item.Text(directItemText));
            experience.Item(item => item.Text($"interpolated {RichTextFactory.Code("text")}"));
        });

        var list = Assert.Single(databaseBuilder.Build().Experiences);

        Assert.Same(directDescription, list.Description);
        Assert.Same(directItemText, list.Items[0].Text);
        Assert.IsType<RichText>(list.Items[1].Text);
    }

    [Fact]
    public void Builder_ConvertsPlainDescriptionToPlainText()
    {
        var databaseBuilder = new ExperienceDatabaseBuilder();
        var place = databaseBuilder.Place("test");
        databaseBuilder.Job(experience =>
        {
            experience.Title("job");
            experience.Place(place);
            experience.DateRange(DateRange.Completed(new(2024), new(2025)));
            experience.Description(@"\textbf{literal}");
        });

        var description = Assert.IsType<PlainText>(
            Assert.Single(databaseBuilder.Build().Experiences).Description);

        Assert.Equal(@"\textbf{literal}", description.Text);
        Assert.Equal(@"\textbackslash{}textbf\{literal\}", description.ToLatexString());
    }

    [Fact]
    public void Builder_BuildsInterpolatedDescriptionAsRichText()
    {
        var databaseBuilder = new ExperienceDatabaseBuilder();
        var place = databaseBuilder.Place("test");
        databaseBuilder.Job(experience =>
        {
            experience.Title("job");
            experience.Place(place);
            experience.DateRange(DateRange.Completed(new(2024), new(2025)));
            experience.Description($"interpolated {RichTextFactory.Italic("description")}");
        });

        var description = Assert.IsType<RichText>(
            Assert.Single(databaseBuilder.Build().Experiences).Description);

        Assert.Collection(
            description.Items,
            item => Assert.IsType<PlainText>(item),
            item => Assert.IsType<StyledText>(item));
    }

    [Fact]
    public void RequiredTextConstructorsAndBuildersRejectNull()
    {
        var item = new ExperienceItemBuilder();
        var list = new ExperienceDatabaseBuilder();
        var place = list.Place("test");

        Assert.Throws<ArgumentNullException>(() => item.Text((IRichTextNode) null!));
        Assert.Throws<ArgumentNullException>(() => new SubEvent(0, null!));
        Assert.Throws<ArgumentNullException>(() => new ExperienceListItem { Text = null! });

        Assert.Throws<ArgumentNullException>(() => list.Job(experience =>
        {
            experience.Title("job");
            experience.Place(place);
            experience.DateRange(DateRange.Completed(new(2024), new(2025)));
            experience.Description((IRichTextNode) null!);
        }));
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
