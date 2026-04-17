using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using MainCli;
using static RichTextFactory;

public sealed class RichTextFactory
{
    public static StyledText Styled(string text, StyleFlags flags)
    {
        return new()
        {
            Text = text,
            Style = flags,
        };
    }

    public static StyledText Italic(string text)
    {
        return Styled(text, StyleFlags.Italic);
    }

    public static StyledText Bold(string text)
    {
        return Styled(text, StyleFlags.Bold);
    }

    public static StyledText Code(string text)
    {
        return Styled(text, StyleFlags.Code);
    }

    public static Href Href(string url, RichTextInterpolatedStringHandler rt)
    {
        var ret = Href(url, rt.Build());
        return ret;
    }

    public static Href Href(string url, IRichTextNode text)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"The given url '{url}' must be a valid url");
        }
        return new()
        {
            Url = uri,
            Text = text,
        };
    }
}

public interface IRichTextNode
{
}

[Flags]
public enum StyleFlags
{
    Italic = 1 << 0,
    Bold = 1 << 1,
    Code = 1 << 2,
}

public sealed class StyledText : IRichTextNode
{
    public required string Text { get; init; }
    public required StyleFlags Style { get; init; }

    public override string ToString() => Text;
}

public sealed class Href : IRichTextNode
{
    public required Uri Url { get; init; }
    public required IRichTextNode Text { get; init; }

    public override string ToString() => Text.ToString()!;
}

public sealed class RichText : IRichTextNode
{
    public required ImmutableArray<IRichTextNode> Items { get; init; }

    public override string ToString() => string.Join("", Items);

    public static RichText Create(RichTextInterpolatedStringHandler rt)
    {
        return rt.Build();
    }
}

public sealed class PlainText : IRichTextNode
{
    public required string Text { get; init; }

    public override string ToString() => Text;
}

public static class ExampleUsage
{


    public static void Test()
    {
        _ = RichText.Create($"""
        Hello this is plain text,
        Here's a link: {Href("https://example.com", $"{Italic("Hello")} world")}.
        Here's some text: {Styled("", StyleFlags.Bold | StyleFlags.Italic)} {Bold("bold")} {Code("code")}
        """);
    }
}

public readonly struct RichTextBuilder(int len)
{
    private readonly ImmutableArray<IRichTextNode>.Builder Builder =
        ImmutableArray.CreateBuilder<IRichTextNode>(len);

    public void Add(IRichTextNode t)
    {
        Builder.Add(t);
    }

    public RichText Build()
    {
        var x = Builder.DrainToImmutable();
        return new()
        {
            Items = x,
        };
    }
}

[InterpolatedStringHandler]
public readonly struct RichTextInterpolatedStringHandler
{
    private readonly RichTextBuilder _builder;

    public RichTextInterpolatedStringHandler(
        int literalLen,
        int formattedCount)
    {
        int segmentCountEstimate = formattedCount + literalLen / 8;
        _builder = new(segmentCountEstimate);
    }

    public void AppendLiteral(string s)
    {
        if (s.Length == 0)
        {
            return;
        }
        _builder.Add(new PlainText
        {
            Text = s,
        });
    }

    public void AppendFormatted(IRichTextNode rt)
    {
        _builder.Add(rt);
    }

    public RichText Build()
    {
        return _builder.Build();
    }
}

public delegate void DefaultVisit(IRichTextNode node, RichTextVisitor visitor);
public delegate DefaultVisit Visit(DefaultVisit next);
public delegate void DefaultVisit<T>(T node, RichTextVisitor visitor);
public delegate DefaultVisit<T> Visit<T>(DefaultVisit<T> next);
public delegate IEnumerator<IRichTextNode> GetChildren(IRichTextNode node);
public delegate IEnumerator<IRichTextNode> GetChildren<T>(T node);

public sealed class RichTextVisitationMapBuilder
{
    private sealed class Impl()
    {
        public Delegate? Default = null;
        public Delegate? GetChildren = null;
        public List<Delegate> List = new(1);
    }

    private DefaultVisit? _unhandled = null;
    private Visit? _each = null;
    private readonly Dictionary<Type, Impl> _impls;

    public RichTextVisitationMapBuilder(RichTextVisitationMapBuilder other)
    {
        _impls = other._impls.ToDictionary();
    }

    public RichTextVisitationMapBuilder()
    {
        _impls = new();
    }

    // private Visit Wrap<T>(Visit<T> func) => (node, visitor) => func((T) node, visitor);

    private Impl GetOrAdd<T>()
    {
        ref var x = ref CollectionsMarshal.GetValueRefOrAddDefault(_impls, typeof(T), out bool exists);
        if (!exists)
        {
            x = new();
        }
        return x!;
    }

    public RichTextVisitationMapBuilder Children<T>(GetChildren<T> func)
    {
        var x = GetOrAdd<T>();
        x.GetChildren = func;
        return this;
    }

    public RichTextVisitationMapBuilder OnUnhandled(DefaultVisit func)
    {
        _unhandled = func;
        return this;
    }

    public RichTextVisitationMapBuilder SetDefault<T>(DefaultVisit<T> func)
    {
        var x = GetOrAdd<T>();
        x.Default = func;
        return this;
    }

    public RichTextVisitationMapBuilder Default<T>()
    {
        var x = GetOrAdd<T>();
        // TODO: Some flag to ensure this.
        // Maybe add default action flag to validate
        _ = x;
        return this;
    }

    public RichTextVisitationMapBuilder DefaultDoNothing<T>()
    {
        return SetDefault<T>((a, b) =>
        {
            _ = a;
            _ = b;
        });
    }

    public RichTextVisitationMapBuilder Override<T>(Visit<T> func)
    {
        var x = GetOrAdd<T>();
        x.List.Add(func);
        return this;
    }

    public RichTextVisitationMapBuilder Each(Visit func)
    {
        _each = func;
        return this;
    }

    public RichTextVisitationMapBuilder Copy() => new(this);

    public RichTextVisitationMap Build()
    {
        var unhandled = _unhandled ?? (static (a, b) =>
        {
            _ = b;
            throw new InvalidOperationException($"Unhandled type {a.GetType()}");
        });
        return new(unhandled, Ret().ToFrozenDictionary());

        IEnumerable<KeyValuePair<Type, RichTextVisitationMap.Impl>> Ret()
        {
            foreach (var (type, list) in _impls)
            {
                var factory = GenericHandleList
                    .MakeGenericMethod(type)
                    .CreateDelegate<Func<Impl, RichTextVisitationMap.Impl>>(this);
                var handler = factory(list);
                // Do it like this for now.
                if (_each is { } each)
                {
                    var prev = handler.Visit;
                    handler = handler with
                    {
                        Visit = each(prev),
                    };
                }
                yield return new(type, handler);
            }
        }
    }

    private RichTextVisitationMap.Impl HandleList<T>(Impl list)
        where T : IRichTextNode
    {
        var span = CollectionsMarshal.AsSpan(list.List);
        var current = ((DefaultVisit<T>?) list.Default);

        GetChildren? getChildrenRet = null;
        {
            if (list.GetChildren is { } getChildren)
            {
                var c = (GetChildren<T>) getChildren;
                getChildrenRet = n => c((T) n);
            }
        }

        {
            if (current is null && list.GetChildren is { } getChildren)
            {
                var c = (GetChildren<T>) getChildren;
                current = (a, b) =>
                {
                    using var children = c(a);
                    while (true)
                    {
                        if (!children.MoveNext())
                        {
                            return;
                        }
                        if (b.Data.Action != VisitationAction.Recurse)
                        {
                            return;
                        }
                        b.Visit(children.Current);
                    }
                };
            }
        }
        if (current is null)
        {
            current = static (a, b) =>
            {
                _ = a;
                _ = b;
            };
        }
        foreach (var x in span)
        {
            var t = (Visit<T>) x;
            current = t(current);
        }
        return new(
            Visit: (a, b) => current((T) a, b),
            GetChildren: getChildrenRet);
    }

    private static readonly MethodInfo GenericHandleList = typeof(RichTextVisitationMapBuilder)
        .GetMethod(nameof(HandleList), BindingFlags.Instance | BindingFlags.NonPublic)!;
}



public sealed class RichTextVisitationMap
{
    internal readonly record struct Impl(DefaultVisit Visit, GetChildren? GetChildren);
    private readonly DefaultVisit _onUnhandled;
    private readonly FrozenDictionary<Type, Impl> _impls;
    private static readonly CompletedEnumeratorT CompletedEnumerator = new();

    private sealed class CompletedEnumeratorT : IEnumerator<IRichTextNode>
    {
        public bool MoveNext() => false;
        public void Reset()
        {
        }
        public IRichTextNode Current => throw new InvalidOperationException();
        object? IEnumerator.Current => Current;
        public void Dispose()
        {
        }
    }

    internal RichTextVisitationMap(
        DefaultVisit onUnhandled,
        FrozenDictionary<Type, Impl> impls)
    {
        _impls = impls;
        _onUnhandled = onUnhandled;
    }

    public DefaultVisit GetHandler(Type type)
    {
        if (_impls.TryGetValue(type, out var i))
        {
            return i.Visit;
        }
        return _onUnhandled;
    }

    public IEnumerator<IRichTextNode> GetChildren(IRichTextNode node)
    {
        var type = node.GetType();
        if (_impls.TryGetValue(type, out var t) && t.GetChildren is { } getChildren)
        {
            return getChildren(node);
        }
        return CompletedEnumerator;
    }

    public RichTextVisitor CreateVisitor()
    {
        return new(this, new());
    }

    public RichTextVisitor CreateVisitor(VisitationContextData contextData)
    {
        return new(this, contextData);
    }
}

public readonly record struct Key(string Value);
public readonly record struct Key<T>(Key Value);

public enum VisitationAction
{
    Recurse,
    Default = Recurse,
    Stop,
}

public sealed class VisitationContextData()
{
    private struct V
    {
        public V(Key key) => Key = key;
        public readonly Key Key;
        public object? Value;
    }
    private readonly List<V> _values = new();
    public VisitationAction Action { get; set; }

    public void Clear() => _values.Clear();

    public void Set<T>(Key<T> key, T value)
    {
        ref var x = ref GetOrAdd(key.Value);
        x = value;
    }
    public T GetOrAdd<T>(Key<T> key, Func<T> factory)
    {
        ref object? obj = ref GetRef(key.Value, out bool exists);
        if (exists)
        {
            return (T) obj!;
        }
        var ret = factory();
        AddRef(key.Value) = ret;
        return ret;
    }
    public void Add<T>(Key<T> key, T value)
    {
        _ = ref GetRef(key.Value, out bool exists);
        if (exists)
        {
            throw new InvalidOperationException($"Cannot reset key '{key.Value.Value}' that has not been added!");
        }
        AddRef(key.Value) = value;
    }
    public void Reset<T>(Key<T> key, T value)
    {
        ref var x = ref GetRef(key.Value, out bool exists);
        if (!exists)
        {
            throw new InvalidOperationException($"Cannot reset key '{key.Value.Value}' that has not been added!");
        }
        x = value;
    }
    public T Get<T>(Key<T> key)
    {
        ref object? obj = ref GetRef(key.Value, out bool exists);
        if (!exists)
        {
            throw new InvalidOperationException($"Cannot reset key '{key.Value.Value}' that has not been added!");
        }
        return (T) obj!;
    }
    public bool TryGet<T>(Key<T> key, [NotNullWhen(true)] out T? value)
    {
        ref object? obj = ref GetRef(key.Value, out bool exists);
        if (!exists)
        {
            value = default;
        }
        else
        {
            value = (T) obj!;
        }
        return exists;
    }

    private ref object? GetOrAdd(Key key)
    {
        {
            ref object? obj = ref GetRef(key, out bool exists);
            if (exists)
            {
                return ref obj;
            }
        }
        {
            return ref AddRef(key);
        }
    }

    private ref object? AddRef(Key key)
    {
        _values.Add(new V(key));
        return ref CollectionsMarshal.AsSpan(_values)[^1].Value;
    }

    private ref object? GetRef(Key key, out bool exists)
    {
        var span = CollectionsMarshal.AsSpan(_values);
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i].Key == key)
            {
                exists = true;
                return ref span[i].Value;
            }
        }
        exists = false;
        return ref Unsafe.NullRef<object?>();
    }
}

public readonly struct RichTextVisitor
{
    private readonly RichTextVisitationMap _visitationMap;
    public VisitationContextData Data { get; }

    public RichTextVisitor(
        RichTextVisitationMap visitationMap,
        VisitationContextData data)
    {
        _visitationMap = visitationMap;
        Data = data;
    }

    public void Visit(IRichTextNode node)
    {
        var type = node.GetType();
        var handler = _visitationMap.GetHandler(type);
        handler(node, this);
    }

    public IEnumerator<IRichTextNode> GetChildren(IRichTextNode node)
    {
        return _visitationMap.GetChildren(node);
    }
}

public static class RichTextVisitorDefaults
{
    private static RichTextVisitationMapBuilder Builder { get; } =
        new RichTextVisitationMapBuilder()
            .Children<Href>(node => SingleItemEnumerator.Create(node.Text))
            .Children<RichText>(GetChildren)
        // .OnUnhandled((node, c) =>
        // {
        //     _ = node;
        //     _ = c;
        // })
        ;

    private static IEnumerator<IRichTextNode> GetChildren(RichText rt)
    {
        foreach (var x in rt.Items)
        {
            yield return x;
        }
    }

    public static RichTextVisitationMapBuilder CreateBuilder() => Builder.Copy();
    public static readonly Key<StringBuilder> OutputKey = new(new("Output"));
}


public static class LatexConverter
{
    public static LatexString ToLatexString(
        this RichText richText)
    {
        var visitor = VisitationMap.CreateVisitor();
        var data = new StringBuilder();
        visitor.Data.Add(RichTextVisitorDefaults.OutputKey, data);

        visitor.Visit(richText);

        return new(data.ToString());
    }
    private static Key<StringBuilder> Key => RichTextVisitorDefaults.OutputKey;

    private static RichTextVisitationMap VisitationMap = RichTextVisitorDefaults.CreateBuilder()
        .Override<Href>(next => (node, c) =>
        {
            var sb = c.Data.Get(Key);
            // needs escaping or no?
            // might depend on the context.
            var str = node.Url.ToString();
            sb.Append($@"\href{{{str}}}{{");
            next(node, c);
            sb.Append("}");
        })
        .Override<StyledText>(next => (node, c) =>
        {
            var sb = c.Data.Get(Key);
            var str = new RegularString(node.Text);

            int indent = 0;
            foreach (var x in new (StyleFlags Flag, string Label)[]
                {
                    (StyleFlags.Bold, "textbf"),
                    // Might fail, consider verb||
                    (StyleFlags.Code, "texttt"),
                    (StyleFlags.Italic, "textit"),
                })
            {
                if (node.Style.HasFlag(x.Flag))
                {
                    sb.Append($@"\{x.Label}{{");
                    indent++;
                }
            }

            sb.Append($"{str}");

            next(node, c);

            for (int i = 0; i < indent; i++)
            {
                sb.Append("}");
            }
        })
        .Override<PlainText>(next => (node, c) =>
        {
            // Won't escape by default??
            c.Data.Get(Key).Append(new RegularString(node.Text));
            next(node, c);
        })
        .Default<RichText>()
        .Build();
}

public static class MarkdownConverter
{
    public static string ToMarkdownString(
        this RichText richText)
    {
        var visitor = VisitationMap.CreateVisitor();
        var sb = new StringBuilder();
        visitor.Data.Add(Key, sb);

        visitor.Visit(richText);

        return sb.ToString();
    }

    private static Key<StringBuilder> Key = RichTextVisitorDefaults.OutputKey;
    private static RichTextVisitationMap VisitationMap = RichTextVisitorDefaults.CreateBuilder()
        .Override<Href>(next => (node, c) =>
        {
            var sb = c.Data.Get(Key);
            sb.Append("[");
            next(node, c);
            var str = node.Url.ToString();
            sb.Append($"]({str})");
        })
        .Override<StyledText>(next => (node, c) =>
        {
            var sb = c.Data.Get(Key);

            void InsertChars(bool reverse)
            {
                var chars = new (StyleFlags Flag, string Label)[]
                {
                    (StyleFlags.Bold, "**"),
                    // Might fail, consider verb||
                    (StyleFlags.Italic, "*"),
                    (StyleFlags.Code, "`"),
                };
                if (reverse)
                {
                    Array.Reverse(chars);
                }
                foreach (var x in chars)
                {
                    if (node.Style.HasFlag(x.Flag))
                    {
                        sb.Append(x.Label);
                    }
                }
            }

            InsertChars(reverse: false);
            // TODO: escape
            sb.Append($"{node.Text}");
            next(node, c);
            InsertChars(reverse: true);
        })
        .Override<PlainText>(next => (node, c) =>
        {
            c.Data.Get(Key).Append(node.Text);
            next(node, c);
        })
        .Default<RichText>()
        .Build();
}
