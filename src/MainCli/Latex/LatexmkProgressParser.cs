using System.Globalization;
using System.Text.RegularExpressions;

namespace FindJobHelper.CVGeneration;

internal sealed partial class LatexmkProgressParser
{
    private readonly object _sync = new();
    private readonly IProgressReporter _progress;
    private readonly HashSet<int> _startedXeLatexPasses = [];
    private readonly HashSet<int> _completedXeLatexPasses = [];
    private readonly HashSet<int> _startedPdfConversionPasses = [];
    private int? _activeXeLatexPass;
    private string? _overrunDetail;

    public LatexmkProgressParser(IProgressReporter progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _progress = progress;
        Report();
    }

    public int StartedXeLatexPassCount
    {
        get
        {
            lock (_sync)
            {
                return _startedXeLatexPasses.Count;
            }
        }
    }

    public int StartedPdfConversionPassCount
    {
        get
        {
            lock (_sync)
            {
                return _startedPdfConversionPasses.Count;
            }
        }
    }

    public void ParseLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        lock (_sync)
        {
            var run = RuleRunRegex().Match(line);
            if (run.Success
                && int.TryParse(
                    run.Groups["number"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var passNumber)
                && passNumber > 0)
            {
                switch (run.Groups["rule"].Value)
                {
                    case "xelatex":
                        _activeXeLatexPass = passNumber;
                        _startedXeLatexPasses.Add(passNumber);
                        if (passNumber > CvTemplate.ExpectedXeLatexPassCount)
                        {
                            _overrunDetail =
                                "Rendering PDF — taking longer than expected: " +
                                $"XeLaTeX pass {passNumber}; expected " +
                                CvTemplate.ExpectedXeLatexPassCount;
                        }
                        Report();
                        return;
                    case "xdvipdfmx":
                        _startedPdfConversionPasses.Add(passNumber);
                        if (passNumber
                            > CvTemplate.ExpectedPdfConversionPassCount)
                        {
                            _overrunDetail =
                                "Rendering PDF — taking longer than expected: " +
                                $"xdvipdfmx pass {passNumber}; expected " +
                                CvTemplate.ExpectedPdfConversionPassCount;
                        }
                        Report();
                        return;
                }
            }

            if (_activeXeLatexPass is { } activePass
                && line.Contains(
                    "Output written on main.xdv",
                    StringComparison.Ordinal)
                && _completedXeLatexPasses.Add(activePass))
            {
                _activeXeLatexPass = null;
                Report();
            }
        }
    }

    public void CompleteConversionAndValidation()
    {
        lock (_sync)
        {
            _progress.Report(new(
                CompletedWorkUnits: CvTemplate.ExpectedPdfWorkUnitCount,
                TotalWorkUnits: CvTemplate.ExpectedPdfWorkUnitCount,
                Detail: _overrunDetail ?? "Rendering PDF"));
        }
    }

    private void Report()
    {
        var completedExpectedPasses = _completedXeLatexPasses.Count(
            static pass =>
                pass <= CvTemplate.ExpectedXeLatexPassCount);
        _progress.Report(new(
            CompletedWorkUnits: completedExpectedPasses,
            TotalWorkUnits: CvTemplate.ExpectedPdfWorkUnitCount,
            Detail: _overrunDetail ?? "Rendering PDF"));
    }

    [GeneratedRegex(
        @"Run number (?<number>\d+) of rule '(?<rule>xelatex|xdvipdfmx)'",
        RegexOptions.CultureInvariant)]
    private static partial Regex RuleRunRegex();
}
