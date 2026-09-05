using System.Collections.Immutable;
using FindJobHelper.Configuration;

namespace FindJobHelper.CVGeneration;

public abstract class CvLayoutException : Exception
{
    protected CvLayoutException(string message)
        : base(message)
    {
    }

    protected CvLayoutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public class CvMeasurementException : CvLayoutException
{
    public CvMeasurementException(string message)
        : base(message)
    {
    }

    public CvMeasurementException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class CvMeasurementInvariantException : CvMeasurementException
{
    public CvMeasurementInvariantException(string message)
        : base(message)
    {
    }
}

public sealed class FixedCvContentLayoutException : CvLayoutException
{
    public FixedCvContentLayoutException(string message)
        : base(message)
    {
    }
}

public sealed class RequiredExperienceHeadingLayoutException : CvLayoutException
{
    public RequiredExperienceHeadingLayoutException(string heading, string rejectionReason)
        : base(FormatMessage(heading, rejectionReason))
    {
        Heading = heading;
        RejectionReason = rejectionReason;
    }

    public string Heading { get; }

    public string RejectionReason { get; }

    private static string FormatMessage(string heading, string rejectionReason)
    {
        var punctuation = rejectionReason.EndsWith(".", StringComparison.Ordinal)
            ? string.Empty
            : ".";
        return $"Required experience heading for '{heading}' could not be included because {rejectionReason}{punctuation}";
    }
}

public sealed class RequiredExperienceItemLayoutException : CvLayoutException
{
    public RequiredExperienceItemLayoutException(
        string experienceTitle,
        string itemText,
        string rejectionReason)
        : base(FormatMessage(experienceTitle, itemText, rejectionReason))
    {
        ExperienceTitle = experienceTitle;
        ItemText = itemText;
        RejectionReason = rejectionReason;
    }

    public string ExperienceTitle { get; }

    public string ItemText { get; }

    public string RejectionReason { get; }

    private static string FormatMessage(
        string experienceTitle,
        string itemText,
        string rejectionReason)
    {
        var punctuation = rejectionReason.EndsWith(".", StringComparison.Ordinal)
            ? string.Empty
            : ".";
        return $"Required experience item '{itemText}' from '{experienceTitle}' " +
            $"could not be included because {rejectionReason}{punctuation}";
    }
}

public sealed class CvSelectionCommitException : CvLayoutException
{
    public CvSelectionCommitException(string message)
        : base(message)
    {
    }
}

public sealed class PredictedPageCountMismatchException : CvLayoutException
{
    public PredictedPageCountMismatchException(int configuredPageCount, int predictedPageCount)
        : base(
            $"Configured pageCount {configuredPageCount}, but the selected CV is predicted to contain {predictedPageCount} page(s). " +
            "The exact page count cannot be reached without inserting blank pages.")
    {
        ConfiguredPageCount = configuredPageCount;
        PredictedPageCount = predictedPageCount;
    }

    public int ConfiguredPageCount { get; }

    public int PredictedPageCount { get; }
}

public sealed class CvPageLayoutUnderfillException : CvLayoutException
{
    public CvPageLayoutUnderfillException(
        CvPageLayoutBlock block,
        int naturallyOccupiedPageCount)
        : base(FormatMessage(block, naturallyOccupiedPageCount))
    {
        ArgumentNullException.ThrowIfNull(block);
        if (naturallyOccupiedPageCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(naturallyOccupiedPageCount));
        }

        ConfiguredPages = block.ConfiguredPages;
        FirstPage = block.FirstPage;
        LastPage = block.LastPage;
        AssignedSections = block.Sections;
        RequiredPageCount = block.AllocatedPageCount;
        NaturallyOccupiedPageCount = naturallyOccupiedPageCount;
    }

    public string ConfiguredPages { get; }

    public int FirstPage { get; }

    public int LastPage { get; }

    public ImmutableArray<Section> AssignedSections { get; }

    public int RequiredPageCount { get; }

    public int NaturallyOccupiedPageCount { get; }

    private static string FormatMessage(
        CvPageLayoutBlock block,
        int naturallyOccupiedPageCount)
    {
        ArgumentNullException.ThrowIfNull(block);
        var sections = string.Join(
            ", ",
            block.Sections.Select(static section => section.ToDisplayString()));
        return $"Explicit layout block {block.ConfiguredPages} ({sections}) requires {block.AllocatedPageCount} page(s), " +
            $"but its selected section content naturally occupies {naturallyOccupiedPageCount} page(s). " +
            "Explicit layouts are not padded with blank pages.";
    }
}

public abstract class CvLatexException : CvLayoutException
{
    protected CvLatexException(string message)
        : base(message)
    {
    }
}

public sealed class CvMetadataOverflowException : CvLatexException
{
    public const string ErrorMessage = "Left-side metadata must fit within its column.";

    public CvMetadataOverflowException()
        : base(ErrorMessage)
    {
    }
}

public sealed class CvSectionPageOverflowException : CvLatexException
{
    public CvSectionPageOverflowException(string? sectionLabel)
        : base(sectionLabel is null or ""
            ? "A CV section exceeds the usable height of a single page."
            : $"CV section '{sectionLabel}' exceeds the usable height of a single page.")
    {
        SectionLabel = sectionLabel;
    }

    public string? SectionLabel { get; }
}

public sealed class CvEventPageOverflowException : CvLatexException
{
    public CvEventPageOverflowException(
        string? sectionLabel,
        string? eventLabel)
        : base(sectionLabel is null or ""
            ? "A complete CV event exceeds the usable height of a fresh page."
            : eventLabel is null or ""
                ? $"A complete event in CV section '{sectionLabel}' exceeds the usable height of a fresh page."
                : $"CV event '{eventLabel}' in section '{sectionLabel}' exceeds the usable height of a fresh page.")
    {
        SectionLabel = sectionLabel;
        EventLabel = eventLabel;
    }

    public string? SectionLabel { get; }

    public string? EventLabel { get; }
}

public sealed class CvLatexCompilationException : CvLatexException
{
    public CvLatexCompilationException(string message)
        : base(message)
    {
    }
}

public sealed class RenderedPageCountUnavailableException : CvLatexException
{
    public RenderedPageCountUnavailableException(int configuredPageCount)
        : base(
            $"Configured pageCount {configuredPageCount}, but the LaTeX log does not contain a parseable rendered page count.")
    {
        ConfiguredPageCount = configuredPageCount;
    }

    public int ConfiguredPageCount { get; }
}

public sealed class RenderedPageCountMismatchException : CvLatexException
{
    public RenderedPageCountMismatchException(int configuredPageCount, int renderedPageCount)
        : base(
            $"Configured pageCount {configuredPageCount}, but the rendered PDF contains {renderedPageCount} pages")
    {
        ConfiguredPageCount = configuredPageCount;
        RenderedPageCount = renderedPageCount;
    }

    public int ConfiguredPageCount { get; }

    public int RenderedPageCount { get; }
}

public sealed class RenderedPageLayoutMismatchException : CvLatexException
{
    public RenderedPageLayoutMismatchException(string details)
        : base($"The rendered PDF does not match the explicit page layout: {details}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(details);
        Details = details;
    }

    public string Details { get; }
}

public sealed class CvPdfNotProducedException : CvLatexException
{
    public CvPdfNotProducedException()
        : base("LaTeX completed without creating a PDF.")
    {
    }
}
