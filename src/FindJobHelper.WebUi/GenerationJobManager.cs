using System.Collections.Immutable;
using FindJobHelper.Configuration.Json;
using FindJobHelper.Generation;

namespace FindJobHelper.WebUi;

public enum GenerationJobState
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

public sealed record GenerationJobSnapshot(
    string Id,
    string ApplicationKey,
    string? FolderPath,
    bool Debug,
    GenerationJobState State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? FinishedUtc,
    string? ModuleDescription,
    string? ProgressDetail,
    double OverallPercent,
    double ModulePercent,
    string? Error,
    IReadOnlyList<string> Artifacts);

/// <summary>
/// Runs CV generations through <see cref="CvGenerationPipeline"/>, one at a
/// time, tracking progress for the UI. Jobs keep running if the browser
/// disconnects; the client polls the job id.
/// </summary>
public sealed class GenerationJobManager : IDisposable
{
    private readonly SemaphoreSlim _generationGate = new(initialCount: 1, maxCount: 1);
    private readonly Dictionary<string, GenerationJob> _jobs = new();
    private readonly object _jobsSync = new();
    private readonly ApplicationCatalog _catalog;
    private readonly ApplicationIngestion _ingestion;
    private readonly WebUiOptions _options;
    private readonly ILogger<GenerationJobManager> _logger;

    public GenerationJobManager(
        ApplicationCatalog catalog,
        ApplicationIngestion ingestion,
        WebUiOptions options,
        ILogger<GenerationJobManager> logger)
    {
        _catalog = catalog;
        _ingestion = ingestion;
        _options = options;
        _logger = logger;
    }

    public void Dispose()
    {
        _generationGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public GenerationJobSnapshot? Find(string id)
    {
        lock (_jobsSync)
        {
            return _jobs.TryGetValue(id, out var job) ? job.Snapshot() : null;
        }
    }

    public IReadOnlyList<GenerationJobSnapshot> ActiveJobs()
    {
        lock (_jobsSync)
        {
            return _jobs.Values
                .Where(static job => job.State is GenerationJobState.Queued or GenerationJobState.Running)
                .Select(static job => job.Snapshot())
                .ToList();
        }
    }

    public async Task<GenerationJobSnapshot> StartAsync(
        string applicationKey,
        bool debug,
        string? outputDirectory,
        CancellationToken cancellationToken)
    {
        var folder = await _catalog.ResolveFolderAsync(applicationKey, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No application folder found for key '{applicationKey}'.");
        var configPath = Path.Combine(folder, "config.json");
        if (!File.Exists(configPath))
        {
            throw new InvalidOperationException(
                $"The application folder does not contain config.json: '{folder}'.");
        }

        var databasePath = _options.DatabasePathOrDefault;
        if (!File.Exists(databasePath))
        {
            throw new InvalidOperationException(
                $"The experience database DLL was not found at '{databasePath}'. Rebuild the database first.");
        }

        var job = new GenerationJob(applicationKey, folder, debug);
        lock (_jobsSync)
        {
            PruneFinishedJobsLocked();
            _jobs[job.Id] = job;
        }

        _ = Task.Run(
            () => RunJobAsync(job, configPath, databasePath, outputDirectory),
            CancellationToken.None);
        return job.Snapshot();
    }

    private async Task RunJobAsync(
        GenerationJob job,
        string configPath,
        string databasePath,
        string? outputDirectory)
    {
        await _generationGate.WaitAsync();
        try
        {
            job.MarkRunning();
            var shadowDatabasePath = ExperienceDatabaseShadow.Copy(databasePath);
            var configuration = await CvSelectionConfigurationLoader.LoadAsync(
                configPath,
                CancellationToken.None);
            var request = new CvGenerationPipelineRequest
            {
                Config = configuration,
                ExperienceDatabasePath = shadowDatabasePath,
                OutputDirectory = outputDirectory ?? job.FolderPath,
                OutputFormat = CvOutputFormat.Tex,
                Debug = job.Debug,
                ProgressDisplay = new JobProgressDisplay(job),
            };
            var result = await CvGenerationPipeline.RunAsync(
                request,
                CancellationToken.None);
            if (result.Success)
            {
                var artifactPaths = result.Artifacts
                    .Select(artifact => result.PublishedPaths[artifact.Kind])
                    .ToList();
                job.MarkSucceeded(artifactPaths);
                await UpgradeApplicationStateAsync(job.ApplicationKey);
            }
            else
            {
                job.MarkFailed(result.Failure!.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CV generation failed for '{Key}'.", job.ApplicationKey);
            job.MarkFailed(ex.Message);
        }
        finally
        {
            _generationGate.Release();
        }
    }

    private async Task UpgradeApplicationStateAsync(string applicationKey)
    {
        var application = await _catalog.FindByKeyAsync(applicationKey, CancellationToken.None);
        if (application is null)
        {
            return;
        }

        var isAdded = string.Equals(application.State, ApplicationState.Added.ToWireName(), StringComparison.Ordinal);
        if (!isAdded)
        {
            return;
        }

        await _ingestion.TryUpdateStateAsync(
            key: applicationKey,
            state: ApplicationState.Generated,
            note: null,
            cancellationToken: CancellationToken.None);
    }

    private void PruneFinishedJobsLocked()
    {
        if (_jobs.Count <= 50)
        {
            return;
        }

        var finishedIds = _jobs
            .Where(static pair => pair.Value.State
                is GenerationJobState.Succeeded
                or GenerationJobState.Failed)
            .OrderBy(static pair => pair.Value.FinishedUtc)
            .Select(static pair => pair.Key)
            .ToList();
        var removeCount = Math.Min(finishedIds.Count, _jobs.Count - 50);
        foreach (var id in finishedIds.Take(removeCount))
        {
            _jobs.Remove(id);
        }
    }

    private sealed class GenerationJob(string applicationKey, string folderPath, bool debug)
    {
        private readonly object _sync = new();
        private double _overallPercent;
        private double _modulePercent;
        private string? _moduleDescription;
        private string? _progressDetail;

        public string Id { get; } = Guid.NewGuid().ToString("N");

        public string ApplicationKey { get; } = applicationKey;

        public string FolderPath { get; } = folderPath;

        public bool Debug { get; } = debug;

        public GenerationJobState State { get; private set; } = GenerationJobState.Queued;

        public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? StartedUtc { get; private set; }

        public DateTimeOffset? FinishedUtc { get; private set; }

        public string? Error { get; private set; }

        public ImmutableArray<string> ArtifactPaths { get; private set; } = [];

        public void MarkRunning()
        {
            lock (_sync)
            {
                StartedUtc = DateTimeOffset.UtcNow;
                State = GenerationJobState.Running;
            }
        }

        public void MarkSucceeded(IReadOnlyList<string> artifactPaths)
        {
            lock (_sync)
            {
                State = GenerationJobState.Succeeded;
                FinishedUtc = DateTimeOffset.UtcNow;
                _overallPercent = 100;
                _modulePercent = 100;
                ArtifactPaths = [.. artifactPaths];
            }
        }

        public void MarkFailed(string error)
        {
            lock (_sync)
            {
                State = GenerationJobState.Failed;
                FinishedUtc = DateTimeOffset.UtcNow;
                Error = error;
            }
        }

        public void RecordProgress(CvProgressDisplayState state)
        {
            lock (_sync)
            {
                _overallPercent = state.OverallPercentage;
                _modulePercent = state.ModulePercentage;
                _moduleDescription = state.ModuleDescription;
                _progressDetail = state.DisplayDescription;
            }
        }

        public GenerationJobSnapshot Snapshot()
        {
            lock (_sync)
            {
                return new GenerationJobSnapshot(
                    Id: Id,
                    ApplicationKey: ApplicationKey,
                    FolderPath: FolderPath,
                    Debug: Debug,
                    State: State,
                    CreatedUtc: CreatedUtc,
                    StartedUtc: StartedUtc,
                    FinishedUtc: FinishedUtc,
                    ModuleDescription: _moduleDescription,
                    ProgressDetail: _progressDetail,
                    OverallPercent: _overallPercent,
                    ModulePercent: _modulePercent,
                    Error: Error,
                    Artifacts: ArtifactPaths);
            }
        }
    }

    private sealed class JobProgressDisplay(GenerationJob job) : ICvGenerationProgressDisplay
    {
        public async Task<T> RunAsync<T>(
            CvGenerationProgressPlan plan,
            Func<CvGenerationProgressContext, Task<T>> action,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(action);
            cancellationToken.ThrowIfCancellationRequested();
            var context = new CvGenerationProgressContext(plan, new JobProgressSink(job));
            try
            {
                var result = await action(context);
                context.Complete();
                return result;
            }
            catch
            {
                context.Fail();
                throw;
            }
        }

        private sealed class JobProgressSink(GenerationJob job) : ICvGenerationProgressSink
        {
            public void Update(
                CvProgressDisplayState state,
                CvProgressDisplayEvent displayEvent)
            {
                _ = displayEvent;
                job.RecordProgress(state);
            }
        }
    }
}
