using System.Collections.Immutable;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

internal sealed class PageHeightSelectionAdmissionPolicy : ISelectionAdmissionPolicy
{
    private readonly CvMeasurementSnapshot _measurements;
    private readonly CvExperienceSectionBindings _sectionBindings;
    private readonly HashSet<Section> _renderedSections;
    private readonly Dictionary<ExperienceList, ExperienceListId> _listIds;
    private readonly Dictionary<ExperienceListItem, ExperienceItemId> _itemIds;
    private readonly HashSet<Section> _visibleSections = [];
    private readonly HashSet<ExperienceList> _visibleLists = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ExperienceList> _itemizedLists = new(ReferenceEqualityComparer.Instance);
    private long _currentHeight;

    public PageHeightSelectionAdmissionPolicy(
        ExperienceDatabase database,
        CvMeasurementSnapshot measurements,
        CvExperienceSectionBindings sectionBindings,
        ImmutableArray<Section> sectionOrder)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(sectionBindings);

        _measurements = measurements;
        _sectionBindings = sectionBindings;
        _renderedSections = sectionOrder.ToHashSet();
        _listIds = new(ReferenceEqualityComparer.Instance);
        foreach (var identified in database.EnumerateExperienceLists())
        {
            _listIds.Add(identified.Value, identified.Id);
            var headingHeight = measurements.GetExperienceHeadingHeight(identified.Id).ScaledPoints;
            var chromeHeight = measurements.GetExperienceChromeHeight(identified.Id).ScaledPoints;
            if (chromeHeight < headingHeight)
            {
                throw new InvalidOperationException(
                    $"Measured experience chrome for '{identified.Value.Title}' is smaller than its heading.");
            }
        }

        _itemIds = new(ReferenceEqualityComparer.Instance);
        foreach (var identified in database.EnumerateExperienceItems())
        {
            _itemIds.Add(identified.Value, identified.Id);
        }

        _currentHeight = measurements.DocumentChrome.ScaledPoints;
        var dynamicSections = sectionBindings.Sections.ToHashSet();
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

    public bool CanAccept(SelectionAdmission admission)
    {
        var additionalHeight = GetAdditionalHeight(admission);
        return checked(_currentHeight + additionalHeight) <= _measurements.UsablePageHeight.ScaledPoints;
    }

    public void Commit(SelectionAdmission admission)
    {
        _currentHeight = checked(_currentHeight + GetAdditionalHeight(admission));
        var section = GetSection(admission.Group);
        if (_renderedSections.Contains(section))
        {
            _visibleSections.Add(section);
            _visibleLists.Add(admission.List);
            if (admission.Items.Count > 0)
            {
                _itemizedLists.Add(admission.List);
            }
        }
    }

    private long GetAdditionalHeight(SelectionAdmission admission)
    {
        var section = GetSection(admission.Group);
        if (!_renderedSections.Contains(section))
        {
            return 0;
        }

        long height = 0;
        if (!_visibleSections.Contains(section))
        {
            height = checked(height + _measurements.GetSectionChromeHeight(section).ScaledPoints);
        }

        if (!_listIds.TryGetValue(admission.List, out var listId))
        {
            throw new InvalidOperationException("Selected experience list was not found in the measured database.");
        }

        var hasItems = admission.Items.Count > 0;
        if (!_visibleLists.Contains(admission.List))
        {
            var listHeight = hasItems
                ? _measurements.GetExperienceChromeHeight(listId).ScaledPoints
                : _measurements.GetExperienceHeadingHeight(listId).ScaledPoints;
            height = checked(height + listHeight);
        }
        else if (hasItems && !_itemizedLists.Contains(admission.List))
        {
            var chromeHeight = _measurements.GetExperienceChromeHeight(listId).ScaledPoints;
            var headingHeight = _measurements.GetExperienceHeadingHeight(listId).ScaledPoints;
            height = checked(height + chromeHeight - headingHeight);
        }

        foreach (var item in admission.Items)
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
        return _sectionBindings.GetSection(group.Key);
    }
}
