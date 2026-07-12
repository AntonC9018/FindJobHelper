using System.Collections.Immutable;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

internal sealed class PageHeightSelectionAdmissionPolicy : ISelectionAdmissionPolicy
{
    private readonly CvMeasurementSnapshot _measurements;
    private readonly IReadOnlyDictionary<ExperienceKey, Section> _groupSections;
    private readonly HashSet<Section> _renderedSections;
    private readonly Dictionary<ExperienceList, ExperienceListId> _listIds;
    private readonly Dictionary<ExperienceListItem, ExperienceItemId> _itemIds;
    private readonly HashSet<Section> _visibleSections = [];
    private readonly HashSet<ExperienceList> _visibleLists = new(ReferenceEqualityComparer.Instance);
    private long _currentHeight;

    public PageHeightSelectionAdmissionPolicy(
        ExperienceDatabase database,
        CvMeasurementSnapshot measurements,
        IReadOnlyDictionary<ExperienceKey, Section> groupSections,
        ImmutableArray<Section> sectionOrder)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(groupSections);

        _measurements = measurements;
        _groupSections = groupSections;
        _renderedSections = sectionOrder.ToHashSet();
        _listIds = new(ReferenceEqualityComparer.Instance);
        foreach (var identified in database.EnumerateExperienceLists())
        {
            _listIds.Add(identified.Value, identified.Id);
        }

        _itemIds = new(ReferenceEqualityComparer.Instance);
        foreach (var identified in database.EnumerateExperienceItems())
        {
            _itemIds.Add(identified.Value, identified.Id);
        }

        _currentHeight = measurements.DocumentChrome.ScaledPoints;
        var dynamicSections = groupSections.Values.ToHashSet();
        foreach (var section in sectionOrder)
        {
            if (!dynamicSections.Contains(section))
            {
                _currentHeight = checked(
                    _currentHeight + measurements.GetCompleteSectionHeight(section).ScaledPoints);
            }
        }

        if (_currentHeight > measurements.UsablePageHeight.ScaledPoints)
        {
            throw new InvalidOperationException(
                $"Fixed CV content requires {_currentHeight}sp, exceeding the usable one-page height of {measurements.UsablePageHeight.ScaledPoints}sp.");
        }
    }

    public bool PrioritizeMinimums => true;

    internal LatexHeight CurrentHeight => new(_currentHeight);

    public bool CanAccept(
        ExperienceSelectionGroup group,
        ExperienceList list,
        IReadOnlyList<ExperienceListItem> items)
    {
        var additionalHeight = GetAdditionalHeight(group, list, items);
        return checked(_currentHeight + additionalHeight) <= _measurements.UsablePageHeight.ScaledPoints;
    }

    public void Commit(
        ExperienceSelectionGroup group,
        ExperienceList list,
        IReadOnlyList<ExperienceListItem> items)
    {
        _currentHeight = checked(_currentHeight + GetAdditionalHeight(group, list, items));
        var section = GetSection(group);
        if (_renderedSections.Contains(section))
        {
            _visibleSections.Add(section);
            _visibleLists.Add(list);
        }
    }

    private long GetAdditionalHeight(
        ExperienceSelectionGroup group,
        ExperienceList list,
        IReadOnlyList<ExperienceListItem> items)
    {
        var section = GetSection(group);
        if (!_renderedSections.Contains(section))
        {
            return 0;
        }

        long height = 0;
        if (!_visibleSections.Contains(section))
        {
            height = checked(height + _measurements.GetSectionChromeHeight(section).ScaledPoints);
        }

        if (!_visibleLists.Contains(list))
        {
            if (!_listIds.TryGetValue(list, out var listId))
            {
                throw new InvalidOperationException("Selected experience list was not found in the measured database.");
            }
            height = checked(height + _measurements.GetExperienceChromeHeight(listId).ScaledPoints);
        }

        foreach (var item in items)
        {
            if (!_itemIds.TryGetValue(item, out var itemId))
            {
                throw new InvalidOperationException("Selected experience item was not found in the measured database.");
            }
            height = checked(height + _measurements.GetExperienceItemHeight(itemId).ScaledPoints);
        }

        return height;
    }

    private Section GetSection(ExperienceSelectionGroup group)
    {
        if (_groupSections.TryGetValue(group.Key, out var section))
        {
            return section;
        }

        throw new InvalidOperationException(
            $"No CV section mapping was configured for experience group '{group.Key}'.");
    }
}
