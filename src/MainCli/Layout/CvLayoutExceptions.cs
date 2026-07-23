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

public sealed class CvPdfNotProducedException : CvLatexException
{
    public CvPdfNotProducedException()
        : base("LaTeX completed without creating a PDF.")
    {
    }
}
