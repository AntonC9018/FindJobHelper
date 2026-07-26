using System.Collections;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindJobHelper.Core;
using FindJobHelper.CVGeneration;

namespace MainCli;

public sealed class CvConfigurationException : Exception
{
    public ImmutableArray<string> Errors { get; }

    public CvConfigurationException(string message)
        : base(message)
    {
        Errors = [message];
    }

    public CvConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    public CvConfigurationException(IEnumerable<string> errors)
        : this([.. errors])
    {
    }

    private CvConfigurationException(ImmutableArray<string> errors)
        : base(FormatErrors(errors))
    {
        if (errors.IsEmpty)
        {
            throw new ArgumentException("At least one configuration error is required.", nameof(errors));
        }

        Errors = errors;
    }

    private static string FormatErrors(ImmutableArray<string> errors)
    {
        return string.Join(Environment.NewLine, errors.Select(static error => $"- {error}"));
    }
}

internal sealed class JsonCvSelectionConfiguration
{
    private bool _limitToOnePage = true;
    private int? _pageCount;

    public bool LimitToOnePage
    {
        get => _limitToOnePage;
        init
        {
            _limitToOnePage = value;
            IsLimitToOnePageSpecified = true;
        }
    }

    public int? PageCount
    {
        get => _pageCount;
        init
        {
            _pageCount = value;
            IsPageCountSpecified = true;
        }
    }

    [JsonIgnore]
    internal bool IsLimitToOnePageSpecified { get; private set; }

    [JsonIgnore]
    internal bool IsPageCountSpecified { get; private set; }

    public required List<RequiredTagConfiguration> RequiredTags { get; init; }
    public required List<string> Skills { get; init; }
    public required List<string> Technologies { get; init; }
    public required MmrConfiguration Mmr { get; init; }
    public required SelectionConfiguration Selection { get; init; }
    public required JsonElement SectionOrder { get; init; }

    public CvSelectionConfiguration ToDomain()
    {
        var errors = new List<string>();
        var parsedSectionOrder = ParseSectionOrder(errors);
        if (parsedSectionOrder.IsExplicit)
        {
            if (IsPageCountSpecified)
            {
                errors.Add(
                    "'pageCount' cannot be supplied with object-form 'sectionOrder' because the page count is derived from the layout.");
            }
            if (IsLimitToOnePageSpecified)
            {
                errors.Add(
                    "'limitToOnePage' cannot be supplied with object-form 'sectionOrder' because the page count is derived from the layout.");
            }
        }
        else if (IsLimitToOnePageSpecified && IsPageCountSpecified)
        {
            errors.Add("'limitToOnePage' and 'pageCount' cannot both be supplied.");
        }

        if (IsPageCountSpecified && PageCount is null or <= 0)
        {
            errors.Add("'pageCount' must be a positive 32-bit integer.");
        }

        if (RequiredTags is null || RequiredTags.Count == 0)
        {
            errors.Add("'requiredTags' must contain at least one tag.");
        }
        else
        {
            var tagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in RequiredTags)
            {
                if (tag is null)
                {
                    errors.Add("'requiredTags' cannot contain null items.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(tag.Name))
                {
                    errors.Add("Every required tag must have a non-empty 'name'.");
                }
                else if (!tagNames.Add(tag.Name))
                {
                    errors.Add($"Required tag '{tag.Name}' is configured more than once.");
                }

                if (!float.IsFinite(tag.Weight) || tag.Weight <= 0)
                {
                    errors.Add(
                        $"Required tag '{tag.Name}' must have a finite, positive 'weight'.");
                }
            }
        }

        if (Skills is null || Skills.Count == 0)
        {
            errors.Add("'skills' must contain at least one item.");
        }
        else if (Skills.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("'skills' cannot contain blank items.");
        }

        if (Technologies is null || Technologies.Count == 0)
        {
            errors.Add("'technologies' must contain at least one item.");
        }
        else if (Technologies.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("'technologies' cannot contain blank items.");
        }

        if (Mmr is null)
        {
            errors.Add("'mmr' is required.");
        }
        else
        {
            Mmr.CollectValidationErrors(errors);
        }

        if (Selection is null)
        {
            errors.Add("'selection' is required.");
        }
        else
        {
            Selection.CollectValidationErrors(errors);
        }

        if (errors.Count > 0)
        {
            throw new CvConfigurationException(errors);
        }

        var pageLayout = parsedSectionOrder.PageLayout;
        return new(
            pageLayout is null
                ? ResolvePageCount()
                : CvPageCount.Exact(pageLayout.PageCount),
            [.. RequiredTags!],
            [.. Skills!],
            [.. Technologies!],
            Mmr!,
            Selection!,
            parsedSectionOrder.SectionOrder,
            pageLayout);
    }

    private CvPageCount ResolvePageCount()
    {
        if (IsPageCountSpecified)
        {
            return CvPageCount.Exact(PageCount!.Value);
        }

        return IsLimitToOnePageSpecified && !LimitToOnePage
            ? CvPageCount.Unrestricted
            : CvPageCount.OnePage;
    }

    private ParsedSectionOrder ParseSectionOrder(List<string> errors)
    {
        if (SectionOrder.ValueKind != JsonValueKind.Array)
        {
            errors.Add(
                "'sectionOrder' must be an array containing either section-name strings or page-layout objects.");
            return new([], PageLayout: null, IsExplicit: false);
        }

        var entries = SectionOrder.EnumerateArray().ToArray();
        if (entries.Length == 0)
        {
            errors.Add("'sectionOrder' must contain at least one section or layout block.");
            return new([], PageLayout: null, IsExplicit: false);
        }

        var containsStrings = entries.Any(static entry => entry.ValueKind == JsonValueKind.String);
        var containsObjects = entries.Any(static entry => entry.ValueKind == JsonValueKind.Object);
        if (containsStrings && containsObjects)
        {
            errors.Add(
                "'sectionOrder' must be entirely strings or entirely layout objects; mixed forms are not allowed.");
            return new([], PageLayout: null, IsExplicit: true);
        }

        if (containsStrings)
        {
            return ParseLegacySectionOrder(entries, errors);
        }

        if (containsObjects)
        {
            return ParseExplicitSectionOrder(entries, errors);
        }

        errors.Add(
            "'sectionOrder' must contain either section-name strings or page-layout objects.");
        return new([], PageLayout: null, IsExplicit: false);
    }

    private static ParsedSectionOrder ParseLegacySectionOrder(
        IReadOnlyList<JsonElement> entries,
        List<string> errors)
    {
        var sections = ImmutableArray.CreateBuilder<Section>(entries.Count);
        var seenSections = new HashSet<Section>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.ValueKind != JsonValueKind.String
                || !TryParseSection(entry.GetString(), out var section))
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

        return new(sections.DrainToImmutable(), PageLayout: null, IsExplicit: false);
    }

    private static ParsedSectionOrder ParseExplicitSectionOrder(
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

            var hasPage = properties.TryGetValue("page", out var pageElement);
            var hasPages = properties.TryGetValue("pages", out var pagesElement);
            if (hasPage == hasPages)
            {
                errors.Add(
                    $"'sectionOrder[{index}]' must contain exactly one of 'page' or 'pages'.");
                hasLayoutErrors = true;
            }

            var firstPage = 0;
            var lastPage = 0;
            var validPages = false;
            if (hasPage && !hasPages)
            {
                if (pageElement.ValueKind != JsonValueKind.Number
                    || !pageElement.TryGetInt32(out firstPage)
                    || firstPage <= 0)
                {
                    errors.Add(
                        $"'sectionOrder[{index}].page' must be a positive 32-bit integer.");
                    hasLayoutErrors = true;
                }
                else
                {
                    lastPage = firstPage;
                    validPages = true;
                }
            }
            else if (hasPages && !hasPage)
            {
                if (!TryParsePageRange(pagesElement, out firstPage, out lastPage))
                {
                    errors.Add(
                        $"'sectionOrder[{index}].pages' must be an inclusive 'start-end' range of positive 32-bit integers where start is less than end.");
                    hasLayoutErrors = true;
                }
                else
                {
                    validPages = true;
                }
            }

            var blockSections = ImmutableArray.CreateBuilder<Section>();
            var validSections = true;
            if (!properties.TryGetValue("sections", out var sectionsElement)
                || sectionsElement.ValueKind != JsonValueKind.Array)
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
                    if (sectionEntry.ValueKind != JsonValueKind.String
                        || !TryParseSection(sectionEntry.GetString(), out var section))
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

            if (validPages)
            {
                if (firstPage != expectedFirstPage)
                {
                    errors.Add(
                        $"'sectionOrder[{index}]' starts at page {firstPage}, but ordered contiguous coverage requires page {expectedFirstPage}; gaps, overlaps, and unordered entries are not allowed.");
                    hasLayoutErrors = true;
                }

                try
                {
                    if (index < entries.Count - 1)
                    {
                        expectedFirstPage = checked(lastPage + 1);
                    }
                }
                catch (OverflowException)
                {
                    errors.Add(
                        $"'sectionOrder[{index}]' cannot end at page {lastPage} because no following contiguous page can be represented.");
                    hasLayoutErrors = true;
                }
            }

            if (validPages && validSections && blockSections.Count > 0)
            {
                blocks.Add(new(firstPage, lastPage, blockSections.DrainToImmutable()));
            }
        }

        CvPageLayout? pageLayout = null;
        if (!hasLayoutErrors && blocks.Count == entries.Count)
        {
            pageLayout = new(blocks.DrainToImmutable());
        }

        return new(
            flattenedSections.DrainToImmutable(),
            pageLayout,
            IsExplicit: true);
    }

    private static bool TryParsePageRange(
        JsonElement element,
        out int firstPage,
        out int lastPage)
    {
        firstPage = 0;
        lastPage = 0;
        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var value = element.GetString();
        if (value is null)
        {
            return false;
        }

        var separator = value.IndexOf('-');
        return separator > 0
            && separator == value.LastIndexOf('-')
            && separator < value.Length - 1
            && int.TryParse(
                value.AsSpan(0, separator),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out firstPage)
            && int.TryParse(
                value.AsSpan(separator + 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out lastPage)
            && firstPage > 0
            && lastPage > 0
            && firstPage < lastPage;
    }

    private static bool TryParseSection(string? value, out Section section)
        => Enum.TryParse(value, ignoreCase: false, out section)
           && Enum.IsDefined(section);

    private readonly record struct ParsedSectionOrder(
        ImmutableArray<Section> SectionOrder,
        CvPageLayout? PageLayout,
        bool IsExplicit);
}

public static class CvSelectionConfigurationLoader
{
    public static async Task<CvSelectionConfiguration> LoadAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new CvConfigurationException($"Configuration file was not found: '{fullPath}'.");
        }

        try
        {
            await using var input = File.OpenRead(fullPath);
            var json = await JsonSerializer.DeserializeAsync<JsonCvSelectionConfiguration>(
                input,
                JsonOptions,
                cancellationToken);
            if (json is null)
            {
                throw new CvConfigurationException("The configuration file must contain a JSON object.");
            }

            return json.ToDomain();
        }
        catch (JsonException ex)
        {
            throw new CvConfigurationException(
                $"Configuration file '{fullPath}' is invalid: {ex.Message}",
                ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<Section>(allowIntegerValues: false),
        },
    };
}

public sealed class CvSelectionConfiguration
{
    internal CvSelectionConfiguration(
        CvPageCount pageCount,
        ImmutableArray<RequiredTagConfiguration> requiredTags,
        ImmutableArray<string> skills,
        ImmutableArray<string> technologies,
        MmrConfiguration mmr,
        SelectionConfiguration selection,
        ImmutableArray<Section> sectionOrder,
        CvPageLayout? pageLayout = null)
    {
        PageCount = pageCount;
        RequiredTags = requiredTags;
        Skills = skills;
        Technologies = technologies;
        Mmr = mmr;
        Selection = selection;
        SectionOrder = sectionOrder;
        PageLayout = pageLayout;
    }

    public CvPageCount PageCount { get; }

    public ImmutableArray<RequiredTagConfiguration> RequiredTags { get; }

    public ImmutableArray<string> Skills { get; }

    public ImmutableArray<string> Technologies { get; }

    public MmrConfiguration Mmr { get; }

    public SelectionConfiguration Selection { get; }

    public ImmutableArray<Section> SectionOrder { get; }

    public CvPageLayout? PageLayout { get; }

    public ConfiguredCvSearch BuildSearch(TagsDatabase tagsDatabase)
    {
        ArgumentNullException.ThrowIfNull(tagsDatabase);
        var tagInputs = RequiredTags
            .Select(tag => (tag.Name, tag.Weight))
            .ToArray();

        WeightedTags weightedTags;
        var unknownTags = new List<string>();
        foreach (var tag in RequiredTags)
        {
            try
            {
                _ = tagsDatabase.Find(tag.Name);
            }
            catch (InvalidOperationException)
            {
                unknownTags.Add($"Required tag '{tag.Name}' was not found in the tag database.");
            }
        }

        if (unknownTags.Count > 0)
        {
            throw new CvConfigurationException(unknownTags);
        }

        try
        {
            weightedTags = tagsDatabase.Weighted(tagInputs);
        }
        catch (InvalidOperationException ex)
        {
            throw new CvConfigurationException(ex.Message, ex);
        }

        var mmr = new MmrOptions(
            Mmr.RelevanceWeight,
            Mmr.SaturationQuota,
            Mmr.SaturationPenalty);
        try
        {
            mmr.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new CvConfigurationException($"Invalid MMR configuration: {ex.Message}", ex);
        }

        var educationKey = new ExperienceKey("Education");
        var workKey = new ExperienceKey("Work");
        var personalProjectsKey = new ExperienceKey("PersonalProjects");

        var builder = new SearchBuilder();
        builder.Tags(weightedTags);
        builder.Mmr(mmr);
        builder.ConfigureDefaults(Selection.Default.Apply);
        builder.Configure(
            educationKey,
            predicate: static experience => experience.Type.IsDegree(),
            Selection.Education.Apply);
        builder.Configure(
            workKey,
            predicate: static experience => experience.Type == ExperienceType.Job,
            options =>
            {
                Selection.WorkExperience.Apply(options);
                // Keep every job heading, but let its bullets compete globally.
                // A job without a selected bullet is still rendered as an empty list.
                options.IncludeEmptyLists = true;
                options.PreserveOneItemPerList = false;
            });
        builder.Configure(
            personalProjectsKey,
            predicate: static experience => experience.Type == ExperienceType.Project,
            options =>
            {
                Selection.PersonalProjects.Apply(options);
                options.PreserveOneItemPerList = false;
            });

        try
        {
            return new(
                builder.Build(),
                new(educationKey, workKey, personalProjectsKey),
                Skills.Select(static skill => new RegularString(skill)).ToImmutableArray(),
                Technologies.Select(static technology => new RegularString(technology)).ToImmutableArray(),
                SectionOrder,
                PageCount,
                PageLayout);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new CvConfigurationException($"Invalid selection configuration: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new CvConfigurationException($"Invalid selection configuration: {ex.Message}", ex);
        }
    }
}

public sealed class RequiredTagConfiguration
{
    public required string Name { get; init; }
    public required float Weight { get; init; }
}

public sealed class MmrConfiguration
{
    public required float RelevanceWeight { get; init; }
    public required int SaturationQuota { get; init; }
    public required float SaturationPenalty { get; init; }

    public void CollectValidationErrors(List<string> errors)
    {
        if (!float.IsFinite(RelevanceWeight) || RelevanceWeight is < 0 or > 1)
        {
            errors.Add("'mmr.relevanceWeight' must be finite and between 0 and 1.");
        }

        if (SaturationQuota < 1)
        {
            errors.Add("'mmr.saturationQuota' must be at least 1.");
        }

        if (!float.IsFinite(SaturationPenalty) || SaturationPenalty < 0)
        {
            errors.Add("'mmr.saturationPenalty' must be finite and non-negative.");
        }
    }
}

public sealed class SelectionConfiguration
{
    public SelectionOptionsConfiguration Default { get; init; } = new();
    public SelectionOptionsConfiguration Education { get; init; } = new();
    public SelectionOptionsConfiguration WorkExperience { get; init; } = new();
    public SelectionOptionsConfiguration PersonalProjects { get; init; } = new();

    public void CollectValidationErrors(List<string> errors)
    {
        if (Default is null)
        {
            errors.Add("'selection.default' must be an object when supplied.");
        }

        Default?.CollectValidationErrors("selection.default", errors);

        if (Education is null)
        {
            errors.Add("'selection.education' must be an object when supplied.");
        }

        if (WorkExperience is null)
        {
            errors.Add("'selection.workExperience' must be an object when supplied.");
        }

        if (PersonalProjects is null)
        {
            errors.Add("'selection.personalProjects' must be an object when supplied.");
        }

        Education?.CollectValidationErrors("selection.education", errors);
        WorkExperience?.CollectValidationErrors("selection.workExperience", errors);
        PersonalProjects?.CollectValidationErrors("selection.personalProjects", errors);
    }
}

public static class SelectionConfigurationExtensions
{
    extension(SelectionConfiguration configuration)
    {
        public SelectionOptionsEnumerable Options => new(configuration);
    }
}

public readonly struct SelectionOptionsEnumerable(SelectionConfiguration configuration)
    : IEnumerable<SelectionOptionsConfiguration>
{
    public IEnumerator<SelectionOptionsConfiguration> GetEnumerator()
    {
        yield return configuration.Default;
        yield return configuration.Education;
        yield return configuration.WorkExperience;
        yield return configuration.PersonalProjects;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class SelectionOptionsConfiguration
{
    public int MinTotalItemBudget { get; init; } = 0;
    public int? TotalItemBudget { get; init; }
    public float ScoreLowerBound { get; init; }
    public float RecencyBoost { get; init; }

    public void Apply(SearchPredicateOptions options)
    {
        options.MinTotalItemBudget = MinTotalItemBudget;
        options.TotalItemBudget = TotalItemBudget ?? int.MaxValue;
        options.ScoreLowerBound = ScoreLowerBound;
        options.RecencyBoost = RecencyBoost;
    }

    public void CollectValidationErrors(string path, List<string> errors)
    {
        var options = new SearchPredicateOptions();
        Apply(options);
        foreach (var error in SearchPredicateOptionsValidator.ValidateOptions(options))
        {
            var propertyName = JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName);
            errors.Add($"'{path}.{propertyName}' {error.Message}");
        }
    }
}

public sealed record ConfiguredCvSearch(
    ExperienceSearch Search,
    CvExperienceSectionBindings Sections,
    ImmutableArray<RegularString> Skills,
    ImmutableArray<RegularString> Technologies,
    ImmutableArray<Section> SectionOrder,
    CvPageCount PageCount,
    CvPageLayout? PageLayout);
