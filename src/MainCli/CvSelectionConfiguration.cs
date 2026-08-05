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
    public required SectionOrderCollection SectionOrder { get; init; }

    public CvSelectionConfiguration ToDomain()
    {
        var errors = new List<string>();
        errors.AddRange(SectionOrder.ValidationErrors);
        var pageLayout = SectionOrder.PageLayout;
        if (SectionOrder.IsExplicit)
        {
            if (IsLimitToOnePageSpecified)
            {
                errors.Add(
                    "'limitToOnePage' cannot be supplied with object-form 'sectionOrder' because the page count is derived from the layout.");
            }
            if (IsPageCountSpecified
                && PageCount is > 0
                && pageLayout is not null
                && PageCount.Value != pageLayout.PageCount)
            {
                errors.Add(
                    $"'pageCount' is {PageCount.Value}, but object-form 'sectionOrder' defines {pageLayout.PageCount} page(s).");
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

        return new(
            pageLayout is null
                ? ResolvePageCount()
                : CvPageCount.Exact(pageLayout.PageCount),
            [.. RequiredTags!],
            [.. Skills!],
            [.. Technologies!],
            Mmr!,
            Selection!,
            SectionOrder.Sections,
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

internal struct SelectionOptionsFieldMask
{
    public bool MinTotalItemBudget { get; set; }
    public bool TotalItemBudget { get; set; }
    public bool ScoreLowerBound { get; set; }
    public bool RecencyBoost { get; set; }
    public bool DirectMatchBoost { get; set; }
}

public sealed class SelectionOptionsConfiguration
{
    private int _minTotalItemBudget;
    private int? _totalItemBudget;
    private float _scoreLowerBound;
    private float _recencyBoost;
    private float? _directMatchBoost = 0;

    internal SelectionOptionsFieldMask SpecifiedFields;

    public int MinTotalItemBudget
    {
        get => _minTotalItemBudget;
        init
        {
            _minTotalItemBudget = value;
            SpecifiedFields.MinTotalItemBudget = true;
        }
    }

    public int? TotalItemBudget
    {
        get => _totalItemBudget;
        init
        {
            _totalItemBudget = value;
            SpecifiedFields.TotalItemBudget = true;
        }
    }

    public float ScoreLowerBound
    {
        get => _scoreLowerBound;
        init
        {
            _scoreLowerBound = value;
            SpecifiedFields.ScoreLowerBound = true;
        }
    }

    public float RecencyBoost
    {
        get => _recencyBoost;
        init
        {
            _recencyBoost = value;
            SpecifiedFields.RecencyBoost = true;
        }
    }

    public float? DirectMatchBoost
    {
        get => _directMatchBoost;
        init
        {
            _directMatchBoost = value;
            SpecifiedFields.DirectMatchBoost = true;
        }
    }

    public void Apply(SearchPredicateOptions options)
    {
        if (SpecifiedFields.MinTotalItemBudget)
        {
            options.MinTotalItemBudget = MinTotalItemBudget;
        }
        if (SpecifiedFields.TotalItemBudget)
        {
            options.TotalItemBudget = TotalItemBudget ?? int.MaxValue;
        }
        if (SpecifiedFields.ScoreLowerBound)
        {
            options.ScoreLowerBound = ScoreLowerBound;
        }
        if (SpecifiedFields.RecencyBoost)
        {
            options.RecencyBoost = new(RecencyBoost);
        }
        if (SpecifiedFields.DirectMatchBoost && DirectMatchBoost is { } directMatchBoost)
        {
            options.DirectMatchBoost = new(directMatchBoost);
        }
    }

    public void CollectValidationErrors(string path, List<string> errors)
    {
        var options = new SearchPredicateOptions
        {
            MinTotalItemBudget = MinTotalItemBudget,
            TotalItemBudget = TotalItemBudget ?? int.MaxValue,
            ScoreLowerBound = ScoreLowerBound,
        };
        foreach (var error in SearchPredicateOptionsValidator.ValidateOptions(options))
        {
            var propertyName = JsonNamingPolicy.CamelCase.ConvertName(error.PropertyName);
            errors.Add($"'{path}.{propertyName}' {error.Message}");
        }

        AddBoostValidationError(nameof(RecencyBoost), RecencyBoost);
        if (DirectMatchBoost is { } directMatchBoost)
        {
            AddBoostValidationError(nameof(DirectMatchBoost), directMatchBoost);
        }

        void AddBoostValidationError(string propertyName, float value)
        {
            if (ScoreBoost.IsValid(value))
            {
                return;
            }

            var jsonPropertyName = JsonNamingPolicy.CamelCase.ConvertName(propertyName);
            errors.Add(
                $"'{path}.{jsonPropertyName}' must be finite and non-negative.");
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
