using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core;

// Tag reference with score for a specific experience item
public readonly record struct TagReference(Tag Tag, int Score);

public enum ItemRequirement
{
    None,
    IfAny,
    Always,
}

public enum ItemMove
{
    None,
    ToFront,
}

public readonly record struct ItemOrder
{
    public ItemOrder()
    {
    }

    [JsonRequired]
    public ItemMove Move { get; init; }

    [JsonRequired]
    public ImmutableArray<ExperienceListItem> After { get; init; } =
        ImmutableArray<ExperienceListItem>.Empty;
}

// Experience item with its own tags and ordering constraints
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ExperienceListItem
{
    private IRichTextNode _text = null!;

    public required IRichTextNode Text
    {
        get => _text;
        init => _text = value ?? throw new ArgumentNullException(nameof(Text));
    }
    public ImmutableArray<TagReference> Tags { get; init; } = ImmutableArray<TagReference>.Empty;
    public ImmutableArray<RegularString> Urls { get; init; } = ImmutableArray<RegularString>.Empty;
    [JsonRequired]
    public ImmutableArray<ExperienceListItem> DependsOn { get; init; } = ImmutableArray<ExperienceListItem>.Empty;
    [JsonRequired]
    public ItemRequirement Required { get; init; }
    [JsonRequired]
    public ItemOrder Order { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ExperienceItemGroup
{
    private string _id = null!;

    public required string Id
    {
        get => _id;
        init => _id = string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Experience item group ID cannot be null, empty, or whitespace.", nameof(value))
            : value;
    }

    public required ImmutableArray<ExperienceListItem> Items { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ExperienceItemExclusionSet
{
    public required ImmutableArray<ExperienceListItem> Items { get; init; }
}

public sealed record class MmrOptions(
    float RelevanceWeight,
    int SaturationQuota,
    float SaturationPenalty)
{
    public static MmrOptions Default { get; } = new(
        RelevanceWeight: 0.72f,
        SaturationQuota: 2,
        SaturationPenalty: 0.18f);

    public void Validate()
    {
        if (float.IsNaN(RelevanceWeight) || RelevanceWeight is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RelevanceWeight),
                RelevanceWeight,
                "MMR relevance weight must be between 0 and 1.");
        }

        if (SaturationQuota < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SaturationQuota),
                SaturationQuota,
                "Saturation quota must be at least 1.");
        }

        if (float.IsNaN(SaturationPenalty) || SaturationPenalty < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SaturationPenalty),
                SaturationPenalty,
                "Saturation penalty must be non-negative.");
        }
    }
}

public sealed record MmrScoreBreakdown(
    int SelectionOrdinal,
    float BaseRelevance,
    float DirectMatchBonus,
    float RawRelevance,
    float AppliedRecencyBoost,
    float RecencyBonus,
    float AdjustedPreMmrRelevance,
    float NormalizedRelevance,
    float MaximumCosineSimilarity,
    float Saturation,
    float WeightedRelevanceTerm,
    float WeightedSimilarityPenalty,
    float WeightedSaturationPenalty,
    float NormalizedMmrScore)
{
    public float FinalSignedNormalizedMmrScore => NormalizedMmrScore;
}

public static class ExperienceListSorter
{
    private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static ReferenceEqualityComparer<T> Instance { get; } = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj!);
    }

    public static List<T> TopologicalSort<T>(
        this IEnumerable<T> items,
        Func<T, IEnumerable<T>> predecessors)

        where T : class
    {
        // Materialize input so we can iterate multiple times
        var nodeList = items.ToList();

        var comparer = new ReferenceEqualityComparer<T>();

        // Map each node -> in-degree (number of dependencies inside the provided set)
        var inDegree = new Dictionary<T, int>(nodeList.Count, comparer);
        var adjacency = new Dictionary<T, List<T>>(comparer);

        // Initialize inDegree for all nodes
        foreach (var node in nodeList)
        {
            inDegree[node] = 0;
        }

        // Build graph from the nearest selected predecessors. Traversing
        // through absent nodes preserves transitive constraints such as
        // A after B after C when only A and C are selected.
        var nodeSet = new HashSet<T>(nodeList, comparer);

        foreach (var node in nodeList)
        {
            var nodePredecessors = new HashSet<T>(comparer);
            foreach (var predecessor in SelectedPredecessors(node))
            {
                if (!nodePredecessors.Add(predecessor))
                {
                    continue;
                }

                if (!adjacency.TryGetValue(predecessor, out var list))
                {
                    list = new List<T>();
                    adjacency[predecessor] = list;
                }

                list.Add(node);
                inDegree[node] += 1;
            }
        }

        IEnumerable<T> SelectedPredecessors(T node)
        {
            var visited = new HashSet<T>(comparer);
            var pending = new Stack<T>();
            PushPredecessors(node);

            while (pending.TryPop(out var predecessor))
            {
            if (predecessor is null)
            {
                continue;
            }
            if (!visited.Add(predecessor))
                {
                    continue;
                }

                if (nodeSet.Contains(predecessor))
                {
                    yield return predecessor;
                    continue;
                }

                PushPredecessors(predecessor);
            }

            void PushPredecessors(T item)
            {
                foreach (var predecessor in predecessors(item))
                {
                    if (predecessor is not null)
                    {
                        pending.Push(predecessor);
                    }
                }
            }
        }

        var originalIndexes = new Dictionary<T, int>(nodeList.Count, comparer);
        for (var i = 0; i < nodeList.Count; i++)
        {
            originalIndexes[nodeList[i]] = i;
        }

        // Queue of nodes with zero in-degree, preferring the original order
        // whenever dependency constraints allow it.
        var q = new PriorityQueue<T, int>();
        foreach (var (node, degree) in inDegree)
        {
            if (degree == 0)
            {
                q.Enqueue(node, originalIndexes[node]);
            }
        }

        var result = new List<T>();

        while (q.Count > 0)
        {
            var n = q.Dequeue();
            result.Add(n);

            if (!adjacency.TryGetValue(n, out var dependents))
            {
                continue;
            }

            foreach (var d in dependents)
            {
                inDegree[d] -= 1;
                if (inDegree[d] == 0)
                {
                    q.Enqueue(d, originalIndexes[d]);
                }
            }
        }

        // If not all nodes are processed, there's a cycle
        if (result.Count != nodeList.Count)
        {
            throw new InvalidOperationException("Cycle detected in ordering relationships.");
        }

        return result;
    }

    public static ImmutableArray<Event> AllEvents(
        this IEnumerable<ExperienceList> lists)
    {
        var r = lists.Select(list =>
        {
            var items = list
                .OrderItems(list.Items)
                .Select(it => (it, 0.0f));
            return new OutputEvent(list, TotalScore: 0, items);
        });
        return ToOutput(r);
    }

    internal static List<ExperienceListItem> OrderItems(
        this ExperienceList list,
        IEnumerable<ExperienceListItem> selectedItems)
    {
        list.ValidateItemConfiguration();
        var selected = selectedItems.ToList();
        var comparer = System.Collections.Generic.ReferenceEqualityComparer.Instance;
        var selectedSet = new HashSet<ExperienceListItem>(selected, comparer);
        var itemToGroup = new Dictionary<ExperienceListItem, ExperienceItemGroup>(comparer);
        foreach (var group in list.ItemGroups)
        {
            foreach (var item in group.Items)
            {
                itemToGroup[item] = group;
            }
        }

        var blocks = new List<OrderingBlock>();
        var groupBlocks = new Dictionary<ExperienceItemGroup, OrderingBlock>(
            ReferenceEqualityComparer<ExperienceItemGroup>.Instance);
        foreach (var item in selected)
        {
            if (itemToGroup.TryGetValue(item, out var group))
            {
                if (!groupBlocks.TryGetValue(group, out var block))
                {
                    block = new OrderingBlock(group.Id);
                    groupBlocks.Add(group, block);
                    blocks.Add(block);
                }
                block.Items.Add(item);
            }
            else
            {
                var block = new OrderingBlock(null);
                block.Items.Add(item);
                blocks.Add(block);
            }
        }

        var itemToBlock = new Dictionary<ExperienceListItem, OrderingBlock>(comparer);
        foreach (var block in blocks)
        {
            foreach (var item in block.Items)
            {
                itemToBlock[item] = block;
            }
        }

        var frontBlocks = blocks
            .Where(block => block.Items.Any(item => item.Order.Move == ItemMove.ToFront))
            .OrderBy(block => block.Items.Min(item => list.Items.IndexOf(item)))
            .ToList();

        IEnumerable<OrderingBlock> BlockPredecessors(OrderingBlock block)
        {
            var predecessors = block.Items
                .SelectMany(SelectedPredecessors)
                .Select(item => itemToBlock[item])
                .Where(predecessor => !ReferenceEquals(predecessor, block));

            if (!frontBlocks.Contains(block))
            {
                predecessors = predecessors.Concat(frontBlocks);
            }

            return predecessors;
        }

        List<OrderingBlock> orderedBlocks;
        try
        {
            orderedBlocks = frontBlocks
                .Concat(blocks.Where(block => !frontBlocks.Contains(block)))
                .TopologicalSort(BlockPredecessors);
        }
        catch (InvalidOperationException exception)
        {
            if (!IsGroupingCycle(exception))
            {
                throw;
            }

            // A pre-existing item-level cycle is still reported with the
            // established error. Only a cycle introduced by collapsing valid
            // item ordering into blocks is a grouping/interleaving conflict.
            var frontItems = list.Items
                .Where(item =>
                {
                    if (!selectedSet.Contains(item))
                    {
                        return false;
                    }

                    return item.Order.Move == ItemMove.ToFront;
                })
                .ToList();
            frontItems
                .Concat(selected.Where(item => item.Order.Move != ItemMove.ToFront))
                .TopologicalSort(item =>
                {
                    var predecessors = SelectedPredecessors(item);
                    var effectivePredecessors = item.Order.Move == ItemMove.ToFront
                        ? predecessors
                        : predecessors.Concat(frontItems);
                    return effectivePredecessors;
                });

            throw new InvalidOperationException(
                "Named experience groups conflict with item ordering constraints; " +
                "satisfying them would require group members to interleave.",
                exception);
        }

        bool IsGroupingCycle(InvalidOperationException exception)
        {
            if (list.ItemGroups.Length == 0)
            {
                return false;
            }

            return exception.Message.Contains("Cycle detected", StringComparison.Ordinal);
        }

        return orderedBlocks
            .SelectMany(block =>
            {
                return block.Items.TopologicalSort(item =>
                {
                    var predecessors = SelectedPredecessors(item);
                    return predecessors.Where(predecessor =>
                        ReferenceEquals(itemToBlock[predecessor], block));
                });
            })
            .ToList();

        IEnumerable<ExperienceListItem> SelectedPredecessors(ExperienceListItem item)
        {
            var visited = new HashSet<ExperienceListItem>(comparer);
            var pending = new Stack<ExperienceListItem>(OrderingPredecessors(item));
            while (pending.TryPop(out var predecessor))
            {
                if (predecessor is null)
                {
                    continue;
                }
                if (!visited.Add(predecessor))
                {
                    continue;
                }

                if (selectedSet.Contains(predecessor))
                {
                    yield return predecessor;
                }
                else
                {
                    foreach (var ancestor in OrderingPredecessors(predecessor))
                    {
                        pending.Push(ancestor);
                    }
                }
            }
        }
    }

    private sealed class OrderingBlock(string? groupId)
    {
        public string? GroupId { get; } = groupId;
        public List<ExperienceListItem> Items { get; } = new();
    }

    internal static IEnumerable<ExperienceListItem> OrderingPredecessors(
        ExperienceListItem item)
    {
        return item.DependsOn.Concat(item.Order.After);
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
                subBuilder.Add(new(
                    x.Item.Text,
                    new()
                    {
                        Score = x.Score,
                    }));
            }

            builder.Add(new()
            {
                DateRange = t.List.DateRange,
                Place = t.List.Place,
                Title = t.List.Title,
                Text = t.List.Description,
                DebugInfo = new()
                {
                    Score = t.TotalScore,
                },
                SubItems = subBuilder.ToImmutable(),
                Urls = t.List.Urls,
            });
            subBuilder.Clear();
        }

        return builder.DrainToImmutable();
    }

}

public enum ExperienceType
{
    Job,
    Project,
    BachelorsDegree,
    MastersDegree,
}

public static class ExperienceTypeExtensions
{
    public static bool IsDegree(this ExperienceType t)
        => t is ExperienceType.BachelorsDegree or ExperienceType.MastersDegree;

}

// Experience list
public sealed class ExperienceList
{
    public required RegularString Title { get; init; }
    public required Place Place { get; init; }
    public required DateRange DateRange { get; init; }
    public required ExperienceType Type { get; init; }
    public IRichTextNode? Description { get; init; }
    public required ImmutableArray<ExperienceListItem> Items { get; init; }
    public ImmutableArray<ExperienceItemGroup> ItemGroups { get; init; } =
        ImmutableArray<ExperienceItemGroup>.Empty;
    public ImmutableArray<ExperienceItemExclusionSet> ItemExclusionSets { get; init; } =
        ImmutableArray<ExperienceItemExclusionSet>.Empty;
    public ImmutableArray<RegularString> Urls { get; init; } = ImmutableArray<RegularString>.Empty;

    internal void ValidateItemConfiguration()
    {
        var comparer = System.Collections.Generic.ReferenceEqualityComparer.Instance;
        var items = new HashSet<ExperienceListItem>(Items, comparer);
        var grouped = new HashSet<ExperienceListItem>(comparer);
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in ItemGroups)
        {
            if (!groupIds.Add(group.Id))
            {
                throw new InvalidOperationException(
                    $"Named experience group ID '{group.Id}' is declared more than once in one experience list.");
            }

            if (group.Items.IsDefaultOrEmpty)
            {
                throw new InvalidOperationException(
                    $"Named experience group '{group.Id}' must contain at least one item.");
            }

            foreach (var item in group.Items)
            {
                if (!items.Contains(item))
                {
                    throw new InvalidOperationException(
                        $"Named experience group '{group.Id}' contains an item that does not belong to its experience list.");
                }

                if (!grouped.Add(item))
                {
                    throw new InvalidOperationException(
                        "An experience item cannot belong to more than one named group.");
                }
            }
        }


        foreach (var item in Items)
        {
            if (item is null)
            {
                throw new InvalidOperationException("An experience list cannot contain a null item.");
            }

            ValidateReferences(item.DependsOn, "dependency");
            ValidateReferences(item.Order.After, "ordering");
        }

        var exclusionSets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var exclusionSet in ItemExclusionSets)
        {
            if (exclusionSet is null)
            {
                throw new InvalidOperationException("An experience item exclusion set cannot be null.");
            }

            var members = new HashSet<ExperienceListItem>(comparer);
            foreach (var item in exclusionSet.Items)
            {
                if (item is null)
                {
                    throw new InvalidOperationException("An experience item exclusion set cannot contain a null item.");
                }
                if (!items.Contains(item))
                {
                    throw new InvalidOperationException(
                        "An experience item exclusion set contains an item that does not belong to its experience list.");
                }
                if (!members.Add(item))
                {
                    throw new InvalidOperationException(
                        "An experience item exclusion set cannot contain duplicate items.");
                }
            }

            if (members.Count < 2)
            {
                throw new InvalidOperationException(
                    "An experience item exclusion set must contain at least two distinct items.");
            }

            var key = string.Join(",", members.Select(item => Items.IndexOf(item)).Order());
            if (!exclusionSets.Add(key))
            {
                throw new InvalidOperationException(
                    "Duplicate experience item exclusion sets are not allowed, regardless of member order.");
            }
        }

        void ValidateReferences(ImmutableArray<ExperienceListItem> references, string relationship)
        {
            foreach (var referencedItem in references)
            {
                if (referencedItem is null)
                {
                    throw new InvalidOperationException(
                        "An experience item ordering relationship references an item outside its experience list.");
                }
                if (!items.Contains(referencedItem))
                {
                    throw new InvalidOperationException(
                        $"An experience item {relationship} references an item that does not belong to its experience list.");
                }
            }
        }
    }
}

/// <summary>
/// A process-local identifier derived from an experience list's position in the
/// immutable database. Runtime identifiers are deliberately not persisted.
/// </summary>
public readonly record struct ExperienceListId(int Position);

/// <summary>
/// A process-local identifier derived from an item's position in its parent list.
/// </summary>
public readonly record struct ExperienceItemId(
    ExperienceListId ListId,
    int Position);

public readonly record struct IdentifiedExperienceList(
    ExperienceListId Id,
    ExperienceList Value);

public readonly record struct IdentifiedExperienceItem(
    ExperienceItemId Id,
    ExperienceListId ListId,
    ExperienceList List,
    ExperienceListItem Value);

// Builder for individual experience item
public sealed class ExperienceItemBuilder
{
    private IRichTextNode? _text;
    private readonly ImmutableArray<TagReference>.Builder _tags = ImmutableArray.CreateBuilder<TagReference>();
    private readonly ImmutableArray<RegularString>.Builder _urls = ImmutableArray.CreateBuilder<RegularString>();
    private readonly List<ExperienceItemBuilder> _dependsOn = new();
    private readonly List<ExperienceItemBuilder> _after = new();
    private ItemRequirement _required;

    public ExperienceItemBuilder()
    {
        Order = new(this);
    }

    public ExperienceItemOrderBuilder Order { get; }

    public void Text(IRichTextNode text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    public void Text(RichTextInterpolatedStringHandler h)
    {
        Text(h.Build());
    }

    public void Tag(Tag tag, int score)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(score, 0);
        _tags.Add(new TagReference(tag, score));
    }

    public void Url(string url)
    {
        _urls.Add(new RegularString(url));
    }

    public ExperienceItemBuilder DependsOn(ExperienceItemBuilder other)
    {
        _dependsOn.Add(other);
        return this;
    }

    public ExperienceItemRequirementBuilder Required()
    {
        return new(this);
    }

    internal ExperienceListItem Build(Dictionary<ExperienceItemBuilder, ExperienceListItem> builtItems)
    {
        if (_text is null)
        {
            throw new InvalidOperationException("Text is required for an experience item");
        }

        var dependsOn = Resolve(_dependsOn);
        var after = Resolve(_after);

        return new ExperienceListItem
        {
            Text = _text,
            Tags = _tags.DrainToImmutable(),
            Urls = _urls.DrainToImmutable(),
            DependsOn = dependsOn,
            Required = _required,
            Order = new()
            {
                Move = Order.ItemMove,
                After = after,
            },
        };

        ImmutableArray<ExperienceListItem> Resolve(
            IEnumerable<ExperienceItemBuilder> references)
        {
            return references
                .Select(builder =>
                {
                    if (!builtItems.TryGetValue(builder, out var item))
                    {
                        throw new InvalidOperationException(
                            "Referenced item has not been built yet");
                    }

                    return item;
                })
                .ToImmutableArray();
        }
    }

    internal ExperienceItemBuilder SetRequired(ItemRequirement required)
    {
        if (_required == ItemRequirement.None || _required == required)
        {
            _required = required;
            return this;
        }

        throw new InvalidOperationException(
            $"Experience item requirement is already configured as '{_required}' " +
            $"and cannot also be configured as '{required}'.");
    }

    internal void AddAfter(ExperienceItemBuilder other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _after.Add(other);
    }
}

public sealed class ExperienceItemRequirementBuilder
{
    private readonly ExperienceItemBuilder _item;

    internal ExperienceItemRequirementBuilder(ExperienceItemBuilder item)
    {
        _item = item;
    }

    public ExperienceItemBuilder IfAny()
    {
        return _item.SetRequired(ItemRequirement.IfAny);
    }

    public ExperienceItemBuilder BeforeAny()
    {
        _item.SetRequired(ItemRequirement.IfAny);
        _item.Order.Move().ToFront();
        return _item;
    }

    public ExperienceItemBuilder Always()
    {
        return _item.SetRequired(ItemRequirement.Always);
    }
}

public sealed class ExperienceItemOrderBuilder
{
    private readonly ExperienceItemBuilder _item;

    internal ExperienceItemOrderBuilder(ExperienceItemBuilder item)
    {
        _item = item;
    }

    internal ItemMove ItemMove { get; private set; }

    public ExperienceItemOrderBuilder After(ExperienceItemBuilder other)
    {
        _item.AddAfter(other);
        return this;
    }

    public ExperienceItemMovementBuilder Move()
    {
        return new(this);
    }

    internal ExperienceItemOrderBuilder SetMove(ItemMove move)
    {
        ItemMove = move;
        return this;
    }
}

public sealed class ExperienceItemMovementBuilder
{
    private readonly ExperienceItemOrderBuilder _order;

    internal ExperienceItemMovementBuilder(ExperienceItemOrderBuilder order)
    {
        _order = order;
    }

    public ExperienceItemOrderBuilder ToFront()
    {
        return _order.SetMove(ItemMove.ToFront);
    }
}

// public interface IExperienceBuilder
// {
//     public void Url(string url);
//     public void Tag(Tag tag, int score);
// }

// Builder for experience list
public sealed class ExperienceListBuilder
{
    private string? _title;
    private Place _place;
    private DateRange? _dateRange;
    private ExperienceType _type;
    private readonly List<ExperienceItemBuilder> _itemBuilders = new();
    private readonly Dictionary<string, ExperienceItemGroupBuilder> _groups =
        new(StringComparer.Ordinal);
    private readonly List<ExperienceItemBuilder[]> _exclusionSets = new();
    private readonly ImmutableArray<RegularString>.Builder _urls = ImmutableArray.CreateBuilder<RegularString>();
    private IRichTextNode? _description;

    internal ExperienceListBuilder(ExperienceType type)
    {
        _type = type;
    }

    public void Title(string title)
    {
        _title = title;
    }

    public void Place(Place place)
    {
        _place = place;
    }

    public void DateRange(DateRange dateRange)
    {
        _dateRange = dateRange;
    }

    public void Description(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        Description(new PlainText { Text = description });
    }

    public void Description(IRichTextNode description)
    {
        ArgumentNullException.ThrowIfNull(description);
        _description = description;
    }

    public void Description(RichTextInterpolatedStringHandler description)
    {
        Description(description.Build());
    }

    public ExperienceItemBuilder Item(Action<ExperienceItemBuilder> configure)
    {
        return AddItem(configure, group: null);
    }

    internal ExperienceItemBuilder AddItem(
        Action<ExperienceItemBuilder> configure,
        ExperienceItemGroupBuilder? group)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ExperienceItemBuilder();
        configure(builder);
        _itemBuilders.Add(builder);
        group?.Add(builder);
        return builder;
    }

    public ExperienceItemGroupBuilder Group(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "Experience item group ID cannot be null, empty, or whitespace.",
                nameof(id));
        }

        if (!_groups.TryGetValue(id, out var group))
        {
            group = new ExperienceItemGroupBuilder(this, id);
            _groups.Add(id, group);
        }

        return group;
    }

    public void DoNotIncludeTogether(ReadOnlySpan<ExperienceItemBuilder> items)
    {
        if (items.Length < 2)
        {
            throw new ArgumentException(
                "An exclusion set must contain at least two distinct items.", nameof(items));
        }

        var copy = items.ToArray();
        if (copy.Any(static item => item is null))
        {
            throw new ArgumentNullException(nameof(items), "An exclusion set cannot contain null item handles.");
        }
        _exclusionSets.Add(copy);
    }

    public void Url(string url)
    {
        _urls.Add(new RegularString(url));
    }

    internal ExperienceList Build()
    {
        if (_title is null)
        {
            throw new InvalidOperationException("Title is required");
        }

        if (_place == default)
        {
            throw new InvalidOperationException("Place is required");
        }

        if (_dateRange is null)
        {
            throw new InvalidOperationException("DateRange is required");
        }

        var builtItems = new Dictionary<ExperienceItemBuilder, ExperienceListItem>();

        // Build items in declaration order so relationships can reference
        // previously declared items.
        foreach (var builder in _itemBuilders)
        {
            var item = builder.Build(builtItems);
            builtItems[builder] = item;
        }

        var itemGroups = _groups.Values.Select(group => group.Build(builtItems)).ToImmutableArray();
        var itemExclusionSets = _exclusionSets
            .Select(set =>
            {
                var items = set
                    .Select(builder =>
                    {
                        if (!builtItems.TryGetValue(builder, out var item))
                        {
                            throw new InvalidOperationException(
                                "An exclusion set contains an item handle that does not belong to its experience list.");
                        }

                        return item;
                    })
                    .ToImmutableArray();
                return new ExperienceItemExclusionSet
                {
                    Items = items,
                };
            })
            .ToImmutableArray();

        var result = new ExperienceList
        {
            Title = new RegularString(_title),
            Place = _place,
            DateRange = _dateRange.Value,
            Type = _type,
            Description = _description,
            Items = [.. builtItems.Values],
            ItemGroups = itemGroups,
            ItemExclusionSets = itemExclusionSets,
            Urls = _urls.DrainToImmutable(),
        };
        result.ValidateItemConfiguration();
        return result;
    }
}

public sealed class ExperienceItemGroupBuilder
{
    private readonly ExperienceListBuilder _owner;
    private readonly List<ExperienceItemBuilder> _items = new();

    internal ExperienceItemGroupBuilder(ExperienceListBuilder owner, string id)
    {
        _owner = owner;
        Id = id;
    }

    public string Id { get; }

    public ExperienceItemBuilder Item(Action<ExperienceItemBuilder> configure)
        => _owner.AddItem(configure, this);

    internal void Add(ExperienceItemBuilder item) => _items.Add(item);

    internal ExperienceItemGroup Build(
        IReadOnlyDictionary<ExperienceItemBuilder, ExperienceListItem> builtItems)
    {
        if (_items.Count == 0)
        {
            throw new InvalidOperationException(
                $"Named experience group '{Id}' must contain at least one item.");
        }

        return new ExperienceItemGroup
        {
            Id = Id,
            Items = _items.Select(item => builtItems[item]).ToImmutableArray(),
        };
    }
}

// Immutable built experience database
public sealed class ExperienceDatabase
{
    public required ImmutableArray<Place> AllPlaces { get; init; }
    public required ImmutableArray<ExperienceList> Experiences { get; init; }

    public IEnumerable<IdentifiedExperienceList> EnumerateExperienceLists()
    {
        for (var listPosition = 0; listPosition < Experiences.Length; listPosition++)
        {
            yield return new(
                new ExperienceListId(listPosition),
                Experiences[listPosition]);
        }
    }

    public IEnumerable<IdentifiedExperienceItem> EnumerateExperienceItems()
    {
        foreach (var identifiedList in EnumerateExperienceLists())
        {
            var items = identifiedList.Value.Items;
            for (var itemPosition = 0; itemPosition < items.Length; itemPosition++)
            {
                yield return new(
                    new ExperienceItemId(identifiedList.Id, itemPosition),
                    identifiedList.Id,
                    identifiedList.Value,
                    items[itemPosition]);
            }
        }
    }
}

public readonly record struct ConfiguredTagWeight(Tag Tag, float Weight);

public sealed class RequiredTagGroup
{
    public RequiredTagGroup(
        Tag canonicalTag,
        ImmutableArray<ConfiguredTagWeight> configuredTags,
        float maximumWeight)
    {
        if (configuredTags.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "A required-tag group must contain at least one configured tag.",
                nameof(configuredTags));
        }

        CanonicalTag = canonicalTag;
        ConfiguredTags = configuredTags;
        MaximumWeight = maximumWeight;
    }

    public Tag CanonicalTag { get; }

    public ImmutableArray<ConfiguredTagWeight> ConfiguredTags { get; }

    public ImmutableArray<ConfiguredTagWeight> ConfiguredAliases => ConfiguredTags;

    public float MaximumWeight { get; }

    public float MaximumGroupWeight => MaximumWeight;
}

public sealed class TagMatchOrigin
{
    public TagMatchOrigin(
        RequiredTagGroup requiredTagGroup,
        float coefficient,
        bool isDirect = false)
    {
        ArgumentNullException.ThrowIfNull(requiredTagGroup);
        RequiredTagGroup = requiredTagGroup;
        Coefficient = coefficient;
        IsDirect = isDirect;
    }

    public RequiredTagGroup RequiredTagGroup { get; }

    public RequiredTagGroup Group => RequiredTagGroup;

    public float Coefficient { get; }

    public float EffectiveCoefficient => Coefficient;

    public bool IsDirect { get; }
}

public sealed class WeightedTagProjection
{
    public WeightedTagProjection(
        Tag targetTag,
        float maximumCoefficient,
        float maximumDirectCoefficient,
        ImmutableArray<TagMatchOrigin> origins)
    {
        TargetTag = targetTag;
        MaximumCoefficient = maximumCoefficient;
        MaximumDirectCoefficient = maximumDirectCoefficient;
        Origins = origins.IsDefault ? [] : origins;
    }

    public Tag TargetTag { get; }

    public Tag ExperienceTag => TargetTag;

    public float MaximumCoefficient { get; }

    public float MaximumRawCoefficient => MaximumCoefficient;

    public float MaximumDirectCoefficient { get; }

    public ImmutableArray<TagMatchOrigin> Origins { get; }
}

public sealed class WeightedTags : IReadOnlyCollection<WeightedTagProjection>
{
    private readonly ImmutableDictionary<Tag, WeightedTagProjection> _byTarget;

    public static WeightedTags Empty { get; } = new([], []);

    internal WeightedTags(
        ImmutableArray<RequiredTagGroup> requiredTagGroups,
        ImmutableArray<WeightedTagProjection> projections)
    {
        RequiredTagGroups = requiredTagGroups.IsDefault ? [] : requiredTagGroups;
        Projections = projections.IsDefault ? [] : projections;
        _byTarget = Projections.ToImmutableDictionary(x => x.TargetTag);
    }

    public ImmutableArray<RequiredTagGroup> RequiredTagGroups { get; }

    public ImmutableArray<WeightedTagProjection> Projections { get; }

    public int Count => Projections.Length;

    public bool IsEmpty => Projections.IsEmpty;

    public static WeightedTags Create(
        ReadOnlySpan<(Tag Tag, float Weight)> inputs)
    {
        var groupBuilders = new Dictionary<Tag, DirectGroupBuilder>();
        var groupOrder = new List<DirectGroupBuilder>();
        foreach (var (tag, weight) in inputs)
        {
            if (!groupBuilders.TryGetValue(tag, out var builder))
            {
                builder = new(tag);
                groupBuilders.Add(tag, builder);
                groupOrder.Add(builder);
            }

            builder.ConfiguredTags.Add(new(tag, weight));
            builder.MaximumWeight = Math.Max(builder.MaximumWeight, weight);
        }

        var groups = groupOrder
            .Select(x =>
            {
                var configuredTags = x.ConfiguredTags.ToImmutableArray();
                return new RequiredTagGroup(
                    x.Tag,
                    configuredTags,
                    x.MaximumWeight);
            })
            .ToImmutableArray();
        var projections = groups
            .Select(group =>
            {
                var origin = new TagMatchOrigin(
                    group,
                    group.MaximumWeight,
                    isDirect: true);
                ImmutableArray<TagMatchOrigin> origins = [origin];
                return new WeightedTagProjection(
                    group.CanonicalTag,
                    group.MaximumWeight,
                    group.MaximumWeight,
                    origins);
            })
            .ToImmutableArray();
        return new(groups, projections);
    }

    public static WeightedTags CreateNamed(
        ReadOnlySpan<(string Tag, float Weight)> inputs)
    {
        var converted = new (Tag Tag, float Weight)[inputs.Length];
        for (var index = 0; index < inputs.Length; index++)
        {
            converted[index] = (new(inputs[index].Tag), inputs[index].Weight);
        }

        return Create(converted);
    }

    public static WeightedTags Direct(
        ReadOnlySpan<(Tag Tag, float Weight)> inputs)
        => Create(inputs);

    public static WeightedTags DirectNamed(
        ReadOnlySpan<(string Tag, float Weight)> inputs)
        => CreateNamed(inputs);

    public bool TryGetValue(
        Tag targetTag,
        out WeightedTagProjection projection)
        => _byTarget.TryGetValue(targetTag, out projection!);

    public ImmutableArray<WeightedTagProjection>.Enumerator GetEnumerator()
        => Projections.GetEnumerator();

    IEnumerator<WeightedTagProjection> IEnumerable<WeightedTagProjection>.GetEnumerator()
        => ((IEnumerable<WeightedTagProjection>) Projections).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable) Projections).GetEnumerator();

    private sealed class DirectGroupBuilder(Tag tag)
    {
        public Tag Tag { get; } = tag;
        public List<ConfiguredTagWeight> ConfiguredTags { get; } = new();
        public float MaximumWeight { get; set; } = float.NegativeInfinity;
    }
}

public sealed class ScoredTagMatch
{
    public ScoredTagMatch(
        WeightedTagProjection projection,
        float evidenceScore,
        float baseContribution,
        float directContribution,
        float directMatchBonus,
        float relevanceContribution)
    {
        ArgumentNullException.ThrowIfNull(projection);
        Projection = projection;
        EvidenceScore = evidenceScore;
        BaseContribution = baseContribution;
        DirectContribution = directContribution;
        DirectMatchBonus = directMatchBonus;
        RelevanceContribution = relevanceContribution;
    }

    public WeightedTagProjection Projection { get; }

    public Tag TargetTag => Projection.TargetTag;

    public float EvidenceScore { get; }

    public float BaseContribution { get; }

    public float DirectContribution { get; }

    public float DirectMatchBonus { get; }

    public float RelevanceContribution { get; }

    public float RawContribution => BaseContribution;
}

public sealed class ScoredTags : IReadOnlyDictionary<Tag, float>
{
    private readonly ImmutableDictionary<Tag, float> _scores;

    public static ScoredTags Empty { get; } = new(
        [],
        ImmutableDictionary<RequiredTagGroup, float>.Empty);

    internal ScoredTags(
        ImmutableArray<ScoredTagMatch> matches,
        ImmutableDictionary<RequiredTagGroup, float> requirementCoverage)
    {
        MatchProvenance = matches.IsDefault ? [] : matches;
        RequirementGroupCoverage = requirementCoverage;
        RequirementCoverage = requirementCoverage.ToImmutableDictionary(
            x => x.Key.CanonicalTag,
            x => x.Value);
        _scores = MatchProvenance.ToImmutableDictionary(
            x => x.TargetTag,
            x => x.RelevanceContribution);
        BaseRelevance = MatchProvenance.Sum(x => x.BaseContribution);
        DirectMatchBonus = MatchProvenance.Sum(x => x.DirectMatchBonus);
        Sum = BaseRelevance + DirectMatchBonus;
    }

    public ImmutableArray<ScoredTagMatch> MatchProvenance { get; }

    public ImmutableArray<ScoredTagMatch> Matches => MatchProvenance;

    public IReadOnlyDictionary<Tag, float> RequirementCoverage { get; }

    public IReadOnlyDictionary<RequiredTagGroup, float> RequirementGroupCoverage { get; }

    public float BaseRelevance { get; }

    public float BaseSum => BaseRelevance;

    public float DirectMatchBonus { get; }

    public float DirectMatchBonusSum => DirectMatchBonus;

    public float Sum { get; }

    public int Count => _scores.Count;

    public bool IsEmpty => Count == 0;

    public IEnumerable<Tag> Keys => _scores.Keys;

    public IEnumerable<float> Values => _scores.Values;

    public float this[Tag key] => _scores[key];

    public bool ContainsKey(Tag key) => _scores.ContainsKey(key);

    public bool TryGetValue(Tag key, out float value)
        => _scores.TryGetValue(key, out value);

    public IEnumerator<KeyValuePair<Tag, float>> GetEnumerator()
        => _scores.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

// Main database builder
public sealed class ExperienceDatabaseBuilder
{
    private readonly ImmutableArray<Place>.Builder _allPlaces = ImmutableArray.CreateBuilder<Place>();
    private readonly ImmutableArray<ExperienceList>.Builder _experiences = ImmutableArray.CreateBuilder<ExperienceList>();

    public ExperienceDatabase Build()
    {
        return new ExperienceDatabase
        {
            AllPlaces = _allPlaces.ToImmutable(),
            Experiences = _experiences.ToImmutable(),
        };
    }

    public Place Place(Place place)
    {
        _allPlaces.Add(place);
        return place;
    }

    public Place Place(string name)
    {
        var place = new Place
        {
            Name = name,
        };
        return Place(place);
    }

    private void Create(ExperienceType type, Action<ExperienceListBuilder> configure)
    {
        var builder = new ExperienceListBuilder(type);
        configure(builder);
        var experience = builder.Build();
        _experiences.Add(experience);
    }

    public void Job(Action<ExperienceListBuilder> configure)
    {
        Create(ExperienceType.Job, configure);
    }

    public void PersonalProject(Action<ExperienceListBuilder> configure)
    {
        Create(ExperienceType.Project, configure);
    }

    public void BachelorsDegree(Action<ExperienceListBuilder> configure)
    {
        Create(ExperienceType.BachelorsDegree, configure);
    }

    public void MastersDegree(Action<ExperienceListBuilder> configure)
    {
        Create(ExperienceType.MastersDegree, configure);
    }
}

public static class ExperienceDatabaseSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.Preserve,
        Converters =
        {
            new JsonStringEnumConverter<ExperienceType>(allowIntegerValues: false),
            new JsonStringEnumConverter<ItemRequirement>(allowIntegerValues: false),
            new JsonStringEnumConverter<ItemMove>(allowIntegerValues: false),
        },
    };

    extension(ExperienceDatabase db)
    {
        public async Task Serialize(Stream output, CancellationToken cancellationToken)
        {
            foreach (var experience in db.Experiences)
            {
                experience.ValidateItemConfiguration();
            }
            await JsonSerializer.SerializeAsync(
                options: Options,
                value: db,
                utf8Json: output,
                cancellationToken: cancellationToken);
        }
    }

    public static async Task<ExperienceDatabase> Deserialize(Stream input, CancellationToken cancellationToken)
    {
        var ret = await JsonSerializer.DeserializeAsync<ExperienceDatabase>(
            options: Options,
            utf8Json: input,
            cancellationToken: cancellationToken);
        if (ret == null)
        {
            throw new InvalidOperationException("File did not contain a db object.");
        }
        foreach (var experience in ret.Experiences)
        {
            experience.ValidateItemConfiguration();
        }
        return ret;
    }
}
