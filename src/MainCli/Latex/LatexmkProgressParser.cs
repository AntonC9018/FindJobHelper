using System.Globalization;
using System.Text.RegularExpressions;

namespace FindJobHelper.CVGeneration;

internal sealed partial class LatexmkProgressParser
{
    private readonly object _sync = new();
    private readonly IProgressReporter _progress;
    private readonly LatexRenderProgressPlan _renderProgressPlan;
    private readonly HashSet<int> _startedXeLatexPasses = [];
    private readonly HashSet<int> _completedXeLatexPasses = [];
    private readonly HashSet<int> _startedPdfConversionPasses = [];
    private readonly HashSet<CompletedRenderBullet> _completedRenderBullets = [];
    private int? _activeXeLatexPass;
    private LatexRenderBullet? _activeRenderBullet;
    private string? _overrunDetail;

    public LatexmkProgressParser(
        IProgressReporter progress,
        LatexRenderProgressPlan? renderProgressPlan = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _progress = progress;
        _renderProgressPlan =
            renderProgressPlan ?? LatexRenderProgressPlan.Empty;
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
                        _activeRenderBullet = null;
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
                        _activeRenderBullet = null;
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

            if (_activeXeLatexPass is { } markerPass
                && LatexProgressMarkerProtocol.TryParse(
                    line,
                    out var marker)
                && _renderProgressPlan.TryGetBullet(
                    marker.Id,
                    out var bullet))
            {
                _activeRenderBullet = bullet;
                if (marker.Event == LatexProgressMarkerEvent.Completed)
                {
                    _completedRenderBullets.Add(new(
                        markerPass,
                        marker.Id));
                }
                Report();
                return;
            }

            if (_activeXeLatexPass is { } activePass
                && line.Contains(
                    "Output written on main.xdv",
                    StringComparison.Ordinal)
                && _completedXeLatexPasses.Add(activePass))
            {
                CompleteMissingBullets(activePass);
                _activeXeLatexPass = null;
                _activeRenderBullet = null;
                Report();
            }
        }
    }

    public void CompleteConversionAndValidation()
    {
        lock (_sync)
        {
            var totalWorkUnits = CvTemplate.GetPdfWorkUnitCount(
                _renderProgressPlan.Bullets.Length);
            _progress.Report(new(
                CompletedWorkUnits: totalWorkUnits,
                TotalWorkUnits: totalWorkUnits,
                Detail: CurrentDetail()));
        }
    }

    private void Report()
    {
        var completedExpectedPasses = _completedXeLatexPasses.Count(
            static pass =>
                pass <= CvTemplate.ExpectedXeLatexPassCount);
        var completedExpectedBullets = _completedRenderBullets.Count(
            static completion =>
                completion.Pass <= CvTemplate.ExpectedXeLatexPassCount);
        var totalWorkUnits = CvTemplate.GetPdfWorkUnitCount(
            _renderProgressPlan.Bullets.Length);
        _progress.Report(new(
            CompletedWorkUnits:
                completedExpectedPasses + completedExpectedBullets,
            TotalWorkUnits: totalWorkUnits,
            Detail: CurrentDetail()));
    }

    private void CompleteMissingBullets(int pass)
    {
        foreach (var bullet in _renderProgressPlan.Bullets)
        {
            _completedRenderBullets.Add(new(
                pass,
                bullet.MarkerId));
        }
    }

    private string CurrentDetail()
    {
        var baseDetail = _overrunDetail ?? "Rendering PDF";
        if (_activeXeLatexPass is not { } pass
            || _activeRenderBullet is not { } bullet)
        {
            return baseDetail;
        }

        var passDetail = pass <= CvTemplate.ExpectedXeLatexPassCount
            ? $"{pass.ToString(CultureInfo.InvariantCulture)}/{CvTemplate.ExpectedXeLatexPassCount.ToString(CultureInfo.InvariantCulture)}"
            : pass.ToString(CultureInfo.InvariantCulture);
        return $"{baseDetail} — XeLaTeX {passDetail} — " +
            $"{bullet.Section} / {bullet.ExperienceTitle} — " +
            $"bullet {bullet.ItemNumber.ToString(CultureInfo.InvariantCulture)}/" +
            $"{bullet.ItemCount.ToString(CultureInfo.InvariantCulture)} " +
            $"({bullet.MarkerId.Value.ToString(CultureInfo.InvariantCulture)}/" +
            $"{_renderProgressPlan.Bullets.Length.ToString(CultureInfo.InvariantCulture)} overall)";
    }

    private readonly record struct CompletedRenderBullet(
        int Pass,
        LatexProgressMarkerId MarkerId);

    [GeneratedRegex(
        @"Run number (?<number>\d+) of rule '(?<rule>xelatex|xdvipdfmx)'",
        RegexOptions.CultureInvariant)]
    private static partial Regex RuleRunRegex();
}
