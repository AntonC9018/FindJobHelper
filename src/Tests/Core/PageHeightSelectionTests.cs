using System.Collections.Immutable;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class PageHeightSelectionTests
{
    private static readonly ExperienceKey EducationKey = new("Education");
    private static readonly ExperienceKey WorkKey = new("Work");
    private static readonly ExperienceKey ProjectsKey = new("Projects");

    [Fact]
    public void Selection_SkipsTallCandidateAndAcceptsLaterFit()
    {
        var tag = new Tag("match");
        var tall = Item("tall", tag, 10);
        var shortItem = Item("short", tag, 9);
        var list = Experience("work", ExperienceType.Job, tall, shortItem);
        var database = Database(list);
        var search = Search(tag, (WorkKey, ExperienceType.Job, 0, 2));
        var policy = Policy(
            database,
            pageHeight: 50,
            itemHeights: [40, 10],
            (WorkKey, Section.WorkExperience));

        var result = search.Run(database, policy);

        Assert.Equal(new[] { "short" }, Texts(result.Get(WorkKey)));
        Assert.Equal(30, policy.CurrentHeight.ScaledPoints);
    }

    [Theory]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void Selection_AcceptsExactPageHeightButRejectsOnePointOver(
        long itemHeight,
        bool expectedSelected)
    {
        var tag = new Tag("match");
        var list = Experience("work", ExperienceType.Job, Item("candidate", tag, 10));
        var database = Database(list);
        var search = Search(tag, (WorkKey, ExperienceType.Job, 0, 1));
        var policy = Policy(
            database,
            pageHeight: 50,
            itemHeights: [itemHeight],
            (WorkKey, Section.WorkExperience));

        var result = search.Run(database, policy);

        Assert.Equal(expectedSelected, !result.Get(WorkKey).IsEmpty);
    }

    [Fact]
    public void Selection_RejectsDependencyClosureAtomically()
    {
        var tag = new Tag("match");
        var dependency = Item("dependency", tag, 1);
        var dependent = Item("dependent", tag, 10, dependency);
        var fallback = Item("fallback", tag, 9);
        var list = Experience("work", ExperienceType.Job, dependency, dependent, fallback);
        var database = Database(list);
        var search = Search(tag, (WorkKey, ExperienceType.Job, 0, 3));
        var policy = Policy(
            database,
            pageHeight: 45,
            itemHeights: [25, 10, 5],
            (WorkKey, Section.WorkExperience));

        var result = search.Run(database, policy);

        Assert.Equal(new[] { "fallback" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Selection_RejectsIfAnyClosureAtomically()
    {
        var tag = new Tag("match");
        var otherTag = new Tag("other");
        var candidate = Item("candidate", tag, 10);
        var conditional = RequiredItem(
            "conditional",
            otherTag,
            10,
            ItemRequirement.IfAny);
        var fallback = Item("fallback", tag, 9);
        var rejectedList = Experience(
            "rejected",
            ExperienceType.Job,
            candidate,
            conditional);
        var fallbackList = Experience(
            "fallback",
            ExperienceType.Job,
            fallback);
        var database = Database(rejectedList, fallbackList);
        var search = Search(tag, (WorkKey, ExperienceType.Job, 0, 3));
        var policy = Policy(
            database,
            pageHeight: 45,
            itemHeights: [10, 20, 5],
            (WorkKey, Section.WorkExperience));

        var result = search.Run(database, policy);

        Assert.Equal(new[] { "fallback" }, Texts(result.Get(WorkKey)));
    }

    [Fact]
    public void Selection_RejectedAlwaysDependencyClosureThrowsRequiredItemException()
    {
        var tag = new Tag("match");
        var otherTag = new Tag("other");
        var dependency = Item("dependency", otherTag, 1);
        var always = RequiredItem(
            "always",
            otherTag,
            10,
            ItemRequirement.Always,
            dependency);
        var list = Experience("required work", ExperienceType.Job, dependency, always);
        var database = Database(list);
        var search = Search(tag, (WorkKey, ExperienceType.Job, 0, 1));
        var policy = Policy(
            database,
            pageHeight: 45,
            itemHeights: [20, 10],
            (WorkKey, Section.WorkExperience));

        var exception = Assert.Throws<RequiredExperienceItemLayoutException>(() =>
            search.Run(database, policy));

        Assert.Equal("required work", exception.ExperienceTitle);
        Assert.Equal("always", exception.ItemText);
        Assert.Contains("one-page", exception.RejectionReason, StringComparison.Ordinal);
        Assert.Contains("required work", exception.Message, StringComparison.Ordinal);
        Assert.Contains("always", exception.Message, StringComparison.Ordinal);
        Assert.Contains(exception.RejectionReason, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_PrioritizesSectionMinimumBeforeHigherScoredDiscretionaryItem()
    {
        var tag = new Tag("match");
        var work = Experience("work", ExperienceType.Job, Item("minimum", tag, 5));
        var project = Experience("project", ExperienceType.Project, Item("higher", tag, 10));
        var database = Database(work, project);
        var search = Search(
            tag,
            (WorkKey, ExperienceType.Job, 1, 1),
            (ProjectsKey, ExperienceType.Project, 0, 1));
        var policy = Policy(
            database,
            pageHeight: 35,
            itemHeights: [10, 10],
            (WorkKey, Section.WorkExperience),
            (ProjectsKey, Section.PersonalProjects));

        var result = search.Run(database, policy);

        Assert.Equal(new[] { "minimum" }, Texts(result.Get(WorkKey)));
        Assert.Empty(result.Get(ProjectsKey));
    }

    [Fact]
    public void Selection_ReservesEveryMandatoryHeadingBeforeSelectingBullets()
    {
        var tag = new Tag("match");
        var newest = Experience("newest", ExperienceType.Job, Item("highest", tag, 10));
        var older = Experience("older", ExperienceType.Job, Item("lower", tag, 9));
        var database = Database(newest, older);
        var search = RequiredWorkHeadingsSearch(tag, maximum: 1);
        var policy = Policy(
            database,
            pageHeight: 25,
            itemHeights: [10, 10],
            (WorkKey, Section.WorkExperience));

        var result = search.Run(database, policy);

        Assert.Equal(new[] { "newest", "older" }, result.Get(WorkKey).Select(static item => item.Title.Value));
        Assert.All(result.Get(WorkKey), static item => Assert.Empty(item.SubItems));
        Assert.Equal(25, policy.CurrentHeight.ScaledPoints);
    }

    [Fact]
    public void Selection_AcceptsMandatoryHeadingAtExactPageHeight()
    {
        var tag = new Tag("match");
        var database = Database(Experience("required job", ExperienceType.Job, Item("point", tag, 10)));
        var policy = Policy(
            database,
            pageHeight: 20,
            itemHeights: [10],
            (WorkKey, Section.WorkExperience));

        var result = RequiredWorkHeadingsSearch(tag, maximum: 0).Run(database, policy);

        Assert.Empty(Assert.Single(result.Get(WorkKey)).SubItems);
        Assert.Equal(20, policy.CurrentHeight.ScaledPoints);
    }

    [Fact]
    public void Selection_ThrowsWhenMandatoryHeadingExceedsPageByOnePoint()
    {
        var tag = new Tag("match");
        var database = Database(Experience("required job", ExperienceType.Job, Item("point", tag, 10)));
        var policy = Policy(
            database,
            pageHeight: 19,
            itemHeights: [10],
            (WorkKey, Section.WorkExperience));

        var exception = Assert.Throws<RequiredExperienceHeadingLayoutException>(() =>
            RequiredWorkHeadingsSearch(tag, maximum: 0).Run(database, policy));

        Assert.Contains("required job", exception.Message, StringComparison.Ordinal);
        Assert.Contains("one-page", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_FirstBulletAfterMandatoryHeadingChargesOnlyChromeUpgrade()
    {
        var tag = new Tag("match");
        var database = Database(Experience("work", ExperienceType.Job, Item("point", tag, 10)));
        var policy = PolicyWithListHeights(
            database,
            pageHeight: 33,
            itemHeights: [10],
            headingHeights: [5],
            chromeHeights: [8],
            (WorkKey, Section.WorkExperience));

        var result = RequiredWorkHeadingsSearch(tag, maximum: 1).Run(database, policy);

        Assert.Equal(new[] { "point" }, Texts(result.Get(WorkKey)));
        Assert.Equal(33, policy.CurrentHeight.ScaledPoints);
    }

    [Fact]
    public void Policy_ChargesIncludedStaticSectionsBeforeSelection()
    {
        var tag = new Tag("match");
        var database = Database(Experience("work", ExperienceType.Job, Item("candidate", tag, 10)));
        var identifiedItem = Assert.Single(database.EnumerateExperienceItems());
        var identifiedList = Assert.Single(database.EnumerateExperienceLists());
        var snapshot = new CvMeasurementSnapshot(
            new Dictionary<ExperienceItemId, LatexHeight> { [identifiedItem.Id] = new(1) },
            new Dictionary<ExperienceListId, LatexHeight> { [identifiedList.Id] = new(1) },
            new Dictionary<ExperienceListId, LatexHeight> { [identifiedList.Id] = new(1) },
            new Dictionary<Section, LatexHeight> { [Section.Languages] = new(20) },
            new Dictionary<Section, LatexHeight>
            {
                [Section.Languages] = new(5),
                [Section.WorkExperience] = new(1),
            },
            new Dictionary<Section, LatexHeight>
            {
                [Section.Languages] = new(5),
                [Section.WorkExperience] = new(1),
            },
            new LatexHeight(10),
            LatexHeight.Zero,
            new LatexHeight(29));

        var exception = Assert.Throws<FixedCvContentLayoutException>(() => new PageLayoutSelectionAdmissionPolicy(
            database,
            snapshot,
            new CvExperienceSectionBindings(new("UnusedEducation"), WorkKey, new("UnusedProjects")),
            [Section.Languages, Section.WorkExperience],
            CvPageCount.OnePage));

        Assert.Contains("Fixed CV content", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Policy_RejectsExperienceChromeSmallerThanHeading()
    {
        var tag = new Tag("match");
        var database = Database(Experience("work", ExperienceType.Job, Item("point", tag, 10)));

        var exception = Assert.Throws<CvMeasurementInvariantException>(() => PolicyWithListHeights(
            database,
            pageHeight: 50,
            itemHeights: [1],
            headingHeights: [6],
            chromeHeights: [5],
            (WorkKey, Section.WorkExperience)));

        Assert.Contains("smaller than its heading", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Selection_SkipsCandidateThatWouldRequireThirdConfiguredPageAndAcceptsLaterFit()
    {
        var tag = new Tag("match");
        var education = Experience("education", ExperienceType.BachelorsDegree);
        var work = Experience("work", ExperienceType.Job, Item("too tall", tag, 10));
        var project = Experience("project", ExperienceType.Project, Item("later fit", tag, 9));
        var database = Database(education, work, project);
        var groups = new[]
        {
            (EducationKey, Section.Education),
            (WorkKey, Section.WorkExperience),
            (ProjectsKey, Section.PersonalProjects),
        };
        var policy = MultiPagePolicy(
            database,
            pageHeight: 50,
            headerHeight: 35,
            footerHeight: 0,
            itemHeights: [30, 5],
            currentChromeHeight: 5,
            freshChromeHeight: 10,
            pageCount: CvPageCount.Exact(2),
            groups);

        var result = SearchWithRequiredHeadings(tag).Run(database, policy);

        Assert.DoesNotContain("too tall", Texts(result.Get(WorkKey)));
        Assert.Equal(new[] { "later fit" }, Texts(result.Get(ProjectsKey)));
        Assert.Equal(2, policy.PredictedPageCount);
    }

    [Fact]
    public void Selection_UnrestrictedModeAllowsMorePagesButKeepsSectionsAtomic()
    {
        var tag = new Tag("match");
        var education = Experience("education", ExperienceType.BachelorsDegree);
        var work = Experience("work", ExperienceType.Job, Item("third-page trigger", tag, 10));
        var project = Experience("project", ExperienceType.Project, Item("also selected", tag, 9));
        var database = Database(education, work, project);
        var groups = new[]
        {
            (EducationKey, Section.Education),
            (WorkKey, Section.WorkExperience),
            (ProjectsKey, Section.PersonalProjects),
        };
        var policy = MultiPagePolicy(
            database,
            pageHeight: 50,
            headerHeight: 35,
            footerHeight: 0,
            itemHeights: [30, 5],
            currentChromeHeight: 5,
            freshChromeHeight: 10,
            pageCount: CvPageCount.Unrestricted,
            groups);

        var result = SearchWithRequiredHeadings(tag).Run(database, policy);

        Assert.Equal(new[] { "third-page trigger" }, Texts(result.Get(WorkKey)));
        Assert.Equal(new[] { "also selected" }, Texts(result.Get(ProjectsKey)));
        Assert.Equal(3, policy.PredictedPageCount);
    }

    [Fact]
    public void Selection_UnrestrictedModeStillRejectsAnOversizedIndividualSection()
    {
        var tag = new Tag("match");
        var database = Database(
            Experience("work", ExperienceType.Job, Item("oversized", tag, 10)));
        var policy = MultiPagePolicy(
            database,
            pageHeight: 50,
            headerHeight: 0,
            footerHeight: 0,
            itemHeights: [40],
            currentChromeHeight: 5,
            freshChromeHeight: 10,
            pageCount: CvPageCount.Unrestricted,
            [(WorkKey, Section.WorkExperience)]);

        var result = Search(tag, (WorkKey, ExperienceType.Job, 0, 1)).Run(database, policy);

        Assert.Empty(result.Get(WorkKey));
        Assert.Equal(1, policy.PredictedPageCount);
    }

    [Fact]
    public void Policy_ExactConfiguredCountFailsWhenSelectionUsesFewerPages()
    {
        var tag = new Tag("match");
        var database = Database(
            Experience("work", ExperienceType.Job, Item("small", tag, 10)));
        var policy = MultiPagePolicy(
            database,
            pageHeight: 100,
            headerHeight: 10,
            footerHeight: 0,
            itemHeights: [5],
            currentChromeHeight: 5,
            freshChromeHeight: 10,
            pageCount: CvPageCount.Exact(2),
            [(WorkKey, Section.WorkExperience)]);
        _ = Search(tag, (WorkKey, ExperienceType.Job, 0, 1)).Run(database, policy);

        var exception = Assert.Throws<PredictedPageCountMismatchException>(
            policy.RequireExactPageCount);

        Assert.Equal(2, exception.ConfiguredPageCount);
        Assert.Equal(1, exception.PredictedPageCount);
    }

    [Fact]
    public void Policy_RejectsStaticSectionWhoseFreshPageRepresentationIsTooTall()
    {
        var tag = new Tag("match");
        var database = Database(
            Experience("work", ExperienceType.Job, Item("small", tag, 10)));
        var identifiedItem = Assert.Single(database.EnumerateExperienceItems());
        var identifiedList = Assert.Single(database.EnumerateExperienceLists());
        var currentChrome = new Dictionary<Section, LatexHeight>
        {
            [Section.Languages] = new(5),
            [Section.WorkExperience] = new(5),
        };
        var freshChrome = new Dictionary<Section, LatexHeight>
        {
            [Section.Languages] = new(20),
            [Section.WorkExperience] = new(10),
        };
        var snapshot = new CvMeasurementSnapshot(
            new Dictionary<ExperienceItemId, LatexHeight> { [identifiedItem.Id] = new(1) },
            new Dictionary<ExperienceListId, LatexHeight> { [identifiedList.Id] = new(5) },
            new Dictionary<ExperienceListId, LatexHeight> { [identifiedList.Id] = new(5) },
            new Dictionary<Section, LatexHeight> { [Section.Languages] = new(40) },
            currentChrome,
            freshChrome,
            LatexHeight.Zero,
            LatexHeight.Zero,
            new(50));

        var exception = Assert.Throws<FixedCvContentLayoutException>(() =>
            new PageLayoutSelectionAdmissionPolicy(
                database,
                snapshot,
                Bindings([(WorkKey, Section.WorkExperience)]),
                [Section.Languages, Section.WorkExperience],
                CvPageCount.Unrestricted));

        Assert.Contains("Languages", exception.Message, StringComparison.Ordinal);
        Assert.Contains("fresh page", exception.Message, StringComparison.Ordinal);
    }

    private static ExperienceSearch Search(
        Tag tag,
        params (ExperienceKey Key, ExperienceType Type, int Minimum, int Maximum)[] groups)
    {
        var builder = new SearchBuilder();
        builder.Tags(new WeightedTags { [tag] = 1 });
        foreach (var group in groups)
        {
            builder.Configure(
                group.Key,
                experience => experience.Type == group.Type,
                options =>
                {
                    options.MinTotalItemBudget = group.Minimum;
                    options.TotalItemBudget = group.Maximum;
                });
        }
        return builder.Build();
    }

    private static ExperienceSearch RequiredWorkHeadingsSearch(Tag tag, int maximum)
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

    private static ExperienceSearch SearchWithRequiredHeadings(Tag tag)
    {
        var builder = new SearchBuilder();
        builder.Tags(new WeightedTags { [tag] = 1 });
        builder.Configure(
            EducationKey,
            static experience => experience.Type.IsDegree(),
            static options =>
            {
                options.TotalItemBudget = 1;
                options.IncludeEmptyLists = true;
            });
        builder.Configure(
            WorkKey,
            static experience => experience.Type == ExperienceType.Job,
            static options =>
            {
                options.TotalItemBudget = 1;
                options.IncludeEmptyLists = true;
            });
        builder.Configure(
            ProjectsKey,
            static experience => experience.Type == ExperienceType.Project,
            static options =>
            {
                options.TotalItemBudget = 1;
                options.IncludeEmptyLists = true;
            });
        return builder.Build();
    }

    private static PageLayoutSelectionAdmissionPolicy Policy(
        ExperienceDatabase database,
        long pageHeight,
        long[] itemHeights,
        params (ExperienceKey Key, Section Section)[] groups)
    {
        var listCount = database.Experiences.Length;
        return PolicyWithListHeights(
            database,
            pageHeight,
            itemHeights,
            Enumerable.Repeat(5L, listCount).ToArray(),
            Enumerable.Repeat(5L, listCount).ToArray(),
            groups);
    }

    private static PageLayoutSelectionAdmissionPolicy PolicyWithListHeights(
        ExperienceDatabase database,
        long pageHeight,
        long[] itemHeights,
        long[] headingHeights,
        long[] chromeHeights,
        params (ExperienceKey Key, Section Section)[] groups)
    {
        var items = database.EnumerateExperienceItems().ToArray();
        var lists = database.EnumerateExperienceLists().ToArray();
        Assert.Equal(items.Length, itemHeights.Length);
        Assert.Equal(lists.Length, headingHeights.Length);
        Assert.Equal(lists.Length, chromeHeights.Length);
        var snapshot = new CvMeasurementSnapshot(
            items.Select((item, index) => (item.Id, Height: new LatexHeight(itemHeights[index])))
                .ToDictionary(static x => x.Id, static x => x.Height),
            lists.Select((list, index) => (list.Id, Height: new LatexHeight(headingHeights[index])))
                .ToDictionary(static x => x.Id, static x => x.Height),
            lists.Select((list, index) => (list.Id, Height: new LatexHeight(chromeHeights[index])))
                .ToDictionary(static x => x.Id, static x => x.Height),
            new Dictionary<Section, LatexHeight>(),
            groups.Select(static group => group.Section).Distinct().ToDictionary(
                static section => section,
                static _ => new LatexHeight(5)),
            groups.Select(static group => group.Section).Distinct().ToDictionary(
                static section => section,
                static _ => new LatexHeight(5)),
            new LatexHeight(10),
            LatexHeight.Zero,
            new LatexHeight(pageHeight));
        return new(
            database,
            snapshot,
            Bindings(groups),
            groups.Select(static group => group.Section).ToImmutableArray(),
            CvPageCount.OnePage);
    }

    private static PageLayoutSelectionAdmissionPolicy MultiPagePolicy(
        ExperienceDatabase database,
        long pageHeight,
        long headerHeight,
        long footerHeight,
        long[] itemHeights,
        long currentChromeHeight,
        long freshChromeHeight,
        CvPageCount pageCount,
        params (ExperienceKey Key, Section Section)[] groups)
    {
        var items = database.EnumerateExperienceItems().ToArray();
        var lists = database.EnumerateExperienceLists().ToArray();
        Assert.Equal(items.Length, itemHeights.Length);
        var sections = groups.Select(static group => group.Section).Distinct().ToArray();
        var snapshot = new CvMeasurementSnapshot(
            items.Select((item, index) => (item.Id, Height: new LatexHeight(itemHeights[index])))
                .ToDictionary(static x => x.Id, static x => x.Height),
            lists.ToDictionary(static list => list.Id, static _ => new LatexHeight(5)),
            lists.ToDictionary(static list => list.Id, static _ => new LatexHeight(5)),
            new Dictionary<Section, LatexHeight>(),
            sections.ToDictionary(
                static section => section,
                _ => new LatexHeight(currentChromeHeight)),
            sections.ToDictionary(
                static section => section,
                _ => new LatexHeight(freshChromeHeight)),
            new(headerHeight),
            new(footerHeight),
            new(pageHeight));
        return new(
            database,
            snapshot,
            Bindings(groups),
            groups.Select(static group => group.Section).ToImmutableArray(),
            pageCount);
    }

    private static CvExperienceSectionBindings Bindings(
        IReadOnlyCollection<(ExperienceKey Key, Section Section)> groups)
    {
        var education = groups.FirstOrDefault(static group => group.Section == Section.Education).Key;
        var work = groups.FirstOrDefault(static group => group.Section == Section.WorkExperience).Key;
        var projects = groups.FirstOrDefault(static group => group.Section == Section.PersonalProjects).Key;
        return new(
            education.Value is null ? new("UnusedEducation") : education,
            work.Value is null ? new("UnusedWork") : work,
            projects.Value is null ? new("UnusedProjects") : projects);
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
        int score,
        params ExperienceListItem[] dependencies)
        => new()
        {
            Text = RichText.Create($"{new PlainText { Text = text }}"),
            Tags = [new(tag, score)],
            DependsOn = [.. dependencies],
        };

    private static ExperienceListItem RequiredItem(
        string text,
        Tag tag,
        int score,
        ItemRequirement requirement,
        params ExperienceListItem[] dependencies)
        => new()
        {
            Text = RichText.Create($"{new PlainText { Text = text }}"),
            Tags = [new(tag, score)],
            DependsOn = [.. dependencies],
            Required = requirement,
        };

    private static string[] Texts(ImmutableArray<Event> events)
        => events.SelectMany(static item => item.SubItems)
            .Select(static item => item.String.ToString())
            .ToArray();
}
