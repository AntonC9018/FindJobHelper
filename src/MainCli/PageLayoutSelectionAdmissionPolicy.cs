using System.Collections.Immutable;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

internal sealed class PageLayoutSelectionAdmissionPolicy : ISelectionAdmissionPolicy
{
    private readonly CvMeasurementSnapshot _measurements;
    private readonly CvExperienceSectionBindings _sectionBindings;
    private readonly ImmutableArray<Section> _sectionOrder;
    private readonly HashSet<Section> _renderedSections;
    private readonly Dictionary<ExperienceList, ExperienceListId> _listIds;
    private readonly Dictionary<ExperienceListItem, ExperienceItemId> _itemIds;
    private readonly Dictionary<Section, long> _sectionHeights = new();
    private readonly HashSet<Section> _visibleSections = [];
    private readonly HashSet<ExperienceList> _visibleLists = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ExperienceList> _itemizedLists = new(ReferenceEqualityComparer.Instance);
    private PageLayoutResult _currentLayout;

    public PageLayoutSelectionAdmissionPolicy(
        ExperienceDatabase database,
        CvMeasurementSnapshot measurements,
        CvExperienceSectionBindings sectionBindings,
        ImmutableArray<Section> sectionOrder,
        CvPageCount pageCount = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(measurements);
        ArgumentNullException.ThrowIfNull(sectionBindings);
        if (sectionOrder.Distinct().Count() != sectionOrder.Length)
        {
            throw new ArgumentException("The CV section order cannot contain duplicates.", nameof(sectionOrder));
        }

        _measurements = measurements;
        _sectionBindings = sectionBindings;
        _sectionOrder = sectionOrder;
        _renderedSections = sectionOrder.ToHashSet();
        PageCount = pageCount;

        _listIds = new(ReferenceEqualityComparer.Instance);
        foreach (var identified in database.EnumerateExperienceLists())
        {
            _listIds.Add(identified.Value, identified.Id);
            var headingHeight = measurements.GetExperienceHeadingHeight(identified.Id).ScaledPoints;
            var chromeHeight = measurements.GetExperienceChromeHeight(identified.Id).ScaledPoints;
            if (headingHeight < 0 || chromeHeight < 0)
            {
                throw new CvMeasurementInvariantException(
                    $"Measured experience heights for '{identified.Value.Title}' cannot be negative.");
            }
            if (chromeHeight < headingHeight)
            {
                throw new CvMeasurementInvariantException(
                    $"Measured experience chrome for '{identified.Value.Title}' is smaller than its heading.");
            }
        }

        _itemIds = new(ReferenceEqualityComparer.Instance);
        foreach (var identified in database.EnumerateExperienceItems())
        {
            _itemIds.Add(identified.Value, identified.Id);
            if (measurements.GetExperienceItemHeight(identified.Id).ScaledPoints < 0)
            {
                throw new CvMeasurementInvariantException(
                    $"Measured height for experience item '{identified.Id}' cannot be negative.");
            }
        }

        var dynamicSections = sectionBindings.Sections.ToHashSet();
        foreach (var section in sectionOrder)
        {
            var currentChrome = measurements.GetCurrentPageSectionChromeHeight(section).ScaledPoints;
            var freshChrome = measurements.GetFreshPageSectionChromeHeight(section).ScaledPoints;
            if (currentChrome < 0 || freshChrome < 0)
            {
                throw new CvMeasurementInvariantException(
                    $"Measured section chrome for '{section}' cannot be negative.");
            }
            if (freshChrome < currentChrome)
            {
                throw new CvMeasurementInvariantException(
                    $"Fresh-page wrapper for section '{section}' is shorter than its current-page wrapper.");
            }

            if (!dynamicSections.Contains(section))
            {
                var height = measurements.GetCurrentPageCompleteSectionHeight(section).ScaledPoints;
                if (height < 0)
                {
                    throw new CvMeasurementInvariantException(
                        $"Measured complete height for section '{section}' cannot be negative.");
                }
                if (height > 0)
                {
                    _sectionHeights.Add(section, height);
                }
            }
        }

        _currentLayout = CalculateLayout();
        if (!_currentLayout.Fits)
        {
            throw new FixedCvContentLayoutException(
                $"Fixed CV content cannot satisfy the {LayoutDescription}: {_currentLayout.Failure!.Message}");
        }
    }

    public bool PrioritizeMinimums => true;

    public CvPageCount PageCount { get; }

    internal int PredictedPageCount => _currentLayout.PageCount;

    internal LatexHeight CurrentHeight => new(checked(
        _measurements.DocumentHeader.ScaledPoints
        + _measurements.DocumentFooter.ScaledPoints
        + _sectionHeights.Values.Sum()));

    public SelectionAdmissionDecision Evaluate(SelectionAdmission admission)
    {
        var section = GetSection(admission.Group);
        var additionalHeight = GetAdditionalHeight(admission);
        var currentHeight = _sectionHeights.GetValueOrDefault(section);
        var proposedHeight = checked(currentHeight + additionalHeight);
        var layout = CalculateLayout(section, proposedHeight);
        if (layout.Fits)
        {
            return SelectionAdmissionDecision.Accepted;
        }

        return SelectionAdmissionDecision.Reject(
            $"the {LayoutDescription} cannot include it: {layout.Failure!.Message}");
    }

    public void Commit(SelectionAdmission admission)
    {
        var section = GetSection(admission.Group);
        var additionalHeight = GetAdditionalHeight(admission);
        if (_renderedSections.Contains(section))
        {
            _sectionHeights[section] = checked(
                _sectionHeights.GetValueOrDefault(section) + additionalHeight);
            _visibleSections.Add(section);
            _visibleLists.Add(admission.List);
            if (admission.Items.Count > 0)
            {
                _itemizedLists.Add(admission.List);
            }
        }

        _currentLayout = CalculateLayout();
        if (!_currentLayout.Fits)
        {
            throw new CvSelectionCommitException(
                $"Committed selection no longer satisfies the {LayoutDescription}: {_currentLayout.Failure!.Message}");
        }
    }

    internal void RequireExactPageCount()
    {
        if (PageCount.ExactCount is { } required
            && _currentLayout.PageCount != required)
        {
            throw new PredictedPageCountMismatchException(required, _currentLayout.PageCount);
        }
    }

    private PageLayoutResult CalculateLayout(
        Section? overriddenSection = null,
        long overriddenHeight = 0)
    {
        var sections = new List<PageLayoutSection>(_sectionOrder.Length);
        foreach (var section in _sectionOrder)
        {
            var currentHeight = section == overriddenSection
                ? overriddenHeight
                : _sectionHeights.GetValueOrDefault(section);
            if (currentHeight == 0)
            {
                continue;
            }

            var current = new LatexHeight(currentHeight);
            var fresh = _measurements.DeriveFreshPageSectionHeight(section, current);
            sections.Add(new(section, current, fresh));
        }

        return PageLayoutCalculator.Calculate(
            _measurements.UsablePageHeight,
            _measurements.DocumentHeader,
            _measurements.DocumentFooter,
            sections,
            PageCount);
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
            height = checked(
                height + _measurements.GetCurrentPageSectionChromeHeight(section).ScaledPoints);
        }

        if (!_listIds.TryGetValue(admission.List, out var listId))
        {
            throw new CvMeasurementInvariantException(
                "Selected experience list was not found in the measured database.");
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
                throw new CvMeasurementInvariantException(
                    "Selected experience item was not found in the measured database.");
            }
            height = checked(height + _measurements.GetExperienceItemHeight(itemId).ScaledPoints);
        }

        return height;
    }

    private Section GetSection(ExperienceSelectionGroup group)
        => _sectionBindings.GetSection(group.Key);

    private string LayoutDescription => PageCount.ExactCount switch
    {
        1 => "configured one-page layout",
        { } count => $"configured {count}-page layout",
        null => "legacy unrestricted page layout",
    };
}
