using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

internal sealed class ProgressTestReporter : IProgressReporter
{
    private readonly object _sync = new();
    private readonly List<ProgressReport> _reports = [];

    public IReadOnlyList<ProgressReport> Reports
    {
        get
        {
            lock (_sync)
            {
                return _reports.ToArray();
            }
        }
    }

    public ProgressReport Last => Reports[^1];

    public void Report(ProgressReport report)
    {
        lock (_sync)
        {
            _reports.Add(report);
        }
    }
}
