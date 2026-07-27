using System.Globalization;
using System.Text;
using CliWrap;

namespace FindJobHelper.CVGeneration;

internal sealed record LatexMeasurementRequest(
    MeasurementCorrelationId CorrelationId,
    LatexMeasurementCacheKey CacheKey,
    string RenderedFragment,
    LatexMeasurementMode Mode);

internal interface ILatexMeasurementRunner
{
    Task<IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight>> MeasureAsync(
        string templatePath,
        IReadOnlyList<LatexMeasurementRequest> requests,
        IProgressReporter progress,
        CancellationToken cancellationToken);
}

internal sealed class XeLatexMeasurementRunner : ILatexMeasurementRunner
{
    public async Task<IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight>> MeasureAsync(
        string templatePath,
        IReadOnlyList<LatexMeasurementRequest> requests,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        progress.Report(new(
            CompletedWorkUnits: 0,
            TotalWorkUnits: requests.Count,
            Detail: "Computing heights — running XeLaTeX measurements"));
        if (requests.Count == 0)
        {
            return new Dictionary<MeasurementCorrelationId, LatexHeight>();
        }

        var workingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-measurement-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workingDirectory);
        const string texFileName = "measurement.tex";
        const string resultFileName = "measurement-results.txt";
        try
        {
            var source = LatexMeasurementDocument.Generate(templatePath, resultFileName, requests);
            await File.WriteAllTextAsync(
                Path.Combine(workingDirectory, texFileName),
                source,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var standardOutput = new StringBuilder();
            var standardError = new StringBuilder();
            var progressParser = new LatexMeasurementCompletionParser(
                requests,
                progress);
            var result = await Cli.Wrap("xelatex")
                .WithArguments([
                    "-interaction=nonstopmode",
                    "-halt-on-error",
                    "-file-line-error",
                    texFileName,
                ])
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(CommandResultValidation.None)
                .WithStandardOutputPipe(PipeTarget.Merge(
                    PipeTarget.ToStringBuilder(standardOutput),
                    PipeTarget.ToDelegate(progressParser.ParseLine)))
                .WithStandardErrorPipe(PipeTarget.ToStringBuilder(standardError))
                .ExecuteAsync(cancellationToken);
            if (!result.IsSuccess)
            {
                if (CvLatexErrors.ContainsMetadataLeftOverflowMarker(
                        $"{standardError}{Environment.NewLine}{standardOutput}"))
                {
                    throw new CvMetadataOverflowException();
                }

                throw new CvMeasurementException(
                    $"XeLaTeX height measurement failed with exit code {result.ExitCode}: {standardError}{Environment.NewLine}{standardOutput}");
            }

            var resultPath = Path.Combine(workingDirectory, resultFileName);
            if (!File.Exists(resultPath))
            {
                throw new CvMeasurementException(
                    "XeLaTeX completed without producing the height result file.");
            }

            var lines = await File.ReadAllLinesAsync(resultPath, cancellationToken);
            var parsed = LatexMeasurementResultParser.ParseAndValidate(lines, requests);
            progressParser.CompleteMissingMeasurements();
            return parsed;
        }
        finally
        {
            try
            {
                Directory.Delete(workingDirectory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
    }
}

internal sealed class LatexMeasurementCompletionParser
{
    private const string CompletionMarker = "FJH_MEASUREMENT_COMPLETED:";

    private readonly object _sync = new();
    private readonly IReadOnlyList<LatexMeasurementRequest> _requests;
    private readonly HashSet<MeasurementCorrelationId> _expected;
    private readonly HashSet<MeasurementCorrelationId> _completed = [];
    private readonly IProgressReporter _progress;

    public LatexMeasurementCompletionParser(
        IReadOnlyList<LatexMeasurementRequest> requests,
        IProgressReporter progress)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(progress);

        _requests = requests;
        _expected = requests
            .Select(static request => request.CorrelationId)
            .ToHashSet();
        _progress = progress;
    }

    public void ParseLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var markerIndex = line.IndexOf(
            CompletionMarker,
            StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return;
        }

        var tokenStart = markerIndex + CompletionMarker.Length;
        var token = line.AsSpan(tokenStart).Trim();
        if (token.Length != 9
            || token[0] != 'M'
            || !int.TryParse(
                token[1..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var value))
        {
            return;
        }

        Complete(new(value));
    }

    public void CompleteMissingMeasurements()
    {
        foreach (var request in _requests)
        {
            Complete(request.CorrelationId);
        }
    }

    private void Complete(MeasurementCorrelationId correlationId)
    {
        lock (_sync)
        {
            if (!_expected.Contains(correlationId)
                || !_completed.Add(correlationId))
            {
                return;
            }

            _progress.Report(new(
                CompletedWorkUnits: _completed.Count,
                TotalWorkUnits: _requests.Count,
                Detail: "Computing heights — XeLaTeX measurement completed"));
        }
    }
}

internal static class LatexMeasurementDocument
{
    public static string Generate(
        string templatePath,
        string resultFileName,
        IReadOnlyList<LatexMeasurementRequest> requests)
    {
        var normalizedTemplatePath = templatePath.Replace('\\', '/');
        var source = new StringBuilder();
        source.Append("\\input{").Append(normalizedTemplatePath).AppendLine("}");
        source.AppendLine(@"\begin{document}");
        source.AppendLine(@"\pagestyle{fancy}");
        source.AppendLine(@"\newwrite\fjhmeasurementresults");
        source.AppendLine(@"\newcount\fjhmeasurementpage");
        source.AppendLine(@"\newdimen\fjhmeasurementpagetotal");
        source.Append("\\immediate\\openout\\fjhmeasurementresults=").AppendLine(resultFileName);

        foreach (var request in requests)
        {
            if (request.Mode is LatexMeasurementMode.DocumentHeader or LatexMeasurementMode.PageStart)
            {
                source.AppendLine($$"""
                    \clearpage
                    \begingroup
                    \fjhmeasurementpagetotal=\pagetotal
                    {{request.RenderedFragment}}
                    \par
                    % A zero-height box makes TeX account for pending vertical
                    % glue without adding an assumed measurement correction.
                    \nointerlineskip\vbox{}
                    \dimen0=\dimexpr\pagetotal-\fjhmeasurementpagetotal\relax
                    \setbox\cvmeasurementbox=\vbox to \dimen0{\vfil}
                    \immediate\write\fjhmeasurementresults{FJH1|corr={{request.CorrelationId}}|rule={{request.CacheKey.RuleVersion.ToString(CultureInfo.InvariantCulture)}}|kind={{request.CacheKey.Kind}}|sha256={{request.CacheKey.ContentHash}}|height-sp=\number\dimexpr\ht\cvmeasurementbox+\dp\cvmeasurementbox\relax}
                    \typeout{FJH_MEASUREMENT_COMPLETED:{{request.CorrelationId}}}
                    \endgroup
                    \clearpage
                    """);
                continue;
            }

            var setBoxCommand = request.Mode switch
            {
                LatexMeasurementMode.Box => @"\cvsetmeasurementbox",
                LatexMeasurementMode.FlowBlock => @"\cvsetmeasurementsectionbox",
                LatexMeasurementMode.FreshPageFlowBlock => @"\cvsetmeasurementfreshsectionbox",
                LatexMeasurementMode.SectionChrome => @"\cvsetmeasurementsectionchromebox",
                LatexMeasurementMode.FreshPageSectionChrome => @"\cvsetmeasurementfreshsectionchromebox",
                LatexMeasurementMode.SplitSectionStart => @"\cvsetmeasurementsplitsectionstartbox",
                LatexMeasurementMode.FreshPageSplitSectionStart => @"\cvsetmeasurementfreshsplitsectionstartbox",
                LatexMeasurementMode.SplitSectionEnd => @"\cvsetmeasurementsplitsectionendbox",
                LatexMeasurementMode.FreshPageContinuation => @"\cvsetmeasurementfreshcontinuationbox",
                LatexMeasurementMode.ExperienceItemMarginal => @"\cvsetmeasurementitembox",
                LatexMeasurementMode.ExperienceChromeWithoutPermanentItems => @"\cvsetmeasurementexperiencechromebox",
                _ => throw new ArgumentOutOfRangeException(nameof(request.Mode), request.Mode, null),
            };
            source.AppendLine($$"""
                \begingroup
                \fjhmeasurementpage=\value{page}
                \fjhmeasurementpagetotal=\pagetotal
                {{setBoxCommand}}{
                {{request.RenderedFragment}}
                }
                \ifnum\value{page}=\fjhmeasurementpage\else\errmessage{FJH measurement changed page counter}\fi
                \ifdim\pagetotal=\fjhmeasurementpagetotal\else\errmessage{FJH measurement changed pagetotal}\fi
                \immediate\write\fjhmeasurementresults{FJH1|corr={{request.CorrelationId}}|rule={{request.CacheKey.RuleVersion.ToString(CultureInfo.InvariantCulture)}}|kind={{request.CacheKey.Kind}}|sha256={{request.CacheKey.ContentHash}}|height-sp=\number\dimexpr\ht\cvmeasurementbox+\dp\cvmeasurementbox\relax}
                \typeout{FJH_MEASUREMENT_COMPLETED:{{request.CorrelationId}}}
                \endgroup
                """);
        }

        source.AppendLine(@"\immediate\closeout\fjhmeasurementresults");
        source.AppendLine(@"\end{document}");
        return source.ToString();
    }
}

internal static class LatexMeasurementResultParser
{
    public static IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight> ParseAndValidate(
        IEnumerable<string> lines,
        IReadOnlyList<LatexMeasurementRequest> requests)
    {
        var expected = requests.ToDictionary(static request => request.CorrelationId);
        var results = new Dictionary<MeasurementCorrelationId, LatexHeight>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split('|');
            if (fields.Length != 6 || fields[0] != "FJH1")
            {
                throw new CvMeasurementException(
                    $"Malformed LaTeX measurement result line: '{line}'.");
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < fields.Length; i++)
            {
                var equals = fields[i].IndexOf('=');
                if (equals <= 0 || !values.TryAdd(fields[i][..equals], fields[i][(equals + 1)..]))
                {
                    throw new CvMeasurementException(
                        $"Malformed LaTeX measurement result field in '{line}'.");
                }
            }

            RequireKeys(values, line, "corr", "rule", "kind", "sha256", "height-sp");
            var correlation = ParseCorrelation(values["corr"], line);
            if (!expected.TryGetValue(correlation, out var request))
            {
                throw new CvMeasurementException(
                    $"Unknown LaTeX measurement correlation '{values["corr"]}'.");
            }
            if (results.ContainsKey(correlation))
            {
                throw new CvMeasurementException(
                    $"Duplicate LaTeX measurement correlation '{values["corr"]}'.");
            }

            if (!int.TryParse(values["rule"], NumberStyles.None, CultureInfo.InvariantCulture, out var rule)
                || rule != request.CacheKey.RuleVersion
                || values["kind"] != request.CacheKey.Kind.ToString()
                || !IsSha256(values["sha256"])
                || values["sha256"] != request.CacheKey.ContentHash)
            {
                throw new CvMeasurementException(
                    $"LaTeX measurement metadata mismatch for correlation '{values["corr"]}'.");
            }

            if (!long.TryParse(values["height-sp"], NumberStyles.None, CultureInfo.InvariantCulture, out var height)
                || height < 0)
            {
                throw new CvMeasurementException(
                    $"Invalid LaTeX measurement height for correlation '{values["corr"]}'.");
            }

            results.Add(correlation, new LatexHeight(height));
        }

        if (results.Count != expected.Count)
        {
            var missing = expected.Keys.Where(id => !results.ContainsKey(id));
            throw new CvMeasurementException(
                $"Missing LaTeX measurement results for: {string.Join(", ", missing)}.");
        }

        return results;
    }

    private static MeasurementCorrelationId ParseCorrelation(string value, string line)
    {
        if (value.Length != 9
            || value[0] != 'M'
            || !int.TryParse(value.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || id <= 0)
        {
            throw new CvMeasurementException($"Malformed correlation token in '{line}'.");
        }
        return new(id);
    }

    private static void RequireKeys(
        IReadOnlyDictionary<string, string> values,
        string line,
        params string[] keys)
    {
        if (values.Count != keys.Length || keys.Any(key => !values.ContainsKey(key)))
        {
            throw new CvMeasurementException(
                $"Malformed LaTeX measurement metadata in '{line}'.");
        }
    }

    private static bool IsSha256(string value)
        => value.Length == 64
           && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
