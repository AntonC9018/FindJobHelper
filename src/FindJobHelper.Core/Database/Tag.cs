using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FindJobHelper.Core;

public readonly record struct Tag : IEquatable<Tag>
{
    [JsonConstructor]
    public Tag(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public override int GetHashCode()
    {
        return Name.GetHashCode(StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(Tag other)
    {
        return Name.Equals(other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public override string ToString() => Name;
}

public readonly record struct TagRelation(
    Tag OtherTag,
    OverlapScore PercentageOfSelfIncludedInTheOtherTag);

public readonly struct Builders
{
    internal readonly TagBuilder Self;
    internal readonly TagBuilder Other;

    public Builders(TagBuilder self, TagBuilder other)
    {
        Self = self;
        Other = other;
    }
}

public readonly struct TagBuilderOverlapClause(Builders b)
{
    public TagBuilderOtherClauseStart Fully()
    {
        return By(1.0f);
    }
    public TagBuilderOtherClauseStart By(float score)
    {
        var s = OverlapScore.Create(score);
        b.Self.OverlapsWithArray.Add(new(b.Other, s));
        return new(b);
    }
}

public readonly struct TagBuilderOtherClauseStart(Builders b)
{
    public TagBuilderOtherClause WhichIsIncludedInIt()
    {
        return new(b);
    }
}

public readonly struct TagBuilderOtherClause(Builders b)
{
    public void Fully()
    {
        By(1.0f);
    }

    public void By(float score)
    {
        b.Other.IsIncludedIn(b.Self).By(score);
    }
}

public readonly record struct OverlapScore : ISpanFormattable
{
    internal OverlapScore(float value)
    {
        Value = value;
    }

    public static OverlapScore Create(float value)
    {
        if (value > 1.0f || value <= 0)
        {
            throw new ArgumentException("Must be between 0 and 1", nameof(value));
        }
        return new(value);
    }

    public float Value { get; }

    public static OverlapScore Full => new(1.0f);

    public string ToString(string? format, IFormatProvider? formatProvider) => $"{this}";
    public override string ToString() => $"{this}";

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        return destination.TryWrite(provider, $"{Value}", out charsWritten);
    }
}

public readonly record struct TagBuilderLink(
    TagBuilder OtherTag,
    OverlapScore PercentageOfSelfIncludedInTheOtherTag);

public sealed class TagBuilder
{
    public required string Name { get; init; }
    internal List<TagBuilderLink> OverlapsWithArray { get; } = new();

    /// <summary>
    /// a.IsIncludedIn(b).By(0.9f) means 10% of a is not in b, 90% is
    /// </summary>
    public TagBuilderOverlapClause IsIncludedIn(TagBuilder other)
    {
        return new(new(this, other));
    }

    public void SameAs(TagBuilder other)
    {
        IsIncludedIn(other).Fully().WhichIsIncludedInIt().Fully();
    }
}

public enum MissingOverlapBehavior
{
    Error,
    UseMinimum,
}


public abstract record class TagsDatabaseCreationError
{
}

public sealed record class DifferentOverlapSpecifiedSecondTime : TagsDatabaseCreationError
{
    public required Tag TagA { get; init; }
    public required Tag TagB { get; init; }
    public required OverlapScore ExistingScore { get; init; }
    public required OverlapScore NewScore { get; init; }

    public override string ToString()
    {
        return $"Link '{TagA.Name}' to '{TagB.Name}' different score specified for the second time ({NewScore}, not {ExistingScore})";
    }
}

public readonly record struct TagPath(
    Tag A,
    Tag B,
    Tag C,
    OverlapScore AB,
    OverlapScore BC,
    OverlapScore AC);

public sealed record class TransitiveImplicationError : TagsDatabaseCreationError
{
    public required TagPath TagPath { get; init; }
    public required OverlapScore MinAC { get; init; }

    public override string ToString()
    {
        var x = TagPath;
        return $"{x.A}->{x.B}:{x.AB}' -> '{x.B}->{x.C}:{x.BC}', '{x.A}->{x.C}:{x.AC}' must be at least {MinAC}, but was {x.AC}";
    }
}

public sealed record class NotEnoughInformationToImplyInclusionTransitively : TagsDatabaseCreationError
{
    public required Tag TagA { get; init; }
    public required Tag TagB { get; init; }

    public override string ToString()
    {
        return $"There is not enough information to imply the inclusion of tag '{TagA}' into '{TagB}'";
    }
}

public readonly record struct TagsDatabaseCreateResult(
    List<TagsDatabaseCreationError>? Errors,
    TagsDatabase? Database)
{
    public TagsDatabase GetResultOrThrow()
    {
        if (Errors != null)
        {
            throw new InvalidOperationException(
                string.Join(Environment.NewLine, Errors));
        }
        return Database!;
    }
}

public sealed class TagsDatabaseBuilder
{
    private readonly Dictionary<Tag, TagBuilder> _allTags = new();

    public TagBuilder Tag(Tag tag)
    {
        return AddTag(tag);
    }

    public TagBuilder Tag(string name, params ReadOnlySpan<string> aliases)
    {
        var t = AddTag(new(name));
        foreach (var x in aliases)
        {
            t.SameAs(AddTag(new(x)));
        }
        return t;
    }

    private TagBuilder AddTag(Tag tag)
    {
        if (_allTags.TryGetValue(tag, out var v))
        {
            return v;
        }

        v = new TagBuilder
        {
            Name = tag.Name,
        };
        _allTags.Add(tag, v);
        return v;
    }

    private readonly record struct Combo(
        Tag A,
        Tag B,
        Tag C,
        OverlapScore AB,
        OverlapScore AC,
        OverlapScore BA,
        OverlapScore CA,
        OverlapScore BC,
        OverlapScore CB)
    {
        public TagPath GetPath(Node a, Node b, Node c)
        {
            if (a == b || b == c || a == c)
            {
                Debug.Fail("Must pass all different options");
            }
            return new(
                A: GetTag(a),
                B: GetTag(b),
                C: GetTag(c),
                AB: GetOverlap(a, b),
                BC: GetOverlap(b, c),
                AC: GetOverlap(a, c));
        }

        public OverlapScore GetOverlap(Node a, Node b)
        {
            return (a, b) switch
            {
                (Node.A, Node.B) => AB,
                (Node.A, Node.C) => AC,
                (Node.B, Node.A) => BA,
                (Node.B, Node.C) => BC,
                (Node.C, Node.A) => CA,
                (Node.C, Node.B) => CB,
                _ => throw null!,
            };
        }

        public Tag GetTag(Node node)
        {
            return node switch
            {
                Node.A => A,
                Node.B => B,
                Node.C => C,
                _ => throw null!,
            };
        }
    }

    private enum Node
    {
        A,
        B,
        C,
    }

    private readonly struct OverlapDict(int count)
    {
        public static OverlapScore NoneValue => new(-1);
        private readonly Dictionary<Tag, OverlapScore> _impl = new(count);

        public OverlapScore GetValueOrNone(Tag key) => _impl.GetValueOrDefault(key, NoneValue);
        public ref OverlapScore GetValueRefOrAddDefault(Tag key, out bool exists)
        {
            return ref CollectionsMarshal.GetValueRefOrAddDefault(_impl, key, out exists);
        }

        public OverlapScore Get(Tag tag) => _impl[tag];

        public Dictionary<Tag, OverlapScore>.Enumerator GetEnumerator() => _impl.GetEnumerator();
        public Dictionary<Tag, OverlapScore>.KeyCollection Keys => _impl.Keys;

        public bool ContainsKey(Tag key) => _impl.ContainsKey(key);
        public void Add(Tag key, OverlapScore value) => _impl.Add(key, value);

        public void Clear() => _impl.Clear();
        public bool IsEmpty => _impl.Count == 0;
    }

    private struct BuilderContext(int tagCount)
    {
        private List<TagsDatabaseCreationError>? _errors = null;
        public void AddError(TagsDatabaseCreationError err)
        {
            (_errors ??= new()).Add(err);
        }

        public TagsDatabaseCreateResult? ErrorReturn()
        {
            if (_errors is null)
            {
                return null;
            }
            return new(_errors, null);
        }

        public readonly Dictionary<Tag, OverlapDict> Overlaps = new(tagCount);
        public readonly Dictionary<Tag, OverlapDict> NewMaxMinOverlaps = new(tagCount);

        public void ClearNewMaxMin()
        {
            foreach (var x in NewMaxMinOverlaps)
            {
                x.Value.Clear();
            }
        }
        public bool MinMaxChanged()
        {
            foreach (var x in NewMaxMinOverlaps)
            {
                if (!x.Value.IsEmpty)
                {
                    return true;
                }
            }
            return false;
        }

        public readonly List<Tag> AllKeys = new(tagCount);
    }

    public TagsDatabaseCreateResult Build(
        MissingOverlapBehavior missingBehavior = MissingOverlapBehavior.UseMinimum)
    {
        var context = new BuilderContext(_allTags.Count);

        context.AllKeys.AddRange(_allTags.Keys);

        foreach (var a in context.AllKeys)
        {
            context.NewMaxMinOverlaps.Add(a, new(0));
        }

        foreach (var a in context.AllKeys)
        {
            var value = _allTags[a].OverlapsWithArray;
            var dict = new OverlapDict(value.Count);
            context.Overlaps.Add(a, dict);

            foreach (var x in value)
            {
                var b = new Tag(x.OtherTag.Name);
                ref var score = ref dict.GetValueRefOrAddDefault(b, out bool exists);
                if (exists)
                {
                    if (score != x.PercentageOfSelfIncludedInTheOtherTag)
                    {
                        context.AddError(new DifferentOverlapSpecifiedSecondTime
                        {
                            ExistingScore = score,
                            NewScore = x.PercentageOfSelfIncludedInTheOtherTag,
                            TagA = a,
                            TagB = b,
                        });
                    }
                }
                else
                {
                    score = x.PercentageOfSelfIncludedInTheOtherTag;
                }
            }
        }

        {
            if (context.ErrorReturn() is { } e)
            {
                return e;
            }
        }

        var combos = Combos();
        while (true)
        {
            // ReSharper disable once PossibleMultipleEnumeration
            foreach (var x in combos)
            {
                const Node a = Node.A;
                const Node b = Node.B;
                const Node c = Node.C;

                Handle(x.GetPath(a, b, c));
                Handle(x.GetPath(a, c, b));
                Handle(x.GetPath(b, c, a));
                Handle(x.GetPath(b, a, c));
                Handle(x.GetPath(c, a, b));
                Handle(x.GetPath(c, b, a));
            }

            bool minMaxChanged = context.MinMaxChanged();
            if (minMaxChanged)
            {
                foreach (var x in context.NewMaxMinOverlaps)
                {
                    var nodes = context.Overlaps[x.Key];
                    foreach (var y in x.Value)
                    {
                        Debug.Assert(!nodes.ContainsKey(y.Key));
                        nodes.Add(y.Key, y.Value);
                    }
                }
                context.ClearNewMaxMin();
            }

            {
                if (context.ErrorReturn() is { })
                {
                    break;
                }
            }

            if (!minMaxChanged)
            {
                break;
            }
        }

        // Make sure if a is connected to b, b is connected to a.
        // foreach (var (a, anodes) in context.Overlaps)
        // {
        //     foreach (var b in anodes.Keys)
        //     {
        //         if (!context.Overlaps[b].ContainsKey(a))
        //         {
        //             context.AddError(new NotEnoughInformationToImplyInclusionTransitively
        //             {
        //                 TagA = a,
        //                 TagB = b,
        //             });
        //         }
        //     }
        // }

        {
            if (context.ErrorReturn() is { } e)
            {
                return e;
            }
        }

        var graph = Ret().ToFrozenDictionary();
        var ret = new TagsDatabase(graph, context.AllKeys.ToImmutableArray());
        return new(null, ret);

        IEnumerable<KeyValuePair<Tag, Relations>> Ret()
        {
            var builder = ImmutableArray.CreateBuilder<TagRelation>();
            foreach (var x in context.Overlaps)
            {
                foreach (var val in x.Value)
                {
                    builder.Add(new(val.Key, val.Value));
                }

                var arr = builder.ToImmutable();
                yield return new(x.Key, new(arr));

                builder.Clear();
            }
        }

        void Handle(TagPath x)
        {
            var minac = MinAC(x.AB, x.BC);
            if (minac == OverlapDict.NoneValue)
            {
                return;
            }

            bool Less(OverlapScore a, OverlapScore b)
            {
                float err = 0.0001f;
                if (a.Value + err < b.Value)
                {
                    return true;
                }
                return false;
            }

            switch (missingBehavior)
            {
                case MissingOverlapBehavior.Error:
                    {
                        if (Less(x.AC, minac))
                        {
                            TransitivityError();
                        }
                        break;
                    }
                case MissingOverlapBehavior.UseMinimum:
                    {
                        if (x.AC == OverlapDict.NoneValue)
                        {
                            ref var v = ref context.NewMaxMinOverlaps[x.A].GetValueRefOrAddDefault(x.C, out bool exists);
                            if (!exists || minac.Value > v.Value)
                            {
                                v = minac;
                            }
                        }
                        else if (Less(x.AC, minac))
                        {
                            TransitivityError();
                        }
                        break;
                    }
                default:
                    {
                        throw new NotImplementedException();
                    }
            }

            void TransitivityError()
            {
                context.AddError(new TransitiveImplicationError
                {
                    MinAC = minac,
                    TagPath = x,
                });
            }
        }

        static OverlapScore MinAC(OverlapScore ab, OverlapScore bc)
        {
            if (ab == OverlapDict.NoneValue)
            {
                return OverlapDict.NoneValue;
            }
            if (bc == OverlapDict.NoneValue)
            {
                return OverlapDict.NoneValue;
            }

            var abVal = ab.Value;
            var bcVal = bc.Value;
            var ret = abVal + bcVal - 1;
            if (ret <= 0)
            {
                return OverlapDict.NoneValue;
            }
            return new(ret);
        }

        IEnumerable<Combo> Combos()
        {
            var keys = context.AllKeys;
            for (int ai = 0; ai < keys.Count; ai++)
            {
                var a = keys[ai];
                var anodes = context.Overlaps[a];

                for (int ci = ai + 1; ci < keys.Count; ci++)
                {
                    var c = keys[ci];
                    var cnodes = context.Overlaps[c];

                    var ac = anodes.GetValueOrNone(c);
                    var ca = cnodes.GetValueOrNone(a);

                    foreach (var (b, ab) in anodes)
                    {
                        var cb = cnodes.GetValueOrNone(b);
                        if (cb == OverlapDict.NoneValue)
                        {
                            continue;
                        }

                        var bnodes = context.Overlaps[b];
                        yield return new(
                            A: a,
                            B: b,
                            C: c,
                            AB: ab,
                            BC: bnodes.GetValueOrNone(c),
                            AC: ac,
                            CA: ca,
                            BA: bnodes.GetValueOrNone(a),
                            CB: cb);
                    }
                }
            }
        }

#if false
        IEnumerable<Combo> Combos()
        {
            foreach (var a in context.Overlaps.Keys)
            {
                var anodes = context.Overlaps[a];
                context.AKeys.AddRange(anodes.Keys);

                foreach (var b in context.AKeys)
                {
                    var bnodes = context.Overlaps[b];
                    context.BKeys.AddRange(bnodes.Keys);
                    var ab = anodes[b];

                    foreach (var c in context.BKeys)
                    {
                        var cnodes = context.Overlaps[c];
                        var bc = bnodes[c];

                        var cb = cnodes.GetValueOrDefault(b, default);
                        var ac = anodes.GetValueOrDefault(c, default);
                        var ca = cnodes.GetValueOrDefault(a, default);
                        var ba = bnodes.GetValueOrDefault(a, default);

                        yield return new(
                            A: a,
                            B: b,
                            C: c,
                            AB: ab,
                            BA: ba,
                            BC: bc,
                            CA: ca,
                            CB: cb,
                            AC: ac);
                    }
                }
            }
        }
#endif
    }
}

public readonly struct Relations
{
    private readonly ImmutableArray<TagRelation> _impl;
    public Relations(ImmutableArray<TagRelation> impl)
    {
        _impl = impl;
    }

    public OverlapScore GetOverlapWith(Tag tag)
    {
        foreach (var x in _impl)
        {
            if (x.OtherTag == tag)
            {
                return x.PercentageOfSelfIncludedInTheOtherTag;
            }
        }
        return default;
    }

    public ImmutableArray<TagRelation>.Enumerator GetEnumerator() => _impl.GetEnumerator();
}

public sealed class TagsDatabase
{
    private readonly ImmutableArray<Tag> _declarationOrder;

    public FrozenDictionary<Tag, Relations> TagsGraph { get; }

    internal TagsDatabase(
        FrozenDictionary<Tag, Relations> tagsGraph,
        ImmutableArray<Tag> declarationOrder)
    {
        TagsGraph = tagsGraph;
        _declarationOrder = declarationOrder;
    }

    internal ImmutableArray<Tag> DeclarationOrder => _declarationOrder;

    internal Tag Resolve(Tag tag)
    {
        foreach (var declaredTag in _declarationOrder)
        {
            if (declaredTag == tag)
            {
                return declaredTag;
            }
        }

        throw new InvalidOperationException($"Not found tag {tag.Name}");
    }
}

public readonly record struct TagNode(Tag Tag, Relations Relations)
{
    public static implicit operator Tag(TagNode self) => self.Tag;
}

public static class TagsDatabaseExtensions
{
    private sealed class RequiredGroupBuilder(Tag canonicalTag)
    {
        public Tag CanonicalTag { get; } = canonicalTag;
        public List<ConfiguredTagWeight> ConfiguredTags { get; } = new();
        public float MaximumWeight { get; set; } = float.NegativeInfinity;
    }

    private sealed class ProjectionBuilder(Tag targetTag)
    {
        public Tag TargetTag { get; } = targetTag;
        public float MaximumCoefficient { get; set; } = float.NegativeInfinity;
        public float MaximumDirectCoefficient { get; set; } = float.NegativeInfinity;
        public bool HasDirectCoefficient { get; set; }
        public List<TagMatchOrigin> Origins { get; } = new();
    }

    extension(TagsDatabase self)
    {
        public WeightedTags Weighted(ReadOnlySpan<(string Tag, float Weight)> inputs)
        {
            var converted = new (Tag Tag, float Weight)[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                converted[i] = (new Tag(inputs[i].Tag), inputs[i].Weight);
            }
            return self.Weighted(converted);
        }

        public WeightedTags Weighted(ReadOnlySpan<(Tag Tag, float Weight)> inputs)
        {
            var aliases = AliasCanonicals();
            var groupBuilders = new Dictionary<Tag, RequiredGroupBuilder>();
            var groupOrder = new List<RequiredGroupBuilder>();
            foreach (var (configuredTag, weight) in inputs)
            {
                var resolvedTag = self.Resolve(configuredTag);
                var canonicalTag = aliases[resolvedTag];
                if (!groupBuilders.TryGetValue(canonicalTag, out var builder))
                {
                    builder = new(canonicalTag);
                    groupBuilders.Add(canonicalTag, builder);
                    groupOrder.Add(builder);
                }

                builder.ConfiguredTags.Add(new(configuredTag, weight));
                builder.MaximumWeight = Math.Max(builder.MaximumWeight, weight);
            }

            var groups = groupOrder
                .Select(x => new RequiredTagGroup(
                    x.CanonicalTag,
                    [.. x.ConfiguredTags],
                    x.MaximumWeight))
                .ToImmutableArray();
            var projectionBuilders = new Dictionary<Tag, ProjectionBuilder>();
            var projectionOrder = new List<ProjectionBuilder>();
            foreach (var group in groups)
            {
                AddProjection(
                    group.CanonicalTag,
                    group.MaximumWeight,
                    group,
                    isDirect: true);
                foreach (var link in self.RelationsOf(group.CanonicalTag))
                {
                    AddProjection(
                        link.OtherTag,
                        link.PercentageOfSelfIncludedInTheOtherTag.Value
                            * group.MaximumWeight,
                        group,
                        isDirect: AreAliases(
                            group.CanonicalTag,
                            link.OtherTag));
                }
            }

            return new(
                groups,
                [
                    .. projectionOrder
                        .Select(x => new WeightedTagProjection(
                            x.TargetTag,
                            x.MaximumCoefficient,
                            x.HasDirectCoefficient
                                ? x.MaximumDirectCoefficient
                                : 0,
                            [.. x.Origins])),
                ]);

            void AddProjection(
                Tag targetTag,
                float coefficient,
                RequiredTagGroup group,
                bool isDirect)
            {
                if (!projectionBuilders.TryGetValue(targetTag, out var projection))
                {
                    projection = new(targetTag);
                    projectionBuilders.Add(targetTag, projection);
                    projectionOrder.Add(projection);
                }

                projection.MaximumCoefficient = Math.Max(
                    projection.MaximumCoefficient,
                    coefficient);
                if (isDirect)
                {
                    projection.HasDirectCoefficient = true;
                    projection.MaximumDirectCoefficient = Math.Max(
                        projection.MaximumDirectCoefficient,
                        coefficient);
                }
                if (coefficient > 0)
                {
                    projection.Origins.Add(new(group, coefficient, isDirect));
                }
            }

            Dictionary<Tag, Tag> AliasCanonicals()
            {
                var ret = new Dictionary<Tag, Tag>();
                foreach (var tag in self.DeclarationOrder)
                {
                    var canonical = tag;
                    foreach (var earlierTag in self.DeclarationOrder)
                    {
                        if (earlierTag == tag)
                        {
                            break;
                        }

                        if (AreAliases(earlierTag, tag))
                        {
                            canonical = ret[earlierTag];
                            break;
                        }
                    }

                    ret.Add(tag, canonical);
                }

                return ret;
            }

            bool AreAliases(Tag left, Tag right)
            {
                return self.RelationsOf(left).GetOverlapWith(right)
                           == OverlapScore.Full
                       && self.RelationsOf(right).GetOverlapWith(left)
                           == OverlapScore.Full;
            }
        }

        public Relations RelationsOf(Tag tag)
        {
            if (!self.TagsGraph.TryGetValue(tag, out var x))
            {
                throw new InvalidOperationException($"Not found tag {tag}");
            }
            return x;
        }

        public TagNode Find(Tag tag)
        {
            var resolved = self.Resolve(tag);
            if (!self.TagsGraph.TryGetValue(resolved, out var x))
            {
                throw new InvalidOperationException($"Not found tag {tag.Name}");
            }
            return new(resolved, x);
        }

        public TagNode Find(string text)
        {
            var tag = new Tag(text);
            return self.Find(tag);
        }
    }

    extension(WeightedTags self)
    {
        public ScoredTags Match(
            ImmutableArray<TagReference> tags,
            ScoreBoost directMatchBoost = default)
        {
            var matches = ImmutableArray.CreateBuilder<ScoredTagMatch>();
            var coverage =
                ImmutableDictionary.CreateBuilder<RequiredTagGroup, float>();
            foreach (var tagReference in tags)
            {
                if (!self.TryGetValue(
                        tagReference.Tag,
                        out var projection))
                {
                    continue;
                }

                var evidenceScore = (float) tagReference.Score;
                var baseContribution =
                    evidenceScore * projection.MaximumCoefficient;
                var directContribution =
                    evidenceScore * projection.MaximumDirectCoefficient;
                var directMatchBonus = directMatchBoost.Apply(directContribution);
                matches.Add(new(
                    projection,
                    evidenceScore,
                    baseContribution,
                    directContribution,
                    directMatchBonus,
                    baseContribution + directMatchBonus));
                foreach (var origin in projection.Origins)
                {
                    var contribution = evidenceScore * origin.Coefficient;
                    if (contribution <= 0)
                    {
                        continue;
                    }

                    coverage[origin.RequiredTagGroup] =
                        coverage.GetValueOrDefault(origin.RequiredTagGroup)
                        + contribution;
                }
            }

            return new(
                matches.DrainToImmutable(),
                coverage.ToImmutable());
        }
    }
}

public static class TagDatabaseSerializer
{
    private sealed record SerializedTagDatabase(
        ImmutableArray<SerializedTag> Tags);

    private sealed record SerializedTag(
        string Name,
        ImmutableArray<SerializedTagRelation> Relations);

    private sealed record SerializedTagRelation(
        string OtherTag,
        float Overlap);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    extension(TagsDatabase db)
    {
        public async Task Serialize(Stream output, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(db);
            var serialized = new SerializedTagDatabase(
                db.DeclarationOrder
                    .Select(tag => new SerializedTag(
                        tag.Name,
                        SerializeRelations(db.RelationsOf(tag))))
                    .ToImmutableArray());
            await JsonSerializer.SerializeAsync(
                options: Options,
                value: serialized,
                utf8Json: output,
                cancellationToken: cancellationToken);
        }
    }

    public static async Task<TagsDatabase> Deserialize(Stream input, CancellationToken cancellationToken)
    {
        var serialized = await JsonSerializer.DeserializeAsync<SerializedTagDatabase>(
            options: Options,
            utf8Json: input,
            cancellationToken: cancellationToken);
        if (serialized is null)
        {
            throw new InvalidOperationException("File did not contain a db object.");
        }

        var declarationOrder = serialized.Tags
            .Select(static tag => new Tag(tag.Name))
            .ToImmutableArray();
        var tagsByName = declarationOrder.ToDictionary(
            static tag => tag.Name,
            StringComparer.OrdinalIgnoreCase);
        var graph = serialized.Tags
            .Select(tag => KeyValuePair.Create(
                tagsByName[tag.Name],
                new Relations(tag.Relations
                    .Select(relation => new TagRelation(
                        tagsByName.TryGetValue(relation.OtherTag, out var otherTag)
                            ? otherTag
                            : throw new JsonException(
                                $"Tag relation references unknown tag '{relation.OtherTag}'."),
                        OverlapScore.Create(relation.Overlap)))
                    .ToImmutableArray())))
            .ToFrozenDictionary();
        return new(graph, declarationOrder);
    }

    private static ImmutableArray<SerializedTagRelation> SerializeRelations(
        Relations relations)
    {
        var builder = ImmutableArray.CreateBuilder<SerializedTagRelation>();
        foreach (var relation in relations)
        {
            builder.Add(new(
                relation.OtherTag.Name,
                relation.PercentageOfSelfIncludedInTheOtherTag.Value));
        }

        return builder.DrainToImmutable();
    }
}

public abstract class KnownTags<T>
{
    protected KnownTags<U> MapImpl<U>(Func<T, U> f)
    {
        var retType = GetType().GetGenericTypeDefinition().MakeGenericType(typeof(U));
        var ret = Activator.CreateInstance(retType);
        var source = this.GetType().GetProperties();
        var target = retType.GetProperties();
        int len = source.Length;
        for (int i = 0; i < len; i++)
        {
            var val = f((T) source[i].GetValue(this)!);
            target[i].SetValue(ret, val);
        }
        return (KnownTags<U>) ret!;
    }
}
