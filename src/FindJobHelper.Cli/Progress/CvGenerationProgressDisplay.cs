using System.Collections.Immutable;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace FindJobHelper.CVGeneration;

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
        ProgressTask current) : ICvGenerationProgressSink
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
