using CliWrap;
using CliWrap.Buffered;

namespace FindJobHelper.ExperienceProject;

/// <summary>
/// Builds an experience project class library with
/// <c>dotnet build -c Release -o</c> (ADR 0001) and reports the DLL path the
/// existing experience database loader consumes.
/// </summary>
public static class ExperienceProjectBuilder
{
    public static string GetOutputDllPath(string projectFile, string outputDirectory)
    {
        var projectName = Path.GetFileNameWithoutExtension(projectFile);
        return Path.Combine(outputDirectory, projectName + ".dll");
    }

    public static async Task<string> BuildAsync(
        string projectFile,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        var fullProjectFile = Path.GetFullPath(projectFile);
        if (!File.Exists(fullProjectFile))
        {
            throw new ExperienceProjectBuildException(
                $"Experience project was not found at '{fullProjectFile}'.");
        }

        Directory.CreateDirectory(outputDirectory);
        var command = Cli.Wrap("dotnet")
            .WithArguments(new[]
            {
                "build",
                fullProjectFile,
                "-c",
                "Release",
                "-o",
                outputDirectory,
            })
            .WithValidation(CommandResultValidation.None);
        var result = await command.ExecuteBufferedAsync(cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new ExperienceProjectBuildException(
                $"Building the experience project '{fullProjectFile}' failed "
                + $"with exit code {result.ExitCode}."
                + Environment.NewLine
                + SelectErrorOutput(result.StandardOutput, result.StandardError));
        }

        var dllPath = GetOutputDllPath(fullProjectFile, outputDirectory);
        if (!File.Exists(dllPath))
        {
            throw new ExperienceProjectBuildException(
                $"The build reported success but the experience database DLL "
                + $"was not found at '{dllPath}'.");
        }

        return dllPath;
    }

    private static string SelectErrorOutput(string standardOutput, string standardError)
    {
        var errorOutput = string.IsNullOrWhiteSpace(standardError)
            ? standardOutput
            : standardError;
        var trimmed = errorOutput.TrimEnd();
        var characterBudget = 4000;
        if (trimmed.Length <= characterBudget)
        {
            return trimmed;
        }

        return trimmed[^characterBudget..];
    }
}

public sealed class ExperienceProjectBuildException : Exception
{
    public ExperienceProjectBuildException(string message)
        : base(message)
    {
    }
}
