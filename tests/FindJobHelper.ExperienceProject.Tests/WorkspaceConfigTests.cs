using FindJobHelper.ExperienceProject;

namespace FindJobHelper.ExperienceProject.Tests;

public sealed class WorkspaceConfigTests
{
    [Fact]
    public void Find_ReturnsNearestAncestorConfigFile()
    {
        var root = CreateTempDirectory();
        var nested = Path.Combine(root, "data", "some-application");
        Directory.CreateDirectory(nested);
        var configPath = Path.Combine(root, WorkspaceConfig.FileName);
        File.WriteAllText(configPath, "{ }");

        var found = WorkspaceConfig.Find(nested);

        Assert.Equal(configPath, found);
    }

    [Fact]
    public void Find_ReturnsNull_WhenNoAncestorHasConfig()
    {
        var root = CreateTempDirectory();
        var nested = Path.Combine(root, "deeply", "nested");
        Directory.CreateDirectory(nested);

        var found = WorkspaceConfig.Find(nested);

        Assert.Null(found);
    }

    [Fact]
    public void Load_ReadsExperienceProject_ThroughCommentsAndTrailingCommas()
    {
        var root = CreateTempDirectory();
        var configPath = Path.Combine(root, WorkspaceConfig.FileName);
        File.WriteAllText(configPath, """
            {
                // The experience project the tools build and load.
                "experienceProject": "src/Provider/Provider.csproj",
                "note": "URLs keep their slashes: https://example.com/a//b",
            }
            """);

        var config = WorkspaceConfig.Load(configPath);

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "src", "Provider", "Provider.csproj")),
            config.ExperienceProjectPath);
        Assert.Equal(configPath, config.ConfigFilePath);
    }

    [Fact]
    public void Load_ReturnsNullProject_WhenKeyIsMissing()
    {
        var root = CreateTempDirectory();
        var configPath = Path.Combine(root, WorkspaceConfig.FileName);
        File.WriteAllText(configPath, "{ }");

        var config = WorkspaceConfig.Load(configPath);

        Assert.Null(config.ExperienceProjectPath);
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "experience-project-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
