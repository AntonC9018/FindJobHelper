namespace FindJobHelper.CVGeneration;

public readonly record struct ProgressReport(
    double CompletedWorkUnits,
    double TotalWorkUnits,
    string? Detail = null);

public interface IProgressReporter
{
    void Report(ProgressReport report);
}

public sealed class NoOpProgressReporter : IProgressReporter
{
    public static NoOpProgressReporter Instance { get; } = new();

    private NoOpProgressReporter()
    {
    }

    public void Report(ProgressReport report)
    {
    }
}

public readonly record struct LatexProgressReporters(
    IProgressReporter Tex,
    IProgressReporter Pdf);

internal sealed class ProgressRangeReporter(
    IProgressReporter target,
    double offset,
    double length,
    double targetTotal) : IProgressReporter
{
    public void Report(ProgressReport report)
    {
        ArgumentNullException.ThrowIfNull(target);

        var fraction = ProgressMath.Fraction(report);
        target.Report(new(
            CompletedWorkUnits: offset + length * fraction,
            TotalWorkUnits: targetTotal,
            Detail: report.Detail));
    }
}

internal static class ProgressMath
{
    public static double Fraction(ProgressReport report)
    {
        if (!double.IsFinite(report.TotalWorkUnits)
            || report.TotalWorkUnits <= 0)
        {
            return 1;
        }

        var completed = double.IsFinite(report.CompletedWorkUnits)
            ? report.CompletedWorkUnits
            : 0;
        return Math.Clamp(completed / report.TotalWorkUnits, 0, 1);
    }

    public static double Percentage(ProgressReport report) =>
        Fraction(report) * 100;
}
