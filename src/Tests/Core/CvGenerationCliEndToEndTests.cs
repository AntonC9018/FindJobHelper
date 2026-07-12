using System.Diagnostics;
using System.Text;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class CvGenerationCliEndToEndTests
{
    [Fact]
    public async Task Help_ListsConfigurationAndGenerationFlags()
    {
        var result = await RunCliAsync("--help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--config", result.StandardOutput);
        Assert.Contains("--output-directory", result.StandardOutput);
        Assert.Contains("--debug", result.StandardOutput);
        Assert.Contains("--open", result.StandardOutput);
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
    public async Task Generate_PublishesOnlyTheRenamedPdf()
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
            var pdf = Assert.Single(files);
            Assert.Equal("CurmanchiiAnton.pdf", pdf);
            Assert.DoesNotContain(files, file => file is "main.tex" or "log-stdout.txt" or "log-stderr.txt");

            var pdfPath = Path.Combine(outputDirectory, pdf);
            var pdfInfo = new FileInfo(pdfPath);
            Assert.True(pdfInfo.Length > 4);

            await using var stream = File.OpenRead(pdfPath);
            var header = new byte[5];
            var bytesRead = await stream.ReadAsync(header);
            Assert.Equal(5, bytesRead);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(header));
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
    public async Task Generate_DisabledOnePageLimitPublishesUnrestrictedPdf()
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

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
