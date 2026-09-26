using FindJobHelper.ExperienceProject;

namespace FindJobHelper.WebUi;

public sealed record DatabaseStatus(
    string Path,
    bool Exists,
    DateTimeOffset? LastWriteUtc,
    long SizeBytes);

/// <summary>
/// Builds ExperienceDatabase.dll for CV generation through the shared
/// experience project builder (ADR 0001), so the UI can rebuild the database
/// after experience or tag changes.
/// </summary>
public sealed class DatabaseManager : IDisposable
{
    private readonly SemaphoreSlim _publishGate = new(initialCount: 1, maxCount: 1);
    private readonly WebUiOptions _options;
    private readonly ILogger<DatabaseManager> _logger;

    public DatabaseManager(WebUiOptions options, ILogger<DatabaseManager> logger)
    {
        _options = options;
        _logger = logger;
    }

    public void Dispose()
    {
        _publishGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public DatabaseStatus GetStatus()
    {
        var databasePath = _options.DatabasePathOrDefault;
        var fileInfo = new FileInfo(databasePath);
        return new DatabaseStatus(
            Path: databasePath,
            Exists: fileInfo.Exists,
            LastWriteUtc: fileInfo.Exists ? fileInfo.LastWriteTimeUtc : null,
            SizeBytes: fileInfo.Exists ? fileInfo.Length : 0);
    }

    public async Task<string> RebuildAsync(CancellationToken cancellationToken)
    {
        await _publishGate.WaitAsync(cancellationToken);
        try
        {
            var projectFile = ResolveProjectFile();
            var outputDir = _options.DatabaseBuildOutputDirOrDefault;
            await ExperienceProjectBuilder.BuildAsync(projectFile, outputDir, cancellationToken);
            _logger.LogInformation(
                "Experience database rebuilt into '{OutputDir}'.",
                outputDir);
            return outputDir;
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private string ResolveProjectFile()
    {
        if (!string.IsNullOrWhiteSpace(_options.ExperienceProjectPath))
        {
            return _options.ExperienceProjectPath;
        }

        var projectDir = _options.ExperienceDatabaseProjectDirOrDefault;
        var projectFile = Path.Combine(projectDir, "ExperienceDatabase.csproj");
        if (!File.Exists(projectFile))
        {
            throw new InvalidOperationException(
                $"ExperienceDatabase project was not found at '{projectFile}'.");
        }

        return projectFile;
    }
}
