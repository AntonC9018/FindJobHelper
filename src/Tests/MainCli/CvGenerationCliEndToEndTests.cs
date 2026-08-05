using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using FindJobHelper.CVGeneration;
using FindJobHelper.Usage;
using MainCli;

namespace MainCli.Tests;

public sealed class CvGenerationCliEndToEndTests
{
    [Fact]
    public async Task Generate_RequiresConfiguration()
    {
        var result = await RunCliAsync();

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("config is required", result.StandardError);
        Assert.DoesNotContain("output-directory is required", result.StandardError);
    }

    [Fact]
    public async Task Generate_RequiresExperienceDatabase()
    {
        var result = await RunCliWithoutExperienceDatabaseAsync(
            "--config",
            FixturePath,
            "--debug");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("experience-database is required", result.StandardError);
    }

    [Fact]
    public async Task ExampleConfig_DoesNotRequireExperienceDatabase()
    {
        var result = await RunCliWithoutExperienceDatabaseAsync("example-config");

        AssertSuccessful(result);
        Assert.Contains("requiredTags", result.StandardOutput);
    }

    [Fact]
    public async Task ListTags_LoadsUsageProviderDll()
    {
        var result = await RunCliAsync("list-tags");

        AssertSuccessful(result);
        Assert.Contains(".NET", result.StandardOutput);
        Assert.Contains("ASP.NET Core", result.StandardOutput);
    }

    [Fact]
    public async Task ProviderFailure_DoesNotCreateOutputDirectory()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-provider-failure-{Guid.NewGuid():N}");
        var missingDll = Path.Combine(
            Path.GetTempPath(),
            $"missing-provider-{Guid.NewGuid():N}.dll");

        var result = await RunCliAsync(
            "--config",
            FixturePath,
            "--experience-database",
            missingDll,
            "--output-directory",
            outputDirectory,
            "--debug");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Experience database error:", result.StandardError);
        Assert.False(Directory.Exists(outputDirectory));
    }

    [Fact]
    public async Task NewConfig_WritesExampleConfigToOutputDirectory()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-new-config-{Guid.NewGuid():N}");
        try
        {
            var result = await RunCliAsync(
                "new-config",
                "--output-directory",
                outputDirectory);

            AssertSuccessful(result);
            var outputPath = Path.Combine(outputDirectory, "config.json");
            Assert.True(File.Exists(outputPath));
            Assert.Equal(
                await File.ReadAllTextAsync(Path.Combine(
                    AppContext.BaseDirectory,
                    "data",
                    "cv-selection.example.json")),
                await File.ReadAllTextAsync(outputPath));
            Assert.Contains($"Created '{outputPath}'.", result.StandardOutput);
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
    public async Task NewConfig_DoesNotOverwriteExistingConfig()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-new-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, "config.json");
        const string existingContent = "existing configuration";
        await File.WriteAllTextAsync(outputPath, existingContent);

        try
        {
            var result = await RunCliAsync(
                "new-config",
                "--output-directory",
                outputDirectory);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                $"Cannot create '{outputPath}': the file already exists.",
                result.StandardError);
            Assert.Equal(existingContent, await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            Directory.Delete(outputDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData("tex")]
    [InlineData("md")]
    public async Task Generate_DebugOverridesRequestedFormatAndPublishesBothMarkdownVariants(
        string requestedOutputFormat)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"FindJobHelper-e2e-{Guid.NewGuid():N}");
        try
        {
            var result = await RunCliAsync(
                "--config",
                FixturePath,
                "--output-directory",
                outputDirectory,
                "--output-format",
                requestedOutputFormat,
                "--debug");

            AssertSuccessful(result);
            AssertProgressModuleTransitions(
                result.StandardOutput,
                (0, "Computing heights"),
                (33, "Matching experiences"),
                (67, "Creating Markdown files"),
                (100, "Creating Markdown files"));
            Assert.DoesNotContain(
                "Creating TeX file",
                result.StandardOutput,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Rendering PDF",
                result.StandardOutput,
                StringComparison.Ordinal);
            Assert.Contains(
                "Progress: 100% — Creating Markdown files",
                result.StandardOutput,
                StringComparison.Ordinal);
            Assert.DoesNotContain('\u001b', result.StandardOutput);

            var files = GetFileNames(outputDirectory);
            Assert.Equal(2, files.Length);
            Assert.Contains("CurmanchiiAnton.md", files);
            Assert.Contains("CurmanchiiAnton-debug.md", files);
            Assert.DoesNotContain("CurmanchiiAnton.pdf", files);
            Assert.DoesNotContain(
                files,
                file => file is "main.tex" or "log-stdout.txt" or "log-stderr.txt");

            var cleanMarkdownPath = Path.Combine(outputDirectory, "CurmanchiiAnton.md");
            var debugMarkdownPath = Path.Combine(outputDirectory, "CurmanchiiAnton-debug.md");
            var cleanMarkdown = await ReadAndAssertMarkdownEncodingAsync(cleanMarkdownPath);
            var debugMarkdown = await ReadAndAssertMarkdownEncodingAsync(debugMarkdownPath);

            foreach (var markdown in new[] { cleanMarkdown, debugMarkdown })
            {
                Assert.StartsWith("# Anton Curmanschii\n", markdown, StringComparison.Ordinal);
                Assert.Contains("**Skills:** E2E Skill", markdown, StringComparison.Ordinal);
                Assert.Contains(
                    "**Technologies:** E2E JSON Configuration",
                    markdown,
                    StringComparison.Ordinal);
                Assert.Contains("## Work Experience", markdown, StringComparison.Ordinal);
                Assert.Contains(
                    "**Phone:** 202\\*\\*\\*\\*\\*\\*\\*\\*\\*",
                    markdown,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(
                    "202\\-555\\-0100",
                    markdown,
                    StringComparison.Ordinal);
                Assert.DoesNotContain(@"\begin{document}", markdown, StringComparison.Ordinal);
                Assert.DoesNotContain(@"\cvevent", markdown, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("<details>", cleanMarkdown, StringComparison.Ordinal);
            Assert.DoesNotContain("MMR terms:", cleanMarkdown, StringComparison.Ordinal);
            Assert.Contains(
                "<details>\n<summary>Diagnostics</summary>\n\n```text\nrank:",
                debugMarkdown,
                StringComparison.Ordinal);
            Assert.Contains(
                "- <details>\n  <summary>Diagnostics</summary>\n\n  ```text\n  rank:",
                debugMarkdown,
                StringComparison.Ordinal);
            Assert.DoesNotContain("[configured:", debugMarkdown, StringComparison.Ordinal);

            var cleanMessageIndex = result.StandardOutput.IndexOf(
                $"Generated '{cleanMarkdownPath}'.",
                StringComparison.Ordinal);
            var debugMessageIndex = result.StandardOutput.IndexOf(
                $"Generated '{debugMarkdownPath}'.",
                StringComparison.Ordinal);
            Assert.True(cleanMessageIndex >= 0);
            Assert.True(debugMessageIndex > cleanMessageIndex);
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
    public async Task Generate_MarkdownPublishesOnlyCleanMarkdownWithUnblurredContactData()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-clean-markdown-e2e-{Guid.NewGuid():N}");
        try
        {
            var result = await RunCliAsync(
                "--config",
                FixturePath,
                "--output-directory",
                outputDirectory,
                "--output-format",
                "mD");

            AssertSuccessful(result);
            AssertProgressModuleTransitions(
                result.StandardOutput,
                (0, "Computing heights"),
                (33, "Matching experiences"),
                (67, "Creating Markdown files"),
                (100, "Creating Markdown files"));

            var markdownFile = Assert.Single(GetFileNames(outputDirectory));
            Assert.Equal("CurmanchiiAnton.md", markdownFile);
            var markdown = await ReadAndAssertMarkdownEncodingAsync(
                Path.Combine(outputDirectory, markdownFile));

            Assert.StartsWith("# Anton Curmanschii\n", markdown, StringComparison.Ordinal);
            Assert.Contains(
                "**Phone:** 202\\-555\\-0100",
                markdown,
                StringComparison.Ordinal);
            Assert.Contains("**Skills:** E2E Skill", markdown, StringComparison.Ordinal);
            Assert.Contains("## Work Experience", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("<details>", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("raw:", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("coverage:", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("matches:", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain("MMR terms:", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(@"\begin{document}", markdown, StringComparison.Ordinal);
            Assert.DoesNotContain(@"\cvevent", markdown, StringComparison.Ordinal);
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
    public async Task Generate_UnsupportedOutputFormatFailsBeforePublishingArtifacts()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-invalid-format-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDirectory);
        var unrelatedFile = Path.Combine(outputDirectory, "keep.txt");
        await File.WriteAllTextAsync(unrelatedFile, "keep");
        try
        {
            var result = await RunCliAsync(
                "--config",
                FixturePath,
                "--output-directory",
                outputDirectory,
                "--output-format",
                "html",
                "--debug");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains(
                "'html' is not a valid CvOutputFormat",
                result.StandardOutput,
                StringComparison.Ordinal);
            Assert.Equal(new[] { "keep.txt" }, GetFileNames(outputDirectory));
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
    public async Task Generate_ExplicitTexPublishesOnlyPdf()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-explicit-tex-e2e-{Guid.NewGuid():N}");
        try
        {
            var result = await RunCliAsync(
                "--config",
                FixturePath,
                "--output-directory",
                outputDirectory,
                "--output-format",
                "TeX");

            AssertSuccessful(result);
            AssertProgressModuleTransitions(
                result.StandardOutput,
                (0, "Computing heights"),
                (25, "Matching experiences"),
                (50, "Creating TeX file"),
                (75, "Rendering PDF"),
                (100, "Rendering PDF"));
            Assert.DoesNotContain(
                "Creating Markdown files",
                result.StandardOutput,
                StringComparison.Ordinal);
            Assert.Contains(
                "Progress: 100% — Rendering PDF",
                result.StandardOutput,
                StringComparison.Ordinal);

            var pdfFile = Assert.Single(GetFileNames(outputDirectory));
            Assert.Equal("CurmanchiiAnton.pdf", pdfFile);
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

    [Theory]
    [InlineData("md", false)]
    [InlineData("tex", true)]
    [InlineData("md", true)]
    public async Task Generate_MarkdownPathsRetainUnattainableExactPageCountValidation(
        string requestedOutputFormat,
        bool debug)
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"FindJobHelper-unattainable-e2e-{Guid.NewGuid():N}");
        try
        {
            var arguments = new List<string>
            {
                "--config",
                UnattainableFixturePath,
                "--output-directory",
                outputDirectory,
                "--output-format",
                requestedOutputFormat,
            };
            if (debug)
            {
                arguments.Add("--debug");
            }

            var result = await RunCliAsync([.. arguments]);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Configured pageCount 2", result.StandardError, StringComparison.Ordinal);
            if (Directory.Exists(outputDirectory))
            {
                Assert.Empty(Directory.GetFiles(outputDirectory));
            }
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
    [InlineData(null, false)]
    [InlineData("md", false)]
    [InlineData("tex", true)]
    [InlineData("md", true)]
    public async Task Generate_UnderfilledExplicitLayoutFailsWithoutPublishingArtifacts(
        string? requestedOutputFormat,
        bool debug)
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
            if (requestedOutputFormat is not null)
            {
                arguments.Add("--output-format");
                arguments.Add(requestedOutputFormat);
            }
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

    private static void AssertSuccessful(ProcessResult result)
    {
        Assert.True(
            result.ExitCode == 0,
            $"CLI exited with {result.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{result.StandardError}");
    }

    private static void AssertProgressModuleTransitions(
        string output,
        params (int Percentage, string Module)[] transitions)
    {
        var previousIndex = -1;
        foreach (var (percentage, module) in transitions)
        {
            var expected = $"Progress: {percentage}% — {module}";
            var index = output.IndexOf(
                expected,
                previousIndex + 1,
                StringComparison.Ordinal);
            Assert.True(
                index > previousIndex,
                $"Progress transition '{expected}' was missing or out of order.{Environment.NewLine}{output}");
            previousIndex = index;
        }
    }

    private static string[] GetFileNames(string directory) =>
        Directory.GetFiles(directory)
            .Select(Path.GetFileName)
            .Where(static file => file is not null)
            .Select(static file => file!)
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();

    private static async Task<string> ReadAndAssertMarkdownEncodingAsync(string path)
    {
        var markdownBytes = await File.ReadAllBytesAsync(path);
        Assert.False(markdownBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));

        var markdown = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain('\r', markdown);
        Assert.EndsWith("\n", markdown, StringComparison.Ordinal);
        Assert.False(markdown.EndsWith("\n\n", StringComparison.Ordinal));
        return markdown;
    }

    private static Task<ProcessResult> RunCliAsync(params string[] arguments) =>
        RunCliCoreAsync(addExperienceDatabase: true, arguments);

    private static Task<ProcessResult> RunCliWithoutExperienceDatabaseAsync(
        params string[] arguments) =>
        RunCliCoreAsync(addExperienceDatabase: false, arguments);

    private static async Task<ProcessResult> RunCliCoreAsync(
        bool addExperienceDatabase,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add(typeof(CvGenerationCommand).Assembly.Location);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var command = arguments.FirstOrDefault();
        var acceptsExperienceDatabase = command is not "example-config" and not "new-config";
        if (addExperienceDatabase
            && acceptsExperienceDatabase
            && !arguments.Contains("--experience-database", StringComparer.Ordinal))
        {
            startInfo.ArgumentList.Add("--experience-database");
            startInfo.ArgumentList.Add(
                typeof(ExperienceDatabaseProvider).Assembly.Location);
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
