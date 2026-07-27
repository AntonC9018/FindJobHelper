using FindJobHelper.CVGeneration;
using Spectre.Console.Testing;

namespace FindJobHelper.Core.Tests;

public sealed class CvGenerationProgressDisplayTests
{
    [Fact]
    public async Task InteractiveDisplay_RendersReusableCurrentAndOverallRowsAtOneHundredPercent()
    {
        using var console = new TestConsole();
        console.Interactive().Width(120);
        var display = new InteractiveCvGenerationProgressDisplay(console);
        var plan = new CvGenerationProgressPlan([
            new(CvGenerationTask.ComputingHeights, "Computing heights", 1),
            new(CvGenerationTask.MatchingExperiences, "Matching experiences", 1),
        ]);

        await display.RunAsync(
            plan,
            context =>
            {
                context.BeginTask(CvGenerationTask.ComputingHeights);
                context.Reporter(CvGenerationTask.ComputingHeights)
                    .Report(new(1, 1));
                context.BeginTask(CvGenerationTask.MatchingExperiences);
                context.Reporter(CvGenerationTask.MatchingExperiences)
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
            new(CvGenerationTask.RenderingPdf, "Rendering PDF", 100),
        ]);
        var started = new TaskCompletionSource<CvGenerationProgressContext>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var run = display.RunAsync(
            plan,
            async context =>
            {
                context.BeginTask(CvGenerationTask.RenderingPdf);
                context.Reporter(CvGenerationTask.RenderingPdf)
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
        context.Reporter(CvGenerationTask.RenderingPdf)
            .Report(new(42, 100, warning));
        context.Reporter(CvGenerationTask.RenderingPdf)
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
        var plan = new CvGenerationProgressPlan([
            new(CvGenerationTask.MatchingExperiences, "Matching experiences", 10),
        ]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            display.RunAsync<int>(
                plan,
                context =>
                {
                    context.BeginTask(CvGenerationTask.MatchingExperiences);
                    context.Reporter(CvGenerationTask.MatchingExperiences)
                        .Report(new(3, 10));
                    throw new InvalidOperationException("simulated failure");
                },
                CancellationToken.None));

        Assert.Contains(
            "Progress: 30% — Matching experiences — failed",
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
            new(CvGenerationTask.MatchingExperiences, "Matching experiences", 10),
        ]);

        await display.RunAsync(
            plan,
            context =>
            {
                context.BeginTask(CvGenerationTask.MatchingExperiences);
                context.Reporter(CvGenerationTask.MatchingExperiences)
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

    private static string[] Lines(StringWriter writer) =>
        writer.ToString().Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Yield();
        }

        Assert.True(condition());
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
                    if (_disposed || now < _dueTimestamp)
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
