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
        Assert.Equal(CvPageCount.OnePage, configuration.PageCount);
        Assert.Equal(CvPageCount.OnePage, search.PageCount);
    }

    [Fact]
    public async Task LoadAsync_MapsRecencyBoost()
    {
        var json = (await ReadFixtureAsync()).Replace(
            "\"workExperience\": {",
            "\"workExperience\": {\n      \"recencyBoost\": 0.25,",
            StringComparison.Ordinal);

        var configuration = await LoadAsync(json);

        Assert.Equal(0.25f, configuration.Selection.WorkExperience.RecencyBoost);
        Assert.Equal(0, configuration.Selection.Education.RecencyBoost);
        Assert.Equal(0, configuration.Selection.PersonalProjects.RecencyBoost);
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

        var result = configured.Search.Run(database.Experiences);

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
        var json = (await ReadFixtureAsync())
            .Replace(
                "\"relevanceWeight\": 0.72",
                "\"relevanceWeight\": 0",
                StringComparison.Ordinal)
            .Replace(
                "\"saturationPenalty\": 0.18",
                "\"saturationPenalty\": 0",
                StringComparison.Ordinal);
        var configuration = await LoadAsync(json);
        var (tags, tagsDatabase) = TagsDatabaseFactory.Create();
        var database = ExperienceDatabaseFactory.Create(tags);
        var configured = configuration.BuildSearch(tagsDatabase);

        var result = configured.Search.Run(database.Experiences);

        var work = result.Get(configured.Sections.WorkKey);
        var expectedHeadingCount = database.Experiences.Count(
            static experience => experience.Type == ExperienceType.Job);
        Assert.Equal(expectedHeadingCount, work.Length);
        Assert.All(work, static experience => Assert.Empty(experience.SubItems));
    }

    [Fact]
    public async Task LoadAsync_LegacyPageFlagDefaultsToOnePageAndMapsFalseToUnrestricted()
    {
        var json = await ReadFixtureAsync();
        var omitted = json.Replace("  \"limitToOnePage\": true,\r\n", "", StringComparison.Ordinal)
            .Replace("  \"limitToOnePage\": true,\n", "", StringComparison.Ordinal);
        var disabled = json.Replace(
            "\"limitToOnePage\": true",
            "\"limitToOnePage\": false",
            StringComparison.Ordinal);

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
        var json = (await ReadFixtureAsync()).Replace(
            "\"limitToOnePage\": true",
            $"\"pageCount\": {pageCount}",
            StringComparison.Ordinal);

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
        var json = (await ReadFixtureAsync()).Replace(
            "\"limitToOnePage\": true",
            $"\"pageCount\": {value}",
            StringComparison.Ordinal);

        var exception = await LoadInvalidAsync(json, buildSearch: false);

        Assert.Contains("pageCount", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RejectsPageCountTogetherWithLegacyFlag()
    {
        var json = (await ReadFixtureAsync()).Replace(
            "\"limitToOnePage\": true,",
            "\"limitToOnePage\": true,\n  \"pageCount\": 2,",
            StringComparison.Ordinal);

        var exception = await LoadInvalidAsync(json, buildSearch: false);

        Assert.Contains("cannot both be supplied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RejectsMissingSkills()
    {
        var json = await ReadFixtureAsync();
        var withoutSkills = json
            .Replace("  \"skills\": [\r\n    \"E2E Skill\"\r\n  ],\r\n", "", StringComparison.Ordinal)
            .Replace("  \"skills\": [\n    \"E2E Skill\"\n  ],\n", "", StringComparison.Ordinal);

        var exception = await LoadInvalidAsync(withoutSkills, buildSearch: false);

        Assert.Contains("skills", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadAsync_RejectsNonBooleanLimitToOnePage()
    {
        var json = (await ReadFixtureAsync()).Replace(
            "\"limitToOnePage\": true",
            "\"limitToOnePage\": \"yes\"",
            StringComparison.Ordinal);

        var exception = await LoadInvalidAsync(json, buildSearch: false);

        Assert.Contains("limitToOnePage", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"relevanceWeight\": 0.72", "\"relevanceWeight\": 1.1", "mmr.relevanceWeight")]
    [InlineData("\"totalItemBudget\": 1", "\"totalItemBudget\": -1", "must be non-negative")]
    [InlineData("\"totalItemBudget\": 1", "\"minTotalItemBudget\": -1, \"totalItemBudget\": 1", "minTotalItemBudget")]
    [InlineData("\"totalItemBudget\": 1", "\"minTotalItemBudget\": 2, \"totalItemBudget\": 1", "must not exceed")]
    [InlineData("\"scoreLowerBound\": 0", "\"scoreLowerBound\": 0, \"recencyBoost\": -0.1", "recencyBoost")]
    public async Task LoadAsync_RejectsInvalidSelectionValues(
        string oldValue,
        string newValue,
        string expectedMessage)
    {
        var json = await ReadFixtureAsync();
        var mutated = json.Replace(oldValue, newValue, StringComparison.Ordinal);

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

    [Fact]
    public async Task LoadAsync_MapsMinimumAndTotalSelectionBudgets()
    {
        var json = (await ReadFixtureAsync()).Replace(
            "\"totalItemBudget\": 1",
            "\"minTotalItemBudget\": 1, \"totalItemBudget\": 2",
            StringComparison.Ordinal);

        var configuration = await LoadAsync(json);

        Assert.Equal(1, configuration.Selection.Education.MinTotalItemBudget);
        Assert.Equal(2, configuration.Selection.Education.TotalItemBudget);
    }

    [Fact]
    public async Task LoadAsync_DefaultsMissingSelectionBudgetsToUnboundedRange()
    {
        var json = await ReadFixtureAsync();
        var budgetIndex = json.IndexOf("\"totalItemBudget\": 1,", StringComparison.Ordinal);
        json = json.Remove(budgetIndex, "\"totalItemBudget\": 1,".Length);

        var configuration = await LoadAsync(json);
        var (tags, tagsDatabase) = TagsDatabaseFactory.Create();
        var database = ExperienceDatabaseFactory.Create(tags);
        var configured = configuration.BuildSearch(tagsDatabase);
        var result = configured.Search.Run(database.Experiences);
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
        var json = ReplaceSelection(await ReadFixtureAsync(), new JsonObject());

        var configuration = await LoadAsync(json);

        Assert.All(configuration.Selection.Options, options =>
        {
            Assert.Equal(0, options.MinTotalItemBudget);
            Assert.Null(options.TotalItemBudget);
            Assert.Equal(0, options.ScoreLowerBound);
            Assert.Equal(0, options.RecencyBoost);
        });

        _ = configuration.BuildSearch(TagsDatabaseFactory.Create().TagsDatabase);
    }

    [Fact]
    public async Task LoadAsync_AllowsPartialSectionConfiguration()
    {
        var json = ReplaceSelection(
            await ReadFixtureAsync(),
            new JsonObject
            {
                ["workExperience"] = new JsonObject
                {
                    ["recencyBoost"] = 0.25,
                },
            });

        var configuration = await LoadAsync(json);

        Assert.Null(configuration.Selection.Education.TotalItemBudget);
        Assert.Equal(0.25f, configuration.Selection.WorkExperience.RecencyBoost);
        Assert.Null(configuration.Selection.PersonalProjects.TotalItemBudget);
    }

    [Fact]
    public async Task LoadAsync_RejectsDuplicateSections()
    {
        var json = await ReadFixtureAsync();
        var duplicateSection = json.Replace(
            "\"PersonalProjects\"",
            "\"WorkExperience\"",
            StringComparison.Ordinal);

        var exception = await LoadInvalidAsync(duplicateSection, buildSearch: false);

        Assert.Contains("occurs more than once", exception.Message);
    }

    [Fact]
    public async Task LoadAsync_ReportsAllShapeErrorsTogether()
    {
        var json = await ReadFixtureAsync();
        var invalidJson = json
            .Replace("\"weight\": 1.0", "\"weight\": 0", StringComparison.Ordinal)
            .Replace("E2E Skill", " ", StringComparison.Ordinal)
            .Replace("E2E JSON Configuration", " ", StringComparison.Ordinal)
            .Replace("\"relevanceWeight\": 0.72", "\"relevanceWeight\": 1.1", StringComparison.Ordinal)
            .Replace("\"saturationQuota\": 2", "\"saturationQuota\": 0", StringComparison.Ordinal)
            .Replace("\"saturationPenalty\": 0.18", "\"saturationPenalty\": -1", StringComparison.Ordinal)
            .Replace("\"totalItemBudget\": 1", "\"totalItemBudget\": -1", StringComparison.Ordinal)
            .Replace("\"scoreLowerBound\": 0", "\"scoreLowerBound\": -1", StringComparison.Ordinal)
            .Replace("\"PersonalProjects\"", "\"WorkExperience\"", StringComparison.Ordinal);

        var exception = await LoadInvalidAsync(invalidJson, buildSearch: false);

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
        var json = await ReadFixtureAsync();
        var duplicateTag = json.Replace(
            "{ \"name\": \".NET\", \"weight\": 1.0 }",
            "{ \"name\": \".NET\", \"weight\": 1.0 },\r\n    { \"name\": \".net\", \"weight\": 1.0 }");
        var duplicateException = await LoadInvalidAsync(duplicateTag, buildSearch: false);
        Assert.Contains("more than once", duplicateException.Message);

        var unknownTag = json.Replace(".NET", "No Such Tag", StringComparison.Ordinal);
        var unknownException = await LoadInvalidAsync(unknownTag, buildSearch: true);
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

    private static string ReplaceSelection(string json, JsonObject selection)
    {
        var configuration = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidOperationException("The test fixture must contain a JSON object.");
        configuration["selection"] = selection;
        return configuration.ToJsonString();
    }

    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cli-e2e-config.json");
}
