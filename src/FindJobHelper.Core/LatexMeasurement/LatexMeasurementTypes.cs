using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using FindJobHelper.Core;
using FindJobHelper.Core.Helper;

namespace FindJobHelper.CVGeneration;

public readonly record struct LatexHeight(long ScaledPoints)
{
    public static LatexHeight Zero { get; } = new(0);
}

public sealed record CvMeasurementSnapshot : ICvMeasurementResult
{
    public CvMeasurementSnapshot(
        IReadOnlyDictionary<ExperienceItemId, LatexHeight> experienceItems,
        IReadOnlyDictionary<ExperienceListId, LatexHeight> experienceHeadings,
        IReadOnlyDictionary<ExperienceListId, LatexHeight> experienceChrome,
        IReadOnlyDictionary<Section, LatexHeight> currentPageCompleteSections,
        IReadOnlyDictionary<Section, LatexHeight> currentPageSectionChrome,
        IReadOnlyDictionary<Section, LatexHeight> freshPageSectionChrome,
        LatexHeight documentHeader,
        LatexHeight documentFooter,
        LatexHeight usablePageHeight,
        IReadOnlyDictionary<Section, LatexHeight>? currentPageSplitSectionStart = null,
        IReadOnlyDictionary<Section, LatexHeight>? freshPageSplitSectionStart = null,
        LatexHeight? splitSectionEnd = null,
        LatexHeight? freshPageContinuation = null,
        IReadOnlyDictionary<Section, LatexHeight>? currentPageExplicitStaticSections = null,
        IReadOnlyDictionary<Section, LatexHeight>? freshPageExplicitStaticSections = null)
    {
        ExperienceItems = experienceItems;
        ExperienceHeadings = experienceHeadings;
        ExperienceChrome = experienceChrome;
        CurrentPageCompleteSections = currentPageCompleteSections;
        CurrentPageSectionChrome = currentPageSectionChrome;
        FreshPageSectionChrome = freshPageSectionChrome;
        CurrentPageSplitSectionStart =
            currentPageSplitSectionStart ?? currentPageSectionChrome;
        FreshPageSplitSectionStart =
            freshPageSplitSectionStart ?? freshPageSectionChrome;
        SplitSectionEnd = splitSectionEnd ?? LatexHeight.Zero;
        FreshPageContinuation = freshPageContinuation ?? LatexHeight.Zero;
        CurrentPageExplicitStaticSections =
            currentPageExplicitStaticSections ?? currentPageCompleteSections;
        FreshPageExplicitStaticSections =
            freshPageExplicitStaticSections
            ?? currentPageCompleteSections.ToDictionary(
                static pair => pair.Key,
                pair => pair.Value.ScaledPoints == 0
                    ? LatexHeight.Zero
                    : new LatexHeight(checked(
                        pair.Value.ScaledPoints
                        - GetRequired(
                            currentPageSectionChrome,
                            pair.Key,
                            "current-page section chrome").ScaledPoints
                        + GetRequired(
                            freshPageSectionChrome,
                            pair.Key,
                            "fresh-page section chrome").ScaledPoints)));
        DocumentHeader = documentHeader;
        DocumentFooter = documentFooter;
        UsablePageHeight = usablePageHeight;
    }

    public IReadOnlyDictionary<ExperienceItemId, LatexHeight> ExperienceItems { get; }
    public IReadOnlyDictionary<ExperienceListId, LatexHeight> ExperienceHeadings { get; }
    public IReadOnlyDictionary<ExperienceListId, LatexHeight> ExperienceChrome { get; }
    public IReadOnlyDictionary<Section, LatexHeight> CurrentPageCompleteSections { get; }
    public IReadOnlyDictionary<Section, LatexHeight> CurrentPageSectionChrome { get; }
    public IReadOnlyDictionary<Section, LatexHeight> FreshPageSectionChrome { get; }
    public IReadOnlyDictionary<Section, LatexHeight> CurrentPageSplitSectionStart { get; }
    public IReadOnlyDictionary<Section, LatexHeight> FreshPageSplitSectionStart { get; }
    public LatexHeight SplitSectionEnd { get; }
    public LatexHeight FreshPageContinuation { get; }
    public IReadOnlyDictionary<Section, LatexHeight> CurrentPageExplicitStaticSections { get; }
    public IReadOnlyDictionary<Section, LatexHeight> FreshPageExplicitStaticSections { get; }
    public LatexHeight DocumentHeader { get; }
    public LatexHeight DocumentFooter { get; }
    public LatexHeight UsablePageHeight { get; }

    public LatexHeight GetExperienceItemHeight(ExperienceItemId id)
        => GetRequired(ExperienceItems, id, "experience item");

    public LatexHeight GetExperienceHeadingHeight(ExperienceListId id)
        => GetRequired(ExperienceHeadings, id, "experience list heading");

    public LatexHeight GetExperienceChromeHeight(ExperienceListId id)
        => GetRequired(ExperienceChrome, id, "experience list chrome");

    public LatexHeight GetCurrentPageCompleteSectionHeight(Section section)
        => GetRequired(CurrentPageCompleteSections, section, "current-page complete section");

    public LatexHeight GetCurrentPageSectionChromeHeight(Section section)
        => GetRequired(CurrentPageSectionChrome, section, "current-page section chrome");

    public LatexHeight GetFreshPageSectionChromeHeight(Section section)
        => GetRequired(FreshPageSectionChrome, section, "fresh-page section chrome");

    public LatexHeight GetCurrentPageSplitSectionStartHeight(Section section)
        => GetRequired(
            CurrentPageSplitSectionStart,
            section,
            "current-page split-section start");

    public LatexHeight GetFreshPageSplitSectionStartHeight(Section section)
        => GetRequired(
            FreshPageSplitSectionStart,
            section,
            "fresh-page split-section start");

    public LatexHeight GetCurrentPageExplicitStaticSectionHeight(Section section)
        => GetRequired(
            CurrentPageExplicitStaticSections,
            section,
            "current-page explicit static section");

    public LatexHeight GetFreshPageExplicitStaticSectionHeight(Section section)
        => GetRequired(
            FreshPageExplicitStaticSections,
            section,
            "fresh-page explicit static section");

    public LatexHeight DeriveFreshPageSectionHeight(
        Section section,
        LatexHeight currentPageSectionHeight)
    {
        if (currentPageSectionHeight.ScaledPoints < 0)
        {
            throw new CvMeasurementInvariantException(
                $"Current-page height for section '{section}' cannot be negative.");
        }
        if (currentPageSectionHeight.ScaledPoints == 0)
        {
            return LatexHeight.Zero;
        }

        var height = checked(
            currentPageSectionHeight.ScaledPoints
            - GetCurrentPageSectionChromeHeight(section).ScaledPoints
            + GetFreshPageSectionChromeHeight(section).ScaledPoints);
        if (height < 0)
        {
            throw new CvMeasurementInvariantException(
                $"Derived fresh-page height for section '{section}' cannot be negative.");
        }

        return new(height);
    }

    public LatexHeight GetFreshPageCompleteSectionHeight(Section section)
        => DeriveFreshPageSectionHeight(section, GetCurrentPageCompleteSectionHeight(section));

    internal static CvMeasurementSnapshot CreateFrozen(
        IDictionary<ExperienceItemId, LatexHeight> experienceItems,
        IDictionary<ExperienceListId, LatexHeight> experienceHeadings,
        IDictionary<ExperienceListId, LatexHeight> experienceChrome,
        IDictionary<Section, LatexHeight> currentPageCompleteSections,
        IDictionary<Section, LatexHeight> currentPageSectionChrome,
        IDictionary<Section, LatexHeight> freshPageSectionChrome,
        IDictionary<Section, LatexHeight> currentPageSplitSectionStart,
        IDictionary<Section, LatexHeight> freshPageSplitSectionStart,
        LatexHeight splitSectionEnd,
        LatexHeight freshPageContinuation,
        IDictionary<Section, LatexHeight> currentPageExplicitStaticSections,
        IDictionary<Section, LatexHeight> freshPageExplicitStaticSections,
        LatexHeight documentHeader,
        LatexHeight documentFooter,
        LatexHeight usablePageHeight)
        => new(
            experienceItems: experienceItems.ToFrozenDictionary(),
            experienceHeadings: experienceHeadings.ToFrozenDictionary(),
            experienceChrome: experienceChrome.ToFrozenDictionary(),
            currentPageCompleteSections: currentPageCompleteSections.ToFrozenDictionary(),
            currentPageSectionChrome: currentPageSectionChrome.ToFrozenDictionary(),
            freshPageSectionChrome: freshPageSectionChrome.ToFrozenDictionary(),
            documentHeader: documentHeader,
            documentFooter: documentFooter,
            usablePageHeight: usablePageHeight,
            currentPageSplitSectionStart: currentPageSplitSectionStart.ToFrozenDictionary(),
            freshPageSplitSectionStart: freshPageSplitSectionStart.ToFrozenDictionary(),
            splitSectionEnd: splitSectionEnd,
            freshPageContinuation: freshPageContinuation,
            currentPageExplicitStaticSections:
                currentPageExplicitStaticSections.ToFrozenDictionary(),
            freshPageExplicitStaticSections:
                freshPageExplicitStaticSections.ToFrozenDictionary());

    private static LatexHeight GetRequired<TKey>(
        IReadOnlyDictionary<TKey, LatexHeight> values,
        TKey id,
        string description)
        where TKey : notnull
    {
        if (values.TryGetValue(id, out var height))
        {
            return height;
        }

        throw new KeyNotFoundException(
            $"The CV measurement snapshot does not contain {description} ID '{id}'.");
    }
}

internal readonly record struct MeasurementCorrelationId(int Value)
{
    public LatexProgressMarkerId ProgressMarkerId => new(
        LatexProgressMarkerCategory.Measurement,
        Value);

    public override string ToString() => ProgressMarkerId.ToString();
}

internal enum LatexMeasurementKind
{
    ExperienceItem,
    ExperienceHeading,
    ExperienceChrome,
    SectionChrome,
    FreshPageSectionChrome,
    SplitSectionStart,
    FreshPageSplitSectionStart,
    SplitSectionEnd,
    FreshPageContinuation,
    ExplicitStaticSection,
    FreshPageExplicitStaticSection,
    StaticSection,
    CompleteSection,
    DocumentHeader,
    DocumentFooter,
    UsablePageHeight,
}

internal enum LatexMeasurementMode
{
    Box,
    FlowBlock,
    FreshPageFlowBlock,
    SectionChrome,
    FreshPageSectionChrome,
    SplitSectionStart,
    FreshPageSplitSectionStart,
    SplitSectionEnd,
    FreshPageContinuation,
    ExperienceItemMarginal,
    ExperienceChromeWithoutPermanentItems,
    DocumentHeader,
    PageStart,
}

internal readonly record struct LatexMeasurementCacheKey(
    int RuleVersion,
    LatexMeasurementKind Kind,
    string ContentHash);

internal static class RichTextCanonicalHasher
{
    private static readonly Key<IncrementalHash> HashKey = new(new("CanonicalRichTextHash"));

    private static readonly RichTextVisitationMap VisitationMap = RichTextVisitorDefaults.CreateBuilder()
        .Override<RichText>(next => (node, visitor) =>
        {
            Write(visitor, "node", "RichText");
            Write(visitor, "children", node.Items.Length);
            Write(visitor, "boundary", "begin");
            next(node, visitor);
            Write(visitor, "boundary", "end");
        })
        .Override<Href>(next => (node, visitor) =>
        {
            Write(visitor, "node", "Href");
            Write(visitor, "url", node.Url.AbsoluteUri);
            Write(visitor, "children", 1);
            Write(visitor, "boundary", "begin");
            next(node, visitor);
            Write(visitor, "boundary", "end");
        })
        .Override<PlainText>(next => (node, visitor) =>
        {
            Write(visitor, "node", "PlainText");
            Write(visitor, "text", node.Text);
            Write(visitor, "children", 0);
            next(node, visitor);
        })
        .Override<StyledText>(next => (node, visitor) =>
        {
            Write(visitor, "node", "StyledText");
            Write(visitor, "text", node.Text);
            Write(visitor, "style", (int) node.Style);
            Write(visitor, "children", 0);
            next(node, visitor);
        })
        .Build();

    public static string ComputeHash(IRichTextNode richText)
    {
        ArgumentNullException.ThrowIfNull(richText);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var context = new VisitationContextData();
        context.Add(HashKey, hash);
        VisitationMap.CreateVisitor(context).Visit(richText);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Write(RichTextVisitor visitor, string name, string value)
    {
        FeedField(visitor.Data.Get(HashKey), Encoding.UTF8.GetBytes(name));
        FeedField(visitor.Data.Get(HashKey), Encoding.UTF8.GetBytes(value));
    }

    private static void Write(RichTextVisitor visitor, string name, int value)
    {
        FeedField(visitor.Data.Get(HashKey), Encoding.UTF8.GetBytes(name));
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        FeedField(visitor.Data.Get(HashKey), bytes);
    }

    private static void FeedField(IncrementalHash hash, ReadOnlySpan<byte> value)
    {
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, value.Length);
        hash.AppendData(length);
        hash.AppendData(value);
    }
}

internal static class LatexFragmentHasher
{
    public static string Compute(
        LatexMeasurementKind kind,
        string renderedLatex,
        Section? section = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, kind.ToString());
        if (section is { } sectionValue)
        {
            Append(hash, ((int) sectionValue).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        Append(hash, renderedLatex);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }
}
