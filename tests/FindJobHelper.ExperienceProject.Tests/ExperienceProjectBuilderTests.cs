using FindJobHelper.ExperienceProject;

namespace FindJobHelper.ExperienceProject.Tests;

public sealed class ExperienceProjectBuilderTests
{
    [Fact]
    public void GetOutputDllPath_UsesProjectNameInsideOutputDirectory()
    {
        var dllPath = ExperienceProjectBuilder.GetOutputDllPath(
            "/workspaces/ws/ExperienceDatabase/ExperienceDatabase.csproj",
            "/workspaces/ws/build");

        Assert.Equal(
            Path.Combine("/workspaces/ws/build", "ExperienceDatabase.dll"),
            dllPath);
    }

    [Fact]
    public async Task BuildAsync_BuildsFixtureProject_AndReportsDllPath()
    {
        var root = CreateTempDirectory();
        var projectFile = await WriteFixtureProjectAsync(root);
        var outputDirectory = Path.Combine(root, "build");

        var dllPath = await ExperienceProjectBuilder.BuildAsync(
            projectFile,
            outputDirectory);

        Assert.True(File.Exists(dllPath));
        Assert.Equal(
            ExperienceProjectBuilder.GetOutputDllPath(projectFile, outputDirectory),
            dllPath);
    }

    [Fact]
    public async Task BuildAsync_ThrowsDescriptiveException_WhenProjectIsMissing()
    {
        await Assert.ThrowsAsync<ExperienceProjectBuildException>(
            () => ExperienceProjectBuilder.BuildAsync(
                Path.Combine(CreateTempDirectory(), "Missing.csproj"),
                Path.Combine(CreateTempDirectory(), "build")));
    }

    private static async Task<string> WriteFixtureProjectAsync(string root)
    {
        var projectDirectory = Path.Combine(root, "FixtureProject");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "FixtureProject.csproj");
        await File.WriteAllTextAsync(
            projectFile,
            """
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(projectDirectory, "Fixture.cs"),
            "namespace FixtureProject; public static class Fixture { }");
        return projectFile;
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
