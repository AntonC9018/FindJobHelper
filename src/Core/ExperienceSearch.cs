using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

public readonly record struct ExperienceKey(string Value)
{
    public override string ToString() => Value ?? "";
}

public readonly record struct ScoreBoost
{
    public ScoreBoost(float value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Score boost must be finite and non-negative.");
        }

        Value = value;
    }

    public float Value { get; }

    public static bool IsValid(float value) =>
        float.IsFinite(value) && value >= 0;

    public bool IsZero => Value == 0;

    public float Apply(float score) => Math.Max(0, score) * Value;

    public ScoreBoost Scale(float multiplier) => new(Value * multiplier);

    public static implicit operator ScoreBoost(float value) => new(value);
}

public sealed class SearchPredicateOptions
{
    public int MinItemBudget { get; set; } = 0;
    public int ItemBudget { get; set; } = int.MaxValue;

    public float ScoreLowerBound { get; set; }
    public ScoreBoost RecencyBoost { get; set; }
    public ScoreBoost DirectMatchBoost { get; set; }
    public bool IncludeEmptyLists { get; set; }
    public bool PreserveOneItemPerList { get; set; } = true;

    internal SearchPredicateOptions Copy()
    {
        return new()
        {
            MinItemBudget = MinItemBudget,
            ItemBudget = ItemBudget,
            ScoreLowerBound = ScoreLowerBound,
            RecencyBoost = RecencyBoost,
            DirectMatchBoost = DirectMatchBoost,
            IncludeEmptyLists = IncludeEmptyLists,
            PreserveOneItemPerList = PreserveOneItemPerList,
        };
    }
}

internal readonly record struct SearchPredicateOptionsValidationError(
    string PropertyName,
    string Message,
    bool IsOutOfRange)
{
    public Exception ToException()
    {
        return IsOutOfRange
            ? new ArgumentOutOfRangeException(PropertyName, Message)
            : new ArgumentException(Message, PropertyName);
    }
}

internal static class SearchPredicateOptionsValidator
{
    public static IEnumerable<SearchPredicateOptionsValidationError> ValidateOptions(
        SearchPredicateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MinItemBudget < 0)
        {
            yield return new(
                nameof(options.MinItemBudget),
                "must be non-negative.",
                IsOutOfRange: true);
        }

        if (options.ItemBudget < 0)
        {
            yield return new(
                nameof(options.ItemBudget),
                "must be non-negative.",
                IsOutOfRange: true);
        }
        else if (options.MinItemBudget > options.ItemBudget)
        {
            yield return new(
                nameof(options.MinItemBudget),
                "must not exceed the total item budget.",
                IsOutOfRange: false);
        }

        if (!float.IsFinite(options.ScoreLowerBound) || options.ScoreLowerBound < 0)
        {
            yield return new(
                nameof(options.ScoreLowerBound),
                "must be finite and non-negative.",
                IsOutOfRange: true);
        }

    }
}

public sealed class MmrOptionsBuilder
{
    public float RelevanceWeight { get; set; }
    public int SaturationQuota { get; set; }
    public float SaturationPenalty { get; set; }

    public MmrOptionsBuilder() : this(MmrOptions.Default)
    {
    }

    internal MmrOptionsBuilder(MmrOptions options)
    {
        RelevanceWeight = options.RelevanceWeight;
        SaturationQuota = options.SaturationQuota;
        SaturationPenalty = options.SaturationPenalty;
    }

    public MmrOptions Build()
    {
        var options = new MmrOptions(
            RelevanceWeight,
            SaturationQuota,
            SaturationPenalty);
        options.Validate();
        return options;
    }
}

public sealed class SearchBuilder
{
    private readonly List<SearchPredicate> _predicates = new();
    private SearchPredicateOptions _defaults = new();
    private WeightedTags? _tags;
    private MmrOptions _mmr = MmrOptions.Default;

    public void Tags(WeightedTags tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        _tags = tags;
    }

    public void Mmr(MmrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _mmr = options;
    }

    public void Mmr(Action<MmrOptionsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new MmrOptionsBuilder(_mmr);
        configure(builder);
        Mmr(builder.Build());
    }

    public void ConfigureDefaults(Action<SearchPredicateOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = _defaults.Copy();
        configure(options);
        _defaults = options;
    }

    public void Configure(
        ExperienceKey key,
        Func<ExperienceList, bool> predicate,
        Action<SearchPredicateOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var options = _defaults.Copy();
        configure?.Invoke(options);
        _predicates.Add(new(key, predicate, options));
    }

    public ExperienceSearch Build()
    {
        if (_tags is null)
        {
            throw new InvalidOperationException("Search tags must be configured before building a search.");
        }

        if (_predicates.Count == 0)
        {
            throw new InvalidOperationException("At least one search predicate must be configured.");
        }

        _mmr.Validate();

        var keys = new HashSet<ExperienceKey>();
        var groups = ImmutableArray.CreateBuilder<ExperienceSelectionGroup>(_predicates.Count);
        for (var i = 0; i < _predicates.Count; i++)
        {
            var predicate = _predicates[i];
            ValidateKey(predicate.Key);
            foreach (var error in SearchPredicateOptionsValidator.ValidateOptions(predicate.Options))
            {
                throw error.ToException();
            }

            if (!keys.Add(predicate.Key))
            {
                throw new InvalidOperationException($"Duplicate experience search key '{predicate.Key}'.");
            }

            groups.Add(new(
                predicate.Key,
                predicate.Predicate,
                new(
                    predicate.Options.MinItemBudget,
                    predicate.Options.ItemBudget,
                    predicate.Options.ScoreLowerBound,
                    predicate.Options.RecencyBoost,
                    predicate.Options.DirectMatchBoost,
                    predicate.Options.IncludeEmptyLists,
                    predicate.Options.PreserveOneItemPerList),
                i));
        }

        return new(
            _tags,
            _mmr,
            groups.DrainToImmutable());
    }

    private static void ValidateKey(ExperienceKey key)
    {
        if (string.IsNullOrWhiteSpace(key.Value))
        {
            throw new InvalidOperationException("Experience search keys cannot be empty.");
        }
    }

    private sealed record SearchPredicate(
        ExperienceKey Key,
        Func<ExperienceList, bool> Predicate,
        SearchPredicateOptions Options);
}

public sealed class ExperienceSearch
{
    private readonly WeightedTags _tags;
    private readonly MmrOptions _mmr;
    private readonly ImmutableArray<ExperienceSelectionGroup> _groups;

    internal ExperienceSearch(
        WeightedTags tags,
        MmrOptions mmr,
        ImmutableArray<ExperienceSelectionGroup> groups)
    {
        _tags = tags;
        _mmr = mmr;
        _groups = groups;
    }

    public SearchResult Run(
        IEnumerable<ExperienceList> experiences,
        IProgressReporter progress)
    {
        ArgumentNullException.ThrowIfNull(experiences);
        ArgumentNullException.ThrowIfNull(progress);
        return Run(
            experiences,
            DateOnly.FromDateTime(DateTime.Today),
            progress);
    }

    internal SearchResult Run(
        IEnumerable<ExperienceList> experiences,
        DateOnly currentDate,
        IProgressReporter progress)
    {
        ArgumentNullException.ThrowIfNull(experiences);
        ArgumentNullException.ThrowIfNull(progress);
        return ExperienceSelectionEngine.Select(
            experiences,
            _tags,
            _mmr,
            _groups,
            UnlimitedSelectionAdmissionPolicy.Instance,
            currentDate,
            progress);
    }

    internal SearchResult Run(
        ExperienceDatabase database,
        ISelectionAdmissionPolicy admissionPolicy,
        IProgressReporter progress)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(admissionPolicy);
        ArgumentNullException.ThrowIfNull(progress);
        return ExperienceSelectionEngine.Select(
            database.Experiences,
            _tags,
            _mmr,
            _groups,
            admissionPolicy,
            DateOnly.FromDateTime(DateTime.Today),
            progress);
    }
}

public sealed class SearchResult
{
    private readonly Dictionary<ExperienceKey, ImmutableArray<Event>> _results;

    internal SearchResult(
        IEnumerable<ExperienceKey> keys,
        IReadOnlyDictionary<ExperienceKey, ImmutableArray<Event>> results,
        SelectionDiagnostics? diagnostics = null)
    {
        Diagnostics = diagnostics ?? SelectionDiagnostics.Empty;
        _results = new();
        foreach (var key in keys)
        {
            _results[key] = results.TryGetValue(key, out var value)
                ? value
                : [];
        }
    }

    public ImmutableArray<Event> Get(ExperienceKey key)
    {
        if (!_results.TryGetValue(key, out var value))
        {
            throw new KeyNotFoundException($"Unknown experience search key '{key}'.");
        }

        return value;
    }

    internal SelectionDiagnostics Diagnostics { get; }
}

public static class SearchBuilderExtensions
{
    extension(SearchBuilder builder)
    {
        public void WeightedTags(
            TagsDatabase db,
            ReadOnlySpan<(Tag Tag, float Weight)> inputs)
        {
            ArgumentNullException.ThrowIfNull(db);
            builder.Tags(db.Weighted(inputs));
        }

        public void WeightedTags(
            TagsDatabase db,
            ReadOnlySpan<(string Tag, float Weight)> inputs)
        {
            ArgumentNullException.ThrowIfNull(db);
            builder.Tags(db.Weighted(inputs));
        }
    }
}

internal readonly record struct ExperienceSelectionOptions(
    int MinItemBudget,
    int ItemBudget,
    float ScoreLowerBound,
    ScoreBoost RecencyBoost,
    ScoreBoost DirectMatchBoost,
    bool IncludeEmptyLists,
    bool PreserveOneItemPerList);

internal sealed record ExperienceSelectionGroup(
    ExperienceKey Key,
    Func<ExperienceList, bool> Predicate,
    ExperienceSelectionOptions Options,
    int Order);

internal interface ISelectionAdmissionPolicy
{
    bool PrioritizeMinimums { get; }

    bool FillAvailableCapacity { get; }

    SelectionAdmissionDecision Evaluate(SelectionAdmission admission);

    void Commit(SelectionAdmission admission);
}

internal readonly record struct SelectionAdmissionDecision(
    SelectionAdmissionRejection? Rejection)
{
    public bool IsAccepted => Rejection is null;

    public static SelectionAdmissionDecision Accepted => default;

    public static SelectionAdmissionDecision Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(new(reason));
    }
}

internal sealed record SelectionAdmissionRejection(string Reason);

internal readonly record struct SelectionAdmission(
    ExperienceSelectionGroup Group,
    ExperienceList List,
    IReadOnlyList<ExperienceListItem> Items);

internal sealed class UnlimitedSelectionAdmissionPolicy : ISelectionAdmissionPolicy
{
    public static UnlimitedSelectionAdmissionPolicy Instance { get; } = new();

    private UnlimitedSelectionAdmissionPolicy()
    {
    }

    public bool PrioritizeMinimums => false;

    public bool FillAvailableCapacity => false;

    public SelectionAdmissionDecision Evaluate(SelectionAdmission admission)
        => SelectionAdmissionDecision.Accepted;

    public void Commit(SelectionAdmission admission)
    {
    }
}

internal enum SelectionItemReason
{
    Direct,
    Dependency,
    RequiredIfAny,
    RequiredAlways,
}

internal sealed record SelectionItemTrace(
    ExperienceKey Section,
    ExperienceList Event,
    ExperienceListItem Item,
    SelectionItemReason Reason,
    float RawScore,
    float DebugScore,
    MmrScoreBreakdown ScoreBreakdown,
    ScoredTags Matches,
    ImmutableArray<DebugTagScore> DebugTagScores,
    ExperienceListItem? DependencyOf);

internal sealed record SelectionBudgetTrace(
    ExperienceKey Section,
    int RequestedMinimum,
    int RequestedMaximum,
    int ActualCount,
    int RemainingMaximumBudget);

internal sealed record SelectionDiagnostics(
    ImmutableArray<SelectionItemTrace> Items,
    ImmutableArray<SelectionBudgetTrace> Budgets)
{
    public static SelectionDiagnostics Empty { get; } = new([], []);

    public static SelectionDiagnostics CreateEmpty(
        ImmutableArray<ExperienceSelectionGroup> groups)
    {
        return new(
            [],
            groups
                .Select(x => new SelectionBudgetTrace(
                    x.Key,
                    x.Options.MinItemBudget,
                    x.Options.ItemBudget,
                    ActualCount: 0,
                    RemainingMaximumBudget: x.Options.ItemBudget))
                .ToImmutableArray());
    }
}

internal static class ExperienceSelectionEngine
{
    public static SearchResult Select(
        IEnumerable<ExperienceList> lists,
        WeightedTags tags,
        MmrOptions mmr,
        ImmutableArray<ExperienceSelectionGroup> groups,
        ISelectionAdmissionPolicy admissionPolicy,
        DateOnly currentDate,
        IProgressReporter progress)
    {
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(mmr);
        ArgumentNullException.ThrowIfNull(admissionPolicy);
        ArgumentNullException.ThrowIfNull(progress);

        mmr.Validate();

        var materializedLists = lists.ToList();
        foreach (var list in materializedLists)
        {
            list.ValidateItemConfiguration();
        }
        var progressTracker = new SelectionProgressTracker(
            materializedLists.Sum(static list => list.Items.Length),
            progress);
        var groupedLists = materializedLists
            .OrderByDescending(x => x.DateRange, DateRangeComparer.ByEnd)
            .Select((list, listIndex) => CreateScoredList(
                list,
                listIndex,
                FindGroup(list, groups),
                tags,
                progressTracker))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        var scoredLists = groupedLists
            .Where(x => !x.CandidateItems.IsEmpty)
            .ToList();

        var appliedRecencyBoosts = CalculateAppliedRecencyBoosts(
            scoredLists,
            currentDate);

        var candidates = scoredLists
            .SelectMany(x => x.Candidates(
                appliedRecencyBoosts.GetValueOrDefault(x.List)))
            .ToList();

        var alwaysCandidates = groupedLists
            .Where(x => x.Group.Options.ItemBudget > 0)
            .SelectMany(x => x.AlwaysCandidates(
                appliedRecencyBoosts.GetValueOrDefault(x.List)))
            .ToList();

        var context = new SelectionContext(groups, admissionPolicy);
        foreach (var scoredList in groupedLists.Where(x => x.Group.Options.IncludeEmptyLists))
        {
            context.RequireList(
                scoredList.Group,
                scoredList.List,
                scoredList.ListIndex);
        }

        if (candidates.Count == 0 && alwaysCandidates.Count == 0)
        {
            return CompleteOutput();
        }

        var ranker = new MmrRanker(
            mmr,
            candidates
                .Concat(alwaysCandidates)
                .Max(x => x.AdjustedPreMmrRelevance));

        // Required items can bypass normal candidate filtering. Keep scores for
        // every item so all content committed through a required closure is
        // registered with MMR before subsequent candidates are ranked.
        foreach (var scoredList in groupedLists)
        {
            foreach (var item in scoredList.Items)
            {
                context.Scores.TryAdd(
                    item.Item,
                    item.Matches);
            }
        }

        foreach (var candidate in alwaysCandidates)
        {
            TryAddAndRegister(
                candidate,
                SelectionItemReason.RequiredAlways,
                allowExceedingBudget: true);
        }

        var rejected = new HashSet<ExperienceListItem>(ItemReferenceComparer.Instance);
        if (admissionPolicy.PrioritizeMinimums)
        {
            FillMinimums();
        }

        foreach (var scoredList in scoredLists.Where(
                     static x => x.Group.Options.PreserveOneItemPerList))
        {
            var best = ranker.BestCandidate(
                scoredList.Candidates(
                    appliedRecencyBoosts.GetValueOrDefault(scoredList.List)),
                context.Added);

            if (best is { } candidate)
            {
                if (!TryAddAndRegister(candidate))
                {
                    rejected.Add(candidate.Item);
                }
            }
        }

        while (context.HasRemainingBudget)
        {
            var next = ranker.BestCandidate(candidates, context.Added, rejected);
            if (next is null)
            {
                break;
            }

            if (!admissionPolicy.FillAvailableCapacity
                && ranker.Score(next.Value) <= 0)
            {
                break;
            }

            if (!TryAddAndRegister(next.Value))
            {
                rejected.Add(next.Value.Item);
            }
        }

        if (!admissionPolicy.PrioritizeMinimums)
        {
            FillMinimums();
        }

        return CompleteOutput();

        SearchResult CompleteOutput()
        {
            progressTracker.ResolveRemaining(
                capacityWasFilled: !context.HasRemainingBudget);
            var output = context.Output();
            progressTracker.CompleteAssembly();
            return output;
        }

        void FillMinimums()
        {
            while (context.HasUnmetMinimum)
            {
                var next = ranker.BestCandidate(
                    candidates.Where(context.IsBelowMinimum),
                    context.Added,
                    rejected);
                if (next is null)
                {
                    break;
                }

                if (!TryAddAndRegister(next.Value))
                {
                    rejected.Add(next.Value.Item);
                }
            }
        }

        bool TryAddAndRegister(
            MmrCandidate candidate,
            SelectionItemReason reason = SelectionItemReason.Direct,
            bool allowExceedingBudget = false)
        {
            var accepted = context.TryAdd(
                candidate,
                reason,
                allowExceedingBudget,
                (added, addedReason) =>
                {
                    if (context.Scores.TryGetValue(added, out var matches))
                    {
                        context.ScoreBreakdowns[added] = ranker.AddSelected(
                            matches,
                            candidate.AppliedRecencyBoost);
                    }
                    progressTracker.ItemResolved(
                        added,
                        addedReason is SelectionItemReason.Dependency
                            or SelectionItemReason.RequiredAlways
                            or SelectionItemReason.RequiredIfAny
                            ? "Matching experiences — required or dependent item resolved"
                            : "Matching experiences — candidate selected");
                });
            if (!accepted)
            {
                progressTracker.ItemResolved(
                    candidate.Item,
                    "Matching experiences — candidate rejected");
            }

            return accepted;
        }
    }

    private static Dictionary<ExperienceList, ScoreBoost> CalculateAppliedRecencyBoosts(
        IEnumerable<ScoredList> scoredLists,
        DateOnly currentDate)
    {
        var result = new Dictionary<ExperienceList, ScoreBoost>();

        foreach (var section in scoredLists.GroupBy(x => x.Group.Key))
        {
            var lists = section.ToArray();
            var recencyBoost = lists[0].Group.Options.RecencyBoost;
            if (recencyBoost.IsZero)
            {
                continue;
            }

            var datedLists = lists
                .Select(x => (
                    x.List,
                    EndDate: EffectiveEndDate(x.List.DateRange, currentDate)))
                .ToArray();
            var oldestDay = datedLists.Min(x => x.EndDate.DayNumber);
            var newestDay = datedLists.Max(x => x.EndDate.DayNumber);
            if (oldestDay == newestDay)
            {
                continue;
            }

            var dateRange = newestDay - oldestDay;
            foreach (var (list, endDate) in datedLists)
            {
                var normalizedRecency = (endDate.DayNumber - oldestDay) / (float) dateRange;
                result[list] = recencyBoost.Scale(
                    Math.Clamp(normalizedRecency, 0, 1));
            }
        }

        return result;
    }

    private static DateOnly EffectiveEndDate(
        DateRange dateRange,
        DateOnly currentDate)
    {
        if (dateRange.IsCurrent)
        {
            return currentDate;
        }

        var end = dateRange.End;
        return new(
            end.Year,
            end.Month == 0 ? 1 : end.Month,
            end.Day == 0 ? 1 : end.Day);
    }

    private static ScoredList? CreateScoredList(
        ExperienceList list,
        int listIndex,
        ExperienceSelectionGroup? group,
        WeightedTags tags,
        SelectionProgressTracker progress)
    {
        if (group is null)
        {
            foreach (var item in list.Items)
            {
                progress.ItemScanned(item);
                progress.ItemResolved(
                    item,
                    "Matching experiences — item is outside configured sections");
            }
            return null;
        }

        var items = ImmutableArray.CreateBuilder<ScoredItem>(list.Items.Length);
        var candidateItems = ImmutableArray.CreateBuilder<ScoredItem>();
        foreach (var (item, itemIndex) in list.Items.Select(
                     static (item, index) => (item, index)))
        {
            var matches = tags.Match(
                item.Tags,
                group.Options.DirectMatchBoost);
            var scoredItem = new ScoredItem(item, matches, itemIndex);
            items.Add(scoredItem);
            progress.ItemScanned(item);
            if (matches.IsEmpty
                || matches.Sum < group.Options.ScoreLowerBound)
            {
                progress.ItemResolved(
                    item,
                    "Matching experiences — candidate rejected by score");
                continue;
            }

            candidateItems.Add(scoredItem);
        }

        candidateItems.Sort(CompareCandidateItems);

        return new(
            group,
            list,
            items.DrainToImmutable(),
            candidateItems.DrainToImmutable(),
            listIndex);
    }

    private static int CompareCandidateItems(
        ScoredItem left,
        ScoredItem right)
    {
        var comparison = right.Matches.Sum.CompareTo(left.Matches.Sum);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.Matches.Count.CompareTo(left.Matches.Count);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.Item.Tags.Length.CompareTo(left.Item.Tags.Length);
        return comparison != 0
            ? comparison
            : left.OriginalIndex.CompareTo(right.OriginalIndex);
    }

    private static ExperienceSelectionGroup? FindGroup(
        ExperienceList list,
        ImmutableArray<ExperienceSelectionGroup> groups)
    {
        ExperienceSelectionGroup? match = null;
        foreach (var group in groups)
        {
            if (!group.Predicate(list))
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidOperationException(
                    $"Experience list '{list.Title}' matched multiple search predicates: '{match.Key}', '{group.Key}'.");
            }

            match = group;
        }

        return match;
    }

    private readonly record struct ScoredItem(
        ExperienceListItem Item,
        ScoredTags Matches,
        int OriginalIndex);

    private sealed record ScoredList(
        ExperienceSelectionGroup Group,
        ExperienceList List,
        ImmutableArray<ScoredItem> Items,
        ImmutableArray<ScoredItem> CandidateItems,
        int ListIndex)
    {
        public IEnumerable<MmrCandidate> Candidates(
            ScoreBoost appliedRecencyBoost)
        {
            return CandidateItems.Select((item, candidateIndex) =>
                CreateCandidate(item, appliedRecencyBoost, candidateIndex));
        }

        public IEnumerable<MmrCandidate> AlwaysCandidates(
            ScoreBoost appliedRecencyBoost)
        {
            return Items
                .Where(static item =>
                    item.Item.Required == ItemRequirement.Always)
                .Select(item => CreateCandidate(
                    item,
                    appliedRecencyBoost,
                    item.OriginalIndex));
        }

        private MmrCandidate CreateCandidate(
            ScoredItem item,
            ScoreBoost appliedRecencyBoost,
            int itemIndex)
        {
            return new(
                Group,
                List,
                item.Item,
                item.Matches,
                appliedRecencyBoost,
                ListIndex,
                itemIndex);
        }
    }

    private sealed class SelectionProgressTracker
    {
        private readonly int _itemCount;
        private readonly int _totalWorkUnits;
        private readonly IProgressReporter _progress;
        private readonly HashSet<ExperienceListItem> _resolved =
            new(ItemReferenceComparer.Instance);
        private int _scannedCount;
        private int _resolvedCount;

        public SelectionProgressTracker(
            int itemCount,
            IProgressReporter progress)
        {
            if (itemCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemCount));
            }

            ArgumentNullException.ThrowIfNull(progress);
            _itemCount = itemCount;
            _totalWorkUnits = checked(itemCount * 2 + 1);
            _progress = progress;
            Report("Matching experiences");
        }

        public void ItemScanned(ExperienceListItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            _scannedCount++;
            Report("Matching experiences — item scanned and scored");
        }

        public void ItemResolved(
            ExperienceListItem item,
            string detail)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(detail);
            if (!_resolved.Add(item))
            {
                Report(detail);
                return;
            }

            _resolvedCount++;
            Report(detail);
        }

        public void ResolveRemaining(bool capacityWasFilled)
        {
            if (_resolvedCount >= _itemCount)
            {
                return;
            }

            _resolvedCount = _itemCount;
            Report(
                capacityWasFilled
                    ? "Matching experiences — candidates skipped after capacity was filled"
                    : "Matching experiences — remaining candidate work credited");
        }

        public void CompleteAssembly()
        {
            _scannedCount = _itemCount;
            _resolvedCount = _itemCount;
            _progress.Report(new(
                CompletedWorkUnits: _totalWorkUnits,
                TotalWorkUnits: _totalWorkUnits,
                Detail: "Matching experiences"));
        }

        private void Report(string detail)
        {
            _progress.Report(new(
                CompletedWorkUnits: _scannedCount + _resolvedCount,
                TotalWorkUnits: _totalWorkUnits,
                Detail: detail));
        }
    }

    private readonly record struct MmrCandidate(
        ExperienceSelectionGroup Group,
        ExperienceList List,
        ExperienceListItem Item,
        ScoredTags Matches,
        ScoreBoost AppliedRecencyBoost,
        int ListIndex,
        int ItemIndex)
    {
        public float RecencyBonus =>
            AppliedRecencyBoost.Apply(Matches.BaseRelevance);

        public float AdjustedPreMmrRelevance => Matches.Sum + RecencyBonus;
    }

    private sealed class MmrRanker(
        MmrOptions options,
        float maxRelevance)
    {
        private readonly List<ScoredTags> _selected = new();
        private readonly Dictionary<Tag, int>
            _selectedRequirementCounts = new();
        private int _selectionOrdinal;

        public MmrCandidate? BestCandidate(
            IEnumerable<MmrCandidate> candidates,
            HashSet<ExperienceListItem> added,
            HashSet<ExperienceListItem>? rejected = null)
        {
            MmrCandidate? best = null;
            float bestScore = float.NegativeInfinity;

            foreach (var candidate in candidates)
            {
                if (added.Contains(candidate.Item) ||
                    rejected?.Contains(candidate.Item) == true)
                {
                    continue;
                }

                var score = Score(candidate);
                if (best is null ||
                    score > bestScore ||
                    (score == bestScore && BreakTie(candidate, best.Value) < 0))
                {
                    best = candidate;
                    bestScore = score;
                }
            }

            return best;
        }

        public MmrScoreBreakdown AddSelected(
            ScoredTags matches,
            ScoreBoost appliedRecencyBoost)
        {
            var breakdown = Breakdown(
                matches,
                appliedRecencyBoost,
                ++_selectionOrdinal);
            _selected.Add(matches);

            foreach (var (requirement, coverage) in matches.RequirementCoverage)
            {
                if (coverage <= 0)
                {
                    continue;
                }

                ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(
                    _selectedRequirementCounts,
                    requirement,
                    out _);
                count += 1;
            }

            return breakdown;
        }

        public float Score(MmrCandidate candidate)
        {
            return Score(
                candidate.Matches,
                candidate.AppliedRecencyBoost);
        }

        private float Score(
            ScoredTags matches,
            ScoreBoost appliedRecencyBoost)
            => Breakdown(
                matches,
                appliedRecencyBoost,
                selectionOrdinal: 0).NormalizedMmrScore;

        private MmrScoreBreakdown Breakdown(
            ScoredTags matches,
            ScoreBoost appliedRecencyBoost,
            int selectionOrdinal)
        {
            var recencyBonus = appliedRecencyBoost.Apply(
                matches.BaseRelevance);
            var adjustedPreMmrRelevance = matches.Sum + recencyBonus;
            var relevance = maxRelevance <= 0
                ? 0
                : adjustedPreMmrRelevance / maxRelevance;
            var redundancy = MaxSimilarity(matches);
            var saturation = Saturation(matches);
            var weightedRelevance = options.RelevanceWeight * relevance;
            var weightedSimilarity =
                (1 - options.RelevanceWeight) * redundancy;
            var weightedSaturation =
                options.SaturationPenalty * saturation;
            var score = weightedRelevance
                - weightedSimilarity
                - weightedSaturation;

            return new(
                SelectionOrdinal: selectionOrdinal,
                BaseRelevance: matches.BaseRelevance,
                DirectMatchBonus: matches.DirectMatchBonus,
                RawRelevance: matches.Sum,
                AppliedRecencyBoost: appliedRecencyBoost.Value,
                RecencyBonus: recencyBonus,
                AdjustedPreMmrRelevance: adjustedPreMmrRelevance,
                NormalizedRelevance: relevance,
                MaximumCosineSimilarity: redundancy,
                Saturation: saturation,
                WeightedRelevanceTerm: weightedRelevance,
                WeightedSimilarityPenalty: weightedSimilarity,
                WeightedSaturationPenalty: weightedSaturation,
                NormalizedMmrScore: score);
        }

        private float MaxSimilarity(ScoredTags matches)
        {
            float ret = 0;

            foreach (var selected in _selected)
            {
                ret = Math.Max(ret, CosineSimilarity(matches, selected));
            }

            return ret;
        }

        private float Saturation(ScoredTags matches)
        {
            float totalCoverage = 0;
            foreach (var (_, coverage) in OrderedCoverage(
                         matches.RequirementCoverage))
            {
                totalCoverage += coverage;
            }

            if (totalCoverage <= 0)
            {
                return 0;
            }

            float ret = 0;
            foreach (var (requirement, coverage) in OrderedCoverage(
                         matches.RequirementCoverage))
            {
                if (coverage <= 0)
                {
                    continue;
                }

                var selectedCount =
                    _selectedRequirementCounts.GetValueOrDefault(requirement);
                var overQuota = selectedCount - options.SaturationQuota + 1;
                if (overQuota <= 0)
                {
                    continue;
                }

                ret += (coverage / totalCoverage) * overQuota;
            }

            return ret;
        }

        private static float CosineSimilarity(
            ScoredTags a,
            ScoredTags b)
        {
            var aCoverage = a.RequirementCoverage;
            var bCoverage = b.RequirementCoverage;
            if (aCoverage.Count == 0 || bCoverage.Count == 0)
            {
                return 0;
            }

            float dot = 0;
            var smaller = aCoverage.Count <= bCoverage.Count
                ? aCoverage
                : bCoverage;
            var larger = ReferenceEquals(smaller, aCoverage)
                ? bCoverage
                : aCoverage;

            foreach (var (requirement, score) in OrderedCoverage(smaller))
            {
                if (larger.TryGetValue(requirement, out var otherScore))
                {
                    dot += score * otherScore;
                }
            }

            if (dot <= 0)
            {
                return 0;
            }

            var norm = MathF.Sqrt(
                SquaredLength(aCoverage)
                * SquaredLength(bCoverage));
            if (norm <= 0)
            {
                return 0;
            }

            return dot / norm;
        }

        private static float SquaredLength(
            IReadOnlyDictionary<Tag, float> coverage)
        {
            float ret = 0;
            foreach (var (_, score) in OrderedCoverage(coverage))
            {
                ret += score * score;
            }

            return ret;
        }

        private static IEnumerable<
            KeyValuePair<Tag, float>> OrderedCoverage(
            IReadOnlyDictionary<Tag, float> coverage)
        {
            return coverage.OrderBy(
                x => x.Key.Name,
                StringComparer.OrdinalIgnoreCase);
        }

        private static int BreakTie(
            MmrCandidate left,
            MmrCandidate right)
        {
            var relevance = right.AdjustedPreMmrRelevance.CompareTo(
                left.AdjustedPreMmrRelevance);
            if (relevance != 0)
            {
                return relevance;
            }

            var matchCount = right.Matches.Count.CompareTo(left.Matches.Count);
            if (matchCount != 0)
            {
                return matchCount;
            }

            var tagCount = right.Item.Tags.Length.CompareTo(left.Item.Tags.Length);
            if (tagCount != 0)
            {
                return tagCount;
            }

            var listOrder = left.ListIndex.CompareTo(right.ListIndex);
            if (listOrder != 0)
            {
                return listOrder;
            }

            var itemOrder = left.ItemIndex.CompareTo(right.ItemIndex);
            if (itemOrder != 0)
            {
                return itemOrder;
            }

            return left.Group.Order.CompareTo(right.Group.Order);
        }
    }

    private sealed class ItemReferenceComparer : IEqualityComparer<ExperienceListItem>
    {
        public static readonly ItemReferenceComparer Instance = new();

        private ItemReferenceComparer()
        {
        }

        public bool Equals(ExperienceListItem? x, ExperienceListItem? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(ExperienceListItem obj)
            => RuntimeHelpers.GetHashCode(obj);
    }

    private readonly record struct PendingSelectedItem(
        ExperienceListItem Item,
        SelectionItemReason Reason);

    private sealed class SelectionContext
    {
        private readonly ImmutableArray<ExperienceSelectionGroup> _groups;
        private readonly ISelectionAdmissionPolicy _admissionPolicy;
        private readonly Dictionary<ExperienceKey, int> _remainingMaximumBudgets = new();
        private readonly List<PendingSelectedItem> _temp = new();
        private readonly HashSet<ExperienceListItem> _tempVisited = new(ItemReferenceComparer.Instance);
        private readonly HashSet<ExperienceListItem> _tempVisiting = new(ItemReferenceComparer.Instance);
        private readonly Dictionary<ExperienceList, int> _listOrders = new();

        public readonly HashSet<ExperienceListItem> Added = new(ItemReferenceComparer.Instance);
        public readonly Dictionary<ExperienceKey, Dictionary<ExperienceList, List<ExperienceListItem>>> Results = new();
        public readonly Dictionary<ExperienceListItem, ScoredTags> Scores = new();
        public readonly Dictionary<ExperienceListItem, MmrScoreBreakdown>
            ScoreBreakdowns = new();
        public readonly Dictionary<ExperienceListItem, SelectionItemReason> Reasons = new(ItemReferenceComparer.Instance);
        public readonly Dictionary<ExperienceListItem, ExperienceListItem> DependencyTargets = new(ItemReferenceComparer.Instance);

        public SelectionContext(
            ImmutableArray<ExperienceSelectionGroup> groups,
            ISelectionAdmissionPolicy admissionPolicy)
        {
            _groups = groups;
            _admissionPolicy = admissionPolicy;
            foreach (var group in groups)
            {
                _remainingMaximumBudgets.Add(group.Key, group.Options.ItemBudget);
            }
        }

        public bool HasRemainingBudget => _remainingMaximumBudgets.Values.Any(x => x > 0);

        public bool HasUnmetMinimum => _groups.Any(IsBelowMinimum);

        public bool IsBelowMinimum(MmrCandidate candidate) => IsBelowMinimum(candidate.Group);

        public void RequireList(
            ExperienceSelectionGroup group,
            ExperienceList list,
            int listIndex)
        {
            var admission = new SelectionAdmission(group, list, []);
            var decision = _admissionPolicy.Evaluate(admission);
            if (!decision.IsAccepted)
            {
                throw new RequiredExperienceHeadingLayoutException(
                    list.Title.Value,
                    decision.Rejection!.Reason);
            }

            ref var groupResults = ref CollectionsMarshal.GetValueRefOrAddDefault(
                Results,
                group.Key,
                out _);
            groupResults ??= new();
            groupResults.TryAdd(list, []);
            _listOrders.TryAdd(list, listIndex);
            _admissionPolicy.Commit(admission);
        }

        public bool TryAdd(
            MmrCandidate candidate,
            SelectionItemReason reason = SelectionItemReason.Direct,
            bool allowExceedingBudget = false,
            Action<ExperienceListItem, SelectionItemReason>? onAdded = null)
        {
            if (Added.Contains(candidate.Item))
            {
                if (reason == SelectionItemReason.RequiredAlways)
                {
                    Reasons[candidate.Item] = reason;
                    DependencyTargets.Remove(candidate.Item);
                }

                return true;
            }

            if (!allowExceedingBudget &&
                _remainingMaximumBudgets[candidate.Group.Key] <= 0)
            {
                return false;
            }

            _temp.Clear();
            CollectSelectionClosure(candidate.List, candidate.Item, reason);

            if (_temp.Count == 0)
            {
                return false;
            }


            var closure = new HashSet<ExperienceListItem>(
                _temp.Select(static pending => pending.Item),
                ItemReferenceComparer.Instance);
            foreach (var exclusionSet in candidate.List.ItemExclusionSets)
            {
                var closureMembers = exclusionSet.Items.Count(closure.Contains);
                if (closureMembers > 1)
                {
                    throw new InvalidOperationException(
                        $"Selection closure for experience '{candidate.List.Title.Value}' contains mutually exclusive items.");
                }

                if (closureMembers == 1 && exclusionSet.Items.Any(Added.Contains))
                {
                    if (reason == SelectionItemReason.RequiredAlways)
                    {
                        throw new InvalidOperationException(
                            $"Experience '{candidate.List.Title.Value}' has mutually exclusive Required().Always() items that both require selection.");
                    }

                    return false;
                }
            }

            var pendingItems = _temp.Select(static pending => pending.Item).ToArray();
            var admission = new SelectionAdmission(candidate.Group, candidate.List, pendingItems);
            var decision = _admissionPolicy.Evaluate(admission);
            if (!decision.IsAccepted)
            {
                if (reason == SelectionItemReason.RequiredAlways)
                {
                    throw new RequiredExperienceItemLayoutException(
                        candidate.List.Title.Value,
                        candidate.Item.Text.ToString() ?? string.Empty,
                        decision.Rejection!.Reason);
                }

                return false;
            }

            ref var groupResults = ref CollectionsMarshal.GetValueRefOrAddDefault(
                Results,
                candidate.Group.Key,
                out _);
            groupResults ??= new();

            ref var listItems = ref CollectionsMarshal.GetValueRefOrAddDefault(
                groupResults,
                candidate.List,
                out _);
            listItems ??= new();
            _listOrders.TryAdd(candidate.List, candidate.ListIndex);

            foreach (var pending in _temp)
            {
                var item = pending.Item;
                listItems.Add(item);
                Added.Add(item);
                Reasons.TryAdd(item, pending.Reason);
                if (pending.Reason == SelectionItemReason.Dependency)
                {
                    DependencyTargets.TryAdd(item, candidate.Item);
                }

                onAdded?.Invoke(item, pending.Reason);
            }

            _remainingMaximumBudgets[candidate.Group.Key] -= _temp.Count;
            _admissionPolicy.Commit(admission);
            return true;
        }

        public SearchResult Output()
        {
            var results = new Dictionary<ExperienceKey, ImmutableArray<Event>>();
            var itemTraces = ImmutableArray.CreateBuilder<SelectionItemTrace>();
            var budgetTraces = ImmutableArray.CreateBuilder<SelectionBudgetTrace>();

            foreach (var group in _groups)
            {
                var actualCount = 0;
                if (Results.TryGetValue(group.Key, out var groupResults))
                {
                    var outputEvents = ImmutableArray.CreateBuilder<OutputEvent>();
                    foreach (var (list, items) in groupResults
                        .OrderBy(x => _listOrders.GetValueOrDefault(x.Key, int.MaxValue)))
                    {
                        var sortedItems = list
                            .OrderItems(items)
                            .ToImmutableArray();

                        actualCount += sortedItems.Length;

                        float totalScore = 0;
                        var outputItems = ImmutableArray.CreateBuilder<OutputItem>(sortedItems.Length);
                        foreach (var item in sortedItems)
                        {
                            var matches = MatchesOf(item);
                            var scoreBreakdown = ScoreBreakdownOf(item);
                            var debugScore =
                                scoreBreakdown.NormalizedMmrScore;
                            totalScore += debugScore;
                            outputItems.Add(new(
                                item,
                                matches,
                                scoreBreakdown));

                            itemTraces.Add(new(
                                group.Key,
                                list,
                                item,
                                Reasons.GetValueOrDefault(item, SelectionItemReason.Direct),
                                matches.Sum,
                                debugScore,
                                scoreBreakdown,
                                matches,
                                ToDebugTagScores([matches]),
                                DependencyTargets.GetValueOrDefault(item)));
                        }

                        outputEvents.Add(new(
                            list,
                            totalScore,
                            outputItems.DrainToImmutable()));
                    }

                    results[group.Key] = ToOutput(outputEvents);
                }

                budgetTraces.Add(new(
                    group.Key,
                    group.Options.MinItemBudget,
                    group.Options.ItemBudget,
                    actualCount,
                    _remainingMaximumBudgets[group.Key]));
            }

            return new(
                _groups.Select(x => x.Key),
                results,
                new(
                    itemTraces.DrainToImmutable(),
                    budgetTraces.DrainToImmutable()));

            ScoredTags MatchesOf(ExperienceListItem item)
            {
                return Scores.TryGetValue(item, out var score)
                    ? score
                    : EmptyScoredTags.Instance;
            }

            MmrScoreBreakdown ScoreBreakdownOf(ExperienceListItem item)
            {
                return ScoreBreakdowns.TryGetValue(item, out var breakdown)
                    ? breakdown
                    : throw new InvalidOperationException(
                        "Selected experience item did not have an MMR score breakdown.");
            }
        }

        private bool IsBelowMinimum(ExperienceSelectionGroup group)
        {
            var actualCount = group.Options.ItemBudget - _remainingMaximumBudgets[group.Key];
            return actualCount < group.Options.MinItemBudget;
        }

        private void CollectSelectionClosure(
            ExperienceList list,
            ExperienceListItem item,
            SelectionItemReason reason)
        {
            _tempVisited.Clear();
            _tempVisiting.Clear();

            Visit(item, reason);

            foreach (var requiredItem in list.Items.Where(
                static sibling => sibling.Required == ItemRequirement.IfAny))
            {
                if (!ReferenceEquals(requiredItem, item))
                {
                    Visit(requiredItem, SelectionItemReason.RequiredIfAny);
                }
            }
        }

        private void Visit(
            ExperienceListItem item,
            SelectionItemReason reason)
        {
            if (Added.Contains(item))
            {
                return;
            }

            if (_tempVisited.Contains(item))
            {
                return;
            }

            if (!_tempVisiting.Add(item))
            {
                throw new InvalidOperationException(
                    "Cycle detected in DependsOn relationships while collecting dependency closure.");
            }

            foreach (var dependency in item.DependsOn)
            {
                if (dependency is null)
                {
                    continue;
                }

                Visit(dependency, SelectionItemReason.Dependency);
            }

            _tempVisiting.Remove(item);
            _tempVisited.Add(item);
            _temp.Add(new(item, reason));
        }
    }

    private readonly record struct OutputEvent(
        ExperienceList List,
        float TotalScore,
        IEnumerable<OutputItem> Items);

    private readonly record struct OutputItem(
        ExperienceListItem Item,
        ScoredTags Matches,
        MmrScoreBreakdown ScoreBreakdown);

    private static ImmutableArray<Event> ToOutput(IEnumerable<OutputEvent> s)
    {
        var builder = ImmutableArray.CreateBuilder<Event>();
        var subBuilder = ImmutableArray.CreateBuilder<SubEvent>();

        foreach (var t in s)
        {
            var items = t.Items.ToList();
            var hasItems = items.Count > 0;
            foreach (var x in items)
            {
                subBuilder.Add(new(
                    x.Item.Text,
                    ToDebugInfo(x.ScoreBreakdown, x.Matches)));
            }

            builder.Add(new()
            {
                DateRange = t.List.DateRange,
                Place = t.List.Place,
                Title = t.List.Title,
                Text = hasItems ? t.List.Description : null,
                DebugInfo = ToDebugInfo(
                    t.TotalScore,
                    items.Select(x => x.Matches)),
                SubItems = subBuilder.ToImmutable(),
                Urls = hasItems ? t.List.Urls : [],
            });
            subBuilder.Clear();
        }

        return builder.DrainToImmutable();
    }

    private static SelectionDebugInfo ToDebugInfo(
        MmrScoreBreakdown scoreBreakdown,
        ScoredTags matches)
    {
        return ToDebugInfo(
            scoreBreakdown.NormalizedMmrScore,
            [matches],
            scoreBreakdown);
    }

    private static SelectionDebugInfo ToDebugInfo(
        float score,
        IEnumerable<ScoredTags> matches,
        MmrScoreBreakdown? scoreBreakdown = null)
    {
        var materializedMatches = matches.ToImmutableArray();
        return new()
        {
            Score = score,
            RawScore = materializedMatches.Sum(static x => x.Sum),
            TagScores = ToDebugTagScores(materializedMatches),
            RequirementCoverage = ToDebugRequirementCoverage(
                materializedMatches),
            TagMatches = ToDebugTagMatches(materializedMatches),
            MmrScoreBreakdown = scoreBreakdown,
        };
    }

    private static ImmutableArray<DebugTagScore> ToDebugTagScores(
        IEnumerable<ScoredTags> matches)
    {
        var totals = new Dictionary<Tag, float>();
        foreach (var scoredTags in matches)
        {
            foreach (var (tag, score) in scoredTags)
            {
                AddScore(totals, tag, score);
            }
        }

        if (totals.Count == 0)
        {
            return [];
        }

        return totals
            .Where(x => IsMeaningfulScore(x.Value))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DebugTagScore(x.Key.Name, x.Value))
            .ToImmutableArray();
    }

    private static ImmutableArray<DebugRequirementCoverage>
        ToDebugRequirementCoverage(IEnumerable<ScoredTags> matches)
    {
        var totals = new Dictionary<RequiredTagGroup, float>();
        foreach (var scoredTags in matches)
        {
            foreach (var (requirement, score) in scoredTags.RequirementGroupCoverage)
            {
                AddScore(totals, requirement, score);
            }
        }

        return totals
            .Where(x => IsMeaningfulScore(x.Value))
            .OrderByDescending(x => x.Value)
            .ThenBy(
                x => x.Key.CanonicalTag.Name,
                StringComparer.OrdinalIgnoreCase)
            .Select(x => new DebugRequirementCoverage(x.Key, x.Value))
            .ToImmutableArray();
    }

    private static ImmutableArray<DebugTagMatch> ToDebugTagMatches(
        IEnumerable<ScoredTags> matches)
    {
        var contributionTotals = new Dictionary<
            Tag,
            DebugMatchContributionTotals>();
        var originTotals =
            new Dictionary<
                (Tag Target, RequiredTagGroup Requirement),
                DebugOriginContributionTotal>();
        foreach (var scoredTags in matches)
        {
            foreach (var match in scoredTags.Matches)
            {
                contributionTotals[match.TargetTag] = contributionTotals
                    .GetValueOrDefault(match.TargetTag)
                    .Add(match);
                foreach (var origin in match.Projection.Origins)
                {
                    var key = (match.TargetTag, origin.RequiredTagGroup);
                    originTotals[key] = originTotals
                        .GetValueOrDefault(key)
                        .Add(
                            match.EvidenceScore * origin.Coefficient,
                            origin.IsDirect);
                }
            }
        }

        return contributionTotals
            .Where(x => IsMeaningfulScore(x.Value.Relevance))
            .OrderByDescending(x => x.Value.Relevance)
            .ThenBy(x => x.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(target => new DebugTagMatch(
                target.Key,
                target.Value.BaseContribution,
                target.Value.DirectContribution,
                target.Value.DirectMatchBonus,
                target.Value.Relevance,
                originTotals
                    .Where(x => x.Key.Target == target.Key)
                    .Where(x => IsMeaningfulScore(x.Value.Contribution))
                    .OrderByDescending(x => x.Value.Contribution)
                    .ThenBy(
                        x => x.Key.Requirement.CanonicalTag.Name,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(x => new DebugTagMatchOrigin(
                        x.Key.Requirement,
                        x.Value.Contribution,
                        x.Value.IsDirect))
                    .ToImmutableArray()))
            .ToImmutableArray();
    }

    private static void AddScore<TKey>(
        Dictionary<TKey, float> totals,
        TKey key,
        float score)
        where TKey : notnull
    {
        totals[key] = totals.GetValueOrDefault(key) + score;
    }

    private readonly record struct DebugMatchContributionTotals(
        float BaseContribution,
        float DirectContribution,
        float DirectMatchBonus,
        float Relevance)
    {
        public DebugMatchContributionTotals Add(ScoredTagMatch match)
        {
            return new(
                BaseContribution + match.BaseContribution,
                DirectContribution + match.DirectContribution,
                DirectMatchBonus + match.DirectMatchBonus,
                Relevance + match.RelevanceContribution);
        }
    }

    private readonly record struct DebugOriginContributionTotal(
        float Contribution,
        bool IsDirect)
    {
        public DebugOriginContributionTotal Add(
            float contribution,
            bool isDirect)
        {
            return new(
                Contribution + contribution,
                IsDirect || isDirect);
        }
    }

    private static bool IsMeaningfulScore(float score)
    {
        return MathF.Abs(score) >= 0.0001f;
    }

    private static class EmptyScoredTags
    {
        public static readonly ScoredTags Instance = ScoredTags.Empty;
    }
}
