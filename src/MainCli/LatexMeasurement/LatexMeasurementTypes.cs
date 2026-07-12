using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using FindJobHelper.Core;
using FindJobHelper.Core.Helper;

namespace FindJobHelper.CVGeneration;

public readonly record struct SectionId(int Value)
{
    public static SectionId Languages { get; } = new(0);
    public static SectionId WorkExperience { get; } = new(1);
    public static SectionId Education { get; } = new(2);
    public static SectionId PersonalProjects { get; } = new(3);

    public static SectionId FromSection(Section section) => section switch
    {
        Section.Languages => Languages,
        Section.WorkExperience => WorkExperience,
        Section.Education => Education,
        Section.PersonalProjects => PersonalProjects,
        _ => throw new ArgumentOutOfRangeException(nameof(section), section, null),
    };

    public Section ToSection() => Value switch
    {
        0 => Section.Languages,
        1 => Section.WorkExperience,
        2 => Section.Education,
        3 => Section.PersonalProjects,
        _ => throw new InvalidOperationException($"Unknown section ID '{Value}'."),
    };

    public static IReadOnlyList<SectionId> All { get; } =
        new[] { Languages, WorkExperience, Education, PersonalProjects };
}

public static class SectionIdExtensions
{
    public static SectionId ToSectionId(this Section section) => SectionId.FromSection(section);
}

public readonly record struct LatexHeight(long ScaledPoints)
{
    public static LatexHeight Zero { get; } = new(0);
}

public sealed record CvMeasurementSnapshot(
    IReadOnlyDictionary<ExperienceItemId, LatexHeight> ExperienceItems,
    IReadOnlyDictionary<ExperienceListId, LatexHeight> ExperienceChrome,
    IReadOnlyDictionary<SectionId, LatexHeight> CompleteSections,
    IReadOnlyDictionary<SectionId, LatexHeight> SectionChrome,
    LatexHeight DocumentChrome)
{
    public LatexHeight GetExperienceItemHeight(ExperienceItemId id)
        => GetRequired(ExperienceItems, id, "experience item");

    public LatexHeight GetExperienceChromeHeight(ExperienceListId id)
        => GetRequired(ExperienceChrome, id, "experience list chrome");

    public LatexHeight GetCompleteSectionHeight(SectionId id)
        => GetRequired(CompleteSections, id, "complete section");

    public LatexHeight GetSectionChromeHeight(SectionId id)
        => GetRequired(SectionChrome, id, "section chrome");

    internal static CvMeasurementSnapshot CreateFrozen(
        IDictionary<ExperienceItemId, LatexHeight> experienceItems,
        IDictionary<ExperienceListId, LatexHeight> experienceChrome,
        IDictionary<SectionId, LatexHeight> completeSections,
        IDictionary<SectionId, LatexHeight> sectionChrome,
        LatexHeight documentChrome)
        => new(
            experienceItems.ToFrozenDictionary(),
            experienceChrome.ToFrozenDictionary(),
            completeSections.ToFrozenDictionary(),
            sectionChrome.ToFrozenDictionary(),
            documentChrome);

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

public static class LatexMeasurementRules
{
    // Increment when template layout or measurement semantics change.
    public const int CurrentVersion = 1;
}

internal readonly record struct MeasurementCorrelationId(int Value)
{
    public override string ToString() => $"M{Value:D8}";
}

internal enum LatexMeasurementKind
{
    ExperienceItem,
    ExperienceChrome,
    SectionChrome,
    StaticSection,
    CompleteSection,
    DocumentChrome,
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
            Write(visitor, "style", (int)node.Style);
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
        SectionId? sectionId = null)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, kind.ToString());
        if (sectionId is { } id)
        {
            Append(hash, id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
