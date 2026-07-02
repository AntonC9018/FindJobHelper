using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

public readonly record struct ExperienceKey(string Value)
{
    public override string ToString() => Value ?? "";
}

public sealed class SearchPredicateOptions
{
    public int TotalItemBudget { get; set; }
    public float ScoreLowerBound { get; set; }

    internal SearchPredicateOptions Copy()
    {
        return new()
        {
            TotalItemBudget = TotalItemBudget,
            ScoreLowerBound = ScoreLowerBound,
        };
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
            ValidateOptions(predicate.Options);

            if (!keys.Add(predicate.Key))
            {
                throw new InvalidOperationException($"Duplicate experience search key '{predicate.Key}'.");
            }

            groups.Add(new(
                predicate.Key,
                predicate.Predicate,
                new(
                    predicate.Options.TotalItemBudget,
                    predicate.Options.ScoreLowerBound),
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

    private static void ValidateOptions(SearchPredicateOptions options)
    {
        if (options.TotalItemBudget < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.TotalItemBudget),
                options.TotalItemBudget,
                "Total item budget must be non-negative.");
        }

        if (float.IsNaN(options.ScoreLowerBound) || options.ScoreLowerBound < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.ScoreLowerBound),
                options.ScoreLowerBound,
                "Score lower bound must be non-negative.");
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
        return ExperienceSelectionEngine.Select(
            experiences,
            _tags,
            _mmr,
            _groups);
    }
}

public sealed class SearchResult
{
    private readonly Dictionary<ExperienceKey, ImmutableArray<Event>> _results;

    internal SearchResult(
        IEnumerable<ExperienceKey> keys,
        IReadOnlyDictionary<ExperienceKey, ImmutableArray<Event>> results)
    {
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
    int TotalItemBudget,
    float ScoreLowerBound);

internal sealed record ExperienceSelectionGroup(
    ExperienceKey Key,
    Func<ExperienceList, bool> Predicate,
    ExperienceSelectionOptions Options,
    int Order);

internal static class ExperienceSelectionEngine
{
    public static ImmutableArray<Event> SelectEvents(
        IEnumerable<ExperienceList> lists,
        SearchParams p)
    {
        ArgumentNullException.ThrowIfNull(lists);
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(p.Tags);

        p.Mmr.Validate();

        var key = new ExperienceKey("Default");
        var groups = ImmutableArray.Create(new ExperienceSelectionGroup(
            key,
            static _ => true,
            new(p.TotalItemBudget, p.ScoreLowerBound),
            Order: 0));

        var result = Select(lists, p.Tags, p.Mmr, groups);
        return result.Get(key);
    }

    public static SearchResult Select(
        IEnumerable<ExperienceList> lists,
        WeightedTags tags,
        MmrOptions mmr,
        ImmutableArray<ExperienceSelectionGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(mmr);

        mmr.Validate();

        var scoredLists = lists
            .OrderByDescending(x => x.DateRange, DateRangeComparer.ByEnd)
            .Select((list, listIndex) => CreateScoredList(
                list,
                listIndex,
                FindGroup(list, groups),
                tags))
            .Where(x => x is not null)
            .Select(x => x!)
            .Where(x => x.ScoredItems.Count > 0)
            .ToList();

        var candidates = scoredLists
            .SelectMany(x => x.ScoredItems.Select((i, itemIndex) => new MmrCandidate(
                x.Group,
                x.List,
                i.Item,
                i.Matches,
                x.ListIndex,
                itemIndex)))
            .ToList();

        if (candidates.Count == 0)
        {
            return new(
                groups.Select(x => x.Key),
                new Dictionary<ExperienceKey, ImmutableArray<Event>>());
        }

        var ranker = new MmrRanker(
            mmr,
            candidates.Max(x => x.Matches.Sum));

        var context = new SelectionContext(groups);
        foreach (var x in candidates)
        {
            context.Scores.TryAdd(x.Item, x.Matches);
        }

        foreach (var scoredList in scoredLists)
        {
            var best = ranker.BestCandidate(
                scoredList.ScoredItems.Select((i, itemIndex) => new MmrCandidate(
                    scoredList.Group,
                    scoredList.List,
                    i.Item,
                    i.Matches,
                    scoredList.ListIndex,
                    itemIndex)),
                context.Added);

            if (best is { } candidate)
            {
                TryAddAndRegister(candidate);
            }
        }

        var rejected = new HashSet<ExperienceListItem>();
        while (context.HasRemainingBudget)
        {
            var next = ranker.BestCandidate(candidates, context.Added, rejected);
            if (next is null)
            {
                break;
            }

            if (!TryAddAndRegister(next.Value))
            {
                rejected.Add(next.Value.Item);
            }
        }

        return context.Output();

        bool TryAddAndRegister(MmrCandidate candidate)
        {
            return context.TryAdd(
                candidate,
                added =>
                {
                    if (context.Scores.TryGetValue(added, out var matches))
                    {
                        ranker.AddSelected(matches);
                    }
                });
        }
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
        int ListIndex,
        int ItemIndex);

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

                var score = Score(candidate.Matches);
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

        private float Score(ScoredTags matches)
        {
            var relevance = maxRelevance <= 0
                ? 0
                : matches.Sum / maxRelevance;
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

    private sealed class SelectionContext
    {
        private readonly ImmutableArray<ExperienceSelectionGroup> _groups;
        private readonly Dictionary<ExperienceKey, int> _remainingBudgets = new();
        private readonly List<ExperienceListItem> _temp = new();

        public readonly HashSet<ExperienceListItem> Added = new();
        public readonly Dictionary<ExperienceKey, Dictionary<ExperienceList, List<ExperienceListItem>>> Results = new();
        public readonly Dictionary<ExperienceListItem, ScoredTags> Scores = new();

        public SelectionContext(ImmutableArray<ExperienceSelectionGroup> groups)
        {
            _groups = groups;
            foreach (var group in groups)
            {
                _remainingBudgets.Add(group.Key, group.Options.TotalItemBudget);
            }
        }

        public bool HasRemainingBudget => _remainingBudgets.Values.Any(x => x > 0);

        public bool TryAdd(
            MmrCandidate candidate,
            Action<ExperienceListItem>? onAdded = null)
        {
            _temp.Clear();
            SimulateAdd(candidate.Item, _temp);

            if (_temp.Count > _remainingBudgets[candidate.Group.Key])
            {
                return false;
            }

            ref var groupResults = ref CollectionsMarshal.GetValueRefOrAddDefault(
                Results,
                candidate.Group.Key,
                out _);
            groupResults ??= new();

            foreach (var item in _temp)
            {
                ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(
                    groupResults,
                    candidate.List,
                    out _);
                list ??= new();
                list.Add(item);
                onAdded?.Invoke(item);
            }

            Added.UnionWith(_temp);
            _remainingBudgets[candidate.Group.Key] -= _temp.Count;
            return true;
        }

        public SearchResult Output()
        {
            var results = new Dictionary<ExperienceKey, ImmutableArray<Event>>();
            foreach (var group in _groups)
            {
                if (!Results.TryGetValue(group.Key, out var groupResults))
                {
                    continue;
                }

                results[group.Key] = ToOutput(groupResults.Select(x =>
                {
                    var (list, items) = x;

                    float totalScore = 0;
                    foreach (var item in items)
                    {
                        totalScore += ScoreOf(item);
                    }

                    return new OutputEvent(
                        list,
                        totalScore,
                        items.Select(item => (item, ScoreOf(item))));
                }));
            }

            return new(
                _groups.Select(x => x.Key),
                results);

            float ScoreOf(ExperienceListItem item)
            {
                return Scores.TryGetValue(item, out var score)
                    ? score.Sum
                    : 0;
            }
        }

        private void SimulateAdd(
            ExperienceListItem item,
            List<ExperienceListItem> outThingsToAdd)
        {
            if (Added.Contains(item))
            {
                return;
            }

            if (outThingsToAdd.Contains(item))
            {
                return;
            }

            foreach (var dependency in item.MustBeAfter)
            {
                SimulateAdd(dependency, outThingsToAdd);
            }

            outThingsToAdd.Add(item);
        }
    }

    private readonly record struct OutputEvent(
        ExperienceList List,
        float TotalScore,
        IEnumerable<(
            ExperienceListItem Item,
            float Score)> Items);

    private static ImmutableArray<Event> ToOutput(IEnumerable<OutputEvent> s)
    {
        var builder = ImmutableArray.CreateBuilder<Event>();
        var subBuilder = ImmutableArray.CreateBuilder<SubEvent>();

        foreach (var t in s)
        {
            foreach (var x in t.Items)
            {
                var latexStr = x.Item.Text.ToLatexString();
                subBuilder.Add(new(x.Score, latexStr));
            }

            builder.Add(new()
            {
                DateRange = t.List.DateRange,
                Place = t.List.Place,
                Title = t.List.Title,
                Text = t.List.Description,
                DebugScore = t.TotalScore,
                SubItems = subBuilder.ToImmutable(),
                Urls = t.List.Urls,
            });
            subBuilder.Clear();
        }

        return builder.DrainToImmutable();
    }
}
