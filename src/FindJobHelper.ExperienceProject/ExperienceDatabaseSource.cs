namespace FindJobHelper.ExperienceProject;

/// <summary>
/// Resolves the experience database DLL the way both frontends must (ADR
/// 0001): an explicit DLL wins and skips the build; otherwise the experience
/// project comes from the <c>--experience-project</c> override or the
/// workspace config, and is built before the DLL is used. With
/// <c>--no-build</c> the build is skipped and an existing DLL is required.
/// </summary>
public static class ExperienceDatabaseSource
{
    public static async Task<ResolvedExperienceDatabase> ResolveAsync(
        ExperienceDatabaseRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.ExplicitDllPath))
        {
            var explicitDll = Path.GetFullPath(request.ExplicitDllPath);
            if (!File.Exists(explicitDll))
            {
                throw new ExperienceDatabaseSourceException(
                    $"The experience database DLL was not found at '{explicitDll}'.");
            }

            return new ResolvedExperienceDatabase
            {
                DllPath = explicitDll,
                WorkspaceConfigFilePath = request.ConfigFilePath,
            };
        }

        var projectPath = ResolveProjectPath(request);
        if (projectPath is null)
        {
            throw new ExperienceDatabaseSourceException(
                "No experience database was specified. Pass --experience-database <dll>, "
                + "pass --experience-project <csproj>, or set 'experienceProject' in "
                + $"{WorkspaceConfig.FileName}.");
        }

        var buildOutputDirectory = ResolveBuildOutputDirectory(request, projectPath);
        var dllPath = ExperienceProjectBuilder.GetOutputDllPath(projectPath, buildOutputDirectory);
        var needsBuild = !request.BuildOnlyWhenMissing || !File.Exists(dllPath);
        if (request.NoBuild)
        {
            if (!File.Exists(dllPath))
            {
                throw new ExperienceDatabaseSourceException(
                    $"--no-build skips the experience project build, but the experience "
                    + $"database DLL was not found at '{dllPath}'. Build the project first "
                    + "or drop --no-build.");
            }

            return new ResolvedExperienceDatabase
            {
                DllPath = dllPath,
                WorkspaceConfigFilePath = request.ConfigFilePath,
            };
        }

        if (needsBuild)
        {
            await ExperienceProjectBuilder.BuildAsync(projectPath, buildOutputDirectory, cancellationToken);
        }

        return new ResolvedExperienceDatabase
        {
            DllPath = dllPath,
            Built = needsBuild,
            WorkspaceConfigFilePath = request.ConfigFilePath,
        };
    }

    private static string? ResolveProjectPath(ExperienceDatabaseRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.ExplicitProjectPath))
        {
            return Path.GetFullPath(request.ExplicitProjectPath);
        }

        if (!string.IsNullOrWhiteSpace(request.ConfigFilePath))
        {
            var config = WorkspaceConfig.Load(request.ConfigFilePath);
            return config.ExperienceProjectPath;
        }

        return null;
    }

    private static string ResolveBuildOutputDirectory(
        ExperienceDatabaseRequest request,
        string projectPath)
    {
        if (!string.IsNullOrWhiteSpace(request.BuildOutputDirectory))
        {
            return Path.GetFullPath(request.BuildOutputDirectory);
        }

        var configDirectory = string.IsNullOrWhiteSpace(request.ConfigFilePath)
            ? null
            : Path.GetDirectoryName(Path.GetFullPath(request.ConfigFilePath));
        var baseDirectory = configDirectory ?? Path.GetDirectoryName(projectPath)!;
        return Path.Combine(baseDirectory, "build");
    }
}

public sealed record ExperienceDatabaseRequest
{
    /// <summary>Explicit DLL selection; skips the build entirely.</summary>
    public string? ExplicitDllPath { get; init; }

    /// <summary>Explicit csproj override; wins over the workspace config.</summary>
    public string? ExplicitProjectPath { get; init; }

    /// <summary>Skip the build and require an existing DLL.</summary>
    public bool NoBuild { get; init; }

    /// <summary>Build even when the DLL already exists (CLI semantics).</summary>
    public bool BuildOnlyWhenMissing { get; init; }

    /// <summary>Path of the discovered findjobhelper.config.json, when any.</summary>
    public string? ConfigFilePath { get; init; }

    /// <summary>
    /// Overrides the build output directory. Empty resolves to
    /// <c>build</c> next to the config file, or next to the project when no
    /// config file was found.
    /// </summary>
    public string? BuildOutputDirectory { get; init; }
}

public sealed record ResolvedExperienceDatabase
{
    public required string DllPath { get; init; }

    public bool Built { get; init; }

    public string? WorkspaceConfigFilePath { get; init; }
}

public sealed class ExperienceDatabaseSourceException : Exception
{
    public ExperienceDatabaseSourceException(string message)
        : base(message)
    {
    }
}
