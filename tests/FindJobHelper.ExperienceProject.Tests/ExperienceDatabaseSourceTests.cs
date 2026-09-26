using FindJobHelper.ExperienceProject;

namespace FindJobHelper.ExperienceProject.Tests;

public sealed class ExperienceDatabaseSourceTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsExplicitDll_AndSkipsTheBuild()
    {
        var root = CreateTempDirectory();
        var dllPath = Path.Combine(root, "Database.dll");
        File.WriteAllText(dllPath, "not a real assembly");

        var resolved = await ExperienceDatabaseSource.ResolveAsync(
            new ExperienceDatabaseRequest { ExplicitDllPath = dllPath });

        Assert.Equal(dllPath, resolved.DllPath);
        Assert.False(resolved.Built);
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenExplicitDllIsMissing()
    {
        var root = CreateTempDirectory();
        var request = new ExperienceDatabaseRequest
        {
            ExplicitDllPath = Path.Combine(root, "Missing.dll"),
        };

        await Assert.ThrowsAsync<ExperienceDatabaseSourceException>(
            () => ExperienceDatabaseSource.ResolveAsync(request));
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenNothingIsSpecified()
    {
        await Assert.ThrowsAsync<ExperienceDatabaseSourceException>(
            () => ExperienceDatabaseSource.ResolveAsync(new ExperienceDatabaseRequest()));
    }

    [Fact]
    public async Task ResolveAsync_BuildsConfigProject_AndResolvesRelativePath()
    {
        var root = CreateTempDirectory();
        var projectDirectory = Path.Combine(root, "Provider");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Provider.csproj");
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
            Path.Combine(projectDirectory, "Provider.cs"),
            "namespace Provider; public static class Fixture { }");
        var configPath = Path.Combine(root, WorkspaceConfig.FileName);
        await File.WriteAllTextAsync(
            configPath,
            """
            {
                // Relative to the directory containing this file (ADR 0001).
                "experienceProject": "Provider/Provider.csproj",
            }
            """);

        var resolved = await ExperienceDatabaseSource.ResolveAsync(
            new ExperienceDatabaseRequest { ConfigFilePath = configPath });

        Assert.True(resolved.Built);
        Assert.True(File.Exists(resolved.DllPath));
        Assert.Equal(
            Path.Combine(root, "build", "Provider.dll"),
            resolved.DllPath);
    }

    [Fact]
    public async Task ResolveAsync_ThrowsUnderNoBuild_WhenDllIsMissing()
    {
        var root = CreateTempDirectory();
        var projectDirectory = Path.Combine(root, "Provider");
        Directory.CreateDirectory(projectDirectory);
        var projectFile = Path.Combine(projectDirectory, "Provider.csproj");
        await File.WriteAllTextAsync(
            projectFile,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        var configPath = Path.Combine(root, WorkspaceConfig.FileName);
        await File.WriteAllTextAsync(
            configPath,
            "{ \"experienceProject\": \"Provider/Provider.csproj\" }");

        var request = new ExperienceDatabaseRequest
        {
            ConfigFilePath = configPath,
            NoBuild = true,
        };

        await Assert.ThrowsAsync<ExperienceDatabaseSourceException>(
            () => ExperienceDatabaseSource.ResolveAsync(request));
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
