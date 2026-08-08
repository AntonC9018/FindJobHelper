using FindJobHelper.Core;

namespace FindJobHelper.CVGeneration;

public sealed class LatexMeasurementService
{
    private readonly LatexHeightCache _cache;
    private readonly ILatexMeasurementRunner _runner;
    private readonly int _ruleVersion;

    public LatexMeasurementService(LatexExecutablePaths executables)
        : this(
            LatexHeightCache.DefaultPath,
            new XeLatexMeasurementRunner(executables),
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

    public async Task<CvMeasurementResult> MeasureAsync(
        ExperienceDatabase database,
        CvDataModel currentModel,
        string templatePath,
        IProgressReporter progress,
        LatexExecutionOptions executionOptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(currentModel);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(executionOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("The production LaTeX template was not found.", templatePath);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var measuredSections = currentModel.SectionOrder.Distinct().ToArray();
        var graph = BuildRequestGraph(database, currentModel, measuredSections);
        var totalWorkUnits = graph.WorkItems.Count + 1;
        progress.Report(new(
            CompletedWorkUnits: 0,
            TotalWorkUnits: totalWorkUnits,
            Detail: "Computing heights"));
        await _cache.InitializeAsync(cancellationToken);
        var hits = await _cache.LoadAsync(graph.WorkItems.Keys.ToArray(), cancellationToken);

        var completedWorkUnits = 0;
        foreach (var workItem in graph.WorkItems.Values)
        {
            if (!hits.TryGetValue(workItem.Key, out var height))
            {
                continue;
            }

            graph.Populate(workItem.Destinations, height);
            completedWorkUnits++;
            progress.Report(new(
                CompletedWorkUnits: completedWorkUnits,
                TotalWorkUnits: totalWorkUnits,
                Detail: "Computing heights — cached measurement"));
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

            var runResult = await _runner.MeasureAsync(
                Path.GetFullPath(templatePath),
                requests,
                new ProgressRangeReporter(
                    progress,
                    offset: completedWorkUnits,
                    length: requests.Length,
                    targetTotal: totalWorkUnits),
                executionOptions,
                cancellationToken);
            IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight> measured;
            switch (runResult)
            {
                case SuccessfulLatexMeasurementRun success:
                    measured = success.Measurements;
                    break;
                case IncompleteLatexInstallation incomplete:
                    return incomplete;
                case LatexCompilationFailure compilation:
                    return compilation;
                case MeasurementDataFailure data:
                    return data;
                case RenderLayoutFailure { Value: MetadataOverflowFailure metadata }:
                    return new MeasurementLayoutFailure(
                        new FixedContentLayoutFailure(metadata.Diagnostic));
                case RenderLayoutFailure:
                    throw new InvalidOperationException(
                        "The measurement runner returned an unsupported rendering layout failure.");
                default:
                    throw new InvalidOperationException(
                        "The measurement runner result union is empty.");
            }
            try
            {
                ValidateRunnerResults(requests, measured);
            }
            catch (CvMeasurementException exception)
            {
                return new MeasurementDataFailure(
                    exception.Message,
                    Path.GetDirectoryName(Path.GetFullPath(templatePath))!);
            }
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

        graph.VerifyComplete(database, measuredSections);
        var snapshot = CvMeasurementSnapshot.CreateFrozen(
            experienceItems: graph.ExperienceItems,
            experienceHeadings: graph.ExperienceHeadings,
            experienceChrome: graph.ExperienceChrome,
            currentPageCompleteSections: graph.CurrentPageCompleteSections,
            currentPageSectionChrome: graph.CurrentPageSectionChrome,
            freshPageSectionChrome: graph.FreshPageSectionChrome,
            currentPageSplitSectionStart: graph.CurrentPageSplitSectionStart,
            freshPageSplitSectionStart: graph.FreshPageSplitSectionStart,
            splitSectionEnd: graph.SplitSectionEnd!.Value,
            freshPageContinuation: graph.FreshPageContinuation!.Value,
            currentPageExplicitStaticSections: graph.CurrentPageExplicitStaticSections,
            freshPageExplicitStaticSections: graph.FreshPageExplicitStaticSections,
            documentHeader: graph.DocumentHeader!.Value,
            documentFooter: graph.DocumentFooter!.Value,
            usablePageHeight: graph.UsablePageHeight!.Value);
        progress.Report(new(
            CompletedWorkUnits: totalWorkUnits,
            TotalWorkUnits: totalWorkUnits,
            Detail: "Computing heights"));
        return snapshot;
    }

    public async Task<CvMeasurementSnapshot> MeasureAsync(
        ExperienceDatabase database,
        CvDataModel currentModel,
        string templatePath,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var result = await MeasureAsync(
            database,
            currentModel,
            templatePath,
            progress,
            LatexExecutionOptions.Empty,
            cancellationToken);
        return result switch
        {
            CvMeasurementSnapshot snapshot => snapshot,
            IncompleteLatexInstallation failure => throw new CvMeasurementException(failure.Message),
            LatexCompilationFailure failure => throw new CvMeasurementException(failure.Message),
            MeasurementDataFailure failure => throw new CvMeasurementException(failure.Diagnostic),
            MeasurementLayoutFailure failure => throw new CvMeasurementException(
                failure.Value?.ToString() ?? "CV measurement layout failed."),
            _ => throw new InvalidOperationException("The CV measurement result union is empty."),
        };
    }

    internal int GetWorkUnitCount(
        ExperienceDatabase database,
        CvDataModel currentModel)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(currentModel);

        var measuredSections = currentModel.SectionOrder.Distinct().ToArray();
        return checked(
            BuildRequestGraph(database, currentModel, measuredSections)
                .WorkItems.Count
            + 1);
    }

    private RequestGraph BuildRequestGraph(
        ExperienceDatabase database,
        CvDataModel currentModel,
        IReadOnlyList<Section> measuredSections)
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

        foreach (var section in measuredSections)
        {
            var isStaticSection = section == Section.Languages;
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
            if (!isStaticSection)
            {
                graph.Add(
                    CreateFragmentKey(LatexMeasurementKind.SplitSectionStart, chrome, section),
                    chrome,
                    LatexMeasurementMode.SplitSectionStart,
                    MeasurementDestination.ForCurrentPageSplitSectionStart(section));
                graph.Add(
                    CreateFragmentKey(
                        LatexMeasurementKind.FreshPageSplitSectionStart,
                        chrome,
                        section),
                    chrome,
                    LatexMeasurementMode.FreshPageSplitSectionStart,
                    MeasurementDestination.ForFreshPageSplitSectionStart(section));
            }

            if (CvLatexFragmentRenderer.IsSectionEmpty(section, currentModel))
            {
                graph.CurrentPageCompleteSections.Add(section, LatexHeight.Zero);
                if (isStaticSection)
                {
                    graph.CurrentPageExplicitStaticSections.Add(section, LatexHeight.Zero);
                    graph.FreshPageExplicitStaticSections.Add(section, LatexHeight.Zero);
                }
                continue;
            }

            var complete = CvLatexFragmentRenderer.Materialize(
                CvLatexFragmentRenderer.RenderSectionInner(
                    section,
                    currentModel));
            var kind = isStaticSection
                ? LatexMeasurementKind.StaticSection
                : LatexMeasurementKind.CompleteSection;
            graph.Add(
                CreateFragmentKey(kind, complete, section),
                complete,
                LatexMeasurementMode.FlowBlock,
                MeasurementDestination.ForCompleteSection(section));
            if (isStaticSection)
            {
                var explicitCurrent = $@"\cvflowblockfitskip{complete}\cvexplicitsectionend";
                var explicitFresh =
                    $@"\cvflowblocknewpageskip\cvflowblockfitskip{complete}\cvexplicitsectionend";
                graph.Add(
                    CreateFragmentKey(
                        LatexMeasurementKind.ExplicitStaticSection,
                        explicitCurrent,
                        section),
                    explicitCurrent,
                    LatexMeasurementMode.Box,
                    MeasurementDestination.ForCurrentPageExplicitStaticSection(section));
                graph.Add(
                    CreateFragmentKey(
                        LatexMeasurementKind.FreshPageExplicitStaticSection,
                        explicitFresh,
                        section),
                    explicitFresh,
                    LatexMeasurementMode.Box,
                    MeasurementDestination.ForFreshPageExplicitStaticSection(section));
            }
        }

        graph.Add(
            CreateFragmentKey(LatexMeasurementKind.SplitSectionEnd, string.Empty),
            string.Empty,
            LatexMeasurementMode.SplitSectionEnd,
            MeasurementDestination.ForSplitSectionEnd());
        graph.Add(
            CreateFragmentKey(LatexMeasurementKind.FreshPageContinuation, string.Empty),
            string.Empty,
            LatexMeasurementMode.FreshPageContinuation,
            MeasurementDestination.ForFreshPageContinuation());

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
        public Dictionary<Section, LatexHeight> CurrentPageSplitSectionStart { get; } = new();
        public Dictionary<Section, LatexHeight> FreshPageSplitSectionStart { get; } = new();
        public Dictionary<Section, LatexHeight> CurrentPageExplicitStaticSections { get; } = new();
        public Dictionary<Section, LatexHeight> FreshPageExplicitStaticSections { get; } = new();
        public LatexHeight? SplitSectionEnd { get; private set; }
        public LatexHeight? FreshPageContinuation { get; private set; }
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
                    case MeasurementDestinationKind.CurrentPageSplitSectionStart:
                        CurrentPageSplitSectionStart[destination.Section] = height;
                        break;
                    case MeasurementDestinationKind.FreshPageSplitSectionStart:
                        FreshPageSplitSectionStart[destination.Section] = height;
                        break;
                    case MeasurementDestinationKind.SplitSectionEnd:
                        SplitSectionEnd = height;
                        break;
                    case MeasurementDestinationKind.FreshPageContinuation:
                        FreshPageContinuation = height;
                        break;
                    case MeasurementDestinationKind.CurrentPageExplicitStaticSection:
                        CurrentPageExplicitStaticSections[destination.Section] = height;
                        break;
                    case MeasurementDestinationKind.FreshPageExplicitStaticSection:
                        FreshPageExplicitStaticSections[destination.Section] = height;
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

        public void VerifyComplete(
            ExperienceDatabase database,
            IReadOnlyCollection<Section> measuredSections)
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
            if (!ContainsExactly(CurrentPageCompleteSections, measuredSections))
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: complete sections are missing.");
            }
            if (!ContainsExactly(CurrentPageSectionChrome, measuredSections)
                || !ContainsExactly(FreshPageSectionChrome, measuredSections))
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: section chrome is missing.");
            }
            var dynamicSections = measuredSections
                .Where(static section => section != Section.Languages)
                .ToArray();
            if (!ContainsExactly(CurrentPageSplitSectionStart, dynamicSections)
                || !ContainsExactly(FreshPageSplitSectionStart, dynamicSections)
                || SplitSectionEnd is null
                || FreshPageContinuation is null)
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: split-section measurements are missing.");
            }
            var staticSections = measuredSections
                .Where(static section => section == Section.Languages)
                .ToArray();
            if (!ContainsExactly(CurrentPageExplicitStaticSections, staticSections)
                || !ContainsExactly(FreshPageExplicitStaticSections, staticSections))
            {
                throw new CvMeasurementInvariantException(
                    "Incomplete measurement snapshot: explicit static-section measurements are missing.");
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

            foreach (var section in measuredSections)
            {
                var currentChrome = CurrentPageSectionChrome[section].ScaledPoints;
                var freshChrome = FreshPageSectionChrome[section].ScaledPoints;
                var complete = CurrentPageCompleteSections[section].ScaledPoints;
                if (currentChrome < 0
                    || freshChrome < 0
                    || complete < 0)
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
                if (section == Section.Languages)
                {
                    var currentExplicitStatic =
                        CurrentPageExplicitStaticSections[section].ScaledPoints;
                    var freshExplicitStatic =
                        FreshPageExplicitStaticSections[section].ScaledPoints;
                    if (currentExplicitStatic < 0 || freshExplicitStatic < 0)
                    {
                        throw new CvMeasurementInvariantException(
                            $"Measured explicit static-section heights for '{section}' cannot be negative.");
                    }
                    if (freshExplicitStatic < currentExplicitStatic)
                    {
                        throw new CvMeasurementInvariantException(
                            $"Fresh-page explicit static section '{section}' is shorter than its current-page form.");
                    }
                }
                else
                {
                    var currentStart = CurrentPageSplitSectionStart[section].ScaledPoints;
                    var freshStart = FreshPageSplitSectionStart[section].ScaledPoints;
                    if (currentStart < 0 || freshStart < 0)
                    {
                        throw new CvMeasurementInvariantException(
                            $"Measured split-section start heights for '{section}' cannot be negative.");
                    }
                    if (freshStart < currentStart)
                    {
                        throw new CvMeasurementInvariantException(
                            $"Fresh-page split-section start for '{section}' is shorter than its current-page form.");
                    }
                }
            }

            if (SplitSectionEnd.Value.ScaledPoints < 0
                || FreshPageContinuation.Value.ScaledPoints < 0)
            {
                throw new CvMeasurementInvariantException(
                    "Split-section ending and fresh-page continuation heights cannot be negative.");
            }
        }

        private static bool ContainsExactly(
            IReadOnlyDictionary<Section, LatexHeight> measurements,
            IReadOnlyCollection<Section> expectedSections)
            => measurements.Count == expectedSections.Count
               && expectedSections.All(measurements.ContainsKey);
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
        CurrentPageSplitSectionStart,
        FreshPageSplitSectionStart,
        SplitSectionEnd,
        FreshPageContinuation,
        CurrentPageExplicitStaticSection,
        FreshPageExplicitStaticSection,
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

        public static MeasurementDestination ForCurrentPageSplitSectionStart(Section section)
            => new(MeasurementDestinationKind.CurrentPageSplitSectionStart, default, default, section);

        public static MeasurementDestination ForFreshPageSplitSectionStart(Section section)
            => new(MeasurementDestinationKind.FreshPageSplitSectionStart, default, default, section);

        public static MeasurementDestination ForSplitSectionEnd()
            => new(MeasurementDestinationKind.SplitSectionEnd, default, default, default);

        public static MeasurementDestination ForFreshPageContinuation()
            => new(MeasurementDestinationKind.FreshPageContinuation, default, default, default);

        public static MeasurementDestination ForCurrentPageExplicitStaticSection(Section section)
            => new(MeasurementDestinationKind.CurrentPageExplicitStaticSection, default, default, section);

        public static MeasurementDestination ForFreshPageExplicitStaticSection(Section section)
            => new(MeasurementDestinationKind.FreshPageExplicitStaticSection, default, default, section);

        public static MeasurementDestination ForDocumentHeader()
            => new(MeasurementDestinationKind.DocumentHeader, default, default, default);

        public static MeasurementDestination ForDocumentFooter()
            => new(MeasurementDestinationKind.DocumentFooter, default, default, default);

        public static MeasurementDestination ForUsablePageHeight()
            => new(MeasurementDestinationKind.UsablePageHeight, default, default, default);
    }
}
