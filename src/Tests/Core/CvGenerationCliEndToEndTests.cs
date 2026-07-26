using System.Diagnostics;
using System.Text.Json.Nodes;
using FindJobHelper.CVGeneration;
using MainCli;

namespace FindJobHelper.Core.Tests;

public sealed class CvGenerationCliEndToEndTests
{
    [Fact]
    public async Task Help_ListsConfigurationAndGenerationFlags()
    {
        var result = await RunCliAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("list-tags", result.StandardOutput);
        Assert.Contains("--config", result.StandardOutput);
        Assert.Contains("--output-directory", result.StandardOutput);
        Assert.Contains("--debug", result.StandardOutput);
        Assert.Contains("--open", result.StandardOutput);
    }

    [Fact]
    public async Task ListTags_ListsEveryTagInAlphabeticalOrder()
    {
        var result = await RunCliAsync("list-tags");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Empty, result.StandardError);

        var expected = TagsDatabaseFactory.Create().TagsDatabase.TagsGraph.Keys
            .Select(static tag => tag.Name)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static name => name, StringComparer.Ordinal);
        var actual = result.StandardOutput.Split(
            ["\r\n", "\n"],
            StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Generate_RequiresConfigurationAndOutputDirectory()
    {
        var result = await RunCliAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("config is required", result.StandardError);
        Assert.Contains("output-directory is required", result.StandardError);
    }

    [Fact]
    public async Task Generate_DebugPublishesOnlyAnnotatedMarkdown()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"FindJobHelper-e2e-{Guid.NewGuid():N}");
        try
        {
            var result = await RunCliAsync(
                "--config",
                FixturePath,
                "--output-directory",
                outputDirectory,
                "--debug");

            Assert.True(
                result.ExitCode == 0,
                $"CLI exited with {result.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{result.StandardError}");

            var files = Directory.GetFiles(outputDirectory)
                .Select(Path.GetFileName)
                .Where(static file => file is not null)
                .Select(static file => file!)
                .ToArray();
            var markdownFile = Assert.Single(files);
            Assert.Equal("CurmanchiiAnton-debug.md", markdownFile);
            Assert.DoesNotContain(files, file => file is "main.tex" or "log-stdout.txt" or "log-stderr.txt");

            var markdownPath = Path.Combine(outputDirectory, markdownFile);
            var markdownBytes = await File.ReadAllBytesAsync(markdownPath);
            Assert.False(markdownBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
            var markdown = await File.ReadAllTextAsync(markdownPath);
            Assert.StartsWith("# Anton Curmanschii\n", markdown, StringComparison.Ordinal);
            Assert.Contains("**Skills:** E2E Skill", markdown, StringComparison.Ordinal);
            Assert.Contains("**Technologies:** E2E JSON Configuration", markdown, StringComparison.Ordinal);
            Assert.Contains("## Work Experience", markdown, StringComparison.Ordinal);
            Assert.Contains("`score:", markdown, StringComparison.Ordinal);
            Assert.Contains("- `score:", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(@"\begin{document}", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(@"\cvevent", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("202-555-0100", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain('\r', markdown);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Generate_ExactTwoPageFixturePublishesPdf()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"FindJobHelper-multipage-e2e-{Guid.NewGuid():N}");
        try
        {
            var result = await RunCliAsync(
                "--config",
                MultiPageFixturePath,
                "--output-directory",
                outputDirectory);

            Assert.True(
                result.ExitCode == 0,
                $"CLI exited with {result.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{result.StandardError}");
            Assert.True(File.Exists(Path.Combine(outputDirectory, "CurmanchiiAnton.pdf")));
            Assert.Single(Directory.GetFiles(outputDirectory));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Generate_ExplicitFourPageFixturePublishesPdf()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-explicit-e2e-{Guid.NewGuid():N}");
        try
        {
            var result = await RunCliAsync(
                "--config",
                ExplicitFixturePath,
                "--output-directory",
                outputDirectory);

            Assert.True(
                result.ExitCode == 0,
                $"CLI exited with {result.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{result.StandardError}");
            Assert.True(File.Exists(Path.Combine(outputDirectory, "CurmanchiiAnton.pdf")));
            Assert.Single(Directory.GetFiles(outputDirectory));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Generate_Entry19WithNoMinimumScoreLimitHonorsExactPageCount()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"FindJobHelper-entry-19-e2e-{Guid.NewGuid():N}");
        try
        {
            var result = await RunCliAsync(
                "--config",
                Entry19FixturePath,
                "--output-directory",
                outputDirectory);

            Assert.True(
                result.ExitCode == 0,
                $"CLI exited with {result.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{result.StandardError}");
            Assert.True(File.Exists(Path.Combine(outputDirectory, "CurmanchiiAnton.pdf")));
            Assert.Single(Directory.GetFiles(outputDirectory));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Generate_DebugUnattainableExactCountFailsWithoutPublishingMarkdown()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"FindJobHelper-unattainable-e2e-{Guid.NewGuid():N}");
        try
        {
            var result = await RunCliAsync(
                "--config",
                UnattainableFixturePath,
                "--output-directory",
                outputDirectory,
                "--debug");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Configured pageCount 2", result.StandardError, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(outputDirectory, "CurmanchiiAnton-debug.md")));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(false, "CurmanchiiAnton.pdf")]
    [InlineData(true, "CurmanchiiAnton-debug.md")]
    public async Task Generate_UnderfilledExplicitLayoutFailsWithoutPublishingArtifacts(
        bool debug,
        string artifactFileName)
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-explicit-underfill-{Guid.NewGuid():N}");
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-explicit-underfill-{Guid.NewGuid():N}.json");
        try
        {
            var configuration = JsonNode.Parse(await File.ReadAllTextAsync(FixturePath))!
                .AsObject();
            configuration.Remove("limitToOnePage");
            configuration.Remove("pageCount");
            configuration["sectionOrder"] = JsonNode.Parse(
                """[{ "pages": "1-10", "sections": ["WorkExperience"] }]""");
            await File.WriteAllTextAsync(configPath, configuration.ToJsonString());

            var arguments = new List<string>
            {
                "--config",
                configPath,
                "--output-directory",
                outputDirectory,
            };
            if (debug)
            {
                arguments.Add("--debug");
            }

            var result = await RunCliAsync([.. arguments]);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                "Explicit layout block 1-10",
                result.StandardError,
                StringComparison.Ordinal);
            Assert.Contains(
                "naturally occupies",
                result.StandardError,
                StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(outputDirectory, artifactFileName)));
            if (Directory.Exists(outputDirectory))
            {
                Assert.Empty(Directory.GetFiles(outputDirectory));
            }
        }
        finally
        {
            File.Delete(configPath);
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static async Task<ProcessResult> RunCliAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add(typeof(CvTemplate).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["PersonalInfo__Email"] = "e2e@example.test";
        startInfo.Environment["PersonalInfo__Phone"] = "202-555-0100";

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string FixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cli-e2e-config.json");

    private static string MultiPageFixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cli-e2e-multipage-config.json");

    private static string Entry19FixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cli-e2e-entry-19-config.json");

    private static string ExplicitFixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cli-e2e-explicit-config.json");

    private static string UnattainableFixturePath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cli-e2e-unattainable-config.json");

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
