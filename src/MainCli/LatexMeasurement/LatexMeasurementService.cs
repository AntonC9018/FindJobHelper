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
                    misses[i].IncludeFlowBlockFitSpacing);
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
            graph.ExperienceChrome,
            graph.CompleteSections,
            graph.SectionChrome,
            graph.DocumentChrome!.Value);
    }

    private RequestGraph BuildRequestGraph(
        ExperienceDatabase database,
        CvDataModel currentModel)
    {
        var graph = new RequestGraph(_ruleVersion);
        foreach (var identified in database.EnumerateExperienceItems())
        {
            var fragment = CvLatexFragmentRenderer.RenderExperienceItem(identified.Value);
            var key = new LatexMeasurementCacheKey(
                _ruleVersion,
                LatexMeasurementKind.ExperienceItem,
                RichTextCanonicalHasher.ComputeHash(identified.Value.Text));
            graph.Add(
                key,
                fragment,
                includeFlowBlockFitSpacing: false,
                MeasurementDestination.ForExperienceItem(identified.Id));
        }

        foreach (var identified in database.EnumerateExperienceLists())
        {
            var fragment = CvLatexFragmentRenderer.RenderExperienceChrome(identified.Value);
            graph.Add(
                CreateFragmentKey(LatexMeasurementKind.ExperienceChrome, fragment),
                fragment,
                includeFlowBlockFitSpacing: false,
                MeasurementDestination.ForExperienceChrome(identified.Id));
        }

        foreach (var section in Enum.GetValues<Section>())
        {
            var chrome = CvLatexFragmentRenderer.RenderSectionChrome(section);
            graph.Add(
                CreateFragmentKey(LatexMeasurementKind.SectionChrome, chrome, section),
                chrome,
                includeFlowBlockFitSpacing: true,
                MeasurementDestination.ForSectionChrome(section));

            if (CvLatexFragmentRenderer.IsSectionEmpty(section, currentModel))
            {
                graph.CompleteSections.Add(section, LatexHeight.Zero);
                continue;
            }

            var complete = CvLatexFragmentRenderer.RenderSectionInner(
                section,
                currentModel,
                isDebug: false);
            var kind = section == Section.Languages
                ? LatexMeasurementKind.StaticSection
                : LatexMeasurementKind.CompleteSection;
            graph.Add(
                CreateFragmentKey(kind, complete, section),
                complete,
                includeFlowBlockFitSpacing: true,
                MeasurementDestination.ForCompleteSection(section));
        }

        var documentChrome = CvLatexFragmentRenderer.RenderDocumentChrome(currentModel);
        graph.Add(
            CreateFragmentKey(LatexMeasurementKind.DocumentChrome, documentChrome),
            documentChrome,
            includeFlowBlockFitSpacing: false,
            MeasurementDestination.ForDocumentChrome());
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
            throw new InvalidOperationException(
                $"The measurement runner returned {results.Count} results for {requests.Count} requests.");
        }

        foreach (var request in requests)
        {
            if (!results.TryGetValue(request.CorrelationId, out var height))
            {
                throw new InvalidOperationException(
                    $"The measurement runner omitted correlation '{request.CorrelationId}'.");
            }
            if (height.ScaledPoints < 0)
            {
                throw new InvalidOperationException(
                    $"The measurement runner returned a negative height for '{request.CorrelationId}'.");
            }
        }
    }

    private sealed class RequestGraph(int ruleVersion)
    {
        public Dictionary<LatexMeasurementCacheKey, MeasurementWorkItem> WorkItems { get; } = new();
        public Dictionary<ExperienceItemId, LatexHeight> ExperienceItems { get; } = new();
        public Dictionary<ExperienceListId, LatexHeight> ExperienceChrome { get; } = new();
        public Dictionary<Section, LatexHeight> CompleteSections { get; } = new();
        public Dictionary<Section, LatexHeight> SectionChrome { get; } = new();
        public LatexHeight? DocumentChrome { get; private set; }

        public void Add(
            LatexMeasurementCacheKey key,
            string renderedFragment,
            bool includeFlowBlockFitSpacing,
            MeasurementDestination destination)
        {
            if (key.RuleVersion != ruleVersion)
            {
                throw new InvalidOperationException("A request graph key used the wrong rule version.");
            }

            if (!WorkItems.TryGetValue(key, out var workItem))
            {
                workItem = new(key, renderedFragment, includeFlowBlockFitSpacing);
                WorkItems.Add(key, workItem);
            }
            else if (workItem.RenderedFragment != renderedFragment
                     || workItem.IncludeFlowBlockFitSpacing != includeFlowBlockFitSpacing)
            {
                throw new InvalidOperationException(
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
                    case MeasurementDestinationKind.ExperienceChrome:
                        ExperienceChrome[destination.ExperienceListId] = height;
                        break;
                    case MeasurementDestinationKind.CompleteSection:
                        CompleteSections[destination.Section] = height;
                        break;
                    case MeasurementDestinationKind.SectionChrome:
                        SectionChrome[destination.Section] = height;
                        break;
                    case MeasurementDestinationKind.DocumentChrome:
                        DocumentChrome = height;
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
                throw new InvalidOperationException(
                    $"Incomplete measurement snapshot: expected {expectedItems} experience items, found {ExperienceItems.Count}.");
            }
            if (ExperienceChrome.Count != database.Experiences.Length)
            {
                throw new InvalidOperationException("Incomplete measurement snapshot: experience chrome is missing.");
            }
            if (CompleteSections.Count != Enum.GetValues<Section>().Length)
            {
                throw new InvalidOperationException("Incomplete measurement snapshot: complete sections are missing.");
            }
            if (SectionChrome.Count != Enum.GetValues<Section>().Length)
            {
                throw new InvalidOperationException("Incomplete measurement snapshot: section chrome is missing.");
            }
            if (DocumentChrome is null)
            {
                throw new InvalidOperationException("Incomplete measurement snapshot: document chrome is missing.");
            }
        }
    }

    private sealed class MeasurementWorkItem(
        LatexMeasurementCacheKey key,
        string renderedFragment,
        bool includeFlowBlockFitSpacing)
    {
        public LatexMeasurementCacheKey Key { get; } = key;
        public string RenderedFragment { get; } = renderedFragment;
        public bool IncludeFlowBlockFitSpacing { get; } = includeFlowBlockFitSpacing;
        public List<MeasurementDestination> Destinations { get; } = new();
    }

    private enum MeasurementDestinationKind
    {
        ExperienceItem,
        ExperienceChrome,
        CompleteSection,
        SectionChrome,
        DocumentChrome,
    }

    private readonly record struct MeasurementDestination(
        MeasurementDestinationKind Kind,
        ExperienceItemId ExperienceItemId,
        ExperienceListId ExperienceListId,
        Section Section)
    {
        public static MeasurementDestination ForExperienceItem(ExperienceItemId id)
            => new(MeasurementDestinationKind.ExperienceItem, id, default, default);

        public static MeasurementDestination ForExperienceChrome(ExperienceListId id)
            => new(MeasurementDestinationKind.ExperienceChrome, default, id, default);

        public static MeasurementDestination ForCompleteSection(Section section)
            => new(MeasurementDestinationKind.CompleteSection, default, default, section);

        public static MeasurementDestination ForSectionChrome(Section section)
            => new(MeasurementDestinationKind.SectionChrome, default, default, section);

        public static MeasurementDestination ForDocumentChrome()
            => new(MeasurementDestinationKind.DocumentChrome, default, default, default);
    }
}
