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

public sealed record CvMeasurementSnapshot(
    IReadOnlyDictionary<ExperienceItemId, LatexHeight> ExperienceItems,
    IReadOnlyDictionary<ExperienceListId, LatexHeight> ExperienceHeadings,
    IReadOnlyDictionary<ExperienceListId, LatexHeight> ExperienceChrome,
    IReadOnlyDictionary<Section, LatexHeight> CompleteSections,
    IReadOnlyDictionary<Section, LatexHeight> SectionChrome,
    LatexHeight DocumentChrome,
    LatexHeight UsablePageHeight)
{
    public LatexHeight GetExperienceItemHeight(ExperienceItemId id)
        => GetRequired(ExperienceItems, id, "experience item");

    public LatexHeight GetExperienceHeadingHeight(ExperienceListId id)
        => GetRequired(ExperienceHeadings, id, "experience list heading");

    public LatexHeight GetExperienceChromeHeight(ExperienceListId id)
        => GetRequired(ExperienceChrome, id, "experience list chrome");

    public LatexHeight GetCompleteSectionHeight(Section section)
        => GetRequired(CompleteSections, section, "complete section");

    public LatexHeight GetSectionChromeHeight(Section section)
        => GetRequired(SectionChrome, section, "section chrome");

    internal static CvMeasurementSnapshot CreateFrozen(
        IDictionary<ExperienceItemId, LatexHeight> experienceItems,
        IDictionary<ExperienceListId, LatexHeight> experienceHeadings,
        IDictionary<ExperienceListId, LatexHeight> experienceChrome,
        IDictionary<Section, LatexHeight> completeSections,
        IDictionary<Section, LatexHeight> sectionChrome,
        LatexHeight documentChrome,
        LatexHeight usablePageHeight)
        => new(
            experienceItems.ToFrozenDictionary(),
            experienceHeadings.ToFrozenDictionary(),
            experienceChrome.ToFrozenDictionary(),
            completeSections.ToFrozenDictionary(),
            sectionChrome.ToFrozenDictionary(),
            documentChrome,
            usablePageHeight);

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
    public override string ToString() => $"M{Value:D8}";
}

internal enum LatexMeasurementKind
{
    ExperienceItem,
    ExperienceHeading,
    ExperienceChrome,
    SectionChrome,
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
    SectionChrome,
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

    public static string ComputeHash(RichText richText)
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
