using System.Diagnostics;

namespace FindJobHelper.WebUi;

public sealed record DatabaseStatus(
    string Path,
    bool Exists,
    DateTimeOffset? LastWriteUtc,
    long SizeBytes);

/// <summary>
/// Publishes ExperienceDatabase.dll for CV generation, mirroring what
/// `run.ps1` does, so the UI can rebuild the database after experience or tag
/// changes.
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
            var projectDir = _options.ExperienceDatabaseProjectDirOrDefault;
            var projectFile = Path.Combine(projectDir, "ExperienceDatabase.csproj");
            if (!File.Exists(projectFile))
            {
                throw new InvalidOperationException(
                    $"ExperienceDatabase project was not found at '{projectFile}'.");
            }

            var outputDir = _options.DatabaseBuildOutputDirOrDefault;
            Directory.CreateDirectory(outputDir);
            var output = await RunDotnetPublishAsync(projectFile, outputDir, cancellationToken);
            _logger.LogInformation(
                "Experience database rebuilt into '{OutputDir}'.",
                outputDir);
            return output;
        }
        finally
        {
            _publishGate.Release();
        }
    }

    private async Task<string> RunDotnetPublishAsync(
        string projectFile,
        string outputDir,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList =
            {
                "publish",
                projectFile,
                "-c",
                "Release",
                "-o",
                outputDir,
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start 'dotnet publish'.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            KillPublish(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch
            {
            }

            try
            {
                await stdoutTask;
            }
            catch
            {
            }

            try
            {
                await stderrTask;
            }
            catch
            {
            }

            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"dotnet publish failed with exit code {process.ExitCode}: {stderr}");
        }

        return stdout;
    }

    private static void KillPublish(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
