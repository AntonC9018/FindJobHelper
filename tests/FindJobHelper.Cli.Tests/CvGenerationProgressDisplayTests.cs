using FindJobHelper.CVGeneration;
using Spectre.Console.Testing;

namespace MainCli.Tests;

public sealed class CvGenerationProgressDisplayTests
{
    [Fact]
    public async Task InteractiveDisplay_RendersReusableCurrentAndOverallRowsAtOneHundredPercent()
    {
        using var console = new TestConsole();
        console.Interactive().Width(120);
        var display = new InteractiveCvGenerationProgressDisplay(console);
        var plan = new CvGenerationProgressPlan([
            new(
                CvGenerationModule.ComputingHeights,
                "Computing heights"),
            new(
                CvGenerationModule.MatchingExperiences,
                "Matching experiences"),
        ]);

        await display.RunAsync(
            plan,
            context =>
            {
                context.BeginModule(CvGenerationModule.ComputingHeights);
                context.Reporter(CvGenerationModule.ComputingHeights)
                    .Report(new(1, 1));
                context.BeginModule(CvGenerationModule.MatchingExperiences);
                context.Reporter(CvGenerationModule.MatchingExperiences)
                    .Report(new(1, 1));
                return Task.FromResult(0);
            },
            CancellationToken.None);

        Assert.Contains("Overall", console.Output, StringComparison.Ordinal);
        Assert.Contains("Current task:", console.Output, StringComparison.Ordinal);
        Assert.Contains("Matching experiences", console.Output, StringComparison.Ordinal);
        var renderedLines = console.Lines.ToArray();
        var overallDescriptionLine = Array.FindLastIndex(
            renderedLines,
            static line => line.TrimEnd() == "Overall");
        var currentDescriptionLine = Array.FindLastIndex(renderedLines, static line =>
            line.Contains("Current task:", StringComparison.Ordinal));
        Assert.True(overallDescriptionLine > 0, console.Output);
        Assert.True(
            currentDescriptionLine > overallDescriptionLine,
            console.Output);
        Assert.DoesNotContain(
            "Overall",
            renderedLines[overallDescriptionLine - 1],
            StringComparison.Ordinal);
        Assert.Contains(
            "100%",
            renderedLines[overallDescriptionLine - 1],
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Current task:",
            renderedLines[currentDescriptionLine - 1],
            StringComparison.Ordinal);
        Assert.Contains(
            "100%",
            renderedLines[currentDescriptionLine - 1],
            StringComparison.Ordinal);
        Assert.Equal(
            renderedLines[overallDescriptionLine - 1]
                .Count(static character => character == '━'),
            renderedLines[currentDescriptionLine - 1]
                .Count(static character => character == '━'));
    }

    [Fact]
    public void ProgressContext_KeepsCurrentModuleLocalAndScalesOverall()
    {
        var sink = new RecordingProgressSink();
        var context = new CvGenerationProgressContext(
            CreatePdfPlan(),
            sink);

        context.BeginModule(CvGenerationModule.ComputingHeights);
        context.Reporter(CvGenerationModule.ComputingHeights)
            .Report(new(CompletedWorkUnits: 1, TotalWorkUnits: 2));

        Assert.Equal(50, sink.Last.ModulePercentage);
        Assert.Equal(12.5, sink.Last.OverallPercentage);
        Assert.Equal(CvProgressDisplayEvent.Progress, sink.LastEvent);
    }

    [Fact]
    public async Task RedirectedDisplay_EmitsRepeatedHeartbeatsTransitionsWarningsAndFinalCompletion()
    {
        var output = new StringWriter
        {
            NewLine = "\n",
        };
        var time = new ManualTimeProvider();
        var display = new RedirectedCvGenerationProgressDisplay(
            output,
            time,
            TimeSpan.FromSeconds(5));
        var plan = new CvGenerationProgressPlan([
            new(CvGenerationModule.RenderingPdf, "Rendering PDF"),
        ]);
        var started = new TaskCompletionSource<CvGenerationProgressContext>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var run = display.RunAsync(
            plan,
            async context =>
            {
                context.BeginModule(CvGenerationModule.RenderingPdf);
                context.Reporter(CvGenerationModule.RenderingPdf)
                    .Report(new(42, 100));
                started.SetResult(context);
                await release.Task;
                return 0;
            },
            CancellationToken.None);
        var context = await started.Task;

        time.Advance(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() =>
            Lines(output).Count(static line =>
                line == "Progress: 42% — Rendering PDF") == 1);
        time.Advance(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() =>
            Lines(output).Count(static line =>
                line == "Progress: 42% — Rendering PDF") == 2);

        const string warning =
            "Rendering PDF — taking longer than expected: " +
            "XeLaTeX pass 3; expected 2";
        context.Reporter(CvGenerationModule.RenderingPdf)
            .Report(new(42, 100, warning));
        context.Reporter(CvGenerationModule.RenderingPdf)
            .Report(new(100, 100, warning));
        release.SetResult();
        await run;

        var text = output.ToString();
        Assert.DoesNotContain('\u001b', text);
        Assert.DoesNotContain('\r', text);
        Assert.Contains(
            "Progress: 0% — Rendering PDF\n",
            text,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            Lines(output).Count(static line =>
                line == "Progress: 42% — Rendering PDF"));
        Assert.Contains(
            $"Progress: 42% — {warning}\n",
            text,
            StringComparison.Ordinal);
        Assert.EndsWith(
            $"Progress: 100% — {warning}\n",
            text,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedirectedDisplay_ReportsFailureWithoutForcingCompletion()
    {
        var output = new StringWriter();
        var display = new RedirectedCvGenerationProgressDisplay(
            output,
            heartbeatInterval: TimeSpan.FromSeconds(5));
        var plan = CreatePdfPlan();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            display.RunAsync<int>(
                plan,
                context =>
                {
                    context.BeginModule(
                        CvGenerationModule.ComputingHeights);
                    context.Reporter(
                            CvGenerationModule.ComputingHeights)
                        .Report(new(3, 10));
                    throw new InvalidOperationException("simulated failure");
                },
                CancellationToken.None));

        Assert.Contains(
            "Progress: 8% — Computing heights — failed",
            output.ToString(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Progress: 100%",
            output.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedirectedDisplay_SuccessLeavesFinalOneHundredPercentVisible()
    {
        var output = new StringWriter();
        var display = new RedirectedCvGenerationProgressDisplay(
            output,
            heartbeatInterval: TimeSpan.FromSeconds(5));
        var plan = new CvGenerationProgressPlan([
            new(
                CvGenerationModule.MatchingExperiences,
                "Matching experiences"),
        ]);

        await display.RunAsync(
            plan,
            context =>
            {
                context.BeginModule(
                    CvGenerationModule.MatchingExperiences);
                context.Reporter(
                        CvGenerationModule.MatchingExperiences)
                    .Report(new(3, 10, "Matching experiences — assembly"));
                return Task.FromResult(0);
            },
            CancellationToken.None);

        Assert.EndsWith(
            "Progress: 100% — Matching experiences — assembly"
            + Environment.NewLine,
            output.ToString(),
            StringComparison.Ordinal);
    }

    private static CvGenerationProgressPlan CreatePdfPlan() =>
        new([
            new(
                CvGenerationModule.ComputingHeights,
                "Computing heights"),
            new(
                CvGenerationModule.MatchingExperiences,
                "Matching experiences"),
            new(
                CvGenerationModule.CreatingTexFile,
                "Creating TeX file"),
            new(
                CvGenerationModule.RenderingPdf,
                "Rendering PDF"),
        ]);

    private static string[] Lines(StringWriter writer) =>
        writer.ToString().Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }

            await Task.Yield();
        }

        Assert.True(condition());
    }

    private sealed class RecordingProgressSink : ICvProgressSink
    {
        public CvProgressDisplayState Last { get; private set; }

        public CvProgressDisplayEvent LastEvent { get; private set; }

        public void Update(
            CvProgressDisplayState state,
            CvProgressDisplayEvent displayEvent)
        {
            Last = state;
            LastEvent = displayEvent;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_sync)
            {
                return DateTimeOffset.UnixEpoch
                    + TimeSpan.FromTicks(_timestamp);
            }
        }

        public override long GetTimestamp()
        {
            lock (_sync)
            {
                return _timestamp;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            var timer = new ManualTimer(
                this,
                callback,
                state,
                dueTime,
                period);
            lock (_sync)
            {
                _timers.Add(timer);
            }
            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            ManualTimer[] timers;
            long now;
            lock (_sync)
            {
                _timestamp = checked(_timestamp + duration.Ticks);
                now = _timestamp;
                timers = _timers.ToArray();
            }

            foreach (var timer in timers)
            {
                timer.FireIfDue(now);
            }
        }

        private long CurrentTimestamp
        {
            get
            {
                lock (_sync)
                {
                    return _timestamp;
                }
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly object _sync = new();
            private readonly ManualTimeProvider _owner;
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private long _dueTimestamp;
            private TimeSpan _period;
            private bool _disposed;

            public ManualTimer(
                ManualTimeProvider owner,
                TimerCallback callback,
                object? state,
                TimeSpan dueTime,
                TimeSpan period)
            {
                _owner = owner;
                _callback = callback;
                _state = state;
                Change(dueTime, period);
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (_sync)
                {
                    if (_disposed)
                    {
                        return false;
                    }

                    _period = period;
                    _dueTimestamp = dueTime == Timeout.InfiniteTimeSpan
                        ? long.MaxValue
                        : checked(
                            _owner.CurrentTimestamp
                            + dueTime.Ticks);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (_sync)
                {
                    _disposed = true;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public void FireIfDue(long now)
            {
                lock (_sync)
                {
                    if (_disposed)
                    {
                        return;
                    }
                    if (now < _dueTimestamp)
                    {
                        return;
                    }

                    _dueTimestamp = _period == Timeout.InfiniteTimeSpan
                        ? long.MaxValue
                        : checked(_dueTimestamp + _period.Ticks);
                }

                _callback(_state);
            }
        }
    }
}
