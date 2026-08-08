using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace FindJobHelper.CVGeneration;

public enum LatexExecutionPhase
{
    HeightMeasurement,
    FinalRendering,
}

public sealed record ExecutableLatexRequirement(string Name);

public sealed record TexFileLatexRequirement(string FileName);

public sealed record FontLatexRequirement(string FamilyName);

public sealed record BabelLanguageLatexRequirement(string LanguageName);

public union LatexRequirement(
    ExecutableLatexRequirement,
    TexFileLatexRequirement,
    FontLatexRequirement,
    BabelLanguageLatexRequirement);

public sealed record LatexExecutionOptions
{
    public static LatexExecutionOptions Empty { get; } = new([]);

    public LatexExecutionOptions(
        IEnumerable<LatexRequirement> requirements,
        string? setupCommandHint = null)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        Requirements = requirements.ToImmutableArray();
        if (Requirements.Any(static requirement => requirement.Value is null))
        {
            throw new ArgumentException(
                "LaTeX requirements cannot contain an empty union.",
                nameof(requirements));
        }

        SetupCommandHint = string.IsNullOrWhiteSpace(setupCommandHint)
            ? null
            : setupCommandHint;
    }

    public ImmutableArray<LatexRequirement> Requirements { get; }

    public string? SetupCommandHint { get; }
}

public sealed record IncompleteLatexInstallation
{
    public IncompleteLatexInstallation(
        LatexRequirement missingRequirement,
        LatexExecutionPhase phase,
        string diagnostic,
        string diagnosticDirectory,
        string? setupCommandHint)
    {
        if (missingRequirement.Value is null)
        {
            throw new ArgumentException(
                "The missing requirement union must have a value.",
                nameof(missingRequirement));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(diagnostic);
        ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticDirectory);
        MissingRequirement = missingRequirement;
        Phase = phase;
        Diagnostic = diagnostic;
        DiagnosticDirectory = diagnosticDirectory;
        SetupCommandHint = string.IsNullOrWhiteSpace(setupCommandHint)
            ? null
            : setupCommandHint;
        Message = $"Missing LaTeX requirement '{DisplayName(missingRequirement)}' during {DisplayPhase(phase)}. "
            + $"{diagnostic} Diagnostics: {diagnosticDirectory}. "
            + (SetupCommandHint is null
                ? "Make sure all LaTeX dependencies are installed, then retry."
                : $"Make sure all LaTeX dependencies are installed, run {SetupCommandHint}, then retry.");
    }

    public LatexRequirement MissingRequirement { get; }
    public LatexExecutionPhase Phase { get; }
    public string Diagnostic { get; }
    public string DiagnosticDirectory { get; }
    public string? SetupCommandHint { get; }
    public string Message { get; }

    private static string DisplayName(LatexRequirement requirement) => requirement switch
    {
        ExecutableLatexRequirement executable => executable.Name,
        TexFileLatexRequirement texFile => texFile.FileName,
        FontLatexRequirement font => font.FamilyName,
        BabelLanguageLatexRequirement language => language.LanguageName,
        _ => throw new InvalidOperationException("The LaTeX requirement union is empty."),
    };

    private static string DisplayPhase(LatexExecutionPhase phase) => phase switch
    {
        LatexExecutionPhase.HeightMeasurement => "height measurement",
        LatexExecutionPhase.FinalRendering => "final rendering",
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, null),
    };
}

public sealed record LatexCompilationFailure(
    LatexExecutionPhase Phase,
    string Diagnostic,
    string DiagnosticDirectory,
    int? ExitCode)
{
    public string Message => $"LaTeX execution failed during {Phase}: {Diagnostic} Diagnostics: {DiagnosticDirectory}.";
}

public sealed record MeasurementDataFailure(string Diagnostic, string DiagnosticDirectory);

public sealed record FixedContentLayoutFailure(string Diagnostic);
public sealed record RequiredHeadingLayoutFailure(
    string Diagnostic,
    string Heading,
    string RejectionReason);
public sealed record RequiredItemLayoutFailure(
    string Diagnostic,
    string ExperienceTitle,
    string ItemText,
    string RejectionReason);
public sealed record SelectionCommitLayoutFailure(string Diagnostic);
public sealed record PredictedPageCountLayoutFailure(
    string Diagnostic,
    int ConfiguredPageCount,
    int PredictedPageCount);
public sealed record PageLayoutUnderfillFailure(
    string Diagnostic,
    string ConfiguredPages,
    int FirstPage,
    int LastPage,
    ImmutableArray<Section> AssignedSections,
    int RequiredPageCount,
    int NaturallyOccupiedPageCount);

public union MeasurementLayoutFailure(
    FixedContentLayoutFailure,
    RequiredHeadingLayoutFailure,
    RequiredItemLayoutFailure,
    SelectionCommitLayoutFailure,
    PredictedPageCountLayoutFailure,
    PageLayoutUnderfillFailure);

public union CvMeasurementResult(
    CvMeasurementSnapshot,
    IncompleteLatexInstallation,
    LatexCompilationFailure,
    MeasurementDataFailure,
    MeasurementLayoutFailure);

public sealed record MetadataOverflowFailure(string Diagnostic);
public sealed record SectionOverflowFailure(string Diagnostic, string? Section);
public sealed record EventOverflowFailure(string Diagnostic, string? Section, string? Event);

public union RenderLayoutFailure(
    MetadataOverflowFailure,
    SectionOverflowFailure,
    EventOverflowFailure);

public sealed record PageCountUnavailableFailure(string Diagnostic, int RequiredPageCount);
public sealed record PageCountMismatchFailure(string Diagnostic, int RequiredPageCount, int RenderedPageCount);
public sealed record PageLayoutMismatchFailure(string Diagnostic, string Details);
public sealed record MissingPdfFailure(string Diagnostic, string ExpectedPath);

public union RenderValidationFailure(
    PageCountUnavailableFailure,
    PageCountMismatchFailure,
    PageLayoutMismatchFailure,
    MissingPdfFailure);

public union CvRenderResult(
    GeneratedCvArtifacts,
    IncompleteLatexInstallation,
    LatexCompilationFailure,
    RenderLayoutFailure,
    RenderValidationFailure);

internal static partial class LatexFailureClassifier
{
    [GeneratedRegex(@"(?m)^! LaTeX Error: File [`']([^`']+)[`'] not found\.", RegexOptions.CultureInvariant)]
    private static partial Regex MissingTexFileRegex();

    [GeneratedRegex("""(?im)(?:fontspec error:[\s\S]{0,500}?the font|the font)\s*[`"']([^`"']+)[`"']\s*(?:cannot|could not) be found""", RegexOptions.CultureInvariant)]
    private static partial Regex MissingFontRegex();

    [GeneratedRegex(@"(?im)(?:!?\s*package babel error:.*(?:unknown option|language definition file)\s*[`']([^`']+)[`']|unknown language [`']([^`']+)[`'])", RegexOptions.CultureInvariant)]
    private static partial Regex MissingBabelLanguageRegex();

    [GeneratedRegex(@"(?m)^!.*$|(?m)^.+:\d+:.*$", RegexOptions.CultureInvariant)]
    private static partial Regex FatalDiagnosticRegex();

    public static IncompleteLatexInstallation? ClassifyLaunchFailure(
        string executableName,
        LatexExecutionPhase phase,
        Exception exception,
        string diagnosticDirectory,
        LatexExecutionOptions options)
    {
        var normalizedExecutableName = Path.GetFileNameWithoutExtension(executableName);
        var requirement = options.Requirements.FirstOrDefault(
            requirement => requirement is ExecutableLatexRequirement executable
                && string.Equals(
                    Path.GetFileNameWithoutExtension(executable.Name),
                    normalizedExecutableName,
                    StringComparison.OrdinalIgnoreCase));
        return requirement.Value is null
            ? null
            : new(
                requirement,
                phase,
                exception.Message,
                diagnosticDirectory,
                options.SetupCommandHint);
    }

    public static IncompleteLatexInstallation? ClassifyLog(
        string log,
        LatexExecutionPhase phase,
        string diagnosticDirectory,
        LatexExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(log);
        var firstFatal = FatalDiagnosticRegex().Match(log);
        var candidates = new List<(int Index, LatexRequirement Requirement, string Diagnostic)>();
        AddTexFile();
        AddFont();
        AddLanguage();
        if (candidates.Count == 0)
        {
            return null;
        }

        var first = candidates.MinBy(static candidate => candidate.Index);
        if (firstFatal.Success && firstFatal.Index < first.Index)
        {
            return null;
        }

        return new(
            first.Requirement,
            phase,
            first.Diagnostic,
            diagnosticDirectory,
            options.SetupCommandHint);

        void AddTexFile()
        {
            foreach (Match match in MissingTexFileRegex().Matches(log))
            {
                var value = match.Groups[1].Value;
                var requirement = Find(static (item, expected) =>
                    item is TexFileLatexRequirement file
                    && string.Equals(file.FileName, expected, StringComparison.OrdinalIgnoreCase), value);
                if (requirement.Value is not null)
                {
                    candidates.Add((match.Index, requirement, match.Value.Trim()));
                }
            }
        }

        void AddFont()
        {
            foreach (Match match in MissingFontRegex().Matches(log))
            {
                var value = match.Groups[1].Value;
                var requirement = Find(static (item, expected) =>
                    item is FontLatexRequirement font
                    && string.Equals(font.FamilyName, expected, StringComparison.OrdinalIgnoreCase), value);
                if (requirement.Value is not null)
                {
                    candidates.Add((match.Index, requirement, match.Value.Trim()));
                }
            }
        }

        void AddLanguage()
        {
            foreach (Match match in MissingBabelLanguageRegex().Matches(log))
            {
                var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                var requirement = Find(static (item, expected) =>
                    item is BabelLanguageLatexRequirement language
                    && string.Equals(language.LanguageName, expected, StringComparison.OrdinalIgnoreCase), value);
                if (requirement.Value is not null)
                {
                    candidates.Add((match.Index, requirement, match.Value.Trim()));
                }
            }
        }

        LatexRequirement Find(
            Func<LatexRequirement, string, bool> predicate,
            string expected)
        {
            foreach (var requirement in options.Requirements)
            {
                if (predicate(requirement, expected))
                {
                    return requirement;
                }
            }

            return default;
        }
    }

    public static string FirstDiagnostic(string log, string fallback)
    {
        var match = FatalDiagnosticRegex().Match(log);
        return match.Success ? match.Value.Trim() : fallback;
    }
}
