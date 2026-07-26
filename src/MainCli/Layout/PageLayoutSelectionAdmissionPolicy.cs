using System.Collections.Immutable;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

internal sealed class PageLayoutSelectionAdmissionPolicy : ISelectionAdmissionPolicy
{
    private readonly CvMeasurementSnapshot _measurements;
    private readonly CvExperienceSectionBindings _sectionBindings;
    private readonly ImmutableArray<Section> _sectionOrder;
    private readonly HashSet<Section> _renderedSections;
    private readonly HashSet<Section> _dynamicSections;
    private readonly CvPageLayout? _explicitPageLayout;
    private readonly Dictionary<ExperienceList, ExperienceListId> _listIds;
    private readonly Dictionary<ExperienceListItem, ExperienceItemId> _itemIds;
    private readonly Dictionary<ExperienceList, int> _listOrders;
    private readonly Dictionary<Section, long> _sectionHeights = new();
    private readonly Dictionary<ExperienceList, long> _eventHeights =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ExperienceList, Section> _eventSections =
        new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Section> _visibleSections = [];
    private readonly HashSet<ExperienceList> _visibleLists = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ExperienceList> _itemizedLists = new(ReferenceEqualityComparer.Instance);
    private PageLayoutResult? _currentLayout;
    private ExplicitPageLayoutResult? _currentExplicitLayout;

    public PageLayoutSelectionAdmissionPolicy(
        ExperienceDatabase database,
        CvMeasurementSnapshot measurements,
        CvExperienceSectionBindings sectionBindings,
        ImmutableArray<Section> sectionOrder,
        CvPageCount pageCount = default,
        CvPageLayout? explicitPageLayout = null)
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
        _dynamicSections = sectionBindings.Sections.ToHashSet();
        _explicitPageLayout = explicitPageLayout;
        PageCount = pageCount;
        if (explicitPageLayout is not null)
        {
            if (!sectionOrder.SequenceEqual(explicitPageLayout.SectionOrder))
            {
                throw new ArgumentException(
                    "The flattened section order must match the explicit page layout.",
                    nameof(sectionOrder));
            }
            if (pageCount.ExactCount != explicitPageLayout.PageCount)
            {
                throw new ArgumentException(
                    "The configured page count must equal the explicit layout's derived page count.",
                    nameof(pageCount));
            }
        }

        _listOrders = new(ReferenceEqualityComparer.Instance);
        var orderedLists = database.Experiences
            .OrderByDescending(
                static experience => experience.DateRange,
                DateRangeComparer.ByEnd);
        var listOrder = 0;
        foreach (var experience in orderedLists)
        {
            _listOrders.Add(experience, listOrder);
            listOrder++;
        }

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

            if (_explicitPageLayout is not null && _dynamicSections.Contains(section))
            {
                var currentStart = measurements
                    .GetCurrentPageSplitSectionStartHeight(section)
                    .ScaledPoints;
                var freshStart = measurements
                    .GetFreshPageSplitSectionStartHeight(section)
                    .ScaledPoints;
                if (currentStart < 0 || freshStart < 0)
                {
                    throw new CvMeasurementInvariantException(
                        $"Measured split-section start for '{section}' cannot be negative.");
                }
                if (freshStart < currentStart)
                {
                    throw new CvMeasurementInvariantException(
                        $"Fresh-page split-section start for '{section}' is shorter than its current-page form.");
                }
            }

            if (!_dynamicSections.Contains(section))
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

        if (_explicitPageLayout is null)
        {
            _currentLayout = CalculateLayout();
            if (!_currentLayout.Fits)
            {
                throw new FixedCvContentLayoutException(
                    $"Fixed CV content cannot satisfy the {LayoutDescription}: {_currentLayout.Failure!.Message}");
            }
        }
        else
        {
            if (measurements.SplitSectionEnd.ScaledPoints < 0
                || measurements.FreshPageContinuation.ScaledPoints < 0)
            {
                throw new CvMeasurementInvariantException(
                    "Measured split-section ending and fresh-page continuation heights cannot be negative.");
            }

            _currentExplicitLayout = CalculateExplicitLayout();
            if (!_currentExplicitLayout.Fits)
            {
                throw new FixedCvContentLayoutException(
                    $"Fixed CV content cannot satisfy the {LayoutDescription}: {_currentExplicitLayout.Failure!.Message}");
            }
        }
    }

    public bool PrioritizeMinimums => true;

    public CvPageCount PageCount { get; }

    internal int PredictedPageCount => _explicitPageLayout?.PageCount
        ?? _currentLayout!.PageCount;

    internal LatexHeight CurrentHeight => new(checked(
        _measurements.DocumentHeader.ScaledPoints
        + _measurements.DocumentFooter.ScaledPoints
        + _sectionHeights.Values.Sum()));

    public SelectionAdmissionDecision Evaluate(SelectionAdmission admission)
    {
        var section = GetSection(admission.Group);
        if (_explicitPageLayout is not null)
        {
            if (!_renderedSections.Contains(section))
            {
                return SelectionAdmissionDecision.Accepted;
            }

            var proposedEventHeight = checked(
                _eventHeights.GetValueOrDefault(admission.List)
                + GetAdditionalEventHeight(admission));
            var explicitLayout = CalculateExplicitLayout(
                admission.List,
                section,
                proposedEventHeight);
            if (explicitLayout.Fits)
            {
                return SelectionAdmissionDecision.Accepted;
            }

            return SelectionAdmissionDecision.Reject(
                $"the {LayoutDescription} cannot include it: {explicitLayout.Failure!.Message}");
        }

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
        var additionalEventHeight = _explicitPageLayout is null
            ? 0
            : GetAdditionalEventHeight(admission);
        if (_renderedSections.Contains(section))
        {
            _sectionHeights[section] = checked(
                _sectionHeights.GetValueOrDefault(section) + additionalHeight);
            if (_explicitPageLayout is not null)
            {
                _eventHeights[admission.List] = checked(
                    _eventHeights.GetValueOrDefault(admission.List)
                    + additionalEventHeight);
                _eventSections[admission.List] = section;
            }
            _visibleSections.Add(section);
            _visibleLists.Add(admission.List);
            if (admission.Items.Count > 0)
            {
                _itemizedLists.Add(admission.List);
            }
        }

        if (_explicitPageLayout is null)
        {
            _currentLayout = CalculateLayout();
            if (!_currentLayout.Fits)
            {
                throw new CvSelectionCommitException(
                    $"Committed selection no longer satisfies the {LayoutDescription}: {_currentLayout.Failure!.Message}");
            }
        }
        else
        {
            _currentExplicitLayout = CalculateExplicitLayout();
            if (!_currentExplicitLayout.Fits)
            {
                throw new CvSelectionCommitException(
                    $"Committed selection no longer satisfies the {LayoutDescription}: {_currentExplicitLayout.Failure!.Message}");
            }
        }
    }

    internal void RequireExactPageCount()
    {
        if (_explicitPageLayout is not null)
        {
            throw new InvalidOperationException(
                "Explicit layouts must be completed with RequireCompletePageLayout.");
        }

        if (PageCount.ExactCount is { } required
            && _currentLayout!.PageCount != required)
        {
            throw new PredictedPageCountMismatchException(required, _currentLayout.PageCount);
        }
    }

    internal void RequireCompletePageLayout()
    {
        if (_explicitPageLayout is null || _currentExplicitLayout is null)
        {
            throw new InvalidOperationException(
                "RequireCompletePageLayout is only valid for an explicit page layout.");
        }
        if (!_currentExplicitLayout.Fits)
        {
            throw new CvSelectionCommitException(
                $"Selected content does not fit the {LayoutDescription}: {_currentExplicitLayout.Failure!.Message}");
        }

        for (var index = 0; index < _currentExplicitLayout.Blocks.Length; index++)
        {
            var result = _currentExplicitLayout.Blocks[index];
            if (result.NaturallyOccupiedPageCount != result.Block.AllocatedPageCount)
            {
                throw new CvPageLayoutUnderfillException(
                    result.Block,
                    result.NaturallyOccupiedPageCount);
            }
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

    private ExplicitPageLayoutResult CalculateExplicitLayout(
        ExperienceList? overriddenList = null,
        Section? overriddenSection = null,
        long overriddenEventHeight = 0)
    {
        if (_explicitPageLayout is null)
        {
            throw new InvalidOperationException(
                "An explicit layout calculation requires an explicit page layout.");
        }

        var blockUnits =
            new IReadOnlyList<ExplicitPageLayoutUnit>[_explicitPageLayout.Blocks.Length];
        for (var blockIndex = 0; blockIndex < _explicitPageLayout.Blocks.Length; blockIndex++)
        {
            var block = _explicitPageLayout.Blocks[blockIndex];
            var units = new List<ExplicitPageLayoutUnit>();
            foreach (var section in block.Sections)
            {
                if (!_dynamicSections.Contains(section))
                {
                    var current = _measurements
                        .GetCurrentPageExplicitStaticSectionHeight(section);
                    if (current.ScaledPoints > 0)
                    {
                        units.Add(new(
                            section,
                            EventTitle: null,
                            current,
                            _measurements.GetFreshPageExplicitStaticSectionHeight(section)));
                    }
                    continue;
                }

                AddEventUnits(
                    units,
                    section,
                    overriddenList,
                    overriddenSection,
                    overriddenEventHeight);
            }
            blockUnits[blockIndex] = units;
        }

        return ExplicitPageLayoutCalculator.Calculate(
            _measurements.UsablePageHeight,
            _measurements.DocumentHeader,
            _measurements.DocumentFooter,
            _explicitPageLayout,
            blockUnits);
    }

    private void AddEventUnits(
        List<ExplicitPageLayoutUnit> units,
        Section section,
        ExperienceList? overriddenList,
        Section? overriddenSection,
        long overriddenEventHeight)
    {
        var events = _eventHeights
            .Where(pair => _eventSections[pair.Key] == section)
            .Select(static pair => (List: pair.Key, Height: pair.Value))
            .ToList();
        if (overriddenList is not null && overriddenSection == section)
        {
            var existingIndex = events.FindIndex(
                pair => ReferenceEquals(pair.List, overriddenList));
            if (existingIndex >= 0)
            {
                events[existingIndex] = (overriddenList, overriddenEventHeight);
            }
            else
            {
                events.Add((overriddenList, overriddenEventHeight));
            }
        }

        events.Sort((left, right) =>
            _listOrders[left.List].CompareTo(_listOrders[right.List]));
        if (events.Count == 0)
        {
            return;
        }

        var currentStart = _measurements
            .GetCurrentPageSplitSectionStartHeight(section)
            .ScaledPoints;
        var freshStart = _measurements
            .GetFreshPageSplitSectionStartHeight(section)
            .ScaledPoints;
        var continuation = _measurements.FreshPageContinuation.ScaledPoints;
        var ending = _measurements.SplitSectionEnd.ScaledPoints;
        for (var index = 0; index < events.Count; index++)
        {
            var selectedEvent = events[index];
            var isFirst = index == 0;
            var isLast = index == events.Count - 1;
            var current = checked(
                selectedEvent.Height
                + (isFirst ? currentStart : 0)
                + (isLast ? ending : 0));
            var fresh = checked(
                selectedEvent.Height
                + (isFirst ? freshStart : continuation)
                + (isLast ? ending : 0));
            units.Add(new(
                section,
                selectedEvent.List.Title.Value,
                new(current),
                new(fresh)));
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

    private long GetAdditionalEventHeight(SelectionAdmission admission)
    {
        var section = GetSection(admission.Group);
        if (!_renderedSections.Contains(section))
        {
            return 0;
        }
        if (!_listIds.TryGetValue(admission.List, out var listId))
        {
            throw new CvMeasurementInvariantException(
                "Selected experience list was not found in the measured database.");
        }

        long height = 0;
        var hasItems = admission.Items.Count > 0;
        if (!_visibleLists.Contains(admission.List))
        {
            height = hasItems
                ? _measurements.GetExperienceChromeHeight(listId).ScaledPoints
                : _measurements.GetExperienceHeadingHeight(listId).ScaledPoints;
        }
        else if (hasItems && !_itemizedLists.Contains(admission.List))
        {
            height = checked(
                _measurements.GetExperienceChromeHeight(listId).ScaledPoints
                - _measurements.GetExperienceHeadingHeight(listId).ScaledPoints);
        }

        foreach (var item in admission.Items)
        {
            if (!_itemIds.TryGetValue(item, out var itemId))
            {
                throw new CvMeasurementInvariantException(
                    "Selected experience item was not found in the measured database.");
            }
            height = checked(
                height + _measurements.GetExperienceItemHeight(itemId).ScaledPoints);
        }

        return height;
    }

    private Section GetSection(ExperienceSelectionGroup group)
        => _sectionBindings.GetSection(group.Key);

    private string LayoutDescription => PageCount.ExactCount switch
    {
        _ when _explicitPageLayout is not null =>
            $"explicit {_explicitPageLayout.PageCount}-page layout",
        1 => "configured one-page layout",
        { } count => $"configured {count}-page layout",
        null => "legacy unrestricted page layout",
    };
}
