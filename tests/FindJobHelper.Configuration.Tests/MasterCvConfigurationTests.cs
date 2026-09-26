using FindJobHelper.Configuration;
using FindJobHelper.Configuration.Json;

namespace FindJobHelper.Configuration.Tests;

public sealed class MasterCvConfigurationTests
{
    [Fact]
    public async Task LoadAsync_MapsOrderOnlyDocumentConfiguration()
    {
        var configuration = await LoadAsync(
            """
            {
              "skills": ["Architecture"],
              "technologies": [".NET"],
              "sectionOrder": ["WorkExperience", "PersonalProjects"],
              "profession": "Software Engineer",
              "header": { "links": { "order": ["linkedin", "GITHUB"] } }
            }
            """);

        Assert.Equal(new[] { "Architecture" }, configuration.Skills.ToArray());
        Assert.Equal(new[] { ".NET" }, configuration.Technologies.ToArray());
        Assert.Equal(
            new[] { Section.WorkExperience, Section.PersonalProjects },
            configuration.SectionOrder.ToArray());
        Assert.Equal("Software Engineer", configuration.Profession);
        Assert.Equal(
            new[] { HeaderLinkName.LinkedIn, HeaderLinkName.GitHub },
            configuration.HeaderLinkOrder.ToArray());
    }

    [Fact]
    public async Task LoadAsync_RejectsPageLayoutForm()
    {
        var exception = await LoadInvalidAsync(
            """
            {
              "skills": ["Architecture"],
              "technologies": [".NET"],
              "sectionOrder": [
                { "page": 1, "sections": ["WorkExperience"] }
              ]
            }
            """);

        Assert.Contains("string-array form", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("requiredTags")]
    [InlineData("selection")]
    [InlineData("mmr")]
    [InlineData("pageCount")]
    [InlineData("limitToOnePage")]
    public async Task LoadAsync_RejectsSelectionAndPageProperties(string property)
    {
        var json = $$"""
            {
              "skills": ["Architecture"],
              "technologies": [".NET"],
              "sectionOrder": ["WorkExperience"],
              "{{property}}": null
            }
            """;

        var exception = await LoadInvalidAsync(json);

        Assert.Contains(property, exception.Message, StringComparison.Ordinal);
    }

    private static async Task<MasterCvConfiguration> LoadAsync(string json)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"master-cv-config-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(path, json);
        try
        {
            return await MasterCvConfigurationLoader.LoadAsync(
                path,
                CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<CvConfigurationException> LoadInvalidAsync(string json)
    {
        return await Assert.ThrowsAsync<CvConfigurationException>(
            () => LoadAsync(json));
    }
}
