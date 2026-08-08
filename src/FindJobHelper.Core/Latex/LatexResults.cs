using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace FindJobHelper.CVGeneration;

public enum LatexExecutionPhase
{
    HeightMeasurement,
    FinalRendering,
}

public readonly record struct LatexExecutableName(string Value);

public readonly record struct LatexTexFileName(string Value);

public readonly record struct LatexFontFamilyName(string Value);

public readonly record struct LatexLanguageName(string Value);

public sealed record ExecutableLatexRequirement(LatexExecutableName Name);

public sealed record TexFileLatexRequirement(LatexTexFileName FileName);

public sealed record FontLatexRequirement(LatexFontFamilyName FamilyName);

public sealed record BabelLanguageLatexRequirement(LatexLanguageName LanguageName);

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
        LatexExecutionOptions executionOptions)
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
        ArgumentNullException.ThrowIfNull(executionOptions);
        ExecutionOptions = executionOptions;
    }

    public LatexRequirement MissingRequirement { get; }
    public LatexExecutionPhase Phase { get; }
    public string Diagnostic { get; }
    public string DiagnosticDirectory { get; }
    public LatexExecutionOptions ExecutionOptions { get; }
}

public sealed record LatexCompilationFailure(
    LatexExecutionPhase Phase,
    string Diagnostic,
    string DiagnosticDirectory,
    int? ExitCode,
    LatexExecutionOptions ExecutionOptions);

public sealed record MeasurementDataFailure(string Diagnostic, string DiagnosticDirectory);

public sealed record FixedContentLayoutFailure;
public sealed record RequiredHeadingLayoutFailure(
    string Heading,
    string RejectionReason);
public sealed record RequiredItemLayoutFailure(
    string ExperienceTitle,
    string ItemText,
    string RejectionReason);
public sealed record SelectionCommitLayoutFailure(string Reason);
public sealed record PredictedPageCountLayoutFailure(
    int ConfiguredPageCount,
    int PredictedPageCount);
public sealed record PageLayoutUnderfillFailure(
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

public sealed record MetadataOverflowFailure;
public sealed record SectionOverflowFailure(string? Section);
public sealed record EventOverflowFailure(string? Section, string? Event);

public union RenderLayoutFailure(
    MetadataOverflowFailure,
    SectionOverflowFailure,
    EventOverflowFailure);

public sealed record PageCountUnavailableFailure(int RequiredPageCount);
public sealed record PageCountMismatchFailure(int RequiredPageCount, int RenderedPageCount);
public sealed record PageLayoutMismatchFailure(string Details);
public sealed record MissingPdfFailure(string ExpectedPath);

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

public enum CvFailureDisposition
{
    General,
    Validation,
}

public sealed record CvFailurePresentation(string Message, CvFailureDisposition Disposition);

public static class CvFailurePresenter
{
    public static CvFailurePresentation Present(CvMeasurementResult result) => result switch
    {
        IncompleteLatexInstallation failure => General(Format(failure)),
        LatexCompilationFailure failure => General(Format(failure)),
        MeasurementDataFailure failure => General(
            $"LaTeX measurement data failed: {failure.Diagnostic} Diagnostics: {failure.DiagnosticDirectory}."),
        MeasurementLayoutFailure failure => Validation(Format(failure)),
        CvMeasurementSnapshot => throw new InvalidOperationException("A successful CV measurement cannot be presented as a failure."),
        _ => throw new InvalidOperationException("The CV measurement result union is empty."),
    };

    public static CvFailurePresentation Present(CvRenderResult result) => result switch
    {
        IncompleteLatexInstallation failure => General(Format(failure)),
        LatexCompilationFailure failure => General(Format(failure)),
        RenderLayoutFailure failure => Validation(Format(failure)),
        RenderValidationFailure failure => Validation(Format(failure)),
        GeneratedCvArtifacts => throw new InvalidOperationException("A successful CV render cannot be presented as a failure."),
        _ => throw new InvalidOperationException("The CV render result union is empty."),
    };

    private static CvFailurePresentation General(string message) =>
        new(message, CvFailureDisposition.General);

    private static CvFailurePresentation Validation(string message) =>
        new(message, CvFailureDisposition.Validation);

    private static string Format(IncompleteLatexInstallation failure)
    {
        var requirement = failure.MissingRequirement switch
        {
            ExecutableLatexRequirement value => value.Name.Value,
            TexFileLatexRequirement value => value.FileName.Value,
            FontLatexRequirement value => value.FamilyName.Value,
            BabelLanguageLatexRequirement value => value.LanguageName.Value,
            _ => throw new InvalidOperationException("The LaTeX requirement union is empty."),
        };
        var phase = failure.Phase switch
        {
            LatexExecutionPhase.HeightMeasurement => "height measurement",
            LatexExecutionPhase.FinalRendering => "final rendering",
            _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Phase, null),
        };
        var guidance = failure.ExecutionOptions.SetupCommandHint is null
            ? "Make sure all LaTeX dependencies are installed, then retry."
            : $"Make sure all LaTeX dependencies are installed, run {failure.ExecutionOptions.SetupCommandHint}, then retry.";
        return $"Missing LaTeX requirement '{requirement}' during {phase}. {failure.Diagnostic} "
            + $"Diagnostics: {failure.DiagnosticDirectory}. {guidance}";
    }

    private static string Format(LatexCompilationFailure failure) =>
        $"LaTeX execution failed during {failure.Phase}: {failure.Diagnostic} Diagnostics: {failure.DiagnosticDirectory}.";

    private static string Format(MeasurementLayoutFailure failure) => failure switch
    {
        FixedContentLayoutFailure => CvMetadataOverflowException.ErrorMessage,
        RequiredHeadingLayoutFailure value =>
            new RequiredExperienceHeadingLayoutException(value.Heading, value.RejectionReason).Message,
        RequiredItemLayoutFailure value =>
            new RequiredExperienceItemLayoutException(
                value.ExperienceTitle,
                value.ItemText,
                value.RejectionReason).Message,
        SelectionCommitLayoutFailure value => value.Reason,
        PredictedPageCountLayoutFailure value =>
            new PredictedPageCountMismatchException(
                value.ConfiguredPageCount,
                value.PredictedPageCount).Message,
        PageLayoutUnderfillFailure value => FormatPageLayoutUnderfill(value),
        _ => throw new InvalidOperationException("The measurement layout failure union is empty."),
    };

    private static string FormatPageLayoutUnderfill(PageLayoutUnderfillFailure failure)
    {
        var sections = string.Join(
            ", ",
            failure.AssignedSections.Select(static section => section.ToDisplayString()));
        return $"Explicit layout block {failure.ConfiguredPages} ({sections}) requires {failure.RequiredPageCount} page(s), "
            + $"but its selected section content naturally occupies {failure.NaturallyOccupiedPageCount} page(s). "
            + "Explicit layouts are not padded with blank pages.";
    }

    private static string Format(RenderLayoutFailure failure) => failure switch
    {
        MetadataOverflowFailure => CvMetadataOverflowException.ErrorMessage,
        SectionOverflowFailure value => new CvSectionPageOverflowException(value.Section).Message,
        EventOverflowFailure value => new CvEventPageOverflowException(value.Section, value.Event).Message,
        _ => throw new InvalidOperationException("The render layout failure union is empty."),
    };

    private static string Format(RenderValidationFailure failure) => failure switch
    {
        PageCountUnavailableFailure value => new RenderedPageCountUnavailableException(value.RequiredPageCount).Message,
        PageCountMismatchFailure value => new RenderedPageCountMismatchException(value.RequiredPageCount, value.RenderedPageCount).Message,
        PageLayoutMismatchFailure value => new RenderedPageLayoutMismatchException(value.Details).Message,
        MissingPdfFailure => new CvPdfNotProducedException().Message,
        _ => throw new InvalidOperationException("The render validation failure union is empty."),
    };
}

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
                    Path.GetFileNameWithoutExtension(executable.Name.Value),
                    normalizedExecutableName,
                    StringComparison.OrdinalIgnoreCase));
        return requirement.Value is null
            ? null
            : new(
                requirement,
                phase,
                exception.Message,
                diagnosticDirectory,
                options);
    }

    public static IncompleteLatexInstallation? ClassifyLog(
        string log,
        LatexExecutionPhase phase,
        string diagnosticDirectory,
        LatexExecutionOptions options)
    {
        ArgumentNullException.ThrowIfNull(log);
        var rules = new LatexLogRequirementRule[]
        {
            new(
                MissingTexFileRegex(),
                static match => match.Groups[1].Value,
                static (requirement, value) => requirement is TexFileLatexRequirement file
                    && string.Equals(file.FileName.Value, value, StringComparison.OrdinalIgnoreCase)),
            new(
                MissingFontRegex(),
                static match => match.Groups[1].Value,
                static (requirement, value) => requirement is FontLatexRequirement font
                    && string.Equals(font.FamilyName.Value, value, StringComparison.OrdinalIgnoreCase)),
            new(
                MissingBabelLanguageRegex(),
                static match => match.Groups[1].Success
                    ? match.Groups[1].Value
                    : match.Groups[2].Value,
                static (requirement, value) => requirement is BabelLanguageLatexRequirement language
                    && string.Equals(language.LanguageName.Value, value, StringComparison.OrdinalIgnoreCase)),
        };
        var candidates = rules
            .SelectMany(rule => rule.FindMatches(log, options.Requirements))
            .OrderBy(static candidate => candidate.Index)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var first = candidates[0];

        return new(
            first.Requirement,
            phase,
            first.Diagnostic,
            diagnosticDirectory,
            options);

    }

    public static string FirstDiagnostic(string log, string fallback)
    {
        var match = FatalDiagnosticRegex().Match(log);
        return match.Success ? match.Value.Trim() : fallback;
    }
}

internal sealed record LatexLogRequirementMatch(
    int Index,
    LatexRequirement Requirement,
    string Diagnostic);

internal sealed class LatexLogRequirementRule(
    Regex pattern,
    Func<Match, string> readIdentifier,
    Func<LatexRequirement, string, bool> matchesRequirement)
{
    public IEnumerable<LatexLogRequirementMatch> FindMatches(
        string log,
        IEnumerable<LatexRequirement> requirements)
    {
        foreach (Match match in pattern.Matches(log))
        {
            var identifier = readIdentifier(match);
            var requirement = requirements.FirstOrDefault(
                item => matchesRequirement(item, identifier));
            if (requirement.Value is not null)
            {
                yield return new(
                    match.Index,
                    requirement,
                    match.Value.Trim());
            }
        }
    }
}
