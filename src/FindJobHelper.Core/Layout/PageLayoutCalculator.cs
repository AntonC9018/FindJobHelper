using System.Collections.Immutable;
using FindJobHelper.Configuration;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

internal readonly record struct PageLayoutSection(
    Section Section,
    LatexHeight CurrentPageHeight,
    LatexHeight FreshPageHeight);

internal readonly record struct PageLayoutPlacement(
    Section Section,
    int PageNumber,
    bool UsesFreshPageRepresentation,
    LatexHeight Height);

internal enum PageLayoutFailureKind
{
    DocumentHeaderOverflow,
    SectionOverflow,
    DocumentFooterOverflow,
    PageCountExceeded,
    InvalidHeight,
}

internal sealed record PageLayoutFailure(
    PageLayoutFailureKind Kind,
    string Message,
    Section? Section = null);

internal sealed record PageLayoutResult(
    int PageCount,
    int FooterPageNumber,
    ImmutableArray<PageLayoutPlacement> Placements,
    PageLayoutFailure? Failure)
{
    public bool Fits => Failure is null;
}

internal static class PageLayoutCalculator
{
    public static PageLayoutResult Calculate(
        LatexHeight usablePageHeight,
        LatexHeight documentHeaderHeight,
        LatexHeight documentFooterHeight,
        IReadOnlyList<PageLayoutSection> sections,
        CvPageCount pageCount = default)
    {
        ArgumentNullException.ThrowIfNull(sections);
        var maximumPageCount = pageCount.ExactCount;

        var pageHeight = usablePageHeight.ScaledPoints;
        var headerHeight = documentHeaderHeight.ScaledPoints;
        var footerHeight = documentFooterHeight.ScaledPoints;
        if (HasNegativeHeight(pageHeight, headerHeight, footerHeight))
        {
            return Failure(
                PageLayoutFailureKind.InvalidHeight,
                "Page, document-header, and document-footer heights cannot be negative.");
        }
        var documentPartFailure = ValidateDocumentPart(
            "header",
            headerHeight,
            pageHeight,
            PageLayoutFailureKind.DocumentHeaderOverflow);
        if (documentPartFailure is not null)
        {
            return Failure(documentPartFailure);
        }

        documentPartFailure = ValidateDocumentPart(
            "footer",
            footerHeight,
            pageHeight,
            PageLayoutFailureKind.DocumentFooterOverflow);
        if (documentPartFailure is not null)
        {
            return Failure(documentPartFailure);
        }

        foreach (var section in sections)
        {
            if (HasNegativeHeight(
                    section.CurrentPageHeight.ScaledPoints,
                    section.FreshPageHeight.ScaledPoints))
            {
                return Failure(
                    PageLayoutFailureKind.InvalidHeight,
                    $"Section '{section.Section}' has a negative measured height.",
                    section.Section);
            }
            if (section.FreshPageHeight.ScaledPoints > pageHeight)
            {
                return Failure(
                    PageLayoutFailureKind.SectionOverflow,
                    $"Section '{section.Section}' requires {section.FreshPageHeight.ScaledPoints}sp on a fresh page, exceeding the usable page height of {pageHeight}sp.",
                    section.Section);
            }
        }

        var placements = ImmutableArray.CreateBuilder<PageLayoutPlacement>(sections.Count);
        var renderedPageCount = 1;
        var consumed = headerHeight;
        foreach (var section in sections)
        {
            var currentHeight = section.CurrentPageHeight.ScaledPoints;
            if (checked(consumed + currentHeight) <= pageHeight)
            {
                consumed = checked(consumed + currentHeight);
                placements.Add(new(
                    section.Section,
                    renderedPageCount,
                    UsesFreshPageRepresentation: false,
                    section.CurrentPageHeight));
                continue;
            }

            renderedPageCount = checked(renderedPageCount + 1);
            if (ExceedsPageCap(
                    maximumPageCount,
                    renderedPageCount,
                    out var cap))
            {
                return PageCountFailure(renderedPageCount, cap, placements.ToImmutable());
            }

            consumed = section.FreshPageHeight.ScaledPoints;
            placements.Add(new(
                section.Section,
                renderedPageCount,
                UsesFreshPageRepresentation: true,
                section.FreshPageHeight));
        }

        var footerPageNumber = renderedPageCount;
        if (checked(consumed + footerHeight) > pageHeight)
        {
            renderedPageCount = checked(renderedPageCount + 1);
            footerPageNumber = renderedPageCount;
            if (ExceedsPageCap(
                    maximumPageCount,
                    renderedPageCount,
                    out var cap))
            {
                return PageCountFailure(renderedPageCount, cap, placements.ToImmutable());
            }
        }

        return new(renderedPageCount, footerPageNumber, placements.ToImmutable(), Failure: null);
    }

    private static bool HasNegativeHeight(params long[] heights)
    {
        foreach (var height in heights)
        {
            if (height < 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ExceedsPageCap(
        int? maximumPageCount,
        int renderedPageCount,
        out int cap)
    {
        cap = default;
        if (maximumPageCount is not { } maximum)
        {
            return false;
        }

        cap = maximum;
        return renderedPageCount > maximum;
    }

    private static PageLayoutResult PageCountFailure(
        int requiredPageCount,
        int maximumPageCount,
        ImmutableArray<PageLayoutPlacement> placements)
        => new(
            requiredPageCount,
            requiredPageCount,
            placements,
            new(
                PageLayoutFailureKind.PageCountExceeded,
                $"The layout requires more than the configured {maximumPageCount}-page layout."));

    private static PageLayoutResult Failure(
        PageLayoutFailureKind kind,
        string message,
        Section? section = null)
        => new(1, 1, [], new(kind, message, section));

    private static PageLayoutResult Failure(PageLayoutFailure failure)
        => new(1, 1, [], failure);

    private static PageLayoutFailure? ValidateDocumentPart(
        string partName,
        long partHeight,
        long pageHeight,
        PageLayoutFailureKind failureKind)
        => partHeight > pageHeight
            ? new(
                failureKind,
                $"Document {partName} requires {partHeight}sp, exceeding the usable page height of {pageHeight}sp.")
            : null;
}
