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

public sealed class CvSelectionConfiguration
{
    public bool LimitToOnePage { get; init; } = true;
    public required List<RequiredTagConfiguration> RequiredTags { get; init; }
    public required List<string> Skills { get; init; }
    public required List<string> Technologies { get; init; }
    public required MmrConfiguration Mmr { get; init; }
    public required SelectionConfiguration Selection { get; init; }
    public required List<Section> SectionOrder { get; init; }

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
            var result = await JsonSerializer.DeserializeAsync<CvSelectionConfiguration>(
                input,
                JsonOptions,
                cancellationToken);
            if (result is null)
            {
                throw new CvConfigurationException("The configuration file must contain a JSON object.");
            }

            result.ValidateShape();
            return result;
        }
        catch (JsonException ex)
        {
            throw new CvConfigurationException(
                $"Configuration file '{fullPath}' is invalid: {ex.Message}",
                ex);
        }
    }

    public ConfiguredCvSearch BuildSearch(TagsDatabase tagsDatabase)
    {
        ArgumentNullException.ThrowIfNull(tagsDatabase);
        ValidateShape();

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
        // builder.ConfigureDefaults(Selection.Default.Apply);
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
                options.IncludeEmptyLists = true;
            });
        builder.Configure(
            personalProjectsKey,
            predicate: static experience => experience.Type == ExperienceType.Project,
            Selection.PersonalProjects.Apply);

        try
        {
            return new(
                builder.Build(),
                new(educationKey, workKey, personalProjectsKey),
                Skills.Select(static skill => new RegularString(skill)).ToImmutableArray(),
                Technologies.Select(static technology => new RegularString(technology)).ToImmutableArray(),
                [.. SectionOrder],
                LimitToOnePage);
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

    private void ValidateShape()
    {
        var errors = new List<string>();
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

        if (SectionOrder is null || SectionOrder.Count == 0)
        {
            errors.Add("'sectionOrder' must contain at least one section.");
        }
        else
        {
            var sections = new HashSet<Section>();
            foreach (var section in SectionOrder)
            {
                if (!Enum.IsDefined(section))
                {
                    errors.Add($"Section '{section}' is not valid.");
                }
                else if (!sections.Add(section))
                {
                    errors.Add($"Section '{section}' occurs more than once in 'sectionOrder'.");
                }
            }
        }

        if (errors.Count > 0)
        {
            throw new CvConfigurationException(errors);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<Section>(allowIntegerValues: false),
        },
    };
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
    public required SelectionOptionsConfiguration Education { get; init; }
    public required SelectionOptionsConfiguration WorkExperience { get; init; }
    public required SelectionOptionsConfiguration PersonalProjects { get; init; }

    public void CollectValidationErrors(List<string> errors)
    {
        if (Education is null || WorkExperience is null || PersonalProjects is null)
        {
            errors.Add("'selection' must contain 'education', 'workExperience', and 'personalProjects'.");
        }

        Education?.CollectValidationErrors("selection.education", errors);
        WorkExperience?.CollectValidationErrors("selection.workExperience", errors);
        PersonalProjects?.CollectValidationErrors("selection.personalProjects", errors);
    }
}

public sealed class SelectionOptionsConfiguration
{
    public int MinTotalItemBudget { get; init; } = 0;
    public int? TotalItemBudget { get; init; }
    public required float ScoreLowerBound { get; init; }
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
    bool LimitToOnePage);
