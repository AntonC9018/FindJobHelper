using System.Collections.Immutable;
using System.Text.Json.Serialization;
using FindJobHelper.Configuration;

namespace FindJobHelper.Configuration.Json;

public sealed class JsonCvSelectionConfiguration
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
    public MmrConfiguration? Mmr { get; init; }
    public required SelectionConfiguration Selection { get; init; }
    public required SectionOrderCollection SectionOrder { get; init; }
    public string? Profession { get; init; }
    public JsonHeaderConfiguration? Header { get; init; }

    public CvSelectionConfiguration ToDomain()
    {
        var errors = new List<string>();
        errors.AddRange(SectionOrder.ValidationErrors);
        var pageLayout = SectionOrder.PageLayout;
        var headerLinkOrder = MapHeaderLinkOrder(errors);
        CollectPageConfigurationErrors();

        void CollectPageConfigurationErrors()
        {
            if (!SectionOrder.IsExplicit)
            {
                CollectNonExplicitPageConfigurationErrors();
                return;
            }

            if (IsLimitToOnePageSpecified)
            {
                errors.Add(
                    "'limitToOnePage' cannot be supplied with object-form 'sectionOrder' because the page count is derived from the layout.");
            }
            if (TryGetExplicitPageCountConflict(
                    out var configuredPageCount,
                    out var layoutPageCount))
            {
                errors.Add(
                    $"'pageCount' is {configuredPageCount}, but object-form 'sectionOrder' defines {layoutPageCount} page(s).");
            }
        }

        void CollectNonExplicitPageConfigurationErrors()
        {
            if (HasConflictingPageCountOptions())
            {
                errors.Add("'limitToOnePage' and 'pageCount' cannot both be supplied.");
            }
        }

        if (HasInvalidPageCount())
        {
            errors.Add("'pageCount' must be a positive 32-bit integer.");
        }

        if (RequiredTags is not { Count: > 0 })
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

        if (Skills is not { Count: > 0 })
        {
            errors.Add("'skills' must contain at least one item.");
        }
        else if (Skills.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("'skills' cannot contain blank items.");
        }

        if (Technologies is not { Count: > 0 })
        {
            errors.Add("'technologies' must contain at least one item.");
        }
        else if (Technologies.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add("'technologies' cannot contain blank items.");
        }

        Mmr?.CollectValidationErrors(errors);

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

        var pageCount = pageLayout is null
            ? ResolvePageCount()
            : CvPageCount.Exact(pageLayout.PageCount);
        return new(
            pageCount: pageCount,
            requiredTags: [.. RequiredTags!],
            skills: [.. Skills!],
            technologies: [.. Technologies!],
            mmr: Mmr?.ToDomain() ?? MmrOptions.Default,
            selection: Selection!,
            sectionOrder: SectionOrder.Sections,
            profession: Profession,
            headerLinkOrder: headerLinkOrder,
            pageLayout: pageLayout);

        bool TryGetExplicitPageCountConflict(
            out int configuredPageCount,
            out int layoutPageCount)
        {
            configuredPageCount = default;
            layoutPageCount = default;
            if (!IsPageCountSpecified)
            {
                return false;
            }
            if (PageCount is not { } pageCountValue)
            {
                return false;
            }
            if (pageCountValue <= 0)
            {
                return false;
            }
            if (pageLayout is null)
            {
                return false;
            }

            configuredPageCount = pageCountValue;
            layoutPageCount = pageLayout.PageCount;
            return configuredPageCount != layoutPageCount;
        }

        bool HasConflictingPageCountOptions()
        {
            if (!IsLimitToOnePageSpecified)
            {
                return false;
            }

            return IsPageCountSpecified;
        }

        bool HasInvalidPageCount()
        {
            if (!IsPageCountSpecified)
            {
                return false;
            }

            return PageCount is null or <= 0;
        }
    }

    private ImmutableArray<HeaderLinkName> MapHeaderLinkOrder(List<string> errors)
    {
        var configuredOrder = Header?.Links?.Order;
        if (configuredOrder is null)
        {
            return default;
        }

        var mappedOrder = configuredOrder
            .Select(MapHeaderLinkName)
            .ToImmutableArray();
        var uniqueNames = new HashSet<HeaderLinkName>();
        foreach (var name in mappedOrder)
        {
            if (uniqueNames.Add(name))
            {
                continue;
            }

            errors.Add($"Header link '{name}' is configured more than once in 'header.links.order'.");
        }

        return mappedOrder;
    }

    private static HeaderLinkName MapHeaderLinkName(string name)
    {
        if (string.Equals(name, HeaderLinkName.GitHub.Value, StringComparison.OrdinalIgnoreCase))
        {
            return HeaderLinkName.GitHub;
        }
        if (string.Equals(name, HeaderLinkName.LinkedIn.Value, StringComparison.OrdinalIgnoreCase))
        {
            return HeaderLinkName.LinkedIn;
        }
        if (string.Equals(name, HeaderLinkName.YouTube.Value, StringComparison.OrdinalIgnoreCase))
        {
            return HeaderLinkName.YouTube;
        }
        if (string.Equals(name, HeaderLinkName.Portfolio.Value, StringComparison.OrdinalIgnoreCase))
        {
            return HeaderLinkName.Portfolio;
        }

        return new(name);
    }

    private CvPageCount ResolvePageCount()
    {
        if (IsPageCountSpecified)
        {
            return CvPageCount.Exact(PageCount!.Value);
        }

        if (ShouldUseUnrestrictedPageCount())
        {
            return CvPageCount.Unrestricted;
        }

        return CvPageCount.OnePage;

        bool ShouldUseUnrestrictedPageCount()
        {
            if (!IsLimitToOnePageSpecified)
            {
                return false;
            }

            return !LimitToOnePage;
        }
    }

}

public sealed class JsonHeaderConfiguration
{
    public JsonHeaderLinksConfiguration? Links { get; init; }
}

public sealed class JsonHeaderLinksConfiguration
{
    public List<string>? Order { get; init; }
}

public sealed class MmrConfiguration
{
    public float? RelevanceWeight { get; init; }
    public int? SaturationQuota { get; init; }
    public float? SaturationPenalty { get; init; }

    public void CollectValidationErrors(List<string> errors)
    {
        if (HasInvalidRelevanceWeight())
        {
            errors.Add("'mmr.relevanceWeight' must be finite and between 0 and 1.");
        }

        if (SaturationQuota is < 1)
        {
            errors.Add("'mmr.saturationQuota' must be at least 1.");
        }

        if (HasInvalidSaturationPenalty())
        {
            errors.Add("'mmr.saturationPenalty' must be finite and non-negative.");
        }

        bool HasInvalidRelevanceWeight()
        {
            if (RelevanceWeight is not { } relevanceWeight)
            {
                return false;
            }

            if (!float.IsFinite(relevanceWeight))
            {
                return true;
            }

            return relevanceWeight is < 0 or > 1;
        }

        bool HasInvalidSaturationPenalty()
        {
            if (SaturationPenalty is not { } saturationPenalty)
            {
                return false;
            }

            if (!float.IsFinite(saturationPenalty))
            {
                return true;
            }

            return saturationPenalty < 0;
        }
    }

    public MmrOptions ToDomain()
    {
        var relevanceWeight = RelevanceWeight ?? MmrOptions.Default.RelevanceWeight;
        var saturationQuota = SaturationQuota ?? MmrOptions.Default.SaturationQuota;
        var saturationPenalty = SaturationPenalty ?? MmrOptions.Default.SaturationPenalty;
        return new(
            RelevanceWeight: relevanceWeight,
            SaturationQuota: saturationQuota,
            SaturationPenalty: saturationPenalty);
    }
}
