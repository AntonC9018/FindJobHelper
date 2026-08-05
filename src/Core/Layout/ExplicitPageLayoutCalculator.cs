using System.Collections.Immutable;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

internal readonly record struct ExplicitPageLayoutUnit(
    Section Section,
    string? EventTitle,
    LatexHeight CurrentPageHeight,
    LatexHeight FreshPageHeight)
{
    public bool IsEvent => EventTitle is not null;
}

internal readonly record struct ExplicitPageLayoutPlacement(
    int BlockIndex,
    int UnitIndex,
    Section Section,
    string? EventTitle,
    int PageNumber,
    bool UsesFreshPageRepresentation,
    LatexHeight Height);

internal enum ExplicitPageLayoutFailureKind
{
    InvalidHeight,
    DocumentHeaderOverflow,
    DocumentFooterOverflow,
    SectionOverflow,
    EventOverflow,
    BlockOverflow,
}

internal sealed record ExplicitPageLayoutFailure(
    ExplicitPageLayoutFailureKind Kind,
    string Message,
    int? BlockIndex = null,
    Section? Section = null,
    string? EventTitle = null);

internal sealed record ExplicitPageLayoutBlockResult(
    CvPageLayoutBlock Block,
    int NaturallyOccupiedPageCount,
    int? PhysicalFirstContentPage,
    int? PhysicalLastContentPage,
    ImmutableArray<ExplicitPageLayoutPlacement> Placements);

internal sealed record ExplicitPageLayoutResult(
    ImmutableArray<ExplicitPageLayoutBlockResult> Blocks,
    ImmutableArray<ExplicitPageLayoutPlacement> Placements,
    ExplicitPageLayoutFailure? Failure)
{
    public bool Fits => Failure is null;
}

internal static class ExplicitPageLayoutCalculator
{
    public static ExplicitPageLayoutResult Calculate(
        LatexHeight usablePageHeight,
        LatexHeight documentHeaderHeight,
        LatexHeight documentFooterHeight,
        CvPageLayout layout,
        IReadOnlyList<IReadOnlyList<ExplicitPageLayoutUnit>> blockUnits)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(blockUnits);
        if (blockUnits.Count != layout.Blocks.Length)
        {
            throw new ArgumentException(
                "Explicit layout content must be supplied for every layout block.",
                nameof(blockUnits));
        }

        var pageHeight = usablePageHeight.ScaledPoints;
        var headerHeight = documentHeaderHeight.ScaledPoints;
        var footerHeight = documentFooterHeight.ScaledPoints;
        if (pageHeight < 0 || headerHeight < 0 || footerHeight < 0)
        {
            return Failure(
                ExplicitPageLayoutFailureKind.InvalidHeight,
                "Page, document-header, and document-footer heights cannot be negative.");
        }
        if (headerHeight > pageHeight)
        {
            return Failure(
                ExplicitPageLayoutFailureKind.DocumentHeaderOverflow,
                $"Document header requires {headerHeight}sp, exceeding the usable page height of {pageHeight}sp.");
        }
        if (footerHeight > pageHeight)
        {
            return Failure(
                ExplicitPageLayoutFailureKind.DocumentFooterOverflow,
                $"Document footer requires {footerHeight}sp, exceeding the usable page height of {pageHeight}sp.");
        }

        for (var blockIndex = 0; blockIndex < blockUnits.Count; blockIndex++)
        {
            var units = blockUnits[blockIndex]
                ?? throw new ArgumentException(
                    $"Explicit layout block {blockIndex + 1} has null content.",
                    nameof(blockUnits));
            for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
            {
                var unit = units[unitIndex];
                if (unit.CurrentPageHeight.ScaledPoints < 0
                    || unit.FreshPageHeight.ScaledPoints < 0)
                {
                    return Failure(
                        ExplicitPageLayoutFailureKind.InvalidHeight,
                        $"Layout unit in section '{unit.Section}' has a negative measured height.",
                        blockIndex,
                        unit.Section,
                        unit.EventTitle);
                }
                if (unit.FreshPageHeight.ScaledPoints > pageHeight)
                {
                    var kind = unit.IsEvent
                        ? ExplicitPageLayoutFailureKind.EventOverflow
                        : ExplicitPageLayoutFailureKind.SectionOverflow;
                    var subject = unit.IsEvent
                        ? $"Event '{unit.EventTitle}' in section '{unit.Section}'"
                        : $"Section '{unit.Section}'";
                    return Failure(
                        kind,
                        $"{subject} requires {unit.FreshPageHeight.ScaledPoints}sp on a fresh page, exceeding the usable page height of {pageHeight}sp.",
                        blockIndex,
                        unit.Section,
                        unit.EventTitle);
                }
            }
        }

        try
        {
            var allPlacements = ImmutableArray.CreateBuilder<ExplicitPageLayoutPlacement>();
            var blockResults = ImmutableArray.CreateBuilder<ExplicitPageLayoutBlockResult>(
                layout.Blocks.Length);
            for (var blockIndex = 0; blockIndex < layout.Blocks.Length; blockIndex++)
            {
                var block = layout.Blocks[blockIndex];
                var units = blockUnits[blockIndex];
                var naturalPageCount = CalculateNaturalPageCount(
                    pageHeight,
                    block,
                    units);
                var blockPlacements = ImmutableArray.CreateBuilder<ExplicitPageLayoutPlacement>(
                    units.Count);

                var pageNumber = block.FirstPage;
                var consumed = pageNumber == 1 ? headerHeight : 0;
                var forceFreshRepresentation = pageNumber > 1;
                for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
                {
                    var unit = units[unitIndex];
                    var usesFresh = forceFreshRepresentation;
                    var height = usesFresh
                        ? unit.FreshPageHeight.ScaledPoints
                        : unit.CurrentPageHeight.ScaledPoints;
                    var capacity = CapacityFor(
                        pageNumber,
                        pageHeight,
                        footerHeight,
                        layout.PageCount);
                    if (checked(consumed + height) > capacity)
                    {
                        pageNumber = checked(pageNumber + 1);
                        if (pageNumber > block.LastPage)
                        {
                            return Failure(
                                ExplicitPageLayoutFailureKind.BlockOverflow,
                                FormatBlockOverflow(block, unit),
                                blockIndex,
                                unit.Section,
                                unit.EventTitle,
                                blockResults,
                                allPlacements);
                        }

                        usesFresh = true;
                        height = unit.FreshPageHeight.ScaledPoints;
                        capacity = CapacityFor(
                            pageNumber,
                            pageHeight,
                            footerHeight,
                            layout.PageCount);
                        if (height > capacity)
                        {
                            return Failure(
                                ExplicitPageLayoutFailureKind.BlockOverflow,
                                FormatBlockOverflow(block, unit),
                                blockIndex,
                                unit.Section,
                                unit.EventTitle,
                                blockResults,
                                allPlacements);
                        }
                        consumed = 0;
                    }

                    consumed = checked(consumed + height);
                    forceFreshRepresentation = false;
                    var placement = new ExplicitPageLayoutPlacement(
                        blockIndex,
                        unitIndex,
                        unit.Section,
                        unit.EventTitle,
                        pageNumber,
                        usesFresh,
                        new(height));
                    blockPlacements.Add(placement);
                    allPlacements.Add(placement);
                }

                blockResults.Add(new(
                    block,
                    naturalPageCount,
                    blockPlacements.Count == 0
                        ? null
                        : blockPlacements[0].PageNumber,
                    blockPlacements.Count == 0
                        ? null
                        : blockPlacements[^1].PageNumber,
                    blockPlacements.DrainToImmutable()));
            }

            return new(
                blockResults.DrainToImmutable(),
                allPlacements.DrainToImmutable(),
                Failure: null);
        }
        catch (OverflowException)
        {
            return Failure(
                ExplicitPageLayoutFailureKind.InvalidHeight,
                "Explicit page-layout height arithmetic overflowed.");
        }
    }

    private static int CalculateNaturalPageCount(
        long pageHeight,
        CvPageLayoutBlock block,
        IReadOnlyList<ExplicitPageLayoutUnit> units)
    {
        if (units.Count == 0)
        {
            return 0;
        }

        var pageCount = 1;
        long consumed = 0;
        var forceFreshRepresentation = block.FirstPage > 1;
        foreach (var unit in units)
        {
            var height = forceFreshRepresentation
                ? unit.FreshPageHeight.ScaledPoints
                : unit.CurrentPageHeight.ScaledPoints;
            if (checked(consumed + height) > pageHeight)
            {
                pageCount = checked(pageCount + 1);
                consumed = unit.FreshPageHeight.ScaledPoints;
            }
            else
            {
                consumed = checked(consumed + height);
            }

            forceFreshRepresentation = false;
        }

        return pageCount;
    }

    private static long CapacityFor(
        int pageNumber,
        long pageHeight,
        long footerHeight,
        int finalPage)
        => pageNumber == finalPage
            ? checked(pageHeight - footerHeight)
            : pageHeight;

    private static string FormatBlockOverflow(
        CvPageLayoutBlock block,
        ExplicitPageLayoutUnit unit)
    {
        var subject = unit.IsEvent
            ? $"event '{unit.EventTitle}' in section '{unit.Section}'"
            : $"section '{unit.Section}'";
        return $"Layout block {block.ConfiguredPages} cannot include {subject} without exceeding its configured ending page {block.LastPage}.";
    }

    private static ExplicitPageLayoutResult Failure(
        ExplicitPageLayoutFailureKind kind,
        string message,
        int? blockIndex = null,
        Section? section = null,
        string? eventTitle = null,
        ImmutableArray<ExplicitPageLayoutBlockResult>.Builder? blocks = null,
        ImmutableArray<ExplicitPageLayoutPlacement>.Builder? placements = null)
        => new(
            blocks is null ? [] : blocks.ToImmutable(),
            placements is null ? [] : placements.ToImmutable(),
            new(kind, message, blockIndex, section, eventTitle));
}
