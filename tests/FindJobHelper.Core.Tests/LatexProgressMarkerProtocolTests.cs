using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexProgressMarkerProtocolTests
{
    [Fact]
    public void Marker_RoundTripsThroughTypeoutAndRealtimeLogParser()
    {
        var cases = new[]
        {
            (
                Event: LatexProgressMarkerEvent.Started,
                Category: LatexProgressMarkerCategory.Measurement,
                Value: 1,
                ExpectedLogLine: "FJH_PROGRESS_STARTED:M00000001"),
            (
                Event: LatexProgressMarkerEvent.Completed,
                Category: LatexProgressMarkerCategory.RenderBullet,
                Value: 42,
                ExpectedLogLine: "FJH_PROGRESS_COMPLETED:B00000042"),
        };

        foreach (var testCase in cases)
        {
            var markerId = new LatexProgressMarkerId(
                testCase.Category,
                testCase.Value);

            Assert.Equal(
                $@"\typeout{{{testCase.ExpectedLogLine}}}",
                LatexProgressMarkerProtocol.RenderTypeout(
                    testCase.Event,
                    markerId));
            Assert.True(LatexProgressMarkerProtocol.TryParse(
                $"latex output before {testCase.ExpectedLogLine}",
                out var parsed));
            Assert.Equal(
                new LatexProgressMarker(testCase.Event, markerId),
                parsed);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("FJH_PROGRESS_COMPLETED:")]
    [InlineData("FJH_PROGRESS_COMPLETED:B00000000")]
    [InlineData("FJH_PROGRESS_COMPLETED:B0000000")]
    [InlineData("FJH_PROGRESS_COMPLETED:X00000001")]
    [InlineData("FJH_PROGRESS_COMPLETED:B00000001 trailing")]
    [InlineData("FJH_PROGRESS_FINISHED:B00000001")]
    public void MalformedOrUnrelatedMarker_IsRejected(string line)
    {
        Assert.False(LatexProgressMarkerProtocol.TryParse(
            line,
            out _));
    }

    [Fact]
    public void MeasurementCompletionParser_UsesSharedProtocolAndFiltersEventsAndCategories()
    {
        var request = new LatexMeasurementRequest(
            new MeasurementCorrelationId(1),
            new LatexMeasurementCacheKey(
                1,
                LatexMeasurementKind.DocumentHeader,
                new string('a', 64)),
            "fragment",
            LatexMeasurementMode.Box);
        var progress = new ProgressTestReporter();
        var parser = new LatexMeasurementCompletionParser(
            [request],
            progress);

        parser.ParseLine(LatexProgressMarkerProtocol.FormatLogLine(
            LatexProgressMarkerEvent.Started,
            request.CorrelationId.ProgressMarkerId));
        parser.ParseLine(LatexProgressMarkerProtocol.FormatLogLine(
            LatexProgressMarkerEvent.Completed,
            new(
                LatexProgressMarkerCategory.RenderBullet,
                1)));
        Assert.Empty(progress.Reports);

        var completion = LatexProgressMarkerProtocol.FormatLogLine(
            LatexProgressMarkerEvent.Completed,
            request.CorrelationId.ProgressMarkerId);
        parser.ParseLine(completion);
        parser.ParseLine(completion);

        Assert.Equal(
            new ProgressReport(
                1,
                1,
                "Computing heights — XeLaTeX measurement completed"),
            progress.Last);
        Assert.Single(progress.Reports);
    }
}
