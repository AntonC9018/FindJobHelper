using System.Globalization;
using System.Text;
using CliWrap;
using CliWrap.Buffered;

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
        CancellationToken cancellationToken);
}

internal sealed class XeLatexMeasurementRunner : ILatexMeasurementRunner
{
    public async Task<IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight>> MeasureAsync(
        string templatePath,
        IReadOnlyList<LatexMeasurementRequest> requests,
        CancellationToken cancellationToken)
    {
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

            var result = await Cli.Wrap("xelatex")
                .WithArguments([
                    "-interaction=nonstopmode",
                    "-halt-on-error",
                    "-file-line-error",
                    texFileName,
                ])
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cancellationToken);
            if (!result.IsSuccess)
            {
                if (CvLatexErrors.ContainsMetadataLeftOverflowMarker(
                        $"{result.StandardError}{Environment.NewLine}{result.StandardOutput}"))
                {
                    throw new CvMetadataOverflowException();
                }

                throw new CvMeasurementException(
                    $"XeLaTeX height measurement failed with exit code {result.ExitCode}: {result.StandardError}{Environment.NewLine}{result.StandardOutput}");
            }

            var resultPath = Path.Combine(workingDirectory, resultFileName);
            if (!File.Exists(resultPath))
            {
                throw new CvMeasurementException(
                    "XeLaTeX completed without producing the height result file.");
            }

            var lines = await File.ReadAllLinesAsync(resultPath, cancellationToken);
            return LatexMeasurementResultParser.ParseAndValidate(lines, requests);
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
            if (request.Mode == LatexMeasurementMode.DocumentHeader)
            {
                source.AppendLine($$"""
                    \clearpage
                    \begingroup
                    \cvsetmeasurementsectionbox{\cvmeasurementsentinelsection}
                    \dimen2=\dimexpr\ht\cvmeasurementbox+\dp\cvmeasurementbox\relax
                    \fjhmeasurementpagetotal=\pagetotal
                    {{request.RenderedFragment}}
                    \begin{flowblock}{Measurement Sentinel}
                    \cvmeasurementsentinelsection
                    \end{flowblock}
                    \par
                    % The fresh-page header/boxed-section boundary suppresses
                    % 2.5pt that exists when the section is boxed in isolation.
                    \dimen0=\dimexpr\pagetotal-\fjhmeasurementpagetotal-\dimen2-2.5pt\relax
                    \setbox\cvmeasurementbox=\vbox to \dimen0{\vfil}
                    \immediate\write\fjhmeasurementresults{FJH1|corr={{request.CorrelationId}}|rule={{request.CacheKey.RuleVersion.ToString(CultureInfo.InvariantCulture)}}|kind={{request.CacheKey.Kind}}|sha256={{request.CacheKey.ContentHash}}|height-sp=\number\dimexpr\ht\cvmeasurementbox+\dp\cvmeasurementbox\relax}
                    \endgroup
                    \clearpage
                    """);
                continue;
            }

            if (request.Mode == LatexMeasurementMode.PageStart)
            {
                source.AppendLine($$"""
                    \clearpage
                    \begingroup
                    \fjhmeasurementpagetotal=\pagetotal
                    {{request.RenderedFragment}}
                    \par
                    \dimen0=\dimexpr\pagetotal-\fjhmeasurementpagetotal\relax
                    \setbox\cvmeasurementbox=\vbox to \dimen0{\vfil}
                    \immediate\write\fjhmeasurementresults{FJH1|corr={{request.CorrelationId}}|rule={{request.CacheKey.RuleVersion.ToString(CultureInfo.InvariantCulture)}}|kind={{request.CacheKey.Kind}}|sha256={{request.CacheKey.ContentHash}}|height-sp=\number\dimexpr\ht\cvmeasurementbox+\dp\cvmeasurementbox\relax}
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
