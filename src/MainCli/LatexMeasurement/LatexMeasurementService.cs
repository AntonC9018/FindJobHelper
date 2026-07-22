using FindJobHelper.Core;

namespace FindJobHelper.CVGeneration;

public sealed class LatexMeasurementService
{
    private readonly LatexHeightCache _cache;
    private readonly ILatexMeasurementRunner _runner;
    private readonly int _ruleVersion;

    public LatexMeasurementService()
        : this(
            LatexHeightCache.DefaultPath,
            new XeLatexMeasurementRunner(),
            LatexMeasurementRules.CurrentVersion)
    {
    }

    internal LatexMeasurementService(
        string cachePath,
        ILatexMeasurementRunner runner,
        int ruleVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(runner);
        if (ruleVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ruleVersion));
        }

        _cache = new LatexHeightCache(cachePath, ruleVersion);
        _runner = runner;
        _ruleVersion = ruleVersion;
    }

    public async Task<CvMeasurementSnapshot> MeasureAsync(
        ExperienceDatabase database,
        CvDataModel currentModel,
        string templatePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(currentModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("The production LaTeX template was not found.", templatePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var graph = BuildRequestGraph(database, currentModel);
        await _cache.InitializeAsync(cancellationToken);
        var hits = await _cache.LoadAsync(graph.WorkItems.Keys.ToArray(), cancellationToken);

        foreach (var (key, height) in hits)
        {
            graph.Populate(graph.WorkItems[key].Destinations, height);
        }

        var misses = graph.WorkItems.Values
            .Where(workItem => !hits.ContainsKey(workItem.Key))
            .ToArray();
        if (misses.Length > 0)
        {
            var requests = new LatexMeasurementRequest[misses.Length];
            for (var i = 0; i < misses.Length; i++)
            {
                requests[i] = new(
                    new MeasurementCorrelationId(i + 1),
                    misses[i].Key,
                    misses[i].RenderedFragment,
                    misses[i].Mode);
            }

            var measured = await _runner.MeasureAsync(
                Path.GetFullPath(templatePath),
                requests,
                cancellationToken);
            ValidateRunnerResults(requests, measured);
            cancellationToken.ThrowIfCancellationRequested();

            var cacheValues = new Dictionary<LatexMeasurementCacheKey, LatexHeight>(requests.Length);
            for (var i = 0; i < requests.Length; i++)
            {
                var request = requests[i];
                cacheValues.Add(request.CacheKey, measured[request.CorrelationId]);
            }

            await _cache.StoreAsync(cacheValues, cancellationToken);
            foreach (var miss in misses)
            {
                graph.Populate(miss.Destinations, cacheValues[miss.Key]);
            }
        }

        graph.VerifyComplete(database);
        return CvMeasurementSnapshot.CreateFrozen(
            graph.ExperienceItems,
            graph.ExperienceHeadings,
            graph.ExperienceChrome,
            graph.CurrentPageCompleteSections,
            graph.CurrentPageSectionChrome,
            graph.FreshPageSectionChrome,
            graph.DocumentHeader!.Value,
            graph.DocumentFooter!.Value,
            graph.UsablePageHeight!.Value);
    }

    private RequestGraph BuildRequestGraph(
        ExperienceDatabase database,
        CvDataModel currentModel)
    {
        var graph = new RequestGraph(_ruleVersion);
        foreach (var identified in database.EnumerateExperienceItems())
        {
            var fragment = CvLatexFragmentRenderer.Materialize(
                CvLatexFragmentRenderer.RenderExperienceItem(identified.Value));
            var key = new LatexMeasurementCacheKey(
                _ruleVersion,
                LatexMeasurementKind.ExperienceItem,
                RichTextCanonicalHasher.ComputeHash(identified.Value.Text));
            graph.Add(
                key,
                fragment,
                LatexMeasurementMode.ExperienceItemMarginal,
                MeasurementDestination.ForExperienceItem(identified.Id));
        }

        foreach (var identified in database.EnumerateExperienceLists())
        {
            var headingFragment = CvLatexFragmentRenderer.Materialize(
                CvLatexFragmentRenderer.RenderExperienceHeading(identified.Value));
            graph.Add(
                CreateFragmentKey(LatexMeasurementKind.ExperienceHeading, headingFragment),
                headingFragment,
                LatexMeasurementMode.Box,
                MeasurementDestination.ForExperienceHeading(identified.Id));

            var fragment = CvLatexFragmentRenderer.Materialize(
                CvLatexFragmentRenderer.RenderExperienceChrome(identified.Value));
            graph.Add(
                CreateFragmentKey(LatexMeasurementKind.ExperienceChrome, fragment),
                fragment,
                identified.Value.Urls.IsEmpty
                    ? LatexMeasurementMode.ExperienceChromeWithoutPermanentItems
                    : LatexMeasurementMode.Box,
                MeasurementDestination.ForExperienceChrome(identified.Id));
        }

        foreach (var section in Enum.GetValues<Section>())
        {
            var chrome = CvLatexFragmentRenderer.Materialize(
                CvLatexFragmentRenderer.RenderSectionChrome(section));
            graph.Add(
                CreateFragmentKey(LatexMeasurementKind.SectionChrome, chrome, section),
                chrome,
                LatexMeasurementMode.SectionChrome,
                MeasurementDestination.ForCurrentPageSectionChrome(section));
            graph.Add(
                CreateFragmentKey(LatexMeasurementKind.FreshPageSectionChrome, chrome, section),
                chrome,
                LatexMeasurementMode.FreshPageSectionChrome,
                MeasurementDestination.ForFreshPageSectionChrome(section));

            if (CvLatexFragmentRenderer.IsSectionEmpty(section, currentModel))
            {
                graph.CurrentPageCompleteSections.Add(section, LatexHeight.Zero);
                continue;
            }

            var complete = CvLatexFragmentRenderer.Materialize(
                CvLatexFragmentRenderer.RenderSectionInner(
                    section,
                    currentModel,
                    isDebug: false));
            var kind = section == Section.Languages
                ? LatexMeasurementKind.StaticSection
                : LatexMeasurementKind.CompleteSection;
            graph.Add(
                CreateFragmentKey(kind, complete, section),
                complete,
                LatexMeasurementMode.FlowBlock,
                MeasurementDestination.ForCompleteSection(section));
        }

        var documentHeader = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderDocumentHeader(currentModel));
        graph.Add(
            CreateFragmentKey(LatexMeasurementKind.DocumentHeader, documentHeader),
            documentHeader,
            LatexMeasurementMode.DocumentHeader,
            MeasurementDestination.ForDocumentHeader());

        var documentFooter = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderDocumentFooter(currentModel));
        if (documentFooter.Length == 0)
        {
            graph.Populate(
                [MeasurementDestination.ForDocumentFooter()],
                LatexHeight.Zero);
        }
        else
        {
            graph.Add(
                CreateFragmentKey(LatexMeasurementKind.DocumentFooter, documentFooter),
                documentFooter,
                LatexMeasurementMode.Box,
                MeasurementDestination.ForDocumentFooter());
        }

        const string usablePageFragment = @"\rule{0pt}{\textheight}";
        graph.Add(
            CreateFragmentKey(LatexMeasurementKind.UsablePageHeight, usablePageFragment),
            usablePageFragment,
            LatexMeasurementMode.Box,
            MeasurementDestination.ForUsablePageHeight());
        return graph;
    }

    private LatexMeasurementCacheKey CreateFragmentKey(
        LatexMeasurementKind kind,
        string fragment,
        Section? section = null)
        => new(_ruleVersion, kind, LatexFragmentHasher.Compute(kind, fragment, section));

    private static void ValidateRunnerResults(
        IReadOnlyList<LatexMeasurementRequest> requests,
        IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight> results)
    {
        if (results.Count != requests.Count)
        {
            throw new CvMeasurementException(
                $"The measurement runner returned {results.Count} results for {requests.Count} requests.");
        }

        foreach (var request in requests)
        {
            if (!results.TryGetValue(request.CorrelationId, out var height))
            {
                throw new CvMeasurementException(
                    $"The measurement runner omitted correlation '{request.CorrelationId}'.");
            }
            if (height.ScaledPoints < 0)
            {
                throw new CvMeasurementException(
                    $"The measurement runner returned a negative height for '{request.CorrelationId}'.");
            }
        }
    }

    private sealed class RequestGraph(int ruleVersion)
    {
        public Dictionary<LatexMeasurementCacheKey, MeasurementWorkItem> WorkItems { get; } = new();
        public Dictionary<ExperienceItemId, LatexHeight> ExperienceItems { get; } = new();
        public Dictionary<ExperienceListId, LatexHeight> ExperienceHeadings { get; } = new();
        public Dictionary<ExperienceListId, LatexHeight> ExperienceChrome { get; } = new();
        public Dictionary<Section, LatexHeight> CurrentPageCompleteSections { get; } = new();
        public Dictionary<Section, LatexHeight> CurrentPageSectionChrome { get; } = new();
        public Dictionary<Section, LatexHeight> FreshPageSectionChrome { get; } = new();
        public LatexHeight? DocumentHeader { get; private set; }
        public LatexHeight? DocumentFooter { get; private set; }
        public LatexHeight? UsablePageHeight { get; private set; }

        public void Add(
            LatexMeasurementCacheKey key,
            string renderedFragment,
            LatexMeasurementMode mode,
            MeasurementDestination destination)
        {
            if (key.RuleVersion != ruleVersion)
            {
                throw new CvMeasurementException(
                    "A request graph key used the wrong rule version.");
            }

            if (!WorkItems.TryGetValue(key, out var workItem))
            {
                workItem = new(key, renderedFragment, mode);
                WorkItems.Add(key, workItem);
            }
            else if (workItem.RenderedFragment != renderedFragment
                     || workItem.Mode != mode)
            {
                throw new CvMeasurementException(
                    $"Hash collision detected for {key.Kind} key '{key.ContentHash}'.");
            }

            workItem.Destinations.Add(destination);
        }

        public void Populate(
            IReadOnlyList<MeasurementDestination> destinations,
            LatexHeight height)
        {
            foreach (var destination in destinations)
            {
                switch (destination.Kind)
                {
                    case MeasurementDestinationKind.ExperienceItem:
                        ExperienceItems[destination.ExperienceItemId] = height;
                        break;
                    case MeasurementDestinationKind.ExperienceHeading:
                        ExperienceHeadings[destination.ExperienceListId] = height;
                        break;
                    case MeasurementDestinationKind.ExperienceChrome:
                        ExperienceChrome[destination.ExperienceListId] = height;
                        break;
                    case MeasurementDestinationKind.CompleteSection:
                        CurrentPageCompleteSections[destination.Section] = height;
                        break;
                    case MeasurementDestinationKind.CurrentPageSectionChrome:
                        CurrentPageSectionChrome[destination.Section] = height;
                        break;
                    case MeasurementDestinationKind.FreshPageSectionChrome:
                        FreshPageSectionChrome[destination.Section] = height;
                        break;
                    case MeasurementDestinationKind.DocumentHeader:
                        DocumentHeader = height;
                        break;
                    case MeasurementDestinationKind.DocumentFooter:
                        DocumentFooter = height;
                        break;
                    case MeasurementDestinationKind.UsablePageHeight:
                        UsablePageHeight = height;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public void VerifyComplete(ExperienceDatabase database)
        {
            var expectedItems = database.Experiences.Sum(static list => list.Items.Length);
            if (ExperienceItems.Count != expectedItems)
            {
                throw new CvMeasurementInvariantException(
                    $"Incomplete measurement snapshot: expected {expectedItems} experience items, found {ExperienceItems.Count}.");
            }
            if (ExperienceChrome.Count != database.Experiences.Length)
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: experience chrome is missing.");
            }
            if (ExperienceHeadings.Count != database.Experiences.Length)
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: experience headings are missing.");
            }
            if (CurrentPageCompleteSections.Count != Enum.GetValues<Section>().Length)
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: complete sections are missing.");
            }
            if (CurrentPageSectionChrome.Count != Enum.GetValues<Section>().Length
                || FreshPageSectionChrome.Count != Enum.GetValues<Section>().Length)
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: section chrome is missing.");
            }
            if (DocumentHeader is null || DocumentFooter is null)
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: document chrome is missing.");
            }
            if (UsablePageHeight is null)
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: usable page height is missing.");
            }

            foreach (var identified in database.EnumerateExperienceLists())
            {
                if (ExperienceChrome[identified.Id].ScaledPoints
                    < ExperienceHeadings[identified.Id].ScaledPoints)
                {
                    throw new CvMeasurementInvariantException(
                        $"Measured experience chrome for '{identified.Value.Title}' is smaller than its heading.");
                }
            }

            foreach (var section in Enum.GetValues<Section>())
            {
                var currentChrome = CurrentPageSectionChrome[section].ScaledPoints;
                var freshChrome = FreshPageSectionChrome[section].ScaledPoints;
                var complete = CurrentPageCompleteSections[section].ScaledPoints;
                if (currentChrome < 0 || freshChrome < 0 || complete < 0)
                {
                    throw new CvMeasurementInvariantException(
                        $"Measured heights for section '{section}' cannot be negative.");
                }
                if (freshChrome < currentChrome)
                {
                    throw new CvMeasurementInvariantException(
                        $"Fresh-page wrapper for section '{section}' is shorter than its current-page wrapper.");
                }
                if (complete > 0
                    && checked(complete - currentChrome + freshChrome) < 0)
                {
                    throw new CvMeasurementInvariantException(
                        $"Derived fresh-page height for section '{section}' cannot be negative.");
                }
            }
        }
    }

    private sealed class MeasurementWorkItem(
        LatexMeasurementCacheKey key,
        string renderedFragment,
        LatexMeasurementMode mode)
    {
        public LatexMeasurementCacheKey Key { get; } = key;
        public string RenderedFragment { get; } = renderedFragment;
        public LatexMeasurementMode Mode { get; } = mode;
        public List<MeasurementDestination> Destinations { get; } = new();
    }

    private enum MeasurementDestinationKind
    {
        ExperienceItem,
        ExperienceHeading,
        ExperienceChrome,
        CompleteSection,
        CurrentPageSectionChrome,
        FreshPageSectionChrome,
        DocumentHeader,
        DocumentFooter,
        UsablePageHeight,
    }

    private readonly record struct MeasurementDestination(
        MeasurementDestinationKind Kind,
        ExperienceItemId ExperienceItemId,
        ExperienceListId ExperienceListId,
        Section Section)
    {
        public static MeasurementDestination ForExperienceItem(ExperienceItemId id)
            => new(MeasurementDestinationKind.ExperienceItem, id, default, default);

        public static MeasurementDestination ForExperienceHeading(ExperienceListId id)
            => new(MeasurementDestinationKind.ExperienceHeading, default, id, default);

        public static MeasurementDestination ForExperienceChrome(ExperienceListId id)
            => new(MeasurementDestinationKind.ExperienceChrome, default, id, default);

        public static MeasurementDestination ForCompleteSection(Section section)
            => new(MeasurementDestinationKind.CompleteSection, default, default, section);

        public static MeasurementDestination ForCurrentPageSectionChrome(Section section)
            => new(MeasurementDestinationKind.CurrentPageSectionChrome, default, default, section);

        public static MeasurementDestination ForFreshPageSectionChrome(Section section)
            => new(MeasurementDestinationKind.FreshPageSectionChrome, default, default, section);

        public static MeasurementDestination ForDocumentHeader()
            => new(MeasurementDestinationKind.DocumentHeader, default, default, default);

        public static MeasurementDestination ForDocumentFooter()
            => new(MeasurementDestinationKind.DocumentFooter, default, default, default);

        public static MeasurementDestination ForUsablePageHeight()
            => new(MeasurementDestinationKind.UsablePageHeight, default, default, default);
    }
}
