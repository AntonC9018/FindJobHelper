using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

internal static class LatexResultTestExtensions
{
    public static async Task<CvMeasurementSnapshot> MeasureAsync(
        this LatexMeasurementService service,
        ExperienceDatabase database,
        CvDataModel model,
        string templatePath,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var result = await service.MeasureAsync(
            database,
            model,
            templatePath,
            progress,
            LatexFontOptions.Default,
            LatexExecutionOptions.Empty,
            cancellationToken);
        return Assert.IsType<CvMeasurementSnapshot>(result);
    }

    public static async Task<IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight>> MeasureAsync(
        this XeLatexMeasurementRunner runner,
        string templatePath,
        IReadOnlyList<LatexMeasurementRequest> requests,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var result = await runner.MeasureAsync(
            templatePath,
            requests,
            progress,
            LatexExecutionOptions.Empty,
            cancellationToken);
        return Assert.IsType<SuccessfulLatexMeasurementRun>(result).Measurements;
    }
}
