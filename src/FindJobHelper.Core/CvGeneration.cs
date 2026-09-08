using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CliWrap;
using CodegenCS;
using CodegenCS.IO;
using FindJobHelper.Configuration;
using FindJobHelper.Core;
using FindJobHelper.Core.Helper;

namespace FindJobHelper.CVGeneration;

public record struct GenerateParams()
{
    public required string TemplatePath;
    public required string OutputDirectory;
    public required CvDataModel Model;
    public required CancellationToken CancellationToken;
    public CvPageCount PageCount;
    public CvPageLayout? PageLayout;
    public LatexExecutablePaths? LatexExecutables;
    public LatexExecutionOptions ExecutionOptions = LatexExecutionOptions.Empty;
    public LatexFontOptions FontOptions = LatexFontOptions.Default;
}

public sealed record GeneratedCvArtifacts(string PdfPath) : ICvRenderResult;

internal static class CvLatexErrors
{
    public const string MetadataLeftOverflowMarker = "FJH_METADATA_LEFT_OVERFLOW";
    public const string MetadataLeftOverflowMessage = CvMetadataOverflowException.ErrorMessage;
    public const string SectionPageOverflowMarker = "FJH_SECTION_PAGE_OVERFLOW";
    public const string EventPageOverflowMarker = "FJH_EVENT_PAGE_OVERFLOW";

    public static bool ContainsMetadataLeftOverflowMarker(string output)
        => output.Contains(MetadataLeftOverflowMarker, StringComparison.Ordinal);

    public static bool ContainsSectionPageOverflowMarker(string output)
        => output.Contains(SectionPageOverflowMarker, StringComparison.Ordinal);

    public static bool ContainsEventPageOverflowMarker(string output)
        => output.Contains(EventPageOverflowMarker, StringComparison.Ordinal);

    public static CvSectionPageOverflowException CreateSectionPageOverflowException(
        string output)
    {
        var match = Regex.Match(
            output,
            @"FJH_SECTION_PAGE_OVERFLOW:\s*([^\r\n.]+)",
            RegexOptions.CultureInvariant);
        var label = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        var sectionLabel = label.Length == 0 ? null : label;
        return new(sectionLabel);
    }

    public static CvEventPageOverflowException CreateEventPageOverflowException(
        string output,
        CvDataModel? model = null)
    {
        var match = Regex.Match(
            output,
            @"FJH_EVENT_PAGE_OVERFLOW:\s*([^/\r\n.]+)(?:\s*/\s*([^\r\n.]+))?",
            RegexOptions.CultureInvariant);
        var section = match.Success ? match.Groups[1].Value.Trim() : string.Empty;
        var @event = match.Success ? match.Groups[2].Value.Trim() : string.Empty;
        if (TryResolveEvents(model, section, out var events))
        {
            if (ShouldUseOnlyEvent(@event, events))
            {
                @event = events[0].Title.Value;
            }
            else if (TryParseEventNumber(@event, events.Length, out var eventNumber))
            {
                @event = events[eventNumber - 1].Title.Value;
            }
        }

        var sectionLabel = section.Length == 0 ? null : section;
        var eventLabel = @event.Length == 0 ? null : @event;
        return new(sectionLabel, eventLabel);

        static bool TryResolveEvents(
            CvDataModel? model,
            string section,
            out ImmutableArray<Event> events)
        {
            events = [];
            if (model is null)
            {
                return false;
            }
            if (!Enum.TryParse<Section>(
                    section,
                    ignoreCase: false,
                    out var parsedSection))
            {
                return false;
            }

            events = parsedSection switch
            {
                Section.WorkExperience => model.WorkExperiences,
                Section.Education => model.Educations,
                Section.PersonalProjects => model.PersonalProjects,
                _ => [],
            };
            return true;
        }

        static bool ShouldUseOnlyEvent(
            string eventToken,
            ImmutableArray<Event> events)
        {
            if (eventToken.Length != 0)
            {
                return false;
            }

            return events.Length == 1;
        }

        static bool TryParseEventNumber(
            string eventToken,
            int eventCount,
            out int eventNumber)
        {
            if (!int.TryParse(
                    eventToken,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out eventNumber))
            {
                return false;
            }

            return eventNumber > 0 && eventNumber <= eventCount;
        }
    }

}

internal static class LatexProcessEnvironment
{
    // TeX folds output at max_print_line. 999 is the documented effectively
    // unbounded value and remains portable across supported TeX distributions.
    public const string MaxPrintLine = "999";

    public static IEnumerable<string> LatexmkArguments =>
        OperatingSystem.IsWindows()
            ? ["-latexoption=--max-print-line=" + MaxPrintLine]
            : [];

    public static IEnumerable<string> XeLatexArguments =>
        OperatingSystem.IsWindows()
            ? ["--max-print-line=" + MaxPrintLine]
            : [];

    public static Command DisableOutputWrapping(this Command command) =>
        OperatingSystem.IsWindows()
            ? command
            : command.WithEnvironmentVariables(environment =>
                environment.Set("max_print_line", MaxPrintLine));
}

internal static partial class LatexLogPageCountParser
{
    [GeneratedRegex(
        @"Output written on main\.(?:pdf|xdv) \((\d+) pages?\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex PageCountRegex();

    public static bool TryParse(string latexLog, out int pageCount)
    {
        ArgumentNullException.ThrowIfNull(latexLog);
        var matches = PageCountRegex().Matches(latexLog);
        if (matches.Count == 0)
        {
            pageCount = 0;
            return false;
        }
        if (!int.TryParse(
                matches[^1].Groups[1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out pageCount))
        {
            pageCount = 0;
            return false;
        }
        if (pageCount > 0)
        {
            return true;
        }

        pageCount = 0;
        return false;
    }
}

internal sealed record LatexExplicitLayoutMarkers(
    IReadOnlyDictionary<int, int> BlockStartPages,
    IReadOnlyDictionary<int, int> BlockEndPages,
    int? FooterPage);

internal static partial class LatexExplicitLayoutMarkerParser
{
    [GeneratedRegex(
        @"FJH_LAYOUT_BLOCK_(START|END):(\d+):(\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex BlockMarkerRegex();

    [GeneratedRegex(
        @"FJH_LAYOUT_FOOTER:(\d+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex FooterMarkerRegex();

    public static LatexExplicitLayoutMarkers Parse(string latexLog)
    {
        ArgumentNullException.ThrowIfNull(latexLog);
        var starts = new Dictionary<int, int>();
        var ends = new Dictionary<int, int>();
        foreach (Match match in BlockMarkerRegex().Matches(latexLog))
        {
            if (!TryParsePositiveNumber(match.Groups[2].Value, out var blockNumber))
            {
                continue;
            }
            if (!TryParsePositiveNumber(match.Groups[3].Value, out var pageNumber))
            {
                continue;
            }

            var target = match.Groups[1].Value == "START" ? starts : ends;
            target[blockNumber] = pageNumber;
        }

        int? footerPage = null;
        var footerMatches = FooterMarkerRegex().Matches(latexLog);
        if (footerMatches.Count > 0)
        {
            var footerValue = footerMatches[^1].Groups[1].Value;
            if (TryParsePositiveNumber(footerValue, out var parsedFooterPage))
            {
                footerPage = parsedFooterPage;
            }
        }

        return new(starts, ends, footerPage);

        static bool TryParsePositiveNumber(string value, out int number)
        {
            if (!int.TryParse(
                    value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out number))
            {
                return false;
            }

            return number > 0;
        }
    }
}

public static class CvTemplate
{
    private const string LatexFileName = "main.tex";

    public const int ExpectedXeLatexPassCount = 2;
    public const int ExpectedPdfConversionPassCount = 1;

    public static async Task<ICvRenderResult> Generate(
        GenerateParams p,
        LatexProgressReporters progress)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(p.TemplatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(p.OutputDirectory);
        ArgumentNullException.ThrowIfNull(p.FontOptions);
        ArgumentNullException.ThrowIfNull(progress.Tex);
        ArgumentNullException.ThrowIfNull(progress.Pdf);
        var outputDirectory = new DirectoryInfo(p.OutputDirectory);
        outputDirectory.Create();

        var renderProgressPlan =
            GenerateSource(p, outputDirectory, progress.Tex);
        return await CompileAsync(
            p,
            outputDirectory,
            progress.Pdf,
            renderProgressPlan);
    }

    private static LatexRenderProgressPlan GenerateSource(
        GenerateParams p,
        DirectoryInfo outputDirectory,
        IProgressReporter progress)
    {
        var texWorkUnits = GetTexWorkUnitCount(p.Model, p.PageLayout);
        var completedTexWorkUnits = 0;
        progress.Report(new(
            CompletedWorkUnits: completedTexWorkUnits,
            TotalWorkUnits: texWorkUnits,
            Detail: "Creating TeX file"));

        var codegenContext = new CodegenContext();
        var writer = codegenContext[LatexFileName];
        writer.AutoTrimEnd = false;
        writer.CurlyBracesStyle = CodegenTextWriter.CurlyBracesStyleType.C;
        writer.PreserveNonWhitespaceIndentBehavior = CodegenTextWriter.PreserveNonWhitespaceIndentBehaviorType.PreserveAnything;

        var renderProgress = new LatexRenderProgressBuilder();
        var documentHeader =
            CvLatexFragmentRenderer.RenderDocumentHeader(p.Model);
        ReportTexProgress("Creating TeX file — document header");
        var sections = p.PageLayout is null
            ? RenderLegacySections(
                p.Model,
                ReportSection,
                renderProgress)
            : RenderExplicitLayout(
                p.Model,
                p.PageLayout,
                ReportSection,
                renderProgress);
        var documentFooter = CvLatexFragmentRenderer.RenderDocumentFooter(p.Model);
        FormattableString footerMarker = p.PageLayout is null
            ? FormattableStringFactory.Create(string.Empty)
            : documentFooter.Format.Length == 0
                ? FormattableStringFactory.Create(
                    @"\typeout{{FJH_LAYOUT_FOOTER:\number\cvexplicitlastunitpage}}")
                : FormattableStringFactory.Create(
                    @"\typeout{{FJH_LAYOUT_FOOTER:\number\value{{page}}}}");

        writer.Write($$$$"""
        \input{{{{{p.TemplatePath.Replace('\\', '/')}}}}}
        {{{{LatexFontConfigurationRenderer.Render(p.FontOptions)}}}}

        \begin{document}

        \pagestyle{fancy}

        {{{{documentHeader}}}}

        % Main Content

        {{{{sections.Render()}}}}

        {{{{documentFooter}}}}
        {{{{footerMarker}}}}

        \end{document}
        """);
        ReportTexProgress("Creating TeX file — document footer");

        codegenContext.SaveToFolder(outputDirectory.FullName);
        ReportTexProgress("Creating TeX file");
        return renderProgress.Build();

        void ReportSection()
        {
            ReportTexProgress("Creating TeX file — section");
        }

        void ReportTexProgress(string detail)
        {
            completedTexWorkUnits++;
            progress.Report(new(
                CompletedWorkUnits: completedTexWorkUnits,
                TotalWorkUnits: texWorkUnits,
                Detail: detail));
        }
    }

    private static async Task<ICvRenderResult> CompileAsync(
        GenerateParams p,
        DirectoryInfo outputDirectory,
        IProgressReporter progress,
        LatexRenderProgressPlan renderProgressPlan)
    {
        var latexmkProgress = new LatexmkProgressParser(
            progress,
            renderProgressPlan);
        var executables = p.LatexExecutables ?? LatexExecutablePaths.FromPath;
        var latexmk = Cli.Wrap(executables.Latexmk)
            .DisableOutputWrapping();
        latexmk = latexmk.WithArguments([
            .. LatexProcessEnvironment.LatexmkArguments,
            "-xelatex",
            "-latexoption=-halt-on-error",
            "-latexoption=-interaction=nonstopmode",
            "-latexoption=-file-line-error",
            LatexFileName,
        ]);
        var binaryDirectory = Path.GetDirectoryName(executables.Latexmk);
        if (!string.IsNullOrEmpty(binaryDirectory))
        {
            latexmk = latexmk.WithEnvironmentVariables(environment =>
                environment.Set(
                    "PATH",
                    string.Join(
                        Path.PathSeparator,
                        binaryDirectory,
                        Environment.GetEnvironmentVariable("PATH"))));
        }

        {
            var logFile = Path.Join(outputDirectory.FullName, "log-stdout.txt");
            latexmk = latexmk.WithStandardOutputPipe(PipeTarget.Merge(
                PipeTarget.ToFile(logFile),
                PipeTarget.ToDelegate(latexmkProgress.ParseLine)));
        }
        {
            var logFile = Path.Join(outputDirectory.FullName, "log-stderr.txt");
            latexmk = latexmk.WithStandardErrorPipe(PipeTarget.ToFile(logFile));
        }

        latexmk = latexmk.WithWorkingDirectory(outputDirectory.FullName);
        latexmk = latexmk.WithValidation(CommandResultValidation.None);

        CommandResult result;
        try
        {
            result = await latexmk.ExecuteAsync(p.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var incomplete = LatexFailureClassifier.ClassifyLaunchFailure(
                Path.GetFileName(executables.Latexmk),
                LatexExecutionPhase.FinalRendering,
                exception,
                outputDirectory.FullName,
                p.ExecutionOptions);
            return incomplete is null
                ? new LatexCompilationFailure(
                    LatexExecutionPhase.FinalRendering,
                    exception.Message,
                    outputDirectory.FullName,
                    null,
                    p.ExecutionOptions)
                : incomplete;
        }

        if (!result.IsSuccess)
        {
            var latexLogPath = Path.Join(outputDirectory.FullName, "main.log");
            var latexLog = File.Exists(latexLogPath)
                ? await File.ReadAllTextAsync(latexLogPath, p.CancellationToken)
                : string.Empty;
            var incomplete = LatexFailureClassifier.ClassifyLog(
                latexLog,
                LatexExecutionPhase.FinalRendering,
                outputDirectory.FullName,
                p.ExecutionOptions);
            if (incomplete is not null)
            {
                return incomplete;
            }
            if (CvLatexErrors.ContainsMetadataLeftOverflowMarker(latexLog))
            {
                return new MetadataOverflowFailure();
            }
            if (CvLatexErrors.ContainsSectionPageOverflowMarker(latexLog))
            {
                var exception = CvLatexErrors.CreateSectionPageOverflowException(latexLog);
                return new SectionOverflowFailure(exception.SectionLabel);
            }
            if (CvLatexErrors.ContainsEventPageOverflowMarker(latexLog))
            {
                var exception = CvLatexErrors.CreateEventPageOverflowException(latexLog, p.Model);
                return new EventOverflowFailure(
                    exception.SectionLabel,
                    exception.EventLabel);
            }

            return new LatexCompilationFailure(
                LatexExecutionPhase.FinalRendering,
                LatexFailureClassifier.FirstDiagnostic(latexLog, "LaTeX execution failed."),
                outputDirectory.FullName,
                result.ExitCode,
                p.ExecutionOptions);
        }

        var finalLatexLogPath = Path.Join(outputDirectory.FullName, "main.log");
        var finalLatexLog = File.Exists(finalLatexLogPath)
            ? await File.ReadAllTextAsync(finalLatexLogPath, p.CancellationToken)
            : null;
        if (p.PageLayout is { } explicitLayout)
        {
            try
            {
                VerifyExplicitRenderedLayout(explicitLayout, finalLatexLog);
            }
            catch (RenderedPageLayoutMismatchException exception)
            {
                return new PageLayoutMismatchFailure(exception.Details);
            }
        }
        else if (p.PageCount.ExactCount is { } requiredPageCount)
        {
            if (!TryParseRenderedPageCount(
                    finalLatexLog,
                    out var renderedPageCount))
            {
                var exception = new RenderedPageCountUnavailableException(requiredPageCount);
                return new PageCountUnavailableFailure(requiredPageCount);
            }
            if (renderedPageCount != requiredPageCount)
            {
                var exception = new RenderedPageCountMismatchException(
                    requiredPageCount,
                    renderedPageCount);
                return new PageCountMismatchFailure(
                    requiredPageCount,
                    renderedPageCount);
            }
        }

        var pdfOutputName = ReplaceExtension(LatexFileName, ".pdf");
        var pdfOutputPath = Path.Join(outputDirectory.FullName, pdfOutputName);
        if (!File.Exists(pdfOutputPath))
        {
            var exception = new CvPdfNotProducedException();
            return new MissingPdfFailure(pdfOutputPath);
        }

        latexmkProgress.CompleteConversionAndValidation();
        return new GeneratedCvArtifacts(pdfOutputPath);

        static bool TryParseRenderedPageCount(
            string? latexLog,
            out int pageCount)
        {
            pageCount = default;
            if (latexLog is null)
            {
                return false;
            }

            return LatexLogPageCountParser.TryParse(latexLog, out pageCount);
        }
    }

    internal static int GetTexWorkUnitCount(
        CvDataModel model,
        CvPageLayout? layout)
    {
        ArgumentNullException.ThrowIfNull(model);

        var sectionOccurrences = layout is null
            ? model.SectionOrder.Length
            : layout.Blocks.Sum(static block => block.Sections.Length);
        return checked(sectionOccurrences + 3);
    }

    internal static int GetPdfWorkUnitCount(int bulletCount)
    {
        if (bulletCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bulletCount),
                bulletCount,
                "The rendered bullet count cannot be negative.");
        }

        return checked(
            ExpectedXeLatexPassCount * (bulletCount + 1)
            + ExpectedPdfConversionPassCount);
    }

    private static IEnumerable<FormattableString> RenderLegacySections(
        CvDataModel model,
        Action sectionRendered,
        LatexRenderProgressBuilder progress)
    {
        foreach (var section in model.SectionOrder)
        {
            var rendered = model.DispatchSection(
                section,
                renderLanguages: Languages,
                renderEvents: events => Events(
                    section,
                    events,
                    progress));
            sectionRendered();
            yield return rendered;
        }
    }

    private static IEnumerable<FormattableString> RenderExplicitLayout(
        CvDataModel model,
        CvPageLayout layout,
        Action sectionRendered,
        LatexRenderProgressBuilder progress)
    {
        for (var blockIndex = 0; blockIndex < layout.Blocks.Length; blockIndex++)
        {
            if (blockIndex > 0)
            {
                yield return FormattableStringFactory.Create(
                    "\\newpage\n\\cvexplicitnextunitfresh");
            }

            var blockNumber = blockIndex + 1;
            yield return $@"\typeout{{FJH_LAYOUT_BLOCK_START:{blockNumber}:\number\value{{page}}}}";
            foreach (var section in layout.Blocks[blockIndex].Sections)
            {
                var rendered =
                    CvLatexFragmentRenderer.RenderExplicitSection(
                        section,
                        model,
                        progress);
                sectionRendered();
                yield return rendered;
            }
            yield return $@"\typeout{{FJH_LAYOUT_BLOCK_END:{blockNumber}:\number\cvexplicitlastunitpage}}";
        }
    }

    internal static void VerifyExplicitRenderedLayout(
        CvPageLayout layout,
        string? latexLog)
    {
        if (latexLog is null)
        {
            throw new RenderedPageLayoutMismatchException(
                "the final LaTeX log is missing.");
        }
        if (!LatexLogPageCountParser.TryParse(latexLog, out var renderedPageCount))
        {
            throw new RenderedPageLayoutMismatchException(
                "the final LaTeX log does not contain a rendered page count.");
        }
        if (renderedPageCount != layout.PageCount)
        {
            throw new RenderedPageLayoutMismatchException(
                $"the layout declares {layout.PageCount} page(s), but the PDF contains {renderedPageCount} page(s).");
        }

        var markers = LatexExplicitLayoutMarkerParser.Parse(latexLog);
        for (var blockIndex = 0; blockIndex < layout.Blocks.Length; blockIndex++)
        {
            var blockNumber = blockIndex + 1;
            var block = layout.Blocks[blockIndex];
            if (!markers.BlockStartPages.TryGetValue(blockNumber, out var startPage))
            {
                throw new RenderedPageLayoutMismatchException(
                    $"the start marker for block {blockNumber} ({block.ConfiguredPages}) is missing.");
            }
            if (startPage != block.FirstPage)
            {
                throw new RenderedPageLayoutMismatchException(
                    $"block {blockNumber} starts on physical page {startPage}, not declared page {block.FirstPage}.");
            }
            if (!markers.BlockEndPages.TryGetValue(blockNumber, out var endPage))
            {
                throw new RenderedPageLayoutMismatchException(
                    $"the end marker for block {blockNumber} ({block.ConfiguredPages}) is missing.");
            }
            if (endPage != block.LastPage)
            {
                throw new RenderedPageLayoutMismatchException(
                    $"block {blockNumber} ends on physical page {endPage}, not declared page {block.LastPage}.");
            }
        }

        if (markers.FooterPage is not { } footerPage)
        {
            throw new RenderedPageLayoutMismatchException(
                "the final footer-page marker is missing.");
        }
        if (footerPage != layout.PageCount)
        {
            throw new RenderedPageLayoutMismatchException(
                $"the footer is on physical page {footerPage}, not final declared page {layout.PageCount}.");
        }
    }

    private static FormattableString Languages(
        ImmutableArray<LanguageProficiencyInfo> languages)
    {
        var inner = CvLatexFragmentRenderer.RenderLanguagesSectionInner(languages);
        var wrapped = CvLatexFragmentRenderer.RenderProductionSection(Section.Languages, inner);
        return $"{wrapped}";
    }

    private static string ReplaceExtension(
        string filePath,
        string newExtension)
    {
        int extensionStart = filePath.LastIndexOf('.');
        if (extensionStart <= 0)
        {
            return $"{filePath}{newExtension}";
        }

        var s = filePath.AsSpan()[..extensionStart];
        return $"{s}{newExtension}";
    }

    private static FormattableString Events(
        Section section,
        ImmutableArray<Event> events,
        LatexRenderProgressBuilder progress)
    {
        var inner = CvLatexFragmentRenderer.RenderEventsSectionInner(
            events,
            section.ToDisplayString(),
            progress);
        var wrapped = CvLatexFragmentRenderer.RenderProductionSection(section, inner);
        return $"{wrapped}";
    }

}

public sealed class CvDataModel
{
    public required Name Name;
    public required Profession Profession;
    public NullableLocation Location = NullableLocation.Null;
    public required ImmutableArray<CategorizedInfoList> CategorizedInfoLists;
    public required ImmutableArray<CategorizedInfo> CategorizedInfos;
    public IRichTextNode? Summary;
    public ImmutableArray<LanguageProficiencyInfo> Languages = ImmutableArray<LanguageProficiencyInfo>.Empty;
    public ImmutableArray<Event> WorkExperiences = ImmutableArray<Event>.Empty;
    public ImmutableArray<Event> PersonalProjects = ImmutableArray<Event>.Empty;
    public ImmutableArray<Event> Educations = ImmutableArray<Event>.Empty;
    public ImmutableArray<Section> SectionOrder = [
        Section.Languages,
        Section.WorkExperience,
        Section.Education,
        Section.PersonalProjects,
    ];
    public NullableRegularString Website;
    public NullableRegularString GitHub;
}

internal static class CvDataModelExtensions
{
    internal static TResult DispatchSection<TResult>(
        this CvDataModel model,
        Section section,
        Func<ImmutableArray<LanguageProficiencyInfo>, TResult> renderLanguages,
        Func<ImmutableArray<Event>, TResult> renderEvents)
    {
        return section switch
        {
            Section.Languages => renderLanguages(model.Languages),
            Section.WorkExperience => renderEvents(model.WorkExperiences),
            Section.Education => renderEvents(model.Educations),
            Section.PersonalProjects => renderEvents(model.PersonalProjects),
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };
    }

    internal static string ToDisplayString(this Section section)
    {
        return section switch
        {
            Section.Languages => "Languages",
            Section.WorkExperience => "Work Experience",
            Section.Education => "Education",
            Section.PersonalProjects => "Personal Projects",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
        };
    }
}

public record struct Event()
{
    public required RegularString Title;
    public required Place Place;
    public required DateRange DateRange;
    public SelectionDebugInfo DebugInfo = new();
    public IRichTextNode? Text;
    public ImmutableArray<SubEvent> SubItems = [];
    public ImmutableArray<RegularString> Urls = [];
}

public sealed class SelectionDebugInfo
{
    private float _rawScore;

    public float Score { get; set; }

    public float RawScore
    {
        get
        {
            if (_rawScore != 0)
            {
                return _rawScore;
            }

            if (Score == 0)
            {
                return _rawScore;
            }

            if (!RequirementCoverage.IsEmpty)
            {
                return _rawScore;
            }

            if (!TagMatches.IsEmpty)
            {
                return _rawScore;
            }

            return Score;
        }
        set => _rawScore = value;
    }

    public ImmutableArray<DebugTagScore> TagScores { get; set; } = [];

    public ImmutableArray<DebugRequirementCoverage> RequirementCoverage { get; set; } = [];

    public ImmutableArray<DebugTagMatch> TagMatches { get; set; } = [];

    public MmrScoreBreakdown? MmrScoreBreakdown { get; set; }
}

public readonly record struct DebugTagScore(RegularString Tag, float Score);

public sealed record DebugRequirementCoverage(
    RequiredTagGroup Requirement,
    float Score);

public sealed record DebugTagMatchOrigin(
    RequiredTagGroup Requirement,
    float Contribution,
    bool IsDirect = false);

public sealed record DebugTagMatch(
    Tag TargetTag,
    float BaseContribution,
    float DirectContribution,
    float DirectMatchBonus,
    float RelevanceContribution,
    ImmutableArray<DebugTagMatchOrigin> Origins);

public readonly record struct SubEvent
{
    public SubEvent(
        IRichTextNode text,
        SelectionDebugInfo debugInfo)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(debugInfo);
        Text = text;
        DebugInfo = debugInfo;
    }

    public IRichTextNode Text { get; }
    public SelectionDebugInfo DebugInfo { get; }
}
public readonly record struct Place(RegularString Name)
{
    public static Place Personal => new("Personal");
    public bool IsPersonal => Name == "Personal";
}
// public readonly record struct JobPosition(string Title);

public readonly record struct OptionalDateParts
{
    public readonly int Year { get; }
    public readonly int Month { get; }
    public readonly int Day { get; }

    [JsonConstructor]
    public OptionalDateParts(int Year, int Month = 0, int Day = 0)
    {
        Debug.Assert(Month is >= 0 and <= 12);
        Debug.Assert(Day is >= 0 and <= 31);
        if (Year == 0)
        {
            Debug.Assert(Month == 0);
            Debug.Assert(Day == 0);
        }
        if (Month == 0)
        {
            Debug.Assert(Day == 0);
        }

        this.Year = Year;
        this.Month = Month;
        this.Day = Day;
    }

    [JsonIgnore]
    public static OptionalDateParts Unspecified => default;
    [JsonIgnore]
    public bool IsUnspecified => Year == 0;
}

public readonly record struct DateRange(
    OptionalDateParts Start,
    OptionalDateParts End) : ISpanFormattable
{
    public static DateRange Ongoing(OptionalDateParts start)
    {
        Debug.Assert(!start.IsUnspecified);
        return new(start, OptionalDateParts.Unspecified);
    }
    public static DateRange Completed(OptionalDateParts start, OptionalDateParts end)
    {
        Debug.Assert(!start.IsUnspecified);
        Debug.Assert(!end.IsUnspecified);
        return new DateRange(start, end);
    }

    public bool IsCurrent => End.IsUnspecified;

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = format;
        _ = formatProvider;
        return ToString();
    }
    public override string ToString() => $"{this}";

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        _ = format;

        charsWritten = 0;
        var helper = new WriteHelper(destination, ref charsWritten);
        if (!AppendDate(helper, Start, provider))
        {
            return false;
        }
        helper.Append(" - ");
        if (End.IsUnspecified)
        {
            if (!helper.Append("current"))
            {
                return false;
            }
        }
        else
        {
            if (!AppendDate(helper, End, provider))
            {
                return false;
            }
        }
        return true;

        static bool AppendDate(
            WriteHelper helper,
            OptionalDateParts d,
            IFormatProvider? provider)
        {
            // pad with zeros
            const string formatPadLeft = "00";

            if (d.Day != 0)
            {
                if (!helper.Append(d.Day, format: formatPadLeft, provider))
                {
                    return false;
                }
                if (!helper.Append('.'))
                {
                    return false;
                }
            }
            if (d.Month != 0)
            {
                if (!helper.Append(d.Month, format: formatPadLeft, provider))
                {
                    return false;
                }
                if (!helper.Append('.'))
                {
                    return false;
                }
            }
            Debug.Assert(d.Year != 0);
            if (!helper.Append(d.Year, format: null, provider))
            {
                return false;
            }
            return true;
        }
    }
}

public sealed class DateRangeComparer : IComparer<DateRange>
{
    private readonly Func<DateRange, OptionalDateParts> _selector;

    private DateRangeComparer(Func<DateRange, OptionalDateParts> selector)
    {
        _selector = selector;
    }

    public static DateRangeComparer ByStart { get; } = new(dr => dr.Start);
    public static DateRangeComparer ByEnd { get; } = new(dr => dr.End);

    public int Compare(DateRange x, DateRange y)
    {
        return CompareDates(_selector(x), _selector(y));
    }

    private static int CompareDates(OptionalDateParts a, OptionalDateParts b)
    {
        // Unspecified dates are considered "greater than" specified dates
        // (they sort to the end, like ongoing/current dates)
        if (TryCompareUnspecified(a, b, out var unspecifiedComparison))
        {
            return unspecifiedComparison;
        }

        // Compare years
        int yearComparison = a.Year.CompareTo(b.Year);
        if (yearComparison != 0)
        {
            return yearComparison;
        }

        // Compare months (0 means unspecified, treat as less precise but equal within year)
        if (TryCompareMissingPart(a.Month, b.Month, out var monthMissingComparison))
        {
            return monthMissingComparison;
        }

        int monthComparison = a.Month.CompareTo(b.Month);
        if (monthComparison != 0)
        {
            return monthComparison;
        }

        // Compare days (0 means unspecified)
        if (TryCompareMissingPart(a.Day, b.Day, out var dayMissingComparison))
        {
            return dayMissingComparison;
        }

        return a.Day.CompareTo(b.Day);

        static bool TryCompareUnspecified(
            OptionalDateParts left,
            OptionalDateParts right,
            out int comparison)
        {
            comparison = default;
            if (left.IsUnspecified)
            {
                comparison = right.IsUnspecified ? 0 : 1;
                return true;
            }
            if (right.IsUnspecified)
            {
                comparison = -1;
                return true;
            }

            return false;
        }

        static bool TryCompareMissingPart(
            int left,
            int right,
            out int comparison)
        {
            comparison = default;
            if (left == 0)
            {
                comparison = right == 0 ? 0 : -1;
                return true;
            }
            if (right == 0)
            {
                comparison = 1;
                return true;
            }

            return false;
        }
    }
}

public readonly record struct Language(RegularString Name, RegularString ShortName)
{
    public static Language English => new("English", "EN");
    public static Language Romanian => new("Romanian", "RO");
    public static Language Russian => new("Russian", "RU");
}
public readonly record struct LanguageProficiencyLevel(RegularString Value)
{
    public static LanguageProficiencyLevel A1 => new("A1");
    public static LanguageProficiencyLevel A2 => new("A2");
    public static LanguageProficiencyLevel B1 => new("B1");
    public static LanguageProficiencyLevel B2 => new("B2");
    public static LanguageProficiencyLevel C1 => new("C1");
    public static LanguageProficiencyLevel C2 => new("C2");
    public static LanguageProficiencyLevel Native => new("Native");
}
// public readonly record struct LanguageClassificationCategory(string Category);
public readonly record struct LanguageProficiencyInfo(
    Language Language,
    LanguageProficiencyLevel GeneralProficiencyLevel,
    ImmutableArray<LanguageSkill> Skills = default)
{
    public readonly ImmutableArray<LanguageSkill> Skills = Skills == default ? [] : Skills;
}

public readonly record struct LanguageSkill(RegularString Text)
{
}

public readonly record struct RegularString(string Value)
{
    public override string ToString() => Value;

    public static implicit operator RegularString(string s)
    {
        return new RegularString(s);
    }
}

public readonly record struct NullableRegularString
{
    public readonly string? Value;

    public NullableRegularString(string? value)
    {
        Value = value;
    }

    public NullableRegularString(RegularString s) : this(s.Value)
    {
    }

    public static NullableRegularString Null => default;
    public bool IsNull => Value is null;
    public RegularString ToInfoString()
    {
        Debug.Assert(Value is not null);
        return new RegularString(Value);
    }

    public static implicit operator NullableRegularString(RegularString s)
    {
        return new NullableRegularString(s);
    }

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator NullableRegularString(string? s)
    {
        return new NullableRegularString(s);
    }
}

public readonly record struct CategorizedInfo(
    Category Category,
    RegularString Value);

public readonly record struct CategorizedInfoList(
    Category Category,
    ImmutableArray<RegularString> Values);

public readonly record struct Category(string DisplayName, bool IsUrl = false)
{
    public static Category Unspecified = new("");
    public static Category Website => new("Website", IsUrl: true);
    public static Category GitHub => new("GitHub", IsUrl: true);
    public static Category LinkedIn => new("LinkedIn", IsUrl: true);
    public static Category YouTube => new("YouTube", IsUrl: true);
    public static Category Portfolio => new("Portfolio", IsUrl: true);
    public static Category Email => new("Email");
    public static Category Location => new("Location");
    public static Category Phone => new("Phone");
    public static Category Skills => new("Skills");
    public static Category Technologies => new("Technologies");
}

public readonly record struct Name(
    RegularString First,
    RegularString Last)
{
}

public readonly record struct Profession(RegularString Value);

public readonly record struct Location(
    string City,
    string Country)
{
    public static implicit operator NullableLocation(Location location)
    {
        return new NullableLocation(location.City, location.Country);
    }

    public RegularString FormatInfo()
    {
        return new($"{City}, {Country}");
    }
}

public readonly record struct NullableLocation(
    NullableRegularString City,
    NullableRegularString Country)
{
    public static NullableLocation Null => default;
    public readonly bool IsNull
    {
        get
        {
            Debug.Assert(Country.IsNull == City.IsNull);
            return Country.IsNull;
        }
    }
}
