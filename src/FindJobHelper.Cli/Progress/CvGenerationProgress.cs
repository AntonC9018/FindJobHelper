using System.Collections.Immutable;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace FindJobHelper.CVGeneration;

internal enum CvGenerationModule
{
    ComputingHeights,
    MatchingExperiences,
    CreatingTexFile,
    RenderingPdf,
    CreatingMarkdownFiles,
}

internal readonly record struct CvGenerationProgressModule(
    CvGenerationModule Module,
    string Description);

internal sealed class CvGenerationProgressPlan
{
    private readonly ImmutableDictionary<
        CvGenerationModule,
        CvGenerationProgressModule> _modules;

    public CvGenerationProgressPlan(
        IEnumerable<CvGenerationProgressModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var ordered = modules.ToImmutableArray();
        if (ordered.IsEmpty)
        {
            throw new ArgumentException(
                "At least one CV generation progress module is required.",
                nameof(modules));
        }

        OrderedModules = ordered;
        _modules = ordered.ToImmutableDictionary(
            static module => module.Module);
    }

    public ImmutableArray<CvGenerationProgressModule> OrderedModules { get; }

    public CvGenerationProgressModule Get(CvGenerationModule module) =>
        _modules.TryGetValue(module, out var definition)
            ? definition
            : throw new InvalidOperationException(
                $"Progress module '{module}' is not applicable to this generation.");
}

internal readonly record struct EqualShareProgressUpdate(
    double ModulePercentage,
    double OverallPercentage,
    string? Detail);

internal sealed class EqualShareProgressAggregator
{
    private readonly object _sync = new();
    private readonly ImmutableHashSet<CvGenerationModule> _modules;
    private readonly Dictionary<CvGenerationModule, double> _fractions = new();
    private double _overallFraction;

    public EqualShareProgressAggregator(CvGenerationProgressPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        _modules = plan.OrderedModules
            .Select(static module => module.Module)
            .ToImmutableHashSet();
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

    public EqualShareProgressUpdate Update(
        CvGenerationModule module,
        ProgressReport report)
    {
        lock (_sync)
        {
            if (!_modules.Contains(module))
            {
                throw new InvalidOperationException(
                    $"Progress module '{module}' is not part of the generation plan.");
            }

            var reportedFraction = Math.Clamp(
                ProgressMath.Fraction(report),
                0,
                1);
            var fraction = Math.Max(
                _fractions.GetValueOrDefault(module),
                reportedFraction);
            _fractions[module] = fraction;

            var calculatedOverall =
                _fractions.Values.Sum() / _modules.Count;
            _overallFraction = Math.Max(
                _overallFraction,
                Math.Clamp(calculatedOverall, 0, 1));

            return new(
                ModulePercentage: fraction * 100,
                OverallPercentage: _overallFraction * 100,
                Detail: report.Detail);
        }
    }
}

internal enum CvProgressDisplayEvent
{
    Progress,
    ModuleTransition,
    Warning,
    ModuleCompletion,
    Failure,
    FinalCompletion,
}

internal readonly record struct CvProgressDisplayState(
    string ModuleDescription,
    string DisplayDescription,
    double ModulePercentage,
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
    private readonly EqualShareProgressAggregator _aggregator;
    private readonly ICvProgressSink _sink;
    private readonly Dictionary<CvGenerationModule, IProgressReporter> _reporters = new();
    private CvGenerationModule? _currentModule;
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
        foreach (var module in plan.OrderedModules)
        {
            _reporters.Add(
                module.Module,
                new ModuleReporter(this, module.Module));
        }
    }

    public IProgressReporter Reporter(CvGenerationModule module)
    {
        lock (_sync)
        {
            return _reporters.TryGetValue(module, out var reporter)
                ? reporter
                : throw new InvalidOperationException(
                    $"Progress module '{module}' is not applicable to this generation.");
        }
    }

    public void BeginModule(CvGenerationModule module)
    {
        lock (_sync)
        {
            ThrowIfFinished();
            BeginModuleCore(module);
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
                ModulePercentage = 100,
                OverallPercentage = 100,
            };
            _sink.Update(_lastState, CvProgressDisplayEvent.FinalCompletion);
        }
    }

    public void Fail()
    {
        lock (_sync)
        {
            if (_failed)
            {
                return;
            }
            if (_finished)
            {
                return;
            }

            _failed = true;
            _sink.Update(_lastState, CvProgressDisplayEvent.Failure);
        }
    }

    private void Report(
        CvGenerationModule module,
        ProgressReport report)
    {
        lock (_sync)
        {
            ThrowIfFinished();
            if (_currentModule != module)
            {
                BeginModuleCore(module);
            }

            var definition = _plan.Get(module);
            var update = _aggregator.Update(module, report);
            var displayDescription = string.IsNullOrWhiteSpace(update.Detail)
                ? definition.Description
                : update.Detail;
            _lastState = new(
                ModuleDescription: definition.Description,
                DisplayDescription: displayDescription!,
                ModulePercentage: update.ModulePercentage,
                OverallPercentage: update.OverallPercentage);

            var displayEvent = IsWarning(update.Detail)
                ? CvProgressDisplayEvent.Warning
                : update.ModulePercentage >= 100
                    ? CvProgressDisplayEvent.ModuleCompletion
                    : CvProgressDisplayEvent.Progress;
            _sink.Update(_lastState, displayEvent);
        }
    }

    private void BeginModuleCore(CvGenerationModule module)
    {
        var definition = _plan.Get(module);
        _currentModule = module;
        _lastState = new(
            ModuleDescription: definition.Description,
            DisplayDescription: definition.Description,
            ModulePercentage: 0,
            OverallPercentage: _aggregator.OverallPercentage);
        _sink.Update(_lastState, CvProgressDisplayEvent.ModuleTransition);
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

    private sealed class ModuleReporter(
        CvGenerationProgressContext owner,
        CvGenerationModule module) : IProgressReporter
    {
        public void Report(ProgressReport report) =>
            owner.Report(module, report);
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
        private const int Width = 50;
        private readonly ProgressBarColumn _progressBar = new();
        private readonly PercentageColumn _percentage = new();

        public override int? GetColumnWidth(RenderOptions options) => Width;

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
                current.Value = Math.Clamp(state.ModulePercentage, 0, 100);
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
                    case CvProgressDisplayEvent.ModuleTransition:
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
                    case CvProgressDisplayEvent.ModuleCompletion:
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
        if (Console.IsOutputRedirected)
        {
            return new RedirectedCvGenerationProgressDisplay(Console.Out);
        }

        if (!console.Profile.Capabilities.Interactive)
        {
            return new RedirectedCvGenerationProgressDisplay(Console.Out);
        }

        return new InteractiveCvGenerationProgressDisplay(console);
    }
}
