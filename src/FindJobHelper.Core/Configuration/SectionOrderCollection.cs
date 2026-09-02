using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace FindJobHelper.CVGeneration;

[JsonConverter(typeof(SectionOrderCollectionJsonConverter))]
public sealed class SectionOrderCollection : IReadOnlyList<Section>
{
    private readonly ImmutableArray<Section> _sections;

    private SectionOrderCollection(
        ImmutableArray<Section> sections,
        CvPageLayout? pageLayout,
        bool isExplicit,
        ImmutableArray<string> validationErrors)
    {
        _sections = sections;
        PageLayout = pageLayout;
        IsExplicit = isExplicit;
        ValidationErrors = validationErrors;
    }

    public Section this[int index] => _sections[index];

    public int Count => _sections.Length;

    public ImmutableArray<Section> Sections => _sections;

    public CvPageLayout? PageLayout { get; }

    public bool IsExplicit { get; }

    public ImmutableArray<string> ValidationErrors { get; }

    public IEnumerator<Section> GetEnumerator()
        => ((IEnumerable<Section>) _sections).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal static SectionOrderCollection Parse(JsonElement element)
    {
        var errors = new List<string>();
        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add(
                "'sectionOrder' must be an array containing either section-name strings or page-layout objects.");
            return new(
                sections: [],
                pageLayout: null,
                isExplicit: false,
                validationErrors: [.. errors]);
        }

        var entries = element.EnumerateArray().ToArray();
        if (entries.Length == 0)
        {
            errors.Add("'sectionOrder' must contain at least one section or layout block.");
            return new(
                sections: [],
                pageLayout: null,
                isExplicit: false,
                validationErrors: [.. errors]);
        }

        var containsStrings = entries.Any(static entry => entry.ValueKind == JsonValueKind.String);
        var containsObjects = entries.Any(static entry => entry.ValueKind == JsonValueKind.Object);
        if (ContainsMixedEntryTypes(containsStrings, containsObjects))
        {
            errors.Add(
                "'sectionOrder' must be entirely strings or entirely layout objects; mixed forms are not allowed.");
            return new(
                sections: [],
                pageLayout: null,
                isExplicit: true,
                validationErrors: [.. errors]);
        }

        if (containsStrings)
        {
            return ParseLegacy(entries, errors);
        }

        if (containsObjects)
        {
            return ParseExplicit(entries, errors);
        }

        errors.Add(
            "'sectionOrder' must contain either section-name strings or page-layout objects.");
        return new(
            sections: [],
            pageLayout: null,
            isExplicit: false,
            validationErrors: [.. errors]);

        static bool ContainsMixedEntryTypes(
            bool containsStrings,
            bool containsObjects)
        {
            if (!containsStrings)
            {
                return false;
            }

            return containsObjects;
        }
    }

    internal void Write(Utf8JsonWriter writer)
    {
        if (!ValidationErrors.IsEmpty)
        {
            throw new JsonException("An invalid section-order collection cannot be serialized.");
        }

        writer.WriteStartArray();
        if (IsExplicit)
        {
            WriteExplicitLayout(writer);
        }
        else
        {
            foreach (var section in _sections)
            {
                var sectionName = section.ToString();
                writer.WriteStringValue(sectionName);
            }
        }
        writer.WriteEndArray();
    }

    private void WriteExplicitLayout(Utf8JsonWriter writer)
    {
        if (PageLayout is null)
        {
            throw new JsonException(
                "An explicit section-order collection must contain a page layout.");
        }

        foreach (var block in PageLayout.Blocks)
        {
            writer.WriteStartObject();
            WritePageRange(writer, block);
            writer.WritePropertyName("sections");
            writer.WriteStartArray();
            foreach (var section in block.Sections)
            {
                var sectionName = section.ToString();
                writer.WriteStringValue(sectionName);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        static void WritePageRange(Utf8JsonWriter writer, CvPageLayoutBlock block)
        {
            if (block.FirstPage == block.LastPage)
            {
                writer.WriteNumber("page", block.FirstPage);
                return;
            }

            var pageRange = new ConfiguredPageRange(block.FirstPage, block.LastPage);
            var value = pageRange.ToString();
            writer.WriteString("pages", value);
        }
    }

    private static SectionOrderCollection ParseLegacy(
        IReadOnlyList<JsonElement> entries,
        List<string> errors)
    {
        var sections = ImmutableArray.CreateBuilder<Section>(entries.Count);
        var seenSections = new HashSet<Section>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.ValueKind != JsonValueKind.String)
            {
                errors.Add(
                    $"'sectionOrder[{index}]' must be a valid section-name string.");
                continue;
            }

            if (!TryParseSection(entry.GetString(), out var section))
            {
                errors.Add(
                    $"'sectionOrder[{index}]' must be a valid section-name string.");
                continue;
            }

            if (!seenSections.Add(section))
            {
                errors.Add($"Section '{section}' occurs more than once in 'sectionOrder'.");
                continue;
            }
            sections.Add(section);
        }

        return new(
            sections: sections.DrainToImmutable(),
            pageLayout: null,
            isExplicit: false,
            validationErrors: [.. errors]);
    }

    private static SectionOrderCollection ParseExplicit(
        IReadOnlyList<JsonElement> entries,
        List<string> errors)
    {
        var blocks = ImmutableArray.CreateBuilder<CvPageLayoutBlock>(entries.Count);
        var flattenedSections = ImmutableArray.CreateBuilder<Section>();
        var seenSections = new HashSet<Section>();
        var expectedFirstPage = 1;
        var hasLayoutErrors = false;

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.ValueKind != JsonValueKind.Object)
            {
                errors.Add(
                    $"'sectionOrder[{index}]' must be a page-layout object; mixed or invalid entry forms are not allowed.");
                hasLayoutErrors = true;
                continue;
            }

            var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in entry.EnumerateObject())
            {
                if (property.Name is not ("page" or "pages" or "sections"))
                {
                    errors.Add(
                        $"'sectionOrder[{index}]' contains unknown property '{property.Name}'.");
                    hasLayoutErrors = true;
                    continue;
                }
                if (!properties.TryAdd(property.Name, property.Value))
                {
                    errors.Add(
                        $"'sectionOrder[{index}]' contains duplicate property '{property.Name}'.");
                    hasLayoutErrors = true;
                }
            }

            var pageRange = ParsePageRange(
                properties,
                index,
                errors,
                ref hasLayoutErrors);

            var blockSections = ImmutableArray.CreateBuilder<Section>();
            var validSections = true;
            if (!TryGetSections(properties, out var sectionsElement))
            {
                errors.Add(
                    $"'sectionOrder[{index}].sections' must be a nonempty array of valid section names.");
                hasLayoutErrors = true;
                validSections = false;
            }
            else
            {
                var sectionEntries = sectionsElement.EnumerateArray().ToArray();
                if (sectionEntries.Length == 0)
                {
                    errors.Add(
                        $"'sectionOrder[{index}].sections' must contain at least one section.");
                    hasLayoutErrors = true;
                    validSections = false;
                }

                var blockSeenSections = new HashSet<Section>();
                for (var sectionIndex = 0; sectionIndex < sectionEntries.Length; sectionIndex++)
                {
                    var sectionEntry = sectionEntries[sectionIndex];
                    if (sectionEntry.ValueKind != JsonValueKind.String)
                    {
                        errors.Add(
                            $"'sectionOrder[{index}].sections[{sectionIndex}]' must be a valid section name.");
                        hasLayoutErrors = true;
                        validSections = false;
                        continue;
                    }
                    if (!TryParseSection(sectionEntry.GetString(), out var section))
                    {
                        errors.Add(
                            $"'sectionOrder[{index}].sections[{sectionIndex}]' must be a valid section name.");
                        hasLayoutErrors = true;
                        validSections = false;
                        continue;
                    }
                    if (!blockSeenSections.Add(section))
                    {
                        errors.Add(
                            $"Section '{section}' occurs more than once in 'sectionOrder[{index}].sections'.");
                        hasLayoutErrors = true;
                        validSections = false;
                        continue;
                    }
                    if (!seenSections.Add(section))
                    {
                        errors.Add(
                            $"Section '{section}' occurs more than once across the complete explicit 'sectionOrder' layout.");
                        hasLayoutErrors = true;
                        validSections = false;
                        continue;
                    }

                    blockSections.Add(section);
                    flattenedSections.Add(section);
                }
            }

            if (pageRange is { } validPageRange)
            {
                if (validPageRange.FirstPage != expectedFirstPage)
                {
                    errors.Add(
                        $"'sectionOrder[{index}]' starts at page {validPageRange.FirstPage}, but ordered contiguous coverage requires page {expectedFirstPage}; gaps, overlaps, and unordered entries are not allowed.");
                    hasLayoutErrors = true;
                }

                try
                {
                    if (index < entries.Count - 1)
                    {
                        expectedFirstPage = checked(validPageRange.LastPage + 1);
                    }
                }
                catch (OverflowException)
                {
                    errors.Add(
                        $"'sectionOrder[{index}]' cannot end at page {validPageRange.LastPage} because no following contiguous page can be represented.");
                    hasLayoutErrors = true;
                }
            }

            if (TryGetBlockRange(
                    pageRange,
                    validSections,
                    blockSections.Count,
                    out var range))
            {
                blocks.Add(new(
                    range.FirstPage,
                    range.LastPage,
                    blockSections.DrainToImmutable()));
            }
        }

        CvPageLayout? pageLayout = null;
        if (CanCreatePageLayout(hasLayoutErrors, blocks.Count, entries.Count))
        {
            pageLayout = new(blocks.DrainToImmutable());
        }

        return new(
            sections: flattenedSections.DrainToImmutable(),
            pageLayout: pageLayout,
            isExplicit: true,
            validationErrors: [.. errors]);
    }

    private static bool TryParseSection(string? value, out Section section)
    {
        if (!Enum.TryParse(value, ignoreCase: false, out section))
        {
            return false;
        }

        return Enum.IsDefined(section);
    }

    private static bool TryParsePositivePage(JsonElement element, out int page)
    {
        page = default;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }
        if (!element.TryGetInt32(out page))
        {
            return false;
        }

        return page > 0;
    }

    private static ConfiguredPageRange? ParsePageRange(
        IReadOnlyDictionary<string, JsonElement> properties,
        int index,
        List<string> errors,
        ref bool hasLayoutErrors)
    {
        var hasPage = properties.TryGetValue("page", out var pageElement);
        var hasPages = properties.TryGetValue("pages", out var pagesElement);
        if (hasPage == hasPages)
        {
            errors.Add(
                $"'sectionOrder[{index}]' must contain exactly one of 'page' or 'pages'.");
            hasLayoutErrors = true;
            return null;
        }

        if (!hasPage)
        {
            return ParsePageRangeValue(
                pagesElement,
                index,
                errors,
                ref hasLayoutErrors);
        }

        if (!TryParsePositivePage(pageElement, out var page))
        {
            errors.Add(
                $"'sectionOrder[{index}].page' must be a positive 32-bit integer.");
            hasLayoutErrors = true;
            return null;
        }

        return new(page, page);
    }

    private static ConfiguredPageRange? ParsePageRangeValue(
        JsonElement pagesElement,
        int index,
        List<string> errors,
        ref bool hasLayoutErrors)
    {
        if (TryParsePageRange(pagesElement, out var pageRange))
        {
            return pageRange;
        }

        errors.Add(
            $"'sectionOrder[{index}].pages' must be an inclusive 'start-end' range of positive 32-bit integers where start is less than end.");
        hasLayoutErrors = true;
        return null;
    }

    private static bool TryParsePageRange(
        JsonElement element,
        out ConfiguredPageRange pageRange)
    {
        pageRange = default;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        return ConfiguredPageRange.TryParse(element.GetString(), out pageRange);
    }

    private static bool TryGetSections(
        IReadOnlyDictionary<string, JsonElement> properties,
        out JsonElement sections)
    {
        if (!properties.TryGetValue("sections", out sections))
        {
            return false;
        }

        return sections.ValueKind == JsonValueKind.Array;
    }

    private static bool TryGetBlockRange(
        ConfiguredPageRange? pageRange,
        bool validSections,
        int sectionCount,
        out ConfiguredPageRange range)
    {
        range = default;
        if (pageRange is not { } pageRangeValue)
        {
            return false;
        }
        if (!validSections)
        {
            return false;
        }

        if (sectionCount <= 0)
        {
            return false;
        }

        range = pageRangeValue;
        return true;
    }

    private static bool CanCreatePageLayout(
        bool hasLayoutErrors,
        int blockCount,
        int entryCount)
    {
        if (hasLayoutErrors)
        {
            return false;
        }

        return blockCount == entryCount;
    }

    private readonly record struct ConfiguredPageRange(int FirstPage, int LastPage)
    {
        public static bool TryParse(
            string? value,
            out ConfiguredPageRange pageRange)
        {
            pageRange = default;
            if (value is null)
            {
                return false;
            }

            var separator = value.IndexOf('-');
            if (!HasValidSeparator(value, separator))
            {
                return false;
            }
            if (!int.TryParse(
                    value.AsSpan(0, separator),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var firstPage))
            {
                return false;
            }
            if (firstPage <= 0)
            {
                return false;
            }
            if (!int.TryParse(
                    value.AsSpan(separator + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var lastPage))
            {
                return false;
            }
            if (firstPage >= lastPage)
            {
                return false;
            }

            pageRange = new(firstPage, lastPage);
            return true;

            static bool HasValidSeparator(string value, int separator)
            {
                if (separator <= 0)
                {
                    return false;
                }
                if (separator >= value.Length - 1)
                {
                    return false;
                }

                return separator == value.LastIndexOf('-');
            }
        }

        public override string ToString()
            => $"{FirstPage.ToString(CultureInfo.InvariantCulture)}-{LastPage.ToString(CultureInfo.InvariantCulture)}";
    }
}

public sealed class SectionOrderCollectionJsonConverter
    : JsonConverter<SectionOrderCollection>
{
    public override bool HandleNull => true;

    public override SectionOrderCollection Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return SectionOrderCollection.Parse(document.RootElement);
    }

    public override void Write(
        Utf8JsonWriter writer,
        SectionOrderCollection value,
        JsonSerializerOptions options)
        => value.Write(writer);
}
