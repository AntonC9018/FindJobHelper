using System.Globalization;
using System.Text;
using CliWrap;

namespace FindJobHelper.CVGeneration;

internal sealed record LatexMeasurementRequest(
    MeasurementCorrelationId CorrelationId,
    LatexMeasurementCacheKey CacheKey,
    string RenderedFragment,
    LatexMeasurementMode Mode);

internal interface ILatexMeasurementRunResult;

internal sealed record SuccessfulLatexMeasurementRun(
    IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight> Measurements) : ILatexMeasurementRunResult;

internal interface ILatexMeasurementRunner
{
    Task<ILatexMeasurementRunResult> MeasureAsync(
        string templatePath,
        IReadOnlyList<LatexMeasurementRequest> requests,
        IProgressReporter progress,
        LatexFontOptions fontOptions,
        LatexExecutionOptions options,
        CancellationToken cancellationToken);
}

internal sealed class XeLatexMeasurementRunner : ILatexMeasurementRunner
{
    private readonly LatexExecutablePaths _executables;
    private readonly Func<string> _workingDirectoryFactory;

    internal XeLatexMeasurementRunner(
        LatexExecutablePaths executables,
        Func<string> workingDirectoryFactory)
    {
        ArgumentNullException.ThrowIfNull(executables);
        ArgumentNullException.ThrowIfNull(workingDirectoryFactory);
        _executables = executables;
        _workingDirectoryFactory = workingDirectoryFactory;
    }

    public async Task<ILatexMeasurementRunResult> MeasureAsync(
        string templatePath,
        IReadOnlyList<LatexMeasurementRequest> requests,
        IProgressReporter progress,
        LatexFontOptions fontOptions,
        LatexExecutionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(fontOptions);
        ArgumentNullException.ThrowIfNull(options);
        progress.Report(new(
            CompletedWorkUnits: 0,
            TotalWorkUnits: requests.Count,
            Detail: "Computing heights — running XeLaTeX measurements"));
        if (requests.Count == 0)
        {
            return new SuccessfulLatexMeasurementRun(
                new Dictionary<MeasurementCorrelationId, LatexHeight>());
        }

        var workingDirectory = _workingDirectoryFactory();
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        Directory.CreateDirectory(workingDirectory);
        var retainWorkingDirectory = false;
        const string texFileName = "measurement.tex";
        const string resultFileName = "measurement-results.txt";
        try
        {
            var source = LatexMeasurementDocument.Generate(templatePath, resultFileName, requests, fontOptions);
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
            CommandResult result;
            try
            {
                result = await Cli.Wrap(_executables.XeLatex)
                    .DisableOutputWrapping()
                    .WithArguments([
                        .. LatexProcessEnvironment.XeLatexArguments,
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
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                var incomplete = LatexFailureClassifier.ClassifyLaunchFailure(
                    Path.GetFileName(_executables.XeLatex),
                    LatexExecutionPhase.HeightMeasurement,
                    exception,
                    workingDirectory,
                    options);
                if (incomplete is not null)
                {
                    return Retain(incomplete);
                }

                var failure = new LatexCompilationFailure(
                    LatexExecutionPhase.HeightMeasurement,
                    exception.Message,
                    workingDirectory,
                    null,
                    options);
                return Retain(failure);
            }
            if (!result.IsSuccess)
            {
                var output = $"{standardError}{Environment.NewLine}{standardOutput}";
                var latexLogPath = Path.Combine(workingDirectory, "measurement.log");
                var latexLog = File.Exists(latexLogPath)
                    ? await File.ReadAllTextAsync(latexLogPath, cancellationToken)
                    : output;
                var incomplete = LatexFailureClassifier.ClassifyLog(
                    latexLog,
                    LatexExecutionPhase.HeightMeasurement,
                    workingDirectory,
                    options);
                if (incomplete is not null)
                {
                    return Retain(incomplete);
                }
                if (CvLatexErrors.ContainsMetadataLeftOverflowMarker(latexLog))
                {
                    return Retain(new MetadataOverflowFailure());
                }
                return Retain(new LatexCompilationFailure(
                    LatexExecutionPhase.HeightMeasurement,
                    LatexFailureClassifier.FirstDiagnostic(
                        latexLog,
                        $"XeLaTeX exited with code {result.ExitCode}."),
                    workingDirectory,
                    result.ExitCode,
                    options));
            }

            var resultPath = Path.Combine(workingDirectory, resultFileName);
            if (!File.Exists(resultPath))
            {
                return Retain(new MeasurementDataFailure(
                    "XeLaTeX completed without producing the height result file.",
                    workingDirectory));
            }

            var lines = await File.ReadAllLinesAsync(resultPath, cancellationToken);
            IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight> parsed;
            try
            {
                parsed = LatexMeasurementResultParser.ParseAndValidate(lines, requests);
            }
            catch (CvMeasurementException exception)
            {
                return Retain(new MeasurementDataFailure(exception.Message, workingDirectory));
            }
            progressParser.CompleteMissingMeasurements();
            return new SuccessfulLatexMeasurementRun(parsed);
        }
        finally
        {
            try
            {
                if (!retainWorkingDirectory)
                {
                    Directory.Delete(workingDirectory, recursive: true);
                }
            }
            catch (DirectoryNotFoundException)
            {
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        ILatexMeasurementRunResult Retain(ILatexMeasurementRunResult failure)
        {
            retainWorkingDirectory = true;
            return failure;
        }
    }
}

internal sealed class XeLatexMeasurementRunnerBuilder
{
    private LatexExecutablePaths _executables = LatexExecutablePaths.FromPath;
    private Func<string> _workingDirectoryFactory = static () => Path.Combine(
        Path.GetTempPath(),
        $"FindJobHelper-measurement-{Guid.NewGuid():N}");

    public XeLatexMeasurementRunnerBuilder WithExecutables(LatexExecutablePaths executables)
    {
        ArgumentNullException.ThrowIfNull(executables);
        _executables = executables;
        return this;
    }

    internal XeLatexMeasurementRunnerBuilder WithWorkingDirectoryFactory(
        Func<string> workingDirectoryFactory)
    {
        ArgumentNullException.ThrowIfNull(workingDirectoryFactory);
        _workingDirectoryFactory = workingDirectoryFactory;
        return this;
    }

    public XeLatexMeasurementRunner Build() => new(
        _executables,
        _workingDirectoryFactory);
}

internal sealed class LatexMeasurementCompletionParser
{
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

        if (!TryParseCompletedMeasurementMarker(line, out var marker))
        {
            return;
        }

        var correlationId = new MeasurementCorrelationId(marker.Id.Value);
        Complete(correlationId);

        static bool TryParseCompletedMeasurementMarker(
            string line,
            out LatexProgressMarker marker)
        {
            if (!LatexProgressMarkerProtocol.TryParse(line, out marker))
            {
                return false;
            }
            if (marker.Event != LatexProgressMarkerEvent.Completed)
            {
                return false;
            }

            return marker.Id.Category == LatexProgressMarkerCategory.Measurement;
        }
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
            if (!_expected.Contains(correlationId))
            {
                return;
            }
            if (!_completed.Add(correlationId))
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
        IReadOnlyList<LatexMeasurementRequest> requests,
        LatexFontOptions fontOptions)
    {
        ArgumentNullException.ThrowIfNull(fontOptions);
        var normalizedTemplatePath = templatePath.Replace('\\', '/');
        var source = new StringBuilder();
        source.Append("\\input{").Append(normalizedTemplatePath).AppendLine("}");
        source.AppendLine(LatexFontConfigurationRenderer.Render(fontOptions));
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
                    {{LatexProgressMarkerProtocol.RenderTypeout(
                        LatexProgressMarkerEvent.Completed,
                        request.CorrelationId.ProgressMarkerId)}}
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
                {{LatexProgressMarkerProtocol.RenderTypeout(
                    LatexProgressMarkerEvent.Completed,
                    request.CorrelationId.ProgressMarkerId)}}
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
            if (fields.Length != 6)
            {
                throw new CvMeasurementException(
                    $"Malformed LaTeX measurement result line: '{line}'.");
            }
            if (fields[0] != "FJH1")
            {
                throw new CvMeasurementException(
                    $"Malformed LaTeX measurement result line: '{line}'.");
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < fields.Length; i++)
            {
                var equals = fields[i].IndexOf('=');
                if (equals <= 0)
                {
                    throw new CvMeasurementException(
                        $"Malformed LaTeX measurement result field in '{line}'.");
                }
                var key = fields[i][..equals];
                var fieldValue = fields[i][(equals + 1)..];
                if (!values.TryAdd(key, fieldValue))
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

            ValidateMetadata(values, request);
            var height = ParseHeight(values);
            var latexHeight = new LatexHeight(height);
            results.Add(correlation, latexHeight);
        }

        if (results.Count != expected.Count)
        {
            var missing = expected.Keys.Where(id => !results.ContainsKey(id));
            var missingText = string.Join(", ", missing);
            throw new CvMeasurementException(
                $"Missing LaTeX measurement results for: {missingText}.");
        }

        return results;
    }

    private static MeasurementCorrelationId ParseCorrelation(string value, string line)
    {
        if (!LatexProgressMarkerId.TryParse(
                value.AsSpan(),
                out var markerId))
        {
            throw new CvMeasurementException($"Malformed correlation token in '{line}'.");
        }
        if (markerId.Category != LatexProgressMarkerCategory.Measurement)
        {
            throw new CvMeasurementException($"Malformed correlation token in '{line}'.");
        }
        return new(markerId.Value);
    }

    private static void RequireKeys(
        IReadOnlyDictionary<string, string> values,
        string line,
        params string[] keys)
    {
        if (values.Count != keys.Length)
        {
            throw new CvMeasurementException(
                $"Malformed LaTeX measurement metadata in '{line}'.");
        }
        foreach (var key in keys)
        {
            if (!values.ContainsKey(key))
            {
                throw new CvMeasurementException(
                    $"Malformed LaTeX measurement metadata in '{line}'.");
            }
        }
    }

    private static bool IsSha256(string value)
    {
        if (value.Length != 64)
        {
            return false;
        }
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateMetadata(
        IReadOnlyDictionary<string, string> values,
        LatexMeasurementRequest request)
    {
        var correlation = values["corr"];
        if (!int.TryParse(
                values["rule"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var rule))
        {
            throw new CvMeasurementException(
                $"LaTeX measurement metadata mismatch for correlation '{correlation}'.");
        }
        if (rule != request.CacheKey.RuleVersion)
        {
            throw new CvMeasurementException(
                $"LaTeX measurement metadata mismatch for correlation '{correlation}'.");
        }

        var expectedKind = request.CacheKey.Kind.ToString();
        if (values["kind"] != expectedKind)
        {
            throw new CvMeasurementException(
                $"LaTeX measurement metadata mismatch for correlation '{correlation}'.");
        }

        var hash = values["sha256"];
        if (!IsSha256(hash))
        {
            throw new CvMeasurementException(
                $"LaTeX measurement metadata mismatch for correlation '{correlation}'.");
        }
        if (hash != request.CacheKey.ContentHash)
        {
            throw new CvMeasurementException(
                $"LaTeX measurement metadata mismatch for correlation '{correlation}'.");
        }
    }

    private static long ParseHeight(IReadOnlyDictionary<string, string> values)
    {
        var correlation = values["corr"];
        if (!long.TryParse(
                values["height-sp"],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var height))
        {
            throw new CvMeasurementException(
                $"Invalid LaTeX measurement height for correlation '{correlation}'.");
        }
        if (height < 0)
        {
            throw new CvMeasurementException(
                $"Invalid LaTeX measurement height for correlation '{correlation}'.");
        }

        return height;
    }
}
