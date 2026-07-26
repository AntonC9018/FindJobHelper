using System.Collections.Immutable;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class ExplicitPageLayoutCalculatorTests
{
    [Fact]
    public void MultipleSectionsShareOneDeclaredPage()
    {
        var layout = Layout(
            new CvPageLayoutBlock(
                1,
                1,
                [Section.WorkExperience, Section.Education]));

        var result = Calculate(
            layout,
            header: 10,
            footer: 10,
            [
                new(Section.WorkExperience, "Job", new(30), new(35)),
                new(Section.Education, "Degree", new(40), new(45)),
            ]);

        Assert.True(result.Fits);
        var block = Assert.Single(result.Blocks);
        Assert.Equal(1, block.NaturallyOccupiedPageCount);
        Assert.Equal(new[] { 1, 1 }, block.Placements.Select(static x => x.PageNumber));
    }

    [Fact]
    public void AtomicEventMovesIntactAndMakesSectionNaturallySpanTwoPages()
    {
        var layout = Layout(
            new CvPageLayoutBlock(1, 2, [Section.WorkExperience]));

        var result = Calculate(
            layout,
            header: 0,
            footer: 0,
            [
                new(Section.WorkExperience, "First", new(60), new(65)),
                new(Section.WorkExperience, "Second", new(50), new(55)),
            ]);

        Assert.True(result.Fits);
        var block = Assert.Single(result.Blocks);
        Assert.Equal(2, block.NaturallyOccupiedPageCount);
        Assert.Equal(new[] { 1, 2 }, block.Placements.Select(static x => x.PageNumber));
        Assert.False(block.Placements[0].UsesFreshPageRepresentation);
        Assert.True(block.Placements[1].UsesFreshPageRepresentation);
        Assert.Equal(55, block.Placements[1].Height.ScaledPoints);
    }

    [Fact]
    public void EventTooTallForFreshPageReportsSectionAndEvent()
    {
        var layout = Layout(
            new CvPageLayoutBlock(1, 1, [Section.WorkExperience]));

        var result = Calculate(
            layout,
            header: 0,
            footer: 0,
            [new(Section.WorkExperience, "Oversized job", new(99), new(101))]);

        Assert.False(result.Fits);
        Assert.Equal(ExplicitPageLayoutFailureKind.EventOverflow, result.Failure!.Kind);
        Assert.Equal(Section.WorkExperience, result.Failure.Section);
        Assert.Equal("Oversized job", result.Failure.EventTitle);
    }

    [Fact]
    public void HeaderAndFooterReducePhysicalCapacityButNotNaturalUsage()
    {
        var layout = Layout(
            new CvPageLayoutBlock(1, 2, [Section.WorkExperience]));

        var result = Calculate(
            layout,
            header: 80,
            footer: 20,
            [new(Section.WorkExperience, "Job", new(30), new(35))]);

        Assert.True(result.Fits);
        var block = Assert.Single(result.Blocks);
        Assert.Equal(1, block.NaturallyOccupiedPageCount);
        Assert.Equal(2, block.PhysicalFirstContentPage);
        Assert.Equal(2, block.PhysicalLastContentPage);
    }

    [Fact]
    public void FooterReservationAloneCannotMakeContentSatisfyASecondPage()
    {
        var layout = Layout(
            new CvPageLayoutBlock(1, 2, [Section.WorkExperience]));

        var result = Calculate(
            layout,
            header: 0,
            footer: 80,
            [new(Section.WorkExperience, "Job", new(90), new(95))]);

        Assert.True(result.Fits);
        var block = Assert.Single(result.Blocks);
        Assert.Equal(1, block.NaturallyOccupiedPageCount);
        Assert.Equal(1, block.PhysicalLastContentPage);
    }

    private static ExplicitPageLayoutResult Calculate(
        CvPageLayout layout,
        long header,
        long footer,
        params ExplicitPageLayoutUnit[] units)
        => ExplicitPageLayoutCalculator.Calculate(
            new(100),
            new(header),
            new(footer),
            layout,
            [units]);

    private static CvPageLayout Layout(params CvPageLayoutBlock[] blocks)
        => new([.. blocks]);
}

public sealed class ExplicitPageLayoutSelectionTests
{
    private static readonly ExperienceKey EducationKey = new("Education");
    private static readonly ExperienceKey WorkKey = new("Work");
    private static readonly ExperienceKey ProjectsKey = new("Projects");

    [Fact]
    public void CandidateCrossingBlockBoundaryIsRejectedAndLaterFitIsSelected()
    {
        var tag = new Tag("match");
        var work = Experience(
            "Job",
            ExperienceType.Job,
            Item("too tall", tag, 10),
            Item("later fit", tag, 9));
        var database = Database(work);
        var layout = new CvPageLayout([
            new(1, 1, [Section.WorkExperience]),
            new(2, 2, [Section.Languages]),
        ]);
        var policy = Policy(
            database,
            layout,
            pageHeight: 100,
            headerHeight: 20,
            footerHeight: 0,
            itemHeights: [75, 10],
            eventBaseHeight: 5,
            sectionStartHeight: 5);

        var result = WorkSearch(tag, maximum: 1).Run(database, policy);

        Assert.Equal(
            new[] { "later fit" },
            result.Get(WorkKey)
                .SelectMany(static @event => @event.SubItems)
                .Select(static item => item.Text.ToString()));
    }

    [Fact]
    public void HeaderCannotSatisfyAnUnderfilledTwoPageRange()
    {
        var tag = new Tag("match");
        var database = Database(
            Experience("Job", ExperienceType.Job, Item("unused", tag, 1)));
        var layout = new CvPageLayout([
            new(1, 2, [Section.WorkExperience]),
        ]);
        var policy = Policy(
            database,
            layout,
            pageHeight: 100,
            headerHeight: 80,
            footerHeight: 0,
            itemHeights: [1],
            eventBaseHeight: 30,
            sectionStartHeight: 0);

        _ = WorkSearch(tag, maximum: 0).Run(database, policy);
        var exception = Assert.Throws<CvPageLayoutUnderfillException>(
            policy.RequireCompletePageLayout);

        Assert.Equal("1-2", exception.ConfiguredPages);
        Assert.Equal(new[] { Section.WorkExperience }, exception.AssignedSections);
        Assert.Equal(2, exception.RequiredPageCount);
        Assert.Equal(1, exception.NaturallyOccupiedPageCount);
    }

    [Fact]
    public void OneSectionCanNaturallyCompleteAConfiguredTwoPageRange()
    {
        var tag = new Tag("match");
        var database = Database(
            Experience("Newest", ExperienceType.Job, Item("unused 1", tag, 1)),
            Experience("Older", ExperienceType.Job, Item("unused 2", tag, 1)));
        var layout = new CvPageLayout([
            new(1, 2, [Section.WorkExperience]),
        ]);
        var policy = Policy(
            database,
            layout,
            pageHeight: 100,
            headerHeight: 0,
            footerHeight: 0,
            itemHeights: [1, 1],
            eventBaseHeight: 60,
            sectionStartHeight: 5);

        _ = WorkSearch(tag, maximum: 0).Run(database, policy);

        policy.RequireCompletePageLayout();
    }

    [Fact]
    public void CompletenessChecksEveryBlock()
    {
        var tag = new Tag("match");
        var database = Database(
            Experience("Job", ExperienceType.Job, Item("unused work", tag, 1)),
            Experience("Project", ExperienceType.Project, Item("unused project", tag, 1)));
        var layout = new CvPageLayout([
            new(1, 1, [Section.WorkExperience]),
            new(2, 2, [Section.PersonalProjects]),
        ]);
        var policy = Policy(
            database,
            layout,
            pageHeight: 100,
            headerHeight: 0,
            footerHeight: 0,
            itemHeights: [1, 1],
            eventBaseHeight: 30,
            sectionStartHeight: 5);

        _ = WorkAndProjectSearch(tag).Run(database, policy);

        policy.RequireCompletePageLayout();
    }

    [Fact]
    public void EmptyFirstBlockFailsEvenThoughHeaderAndForcedBreakCouldProduceTotalPages()
    {
        var tag = new Tag("match");
        var database = Database(
            Experience("Job", ExperienceType.Job, Item("unused", tag, 1)));
        var layout = new CvPageLayout([
            new(1, 1, [Section.Languages]),
            new(2, 2, [Section.WorkExperience]),
        ]);
        var policy = Policy(
            database,
            layout,
            pageHeight: 100,
            headerHeight: 20,
            footerHeight: 0,
            itemHeights: [1],
            eventBaseHeight: 30,
            sectionStartHeight: 5);

        _ = WorkSearch(tag, maximum: 0).Run(database, policy);
        var exception = Assert.Throws<CvPageLayoutUnderfillException>(
            policy.RequireCompletePageLayout);

        Assert.Equal("1", exception.ConfiguredPages);
        Assert.Equal(0, exception.NaturallyOccupiedPageCount);
        Assert.Equal(new[] { Section.Languages }, exception.AssignedSections);
    }

    private static ExperienceSearch WorkSearch(Tag tag, int maximum)
    {
        var builder = new SearchBuilder();
        builder.Tags(new WeightedTags { [tag] = 1 });
        builder.Configure(
            WorkKey,
            static experience => experience.Type == ExperienceType.Job,
            options =>
            {
                options.TotalItemBudget = maximum;
                options.IncludeEmptyLists = true;
            });
        return builder.Build();
    }

    private static ExperienceSearch WorkAndProjectSearch(Tag tag)
    {
        var builder = new SearchBuilder();
        builder.Tags(new WeightedTags { [tag] = 1 });
        builder.Configure(
            WorkKey,
            static experience => experience.Type == ExperienceType.Job,
            static options =>
            {
                options.TotalItemBudget = 0;
                options.IncludeEmptyLists = true;
            });
        builder.Configure(
            ProjectsKey,
            static experience => experience.Type == ExperienceType.Project,
            static options =>
            {
                options.TotalItemBudget = 0;
                options.IncludeEmptyLists = true;
            });
        return builder.Build();
    }

    private static PageLayoutSelectionAdmissionPolicy Policy(
        ExperienceDatabase database,
        CvPageLayout layout,
        long pageHeight,
        long headerHeight,
        long footerHeight,
        long[] itemHeights,
        long eventBaseHeight,
        long sectionStartHeight)
    {
        var items = database.EnumerateExperienceItems().ToArray();
        var lists = database.EnumerateExperienceLists().ToArray();
        Assert.Equal(items.Length, itemHeights.Length);
        var sections = layout.SectionOrder;
        var currentChrome = sections.ToDictionary(
            static section => section,
            _ => new LatexHeight(sectionStartHeight));
        var freshChrome = sections.ToDictionary(
            static section => section,
            _ => new LatexHeight(sectionStartHeight));
        var completeSections = sections.ToDictionary(
            static section => section,
            static _ => LatexHeight.Zero);
        var snapshot = new CvMeasurementSnapshot(
            items.Select((item, index) => (item.Id, Height: new LatexHeight(itemHeights[index])))
                .ToDictionary(static pair => pair.Id, static pair => pair.Height),
            lists.ToDictionary(static list => list.Id, _ => new LatexHeight(eventBaseHeight)),
            lists.ToDictionary(static list => list.Id, _ => new LatexHeight(eventBaseHeight)),
            completeSections,
            currentChrome,
            freshChrome,
            new(headerHeight),
            new(footerHeight),
            new(pageHeight),
            currentChrome,
            freshChrome,
            splitSectionEnd: LatexHeight.Zero,
            freshPageContinuation: LatexHeight.Zero);
        return new(
            database,
            snapshot,
            new(EducationKey, WorkKey, ProjectsKey),
            layout.SectionOrder,
            CvPageCount.Exact(layout.PageCount),
            layout);
    }

    private static ExperienceDatabase Database(params ExperienceList[] lists)
        => new() { AllPlaces = [], Experiences = [.. lists] };

    private static ExperienceList Experience(
        string title,
        ExperienceType type,
        params ExperienceListItem[] items)
        => new()
        {
            Title = title,
            Place = Place.Personal,
            DateRange = DateRange.Completed(new(2020), new(2021)),
            Type = type,
            Items = [.. items],
        };

    private static ExperienceListItem Item(
        string text,
        Tag tag,
        int score)
        => new()
        {
            Text = RichText.Create($"{new PlainText { Text = text }}"),
            Tags = [new(tag, score)],
        };
}
