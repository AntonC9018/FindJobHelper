using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

public readonly record struct ExperienceKey(string Value)
{
    public override string ToString() => Value ?? "";
}

public sealed class SearchPredicateOptions
{
    public int MinTotalItemBudget { get; set; } = 0;
    public int TotalItemBudget { get; set; } = int.MaxValue;

    public float ScoreLowerBound { get; set; }
    public float RecencyBoost { get; set; }
    public bool IncludeEmptyLists { get; set; }
    public bool PreserveOneItemPerList { get; set; } = true;

    internal SearchPredicateOptions Copy()
    {
        return new()
        {
            MinTotalItemBudget = MinTotalItemBudget,
            TotalItemBudget = TotalItemBudget,
            ScoreLowerBound = ScoreLowerBound,
            RecencyBoost = RecencyBoost,
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

        if (options.MinTotalItemBudget < 0)
        {
            yield return new(
                nameof(options.MinTotalItemBudget),
                "must be non-negative.",
                IsOutOfRange: true);
        }

        if (options.TotalItemBudget < 0)
        {
            yield return new(
                nameof(options.TotalItemBudget),
                "must be non-negative.",
                IsOutOfRange: true);
        }
        else if (options.MinTotalItemBudget > options.TotalItemBudget)
        {
            yield return new(
                nameof(options.MinTotalItemBudget),
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

        if (!float.IsFinite(options.RecencyBoost) || options.RecencyBoost < 0)
        {
            yield return new(
                nameof(options.RecencyBoost),
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
        _tags = CopyTags(tags);
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
                    predicate.Options.MinTotalItemBudget,
                    predicate.Options.TotalItemBudget,
                    predicate.Options.ScoreLowerBound,
                    predicate.Options.RecencyBoost,
                    predicate.Options.IncludeEmptyLists,
                    predicate.Options.PreserveOneItemPerList),
                i));
        }

        return new(
            CopyTags(_tags),
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

    private static WeightedTags CopyTags(WeightedTags tags)
    {
        var copy = new WeightedTags();
        foreach (var (tag, weight) in tags)
        {
            copy.Add(tag, weight);
        }

        return copy;
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

    public SearchResult Run(IEnumerable<ExperienceList> experiences)
    {
        ArgumentNullException.ThrowIfNull(experiences);
        return Run(experiences, DateOnly.FromDateTime(DateTime.Today));
    }

    internal SearchResult Run(
        IEnumerable<ExperienceList> experiences,
        DateOnly currentDate)
    {
        ArgumentNullException.ThrowIfNull(experiences);
        return ExperienceSelectionEngine.Select(
            experiences,
            _tags,
            _mmr,
            _groups,
            UnlimitedSelectionAdmissionPolicy.Instance,
            currentDate);
    }

    internal SearchResult Run(
        ExperienceDatabase database,
        ISelectionAdmissionPolicy admissionPolicy)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(admissionPolicy);
        return ExperienceSelectionEngine.Select(
            database.Experiences,
            _tags,
            _mmr,
            _groups,
            admissionPolicy,
            DateOnly.FromDateTime(DateTime.Today));
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
    int MinTotalItemBudget,
    int TotalItemBudget,
    float ScoreLowerBound,
    float RecencyBoost,
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
                    x.Options.MinTotalItemBudget,
                    x.Options.TotalItemBudget,
                    ActualCount: 0,
                    RemainingMaximumBudget: x.Options.TotalItemBudget))
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
        DateOnly currentDate)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(mmr);
        ArgumentNullException.ThrowIfNull(admissionPolicy);

        mmr.Validate();

        var groupedLists = lists
            .OrderByDescending(x => x.DateRange, DateRangeComparer.ByEnd)
            .Select((list, listIndex) => CreateScoredList(
                list,
                listIndex,
                FindGroup(list, groups),
                tags))
            .Where(x => x is not null)
            .Select(x => x!)
            .ToList();

        var scoredLists = groupedLists
            .Where(x => x.ScoredItems.Count > 0)
            .ToList();

        var recencyMultipliers = CalculateRecencyMultipliers(
            scoredLists,
            currentDate);

        var candidates = scoredLists
            .SelectMany(x => x.ScoredItems.Select((i, itemIndex) => new MmrCandidate(
                x.Group,
                x.List,
                i.Item,
                i.Matches,
                recencyMultipliers.GetValueOrDefault(x.List, 1),
                x.ListIndex,
                itemIndex)))
            .ToList();

        var alwaysCandidates = groupedLists
            .Where(x => x.Group.Options.TotalItemBudget > 0)
            .SelectMany(x => x.List.Items
                .Select((item, itemIndex) => (Item: item, ItemIndex: itemIndex))
                .Where(x => x.Item.Required == ItemRequirement.Always)
                .Select(i => new MmrCandidate(
                    x.Group,
                    x.List,
                    i.Item,
                    tags.Match(i.Item.Tags),
                    recencyMultipliers.GetValueOrDefault(x.List, 1),
                    x.ListIndex,
                    i.ItemIndex)))
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
            return context.Output();
        }

        var ranker = new MmrRanker(
            mmr,
            candidates
                .Concat(alwaysCandidates)
                .Max(x => x.AdjustedRelevance));

        // Required items can bypass normal candidate filtering. Keep scores for
        // every item so all content committed through a required closure is
        // registered with MMR before subsequent candidates are ranked.
        foreach (var scoredList in groupedLists)
        {
            foreach (var item in scoredList.List.Items)
            {
                context.Scores.TryAdd(item, tags.Match(item.Tags));
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
                scoredList.ScoredItems.Select((i, itemIndex) => new MmrCandidate(
                    scoredList.Group,
                    scoredList.List,
                    i.Item,
                    i.Matches,
                    recencyMultipliers.GetValueOrDefault(scoredList.List, 1),
                    scoredList.ListIndex,
                    itemIndex)),
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

        return context.Output();

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
            return context.TryAdd(
                candidate,
                reason,
                allowExceedingBudget,
                added =>
                {
                    if (context.Scores.TryGetValue(added, out var matches))
                    {
                        context.DebugScores[added] = ranker.RawEquivalentScore(
                            matches,
                            candidate.RecencyMultiplier);
                        ranker.AddSelected(matches);
                    }
                });
        }
    }

    private static Dictionary<ExperienceList, float> CalculateRecencyMultipliers(
        IEnumerable<ScoredList> scoredLists,
        DateOnly currentDate)
    {
        var result = new Dictionary<ExperienceList, float>();

        foreach (var section in scoredLists.GroupBy(x => x.Group.Key))
        {
            var lists = section.ToArray();
            var recencyBoost = lists[0].Group.Options.RecencyBoost;
            if (recencyBoost == 0)
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
                var normalizedRecency = (endDate.DayNumber - oldestDay) / (float)dateRange;
                result[list] = 1 + recencyBoost * Math.Clamp(normalizedRecency, 0, 1);
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
        WeightedTags tags)
    {
        if (group is null)
        {
            return null;
        }

        var scoredItems = list.Items
            .Select(i => (Item: i, Matches: tags.Match(i.Tags)))
            .Where(i => !i.Matches.IsEmpty)
            .Where(i => i.Matches.Sum >= group.Options.ScoreLowerBound)
            .OrderByDescending(i => i.Matches.Sum)
            .ThenByDescending(i => i.Matches.Count)
            .ThenByDescending(i => i.Item.Tags.Length)
            .ToList();

        return new(
            group,
            list,
            scoredItems,
            listIndex);
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

    private sealed record ScoredList(
        ExperienceSelectionGroup Group,
        ExperienceList List,
        List<(ExperienceListItem Item, ScoredTags Matches)> ScoredItems,
        int ListIndex);

    private readonly record struct MmrCandidate(
        ExperienceSelectionGroup Group,
        ExperienceList List,
        ExperienceListItem Item,
        ScoredTags Matches,
        float RecencyMultiplier,
        int ListIndex,
        int ItemIndex)
    {
        public float AdjustedRelevance => Matches.Sum * RecencyMultiplier;
    }

    private sealed class MmrRanker(
        MmrOptions options,
        float maxRelevance)
    {
        private readonly List<ScoredTags> _selected = new();
        private readonly Dictionary<Tag, int> _selectedTagCounts = new();

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

        public void AddSelected(ScoredTags matches)
        {
            _selected.Add(matches);

            foreach (var tag in matches.Keys)
            {
                ref var count = ref CollectionsMarshal.GetValueRefOrAddDefault(
                    _selectedTagCounts,
                    tag,
                    out _);
                count += 1;
            }
        }

        public float RawEquivalentScore(
            ScoredTags matches,
            float recencyMultiplier)
        {
            var score = Score(matches, recencyMultiplier);
            if (options.RelevanceWeight <= 0 || maxRelevance <= 0)
            {
                return Math.Max(0, score);
            }

            return Math.Max(0, score * maxRelevance / options.RelevanceWeight);
        }

        public float Score(MmrCandidate candidate)
        {
            return Score(candidate.Matches, candidate.RecencyMultiplier);
        }

        private float Score(
            ScoredTags matches,
            float recencyMultiplier)
        {
            var relevance = maxRelevance <= 0
                ? 0
                : matches.Sum * recencyMultiplier / maxRelevance;
            var redundancy = MaxSimilarity(matches);
            var saturation = Saturation(matches);

            return options.RelevanceWeight * relevance
                - (1 - options.RelevanceWeight) * redundancy
                - options.SaturationPenalty * saturation;
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
            if (matches.Sum <= 0)
            {
                return 0;
            }

            float ret = 0;
            foreach (var (tag, score) in matches)
            {
                var selectedCount = _selectedTagCounts.GetValueOrDefault(tag);
                var overQuota = selectedCount - options.SaturationQuota + 1;
                if (overQuota <= 0)
                {
                    continue;
                }

                ret += (score / matches.Sum) * overQuota;
            }

            return ret;
        }

        private static float CosineSimilarity(
            ScoredTags a,
            ScoredTags b)
        {
            if (a.Count == 0 || b.Count == 0)
            {
                return 0;
            }

            float dot = 0;
            var smaller = a.Count <= b.Count ? a : b;
            var larger = ReferenceEquals(smaller, a) ? b : a;

            foreach (var (tag, score) in smaller)
            {
                if (larger.TryGetValue(tag, out var otherScore))
                {
                    dot += score * otherScore;
                }
            }

            if (dot <= 0)
            {
                return 0;
            }

            var norm = MathF.Sqrt(SquaredLength(a) * SquaredLength(b));
            if (norm <= 0)
            {
                return 0;
            }

            return dot / norm;
        }

        private static float SquaredLength(ScoredTags tags)
        {
            float ret = 0;
            foreach (var score in tags.Values)
            {
                ret += score * score;
            }

            return ret;
        }

        private static int BreakTie(
            MmrCandidate left,
            MmrCandidate right)
        {
            var relevance = right.Matches.Sum.CompareTo(left.Matches.Sum);
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
        public readonly Dictionary<ExperienceListItem, float> DebugScores = new();
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
                _remainingMaximumBudgets.Add(group.Key, group.Options.TotalItemBudget);
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
            Action<ExperienceListItem>? onAdded = null)
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

                onAdded?.Invoke(item);
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
                            var debugScore = DebugScoreOf(item);
                            totalScore += debugScore;
                            outputItems.Add(new(
                                item,
                                matches,
                                debugScore));

                            itemTraces.Add(new(
                                group.Key,
                                list,
                                item,
                                Reasons.GetValueOrDefault(item, SelectionItemReason.Direct),
                                matches.Sum,
                                debugScore,
                                ToDebugTagScores(matches, debugScore),
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
                    group.Options.MinTotalItemBudget,
                    group.Options.TotalItemBudget,
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

            float DebugScoreOf(ExperienceListItem item)
            {
                return DebugScores.TryGetValue(item, out var score)
                    ? score
                    : MatchesOf(item).Sum;
            }
        }

        private bool IsBelowMinimum(ExperienceSelectionGroup group)
        {
            var actualCount = group.Options.TotalItemBudget - _remainingMaximumBudgets[group.Key];
            return actualCount < group.Options.MinTotalItemBudget;
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
        float DebugScore);

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
                    x.DebugScore,
                    x.Item.Text,
                    ToDebugTagScores(x.Matches, x.DebugScore)));
            }

            builder.Add(new()
            {
                DateRange = t.List.DateRange,
                Place = t.List.Place,
                Title = t.List.Title,
                Text = hasItems ? t.List.Description : null,
                DebugScore = t.TotalScore,
                DebugTagScores = ToDebugTagScores(items.Select(x => (x.Matches, x.DebugScore))),
                SubItems = subBuilder.ToImmutable(),
                Urls = hasItems ? t.List.Urls : [],
            });
            subBuilder.Clear();
        }

        return builder.DrainToImmutable();
    }

    private static ImmutableArray<DebugTagScore> ToDebugTagScores(
        ScoredTags matches,
        float debugScore)
    {
        if (matches.IsEmpty)
        {
            return [];
        }

        var scale = matches.Sum <= 0
            ? 0
            : debugScore / matches.Sum;

        return matches
            .Select(x => new DebugTagScore(x.Key.Name, x.Value * scale))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Tag.Value, StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static ImmutableArray<DebugTagScore> ToDebugTagScores(
        IEnumerable<(ScoredTags Matches, float DebugScore)> matches)
    {
        var totals = new Dictionary<Tag, float>();
        foreach (var (scoredTags, debugScore) in matches)
        {
            var scale = scoredTags.Sum <= 0
                ? 0
                : debugScore / scoredTags.Sum;

            foreach (var (tag, score) in scoredTags)
            {
                totals[tag] = totals.GetValueOrDefault(tag) + score * scale;
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

    private static bool IsMeaningfulScore(float score)
    {
        return MathF.Abs(score) >= 0.0001f;
    }

    private static class EmptyScoredTags
    {
        public static readonly ScoredTags Instance = new([]);
    }
}
