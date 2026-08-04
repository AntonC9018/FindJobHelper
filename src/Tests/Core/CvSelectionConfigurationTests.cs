using System.Text.Json;
using System.Text.Json.Nodes;
using FindJobHelper.CVGeneration;
using MainCli;

namespace FindJobHelper.Core.Tests;

public sealed class CvSelectionConfigurationTests
{
    [Fact]
    public void CvPageCount_HasExplicitExactAndUnrestrictedSemantics()
    {
        var unrestricted = CvPageCount.Unrestricted;
        var exact = CvPageCount.Exact(3);

        Assert.True(unrestricted.IsUnrestricted);
        Assert.False(unrestricted.IsExact);
        Assert.Null(unrestricted.ExactCount);
        Assert.True(exact.IsExact);
        Assert.False(exact.IsUnrestricted);
        Assert.Equal(3, exact.ExactCount);
        Assert.Equal("Exactly 3 pages", exact.ToString());
        Assert.Throws<ArgumentOutOfRangeException>(() => CvPageCount.Exact(0));
    }

    [Fact]
    public void DomainConfiguration_DoesNotExposeJsonPresenceTracking()
    {
        var propertyNames = typeof(CvSelectionConfiguration)
            .GetProperties()
            .Select(static property => property.Name)
            .ToArray();

        Assert.Contains(nameof(CvSelectionConfiguration.PageCount), propertyNames);
        Assert.DoesNotContain("LimitToOnePage", propertyNames);
        Assert.DoesNotContain("IsLimitToOnePageSpecified", propertyNames);
        Assert.DoesNotContain("IsPageCountSpecified", propertyNames);
    }

    [Fact]
    public async Task LoadAsync_MapsSelectionConfiguration()
    {
        var configuration = await CvSelectionConfigurationLoader.LoadAsync(
            FixturePath,
            CancellationToken.None);
        var tagsDatabase = TagsDatabaseFactory.Create().TagsDatabase;

        var search = configuration.BuildSearch(tagsDatabase);

        Assert.Equal(new[] { "E2E Skill" }, search.Skills.Select(x => x.Value));
        Assert.Equal(new[] { "E2E JSON Configuration" }, search.Technologies.Select(x => x.Value));
        Assert.Equal(
            new[]
            {
                Section.WorkExperience,
                Section.PersonalProjects,
                Section.Education,
            },
            search.SectionOrder);
        Assert.Equal("Education", search.Sections.EducationKey.Value);
        Assert.Equal("Work", search.Sections.WorkKey.Value);
        Assert.Equal("PersonalProjects", search.Sections.PersonalProjectsKey.Value);
        Assert.Equal(0, configuration.Selection.Education.MinTotalItemBudget);
        Assert.Equal(1, configuration.Selection.Education.TotalItemBudget);
        Assert.Equal(0, configuration.Selection.Education.RecencyBoost);
        Assert.Equal(0, configuration.Selection.WorkExperience.RecencyBoost);
        Assert.Equal(0, configuration.Selection.PersonalProjects.RecencyBoost);
        Assert.Equal(0f, configuration.Selection.Default.DirectMatchBoost);
        Assert.Equal(0f, configuration.Selection.Education.DirectMatchBoost);
        Assert.Equal(0f, configuration.Selection.WorkExperience.DirectMatchBoost);
        Assert.Equal(0f, configuration.Selection.PersonalProjects.DirectMatchBoost);
        Assert.Equal(CvPageCount.OnePage, configuration.PageCount);
        Assert.Equal(CvPageCount.OnePage, search.PageCount);
        Assert.Null(configuration.PageLayout);
        Assert.Null(search.PageLayout);
    }

    [Fact]
    public async Task LoadAsync_MapsFourPageExplicitLayoutAndDerivesFlattenedOrder()
    {
        var json = await WithSectionOrderAsync(
            """
            [
              { "page": 1, "sections": ["Languages", "Education"] },
              { "pages": "2-3", "sections": ["WorkExperience"] },
              { "page": 4, "sections": ["PersonalProjects"] }
            ]
            """);

        var configuration = await LoadAsync(json);
        var search = configuration.BuildSearch(TagsDatabaseFactory.Create().TagsDatabase);

        var layout = Assert.IsType<CvPageLayout>(configuration.PageLayout);
        Assert.Same(layout, search.PageLayout);
        Assert.Equal(CvPageCount.Exact(4), configuration.PageCount);
        Assert.Equal(CvPageCount.Exact(4), search.PageCount);
        Assert.Equal(4, layout.PageCount);
        Assert.Equal(
            new[]
            {
                Section.Languages,
                Section.Education,
                Section.WorkExperience,
                Section.PersonalProjects,
            },
            layout.SectionOrder);
        Assert.True(layout.SectionOrder.SequenceEqual(configuration.SectionOrder));
        Assert.Collection(
            layout.Blocks,
            block =>
            {
                Assert.Equal(1, block.FirstPage);
                Assert.Equal(1, block.LastPage);
                Assert.Equal(1, block.AllocatedPageCount);
                Assert.Equal(
                    new[] { Section.Languages, Section.Education },
                    block.Sections);
            },
            block =>
            {
                Assert.Equal(2, block.FirstPage);
                Assert.Equal(3, block.LastPage);
                Assert.Equal(2, block.AllocatedPageCount);
                Assert.Equal(new[] { Section.WorkExperience }, block.Sections);
            },
            block =>
            {
                Assert.Equal(4, block.FirstPage);
                Assert.Equal(4, block.LastPage);
                Assert.Equal(new[] { Section.PersonalProjects }, block.Sections);
            });
    }

    [Fact]
    public void SectionOrderCollectionConverter_RoundTripsPageRangeSyntax()
    {
        const string json =
            """[{"page":1,"sections":["Languages"]},{"pages":"2-3","sections":["WorkExperience"]}]""";

        var sectionOrder = JsonSerializer.Deserialize<SectionOrderCollection>(json);

        Assert.NotNull(sectionOrder);
        Assert.Empty(sectionOrder.ValidationErrors);
        Assert.True(sectionOrder.IsExplicit);
        Assert.Equal(
            new[] { Section.Languages, Section.WorkExperience },
            sectionOrder.Sections);
        Assert.Equal(3, Assert.IsType<CvPageLayout>(sectionOrder.PageLayout).PageCount);
        Assert.Equal(json, JsonSerializer.Serialize(sectionOrder));
        Assert.Equal(
            typeof(SectionOrderCollection),
            typeof(JsonCvSelectionConfiguration)
                .GetProperty(nameof(JsonCvSelectionConfiguration.SectionOrder))!
                .PropertyType);
    }

    [Fact]
    public async Task LoadAsync_AcceptsMatchingPageCountWithExplicitLayout()
    {
        var json = await WithSectionOrderAsync(
            """
            [
              { "page": 1, "sections": ["Languages", "Education"] },
              { "pages": "2-3", "sections": ["WorkExperience"] },
              { "page": 4, "sections": ["PersonalProjects"] }
            ]
            """);
        var document = TestJsonTree.Parse(json)
            .Set("pageCount", 4);

        var configuration = await LoadAsync(document.ToJsonString());
        var search = configuration.BuildSearch(TagsDatabaseFactory.Create().TagsDatabase);

        Assert.Equal(CvPageCount.Exact(4), configuration.PageCount);
        Assert.Equal(CvPageCount.Exact(4), search.PageCount);
        Assert.Equal(4, Assert.IsType<CvPageLayout>(configuration.PageLayout).PageCount);
        Assert.Same(configuration.PageLayout, search.PageLayout);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task LoadAsync_RejectsMismatchedPageCountWithExplicitLayout(int pageCount)
    {
        var json = await WithSectionOrderAsync(
            """
            [
              { "page": 1, "sections": ["Languages"] },
              { "pages": "2-4", "sections": ["WorkExperience"] }
            ]
            """);
        var document = TestJsonTree.Parse(json)
            .Set("pageCount", pageCount);

        var exception = await LoadInvalidAsync(document.ToJsonString(), buildSearch: false);

        Assert.Contains($"'pageCount' is {pageCount}", exception.Message, StringComparison.Ordinal);
        Assert.Contains(
            "object-form 'sectionOrder' defines 4 page(s)",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("null")]
    public async Task LoadAsync_RejectsInvalidPageCountWithExplicitLayoutWithoutMismatch(
        string pageCount)
    {
        var json = await WithSectionOrderAsync(
            """[{ "page": 1, "sections": ["WorkExperience"] }]""");
        var document = TestJsonTree.Parse(json)
            .SetJson("pageCount", pageCount);

        var exception = await LoadInvalidAsync(document.ToJsonString(), buildSearch: false);

        Assert.Contains(
            "'pageCount' must be a positive 32-bit integer",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "object-form 'sectionOrder' defines",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RejectsLimitToOnePageWithExplicitLayout()
    {
        var json = await WithSectionOrderAsync(
            """[{ "page": 1, "sections": ["WorkExperience"] }]""");
        var document = TestJsonTree.Parse(json)
            .Set("limitToOnePage", false);

        var exception = await LoadInvalidAsync(document.ToJsonString(), buildSearch: false);

        Assert.Contains("limitToOnePage", exception.Message, StringComparison.Ordinal);
        Assert.Contains("derived", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        """["Languages", { "page": 1, "sections": ["WorkExperience"] }]""",
        "mixed")]
    [InlineData(
        """[{ "pages": "1-1", "sections": ["WorkExperience"] }]""",
        "start is less than end")]
    [InlineData(
        """[{ "pages": "1 - 2", "sections": ["WorkExperience"] }]""",
        "start-end")]
    [InlineData(
        """[{ "page": 1, "pages": "1-2", "sections": ["WorkExperience"] }]""",
        "exactly one")]
    [InlineData(
        """[{ "page": 1, "sections": [] }]""",
        "at least one")]
    [InlineData(
        """[{ "page": 1, "sections": ["NotASection"] }]""",
        "valid section")]
    [InlineData(
        """[{ "page": 1, "sections": ["Languages"] }, { "page": 3, "sections": ["WorkExperience"] }]""",
        "requires page 2")]
    [InlineData(
        """[{ "page": 1, "sections": ["Languages"] }, { "page": 1, "sections": ["WorkExperience"] }]""",
        "overlaps")]
    [InlineData(
        """[{ "page": 2, "sections": ["Languages"] }, { "page": 1, "sections": ["WorkExperience"] }]""",
        "unordered")]
    [InlineData(
        """[{ "page": 1, "sections": ["Languages"] }, { "page": 2, "sections": ["Languages"] }]""",
        "more than once")]
    [InlineData(
        """[{ "page": 1, "sections": ["Languages"], "unexpected": true }]""",
        "unknown property")]
    [InlineData(
        """[{ "page": 1, "sections": ["Languages", "Languages"] }]""",
        "more than once")]
    public async Task LoadAsync_RejectsInvalidExplicitLayouts(
        string sectionOrder,
        string expectedMessage)
    {
        var json = await WithSectionOrderAsync(sectionOrder);

        var exception = await LoadInvalidAsync(json, buildSearch: false);

        Assert.Contains(
            expectedMessage,
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_MapsRecencyBoost()
    {
        var json = (await ReadFixtureTreeAsync())
            .Set("selection.workExperience.recencyBoost", 0.25)
            .ToJsonString();

        var configuration = await LoadAsync(json);

        Assert.Equal(0.25f, configuration.Selection.WorkExperience.RecencyBoost);
        Assert.Equal(0, configuration.Selection.Education.RecencyBoost);
        Assert.Equal(0, configuration.Selection.PersonalProjects.RecencyBoost);
    }

    [Fact]
    public async Task LoadAsync_MapsDirectMatchBoostOverrides()
    {
        var json = (await ReadFixtureTreeAsync())
            .Set("selection.default.directMatchBoost", 0.25)
            .Set("selection.workExperience.directMatchBoost", 0.5)
            .Set("selection.personalProjects.directMatchBoost", 0)
            .ToJsonString();

        var configuration = await LoadAsync(json);

        Assert.Equal(0.25f, configuration.Selection.Default.DirectMatchBoost);
        Assert.Equal(0f, configuration.Selection.Education.DirectMatchBoost);
        Assert.Equal(0.5f, configuration.Selection.WorkExperience.DirectMatchBoost);
        Assert.Equal(0, configuration.Selection.PersonalProjects.DirectMatchBoost);
    }

    [Fact]
    public void SelectionOptionsConfiguration_DirectMatchBoostDefaultsToZero()
    {
        var configuration = new SelectionOptionsConfiguration();
        var options = new SearchPredicateOptions
        {
            DirectMatchBoost = 0.25f,
        };

        configuration.Apply(options);

        Assert.Equal(0f, configuration.DirectMatchBoost);
        Assert.Equal(default, options.DirectMatchBoost);
        Assert.Equal(
            default,
            new SearchPredicateOptions().DirectMatchBoost);
    }

    [Fact]
    public async Task LoadAsync_AllowsCommentsAndTrailingCommas()
    {
        var json = await ReadFixtureAsync();
        json = json.Insert(json.IndexOf('{') + 1, "\n  // Job-specific CV settings");
        json = json.Insert(json.LastIndexOf('}'), ",");

        var configuration = await LoadAsync(json);

        Assert.Equal(CvPageCount.OnePage, configuration.PageCount);
    }

    [Fact]
    public async Task BuildSearch_AlwaysIncludesEveryWorkExperienceHeading()
    {
        var configuration = await CvSelectionConfigurationLoader.LoadAsync(
            FixturePath,
            CancellationToken.None);
        var (tags, tagsDatabase) = TagsDatabaseFactory.Create();
        var database = ExperienceDatabaseFactory.Create(tags);
        var configured = configuration.BuildSearch(tagsDatabase);

        var result = configured.Search.Run(
            database.Experiences,
            NoOpProgressReporter.Instance);

        var expectedTitles = database.Experiences
            .Where(static experience => experience.Type == ExperienceType.Job)
            .OrderByDescending(static experience => experience.DateRange, DateRangeComparer.ByEnd)
            .Select(static experience => experience.Title.Value)
            .ToArray();
        var work = result.Get(configured.Sections.WorkKey);
        Assert.Equal(expectedTitles, work.Select(static experience => experience.Title.Value));
        Assert.Contains(work, static experience => experience.SubItems.IsEmpty);
    }

    [Fact]
    public async Task BuildSearch_DoesNotForceAWorkExperienceItemPerHeading()
    {
        var json = (await ReadFixtureTreeAsync())
            .Set("mmr.relevanceWeight", 0)
            .Set("mmr.saturationPenalty", 0)
            .ToJsonString();
        var configuration = await LoadAsync(json);
        var (tags, tagsDatabase) = TagsDatabaseFactory.Create();
        var database = ExperienceDatabaseFactory.Create(tags);
        var configured = configuration.BuildSearch(tagsDatabase);

        var result = configured.Search.Run(
            database.Experiences,
            NoOpProgressReporter.Instance);

        var work = result.Get(configured.Sections.WorkKey);
        var expectedHeadingCount = database.Experiences.Count(
            static experience => experience.Type == ExperienceType.Job);
        Assert.Equal(expectedHeadingCount, work.Length);
        Assert.All(work, static experience => Assert.Empty(experience.SubItems));
    }

    [Fact]
    public async Task LoadAsync_LegacyPageFlagDefaultsToOnePageAndMapsFalseToUnrestricted()
    {
        var omitted = (await ReadFixtureTreeAsync())
            .Remove("limitToOnePage")
            .ToJsonString();
        var disabled = (await ReadFixtureTreeAsync())
            .Set("limitToOnePage", false)
            .ToJsonString();

        var omittedConfiguration = await LoadAsync(omitted);
        var disabledConfiguration = await LoadAsync(disabled);
        var tags = TagsDatabaseFactory.Create().TagsDatabase;

        Assert.Equal(CvPageCount.OnePage, omittedConfiguration.PageCount);
        Assert.Equal(CvPageCount.OnePage, omittedConfiguration.BuildSearch(tags).PageCount);
        Assert.Equal(CvPageCount.Unrestricted, disabledConfiguration.PageCount);
        Assert.Equal(CvPageCount.Unrestricted, disabledConfiguration.BuildSearch(tags).PageCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public async Task LoadAsync_MapsAnyPositivePageCount(int pageCount)
    {
        var json = (await ReadFixtureTreeAsync())
            .Remove("limitToOnePage")
            .Set("pageCount", pageCount)
            .ToJsonString();

        var configuration = await LoadAsync(json);
        var search = configuration.BuildSearch(TagsDatabaseFactory.Create().TagsDatabase);

        Assert.Equal(CvPageCount.Exact(pageCount), configuration.PageCount);
        Assert.Equal(CvPageCount.Exact(pageCount), search.PageCount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("null")]
    [InlineData("2.5")]
    [InlineData("\"2\"")]
    [InlineData("true")]
    [InlineData("2147483648")]
    public async Task LoadAsync_RejectsInvalidPageCount(string value)
    {
        var json = (await ReadFixtureTreeAsync())
            .Remove("limitToOnePage")
            .SetJson("pageCount", value)
            .ToJsonString();

        var exception = await LoadInvalidAsync(json, buildSearch: false);

        Assert.Contains("pageCount", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RejectsPageCountTogetherWithLegacyFlag()
    {
        var json = (await ReadFixtureTreeAsync())
            .Set("pageCount", 2)
            .ToJsonString();

        var exception = await LoadInvalidAsync(json, buildSearch: false);

        Assert.Contains("cannot both be supplied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RejectsMissingSkills()
    {
        var withoutSkills = (await ReadFixtureTreeAsync())
            .Remove("skills")
            .ToJsonString();

        var exception = await LoadInvalidAsync(withoutSkills, buildSearch: false);

        Assert.Contains("skills", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_RejectsNonBooleanLimitToOnePage()
    {
        var json = (await ReadFixtureTreeAsync())
            .Set("limitToOnePage", "yes")
            .ToJsonString();

        var exception = await LoadInvalidAsync(json, buildSearch: false);

        Assert.Contains("limitToOnePage", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mmr.relevanceWeight", "1.1", "mmr.relevanceWeight")]
    [InlineData("selection.education.totalItemBudget", "-1", "must be non-negative")]
    [InlineData("selection.education.minTotalItemBudget", "-1", "minTotalItemBudget")]
    [InlineData("selection.education.minTotalItemBudget", "2", "must not exceed")]
    [InlineData("selection.education.recencyBoost", "-0.1", "recencyBoost")]
    [InlineData("selection.education.directMatchBoost", "-0.1", "directMatchBoost")]
    public async Task LoadAsync_RejectsInvalidSelectionValues(
        string path,
        string value,
        string expectedMessage)
    {
        var mutated = (await ReadFixtureTreeAsync())
            .SetJson(path, value)
            .ToJsonString();

        var exception = await LoadInvalidAsync(mutated, buildSearch: true);

        Assert.Contains(expectedMessage, exception.Message);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void SelectionOptionsConfiguration_RejectsNonFiniteRecencyBoost(float recencyBoost)
    {
        var options = new SelectionOptionsConfiguration
        {
            ScoreLowerBound = 0,
            RecencyBoost = recencyBoost,
        };
        var errors = new List<string>();

        options.CollectValidationErrors("selection.workExperience", errors);

        Assert.Contains(
            "'selection.workExperience.recencyBoost' must be finite and non-negative.",
            errors);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void SelectionOptionsConfiguration_RejectsNonFiniteDirectMatchBoost(
        float directMatchBoost)
    {
        var options = new SelectionOptionsConfiguration
        {
            ScoreLowerBound = 0,
            DirectMatchBoost = directMatchBoost,
        };
        var errors = new List<string>();

        options.CollectValidationErrors("selection.workExperience", errors);

        Assert.Contains(
            "'selection.workExperience.directMatchBoost' must be finite and non-negative.",
            errors);
    }

    [Fact]
    public async Task LoadAsync_MapsMinimumAndTotalSelectionBudgets()
    {
        var json = (await ReadFixtureTreeAsync())
            .Set("selection.education.minTotalItemBudget", 1)
            .Set("selection.education.totalItemBudget", 2)
            .ToJsonString();

        var configuration = await LoadAsync(json);

        Assert.Equal(1, configuration.Selection.Education.MinTotalItemBudget);
        Assert.Equal(2, configuration.Selection.Education.TotalItemBudget);
    }

    [Fact]
    public async Task LoadAsync_DefaultsMissingSelectionBudgetsToUnboundedRange()
    {
        var json = (await ReadFixtureTreeAsync())
            .Remove("selection.education.totalItemBudget")
            .ToJsonString();

        var configuration = await LoadAsync(json);
        var (tags, tagsDatabase) = TagsDatabaseFactory.Create();
        var database = ExperienceDatabaseFactory.Create(tags);
        var configured = configuration.BuildSearch(tagsDatabase);
        var result = configured.Search.Run(
            database.Experiences,
            NoOpProgressReporter.Instance);
        var educationBudget = Assert.Single(
            result.Diagnostics.Budgets,
            budget => budget.Section == configured.Sections.EducationKey);

        Assert.Equal(0, configuration.Selection.Education.MinTotalItemBudget);
        Assert.Null(configuration.Selection.Education.TotalItemBudget);
        Assert.Equal(0, educationBudget.RequestedMinimum);
        Assert.Equal(int.MaxValue, educationBudget.RequestedMaximum);
    }

    [Fact]
    public async Task LoadAsync_AllowsEmptySelectionAndDefaultsEverySection()
    {
        var json = (await ReadFixtureTreeAsync())
            .Set("selection", new JsonObject())
            .ToJsonString();

        var configuration = await LoadAsync(json);

        Assert.All(configuration.Selection.Options, options =>
        {
            Assert.Equal(0, options.MinTotalItemBudget);
            Assert.Null(options.TotalItemBudget);
            Assert.Equal(0, options.ScoreLowerBound);
            Assert.Equal(0, options.RecencyBoost);
            Assert.Equal(0f, options.DirectMatchBoost);
        });

        _ = configuration.BuildSearch(TagsDatabaseFactory.Create().TagsDatabase);
    }

    [Fact]
    public async Task LoadAsync_AllowsPartialSectionConfiguration()
    {
        var json = (await ReadFixtureTreeAsync())
            .Set(
                "selection",
                new JsonObject
                {
                    ["workExperience"] = new JsonObject
                    {
                        ["recencyBoost"] = 0.25,
                    },
                })
            .ToJsonString();

        var configuration = await LoadAsync(json);

        Assert.Null(configuration.Selection.Education.TotalItemBudget);
        Assert.Equal(0.25f, configuration.Selection.WorkExperience.RecencyBoost);
        Assert.Null(configuration.Selection.PersonalProjects.TotalItemBudget);
    }

    [Fact]
    public async Task LoadAsync_RejectsDuplicateSections()
    {
        var json = await ReadFixtureTreeAsync();
        var sectionOrder = json.Array("sectionOrder");
        var personalProjectsIndex = sectionOrder
            .Select(static (section, index) => (section, index))
            .Single(x => x.section?.GetValue<string>() == "PersonalProjects")
            .index;
        sectionOrder[personalProjectsIndex] = "WorkExperience";

        var exception = await LoadInvalidAsync(json.ToJsonString(), buildSearch: false);

        Assert.Contains("occurs more than once", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_ReportsAllShapeErrorsTogether()
    {
        var invalidJson = await ReadFixtureTreeAsync();
        invalidJson.Array("requiredTags")[0]!.AsObject()["weight"] = 0;
        invalidJson.Array("skills")[0] = " ";
        invalidJson.Array("technologies")[0] = " ";
        invalidJson
            .Set("mmr.relevanceWeight", 1.1)
            .Set("mmr.saturationQuota", 0)
            .Set("mmr.saturationPenalty", -1)
            .Set("selection.education.totalItemBudget", -1)
            .Set("selection.education.scoreLowerBound", -1)
            .Set("selection.workExperience.totalItemBudget", -1)
            .Set("selection.workExperience.scoreLowerBound", -1)
            .Set("selection.personalProjects.totalItemBudget", -1)
            .Set("selection.personalProjects.scoreLowerBound", -1);
        var sectionOrder = invalidJson.Array("sectionOrder");
        var personalProjectsIndex = sectionOrder
            .Select(static (section, index) => (section, index))
            .Single(x => x.section?.GetValue<string>() == "PersonalProjects")
            .index;
        sectionOrder[personalProjectsIndex] = "WorkExperience";

        var exception = await LoadInvalidAsync(
            invalidJson.ToJsonString(),
            buildSearch: false);

        Assert.Equal(13, exception.Errors.Length);
        Assert.Contains(exception.Errors, error => error.Contains("required tag", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(exception.Errors, error => error.Contains("skills", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(exception.Errors, error => error.Contains("technologies", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(exception.Errors, error => error.Contains("mmr.relevanceWeight", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("selection.workExperience.totalItemBudget", StringComparison.Ordinal));
        Assert.Contains(exception.Errors, error => error.Contains("occurs more than once", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadAsync_RejectsUnknownAndDuplicateTags()
    {
        var duplicateTag = await ReadFixtureTreeAsync();
        duplicateTag.Array("requiredTags").Add(new JsonObject
        {
            ["name"] = ".net",
            ["weight"] = 1,
        });
        var duplicateException = await LoadInvalidAsync(
            duplicateTag.ToJsonString(),
            buildSearch: false);
        Assert.Contains("more than once", duplicateException.Message);

        var unknownTag = await ReadFixtureTreeAsync();
        unknownTag.Array("requiredTags")[0]!.AsObject()["name"] =
            "No Such Tag";
        var unknownException = await LoadInvalidAsync(
            unknownTag.ToJsonString(),
            buildSearch: true);
        Assert.Contains("was not found", unknownException.Message);
    }

    [Fact]
    public async Task LoadAsync_RejectsUnknownOrMissingJsonMembers()
    {
        var unknownPropertyException = await LoadInvalidAsync(
            """
            {
              "requiredTags": [{ "name": ".NET", "weight": 1 }],
              "skills": ["Backend Development"],
              "technologies": [".NET"],
              "mmr": { "relevanceWeight": 0.72, "saturationQuota": 2, "saturationPenalty": 0.18 },
              "selection": {
                "education": { "totalItemBudget": 1, "scoreLowerBound": 0 },
                "workExperience": { "totalItemBudget": 1, "scoreLowerBound": 0 },
                "personalProjects": { "totalItemBudget": 1, "scoreLowerBound": 0 }
              },
              "sectionOrder": ["WorkExperience"],
              "unexpected": true
            }
            """,
            buildSearch: false);
        Assert.Contains("could not be mapped", unknownPropertyException.Message, StringComparison.OrdinalIgnoreCase);

        var missingMemberException = await LoadInvalidAsync("{}", buildSearch: false);
        Assert.Contains("required properties", missingMemberException.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<CvConfigurationException> LoadInvalidAsync(string json, bool buildSearch)
    {
        var path = Path.Combine(Path.GetTempPath(), $"FindJobHelper-config-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, json);
            var exception = await Assert.ThrowsAsync<CvConfigurationException>(async () =>
            {
                var configuration = await CvSelectionConfigurationLoader.LoadAsync(
                    path,
                    CancellationToken.None);
                if (buildSearch)
                {
                    configuration.BuildSearch(TagsDatabaseFactory.Create().TagsDatabase);
                }
            });
            return exception;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<CvSelectionConfiguration> LoadAsync(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"FindJobHelper-config-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, json);
            return await CvSelectionConfigurationLoader.LoadAsync(path, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Task<string> ReadFixtureAsync() => File.ReadAllTextAsync(FixturePath);

    private static async Task<TestJsonTree> ReadFixtureTreeAsync() =>
        TestJsonTree.Parse(await ReadFixtureAsync());

    private static async Task<string> WithSectionOrderAsync(string sectionOrder)
    {
        return (await ReadFixtureTreeAsync())
            .Remove("limitToOnePage")
            .Remove("pageCount")
            .SetJson("sectionOrder", sectionOrder)
            .ToJsonString();
    }

    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cli-e2e-config.json");
}
