using System.Collections.Immutable;
using FindJobHelper.Configuration;

namespace FindJobHelper.Configuration.Json;

public abstract class JsonCvDocumentConfiguration
{
    public required List<string> Skills { get; init; }

    public required List<string> Technologies { get; init; }

    public required SectionOrderCollection SectionOrder { get; init; }

    public string? Profession { get; init; }

    public JsonHeaderConfiguration? Header { get; init; }

    internal ImmutableArray<HeaderLinkName> CollectDocumentValidationErrors(
        List<string> errors)
    {
        CollectTextListErrors(Skills, "skills", errors);
        CollectTextListErrors(Technologies, "technologies", errors);
        if (SectionOrder is null)
        {
            errors.Add("'sectionOrder' is required.");
        }
        else
        {
            errors.AddRange(SectionOrder.ValidationErrors);
        }

        return MapHeaderLinkOrder(errors);
    }

    private static void CollectTextListErrors(
        List<string>? values,
        string propertyName,
        List<string> errors)
    {
        if (values is not { Count: > 0 })
        {
            errors.Add($"'{propertyName}' must contain at least one item.");
            return;
        }

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            errors.Add($"'{propertyName}' cannot contain blank items.");
        }
    }

    private ImmutableArray<HeaderLinkName> MapHeaderLinkOrder(List<string> errors)
    {
        var configuredOrder = Header?.Links?.Order;
        if (configuredOrder is null)
        {
            return default;
        }

        var hasBlank = false;
        var validEntries = new List<string>();
        foreach (var entry in configuredOrder)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                hasBlank = true;
                continue;
            }

            validEntries.Add(entry);
        }

        if (hasBlank)
        {
            errors.Add("'header.links.order' cannot contain blank items.");
        }

        var mappedOrder = validEntries
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
}
