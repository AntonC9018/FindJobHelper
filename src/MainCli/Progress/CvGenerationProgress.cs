using System.Collections.Immutable;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace FindJobHelper.CVGeneration;

internal enum CvGenerationTask
{
    ComputingHeights,
    MatchingExperiences,
    CreatingTexFile,
    RenderingPdf,
    CreatingMarkdownFiles,
}

internal readonly record struct CvGenerationProgressTask(
    CvGenerationTask Task,
    string Description,
    double WorkUnits);

internal sealed class CvGenerationProgressPlan
{
    private readonly ImmutableDictionary<CvGenerationTask, CvGenerationProgressTask> _tasks;

    public CvGenerationProgressPlan(
        IEnumerable<CvGenerationProgressTask> tasks)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var ordered = tasks.ToImmutableArray();
        if (ordered.IsEmpty)
        {
            throw new ArgumentException(
                "At least one CV generation progress task is required.",
                nameof(tasks));
        }
        if (ordered.Any(static task =>
                !double.IsFinite(task.WorkUnits) || task.WorkUnits < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tasks),
                "CV generation task weights must be finite and non-negative.");
        }

        OrderedTasks = ordered;
        _tasks = ordered.ToImmutableDictionary(static task => task.Task);
    }

    public ImmutableArray<CvGenerationProgressTask> OrderedTasks { get; }

    public CvGenerationProgressTask Get(CvGenerationTask task) =>
        _tasks.TryGetValue(task, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Progress task '{task}' is not applicable to this generation.");
}

internal readonly record struct WeightedProgressUpdate(
    double TaskPercentage,
    double OverallPercentage,
    string? Detail);

internal sealed class WeightedProgressAggregator
{
    private readonly object _sync = new();
    private readonly ImmutableDictionary<CvGenerationTask, double> _weights;
    private readonly Dictionary<CvGenerationTask, double> _fractions = new();
    private readonly double _totalWeight;
    private double _overallFraction;

    public WeightedProgressAggregator(CvGenerationProgressPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _weights = plan.OrderedTasks.ToImmutableDictionary(
            static task => task.Task,
            static task => task.WorkUnits);
        _totalWeight = _weights.Values.Sum();
    }

    public double OverallPercentage
    {
        get
        {
            lock (_sync)
            {
                return _overallFraction * 100;
            }
        }
    }

    public WeightedProgressUpdate Update(
        CvGenerationTask task,
        ProgressReport report)
    {
        lock (_sync)
        {
            if (!_weights.ContainsKey(task))
            {
                throw new InvalidOperationException(
                    $"Progress task '{task}' is not part of the generation plan.");
            }

            var fraction = Math.Max(
                _fractions.GetValueOrDefault(task),
                ProgressMath.Fraction(report));
            _fractions[task] = fraction;

            var calculatedOverall = _totalWeight <= 0
                ? 1
                : _weights.Sum(pair =>
                    pair.Value * _fractions.GetValueOrDefault(pair.Key))
                  / _totalWeight;
            _overallFraction = Math.Max(
                _overallFraction,
                Math.Clamp(calculatedOverall, 0, 1));

            return new(
                TaskPercentage: fraction * 100,
                OverallPercentage: _overallFraction * 100,
                Detail: report.Detail);
        }
    }
}

internal enum CvProgressDisplayEvent
{
    Progress,
    TaskTransition,
    Warning,
    TaskCompletion,
    Failure,
    FinalCompletion,
}

internal readonly record struct CvProgressDisplayState(
    string TaskDescription,
    string DisplayDescription,
    double TaskPercentage,
    double OverallPercentage);

internal interface ICvProgressSink
{
    void Update(
        CvProgressDisplayState state,
        CvProgressDisplayEvent displayEvent);
}

internal sealed class CvGenerationProgressContext
{
    private readonly object _sync = new();
    private readonly CvGenerationProgressPlan _plan;
    private readonly WeightedProgressAggregator _aggregator;
    private readonly ICvProgressSink _sink;
    private readonly Dictionary<CvGenerationTask, IProgressReporter> _reporters = new();
    private CvGenerationTask? _currentTask;
    private CvProgressDisplayState _lastState;
    private bool _failed;
    private bool _finished;

    public CvGenerationProgressContext(
        CvGenerationProgressPlan plan,
        ICvProgressSink sink)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sink);

        _plan = plan;
        _sink = sink;
        _aggregator = new(plan);
        foreach (var task in plan.OrderedTasks)
        {
            _reporters.Add(task.Task, new TaskReporter(this, task.Task));
        }
    }

    public IProgressReporter Reporter(CvGenerationTask task)
    {
        lock (_sync)
        {
            return _reporters.TryGetValue(task, out var reporter)
                ? reporter
                : throw new InvalidOperationException(
                    $"Progress task '{task}' is not applicable to this generation.");
        }
    }

    public void BeginTask(CvGenerationTask task)
    {
        lock (_sync)
        {
            ThrowIfFinished();
            BeginTaskCore(task);
        }
    }

    public void Complete()
    {
        lock (_sync)
        {
            ThrowIfFinished();
            _finished = true;
            _lastState = _lastState with
            {
                TaskPercentage = 100,
                OverallPercentage = 100,
            };
            _sink.Update(_lastState, CvProgressDisplayEvent.FinalCompletion);
        }
    }

    public void Fail()
    {
        lock (_sync)
        {
            if (_failed || _finished)
            {
                return;
            }

            _failed = true;
            _sink.Update(_lastState, CvProgressDisplayEvent.Failure);
        }
    }

    private void Report(
        CvGenerationTask task,
        ProgressReport report)
    {
        lock (_sync)
        {
            ThrowIfFinished();
            if (_currentTask != task)
            {
                BeginTaskCore(task);
            }

            var definition = _plan.Get(task);
            var update = _aggregator.Update(task, report);
            var displayDescription = string.IsNullOrWhiteSpace(update.Detail)
                ? definition.Description
                : update.Detail;
            _lastState = new(
                TaskDescription: definition.Description,
                DisplayDescription: displayDescription!,
                TaskPercentage: update.TaskPercentage,
                OverallPercentage: update.OverallPercentage);

            var displayEvent = IsWarning(update.Detail)
                ? CvProgressDisplayEvent.Warning
                : update.TaskPercentage >= 100
                    ? CvProgressDisplayEvent.TaskCompletion
                    : CvProgressDisplayEvent.Progress;
            _sink.Update(_lastState, displayEvent);
        }
    }

    private void BeginTaskCore(CvGenerationTask task)
    {
        var definition = _plan.Get(task);
        _currentTask = task;
        _lastState = new(
            TaskDescription: definition.Description,
            DisplayDescription: definition.Description,
            TaskPercentage: 0,
            OverallPercentage: _aggregator.OverallPercentage);
        _sink.Update(_lastState, CvProgressDisplayEvent.TaskTransition);
    }

    private void ThrowIfFinished()
    {
        if (_finished)
        {
            throw new InvalidOperationException(
                "CV generation progress has already completed.");
        }
    }

    private static bool IsWarning(string? detail) =>
        detail?.Contains(
            "taking longer than expected",
            StringComparison.OrdinalIgnoreCase) == true;

    private sealed class TaskReporter(
        CvGenerationProgressContext owner,
        CvGenerationTask task) : IProgressReporter
    {
        public void Report(ProgressReport report) => owner.Report(task, report);
    }
}

internal interface ICvGenerationProgressDisplay
{
    Task<T> RunAsync<T>(
        CvGenerationProgressPlan plan,
        Func<CvGenerationProgressContext, Task<T>> action,
        CancellationToken cancellationToken);
}

internal sealed class InteractiveCvGenerationProgressDisplay(
    IAnsiConsole console,
    TimeProvider? timeProvider = null) : ICvGenerationProgressDisplay
{
    public async Task<T> RunAsync<T>(
        CvGenerationProgressPlan plan,
        Func<CvGenerationProgressContext, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        var progress = new Progress(
            console ?? throw new ArgumentNullException(nameof(console)),
            timeProvider ?? TimeProvider.System)
        {
            AutoRefresh = false,
            AutoClear = false,
            HideCompleted = false,
        };
        progress.Columns(
            new StackedProgressColumn());

        return await progress.StartAsync(async spectreContext =>
        {
            var overall = spectreContext.AddTask(
                "Overall",
                autoStart: true,
                maxValue: 100);
            var current = spectreContext.AddTask(
                "Current task",
                autoStart: true,
                maxValue: 100);
            var sink = new SpectreProgressSink(
                spectreContext,
                overall,
                current);
            var context = new CvGenerationProgressContext(plan, sink);
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
        });
    }

    private sealed class StackedProgressColumn : ProgressColumn
    {
        private readonly ProgressBarColumn _progressBar = new();
        private readonly PercentageColumn _percentage = new();

        public override IRenderable Render(
            RenderOptions options,
            ProgressTask task,
            TimeSpan deltaTime) =>
            new Rows(
                new Columns(
                    _progressBar.Render(options, task, deltaTime),
                    _percentage.Render(options, task, deltaTime)),
                new Markup(task.Description));
    }

    private sealed class SpectreProgressSink(
        ProgressContext context,
        ProgressTask overall,
        ProgressTask current) : ICvProgressSink
    {
        private readonly object _sync = new();

        public void Update(
            CvProgressDisplayState state,
            CvProgressDisplayEvent displayEvent)
        {
            _ = displayEvent;
            lock (_sync)
            {
                overall.Value = Math.Clamp(state.OverallPercentage, 0, 100);
                current.Value = Math.Clamp(state.TaskPercentage, 0, 100);
                current.Description =
                    $"Current task: {Markup.Escape(state.DisplayDescription)}";
                context.Refresh();
            }
        }
    }
}

internal sealed class RedirectedCvGenerationProgressDisplay(
    TextWriter writer,
    TimeProvider? timeProvider = null,
    TimeSpan? heartbeatInterval = null) : ICvGenerationProgressDisplay
{
    private readonly TimeSpan _heartbeatInterval =
        heartbeatInterval ?? TimeSpan.FromSeconds(5);

    public async Task<T> RunAsync<T>(
        CvGenerationProgressPlan plan,
        Func<CvGenerationProgressContext, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(action);
        if (_heartbeatInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heartbeatInterval),
                "The heartbeat interval must be positive.");
        }

        var sink = new RedirectedProgressSink(
            writer ?? throw new ArgumentNullException(nameof(writer)));
        var context = new CvGenerationProgressContext(plan, sink);
        using var heartbeatCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeat = RunHeartbeatAsync(
            sink,
            timeProvider ?? TimeProvider.System,
            heartbeatCancellation.Token);
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
        finally
        {
            await heartbeatCancellation.CancelAsync();
            try
            {
                await heartbeat;
            }
            catch (OperationCanceledException)
                when (heartbeatCancellation.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RunHeartbeatAsync(
        RedirectedProgressSink sink,
        TimeProvider provider,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(
                _heartbeatInterval,
                provider,
                cancellationToken);
            sink.Heartbeat();
        }
    }

    private sealed class RedirectedProgressSink(TextWriter writer) : ICvProgressSink
    {
        private readonly object _sync = new();
        private CvProgressDisplayState? _state;
        private string? _lastWarning;

        public void Update(
            CvProgressDisplayState state,
            CvProgressDisplayEvent displayEvent)
        {
            lock (_sync)
            {
                _state = state;
                switch (displayEvent)
                {
                    case CvProgressDisplayEvent.TaskTransition:
                    case CvProgressDisplayEvent.Failure:
                    case CvProgressDisplayEvent.FinalCompletion:
                        Write(state, displayEvent);
                        break;
                    case CvProgressDisplayEvent.Warning:
                        if (!string.Equals(
                                _lastWarning,
                                state.DisplayDescription,
                                StringComparison.Ordinal))
                        {
                            _lastWarning = state.DisplayDescription;
                            Write(state, displayEvent);
                        }
                        break;
                    case CvProgressDisplayEvent.Progress:
                    case CvProgressDisplayEvent.TaskCompletion:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(displayEvent),
                            displayEvent,
                            null);
                }
            }
        }

        public void Heartbeat()
        {
            lock (_sync)
            {
                if (_state is { } state)
                {
                    Write(state, CvProgressDisplayEvent.Progress);
                }
            }
        }

        private void Write(
            CvProgressDisplayState state,
            CvProgressDisplayEvent displayEvent)
        {
            var percentage = (int) Math.Round(
                Math.Clamp(state.OverallPercentage, 0, 100),
                MidpointRounding.AwayFromZero);
            var suffix = displayEvent == CvProgressDisplayEvent.Failure
                ? " — failed"
                : string.Empty;
            writer.WriteLine(
                $"Progress: {percentage}% — {state.DisplayDescription}{suffix}");
            writer.Flush();
        }
    }
}

internal static class CvGenerationProgressDisplay
{
    public static ICvGenerationProgressDisplay CreateDefault()
    {
        var console = AnsiConsole.Console;
        return !Console.IsOutputRedirected
               && console.Profile.Capabilities.Interactive
            ? new InteractiveCvGenerationProgressDisplay(console)
            : new RedirectedCvGenerationProgressDisplay(Console.Out);
    }
}
