using System.Diagnostics;
using System.Diagnostics.SymbolStore;

namespace FindJobHelper.Core.Tests;

public sealed class SomeTests
{
    private static FileStream GetDbFile(CancellationToken ct)
    {
        var input = new FileStream("data/db.json", FileMode.Open, FileAccess.Read);
        return input;
    }
    private static async Task<ExperienceDatabase> GetDb(CancellationToken ct)
    {
        await using var input = GetDbFile(ct);
        var ret = await ExperienceDatabaseSerializer.Deserialize(input, ct);
        return ret;
    }
    [Fact]
    public async Task DbSerializationBackAndForth()
    {
        var ct = CancellationToken.None;

        var input = GetDbFile(ct);
        var prev = await ExperienceDatabaseSerializer.Deserialize(input, ct);

        using var memStream = new MemoryStream();
        await prev.Serialize(memStream, ct);

        memStream.Position = 0;
        input.Position = 0;

#pragma warning disable CA2000 // Streams not disposed
        var expected = await new StreamReader(input).ReadToEndAsync(ct);
        // ReSharper disable once MethodHasAsyncOverloadWithCancellation
        var actual = new StreamReader(memStream).ReadToEnd();
#pragma warning restore CA2000

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task IntegrationTestOfFunction()
    {
        var ct = CancellationToken.None;
        var db = await GetDb(ct);

        var searchParams = new SearchParams(
            Tags: db.WeightedTasks([
                (".NET", 1.0f),
                ("ASP.NET Core", 1.0f),
                ("TypeScript", 0.5f),
                ("JavaScript", 0.5f),
                ("Unit Tests", 0.8f),
                ("Tailwind", 0.2f),
                ("frontend", 0.5f),
                ("git", 0.2f),
                ("SqlServer", 0.8f),
                ("Java", 1.0f),
            ]),
            TotalItemBudget: 20,
            ScoreLowerBound: 0.0f);

        var ev = db.Experiences.Where(x => x.IsJob).SelectEvents(searchParams);
        await Verify(ev);
    }

    private static readonly RichText text = RichText.Create($"""
    {RichTextFactory.Bold("Hello")} world,
    {RichTextFactory.Href("https://Test.com", RichTextFactory.Bold("url"))}
    Regular text
    """);

    [Fact]
    public void RichTextTest()
    {
        const string expected = """
        Hello world,
        url
        Regular text
        """;
        Assert.Equal(expected, text.ToString());
    }

    [Fact]
    public void RichTextTestTreeEqual()
    {
        var expected = new RichText
        {
            Items = [
                new StyledText
                {
                    Style = StyleFlags.Bold,
                    Text = "Hello",
                },
                new PlainText
                {
                    Text = " world,\r\n",
                },
                new Href
                {
                    Text = new StyledText
                    {
                        Style = StyleFlags.Bold,
                        Text = "url",
                    },
                    Url = new("https://Test.com"),
                },
                new PlainText
                {
                    Text = "\r\nRegular text",
                },
            ],
        };
        var ret = EqualityCheckVisitor.Compare(text, expected) ?? [];
        Assert.Empty(ret);
    }

    [Fact]
    public Task ToLatex()
    {
        return Verify(text.ToLatexString().ToString());
    }
}

public static class EqualityCheckVisitor
{
    public static List<string>? Compare(
        RichText a,
        RichText b)
    {
        var data = new VisitationContextData();
        data.Add(StackKey, new()
        {
            Root = b,
        });
        var visitor = VisitationMap.CreateVisitor(data);
        visitor.Visit(a);
        data.TryGet(Errors, out var errors);
        return errors;
    }

    private static void AddError(RichTextVisitor c, string error)
    {
        var errors = c.Data.GetOrAdd(Errors, () => new());
        errors.Add(error);
    }
    private static void ReportChildCountMismatch(
        IRichTextNode node,
        RichTextVisitor c,
        StackFrame frame)
    {
        c.Data.Action = VisitationAction.Stop;
        AddError(c, $"Node '{node}' doesn't have the name number of children as '{frame.Node}'");
    }

    public static readonly RichTextVisitationMap VisitationMap = RichTextVisitorDefaults.CreateBuilder()
        .Each(next => (node, c) =>
        {
            Before();
            if (c.Data.Action != VisitationAction.Recurse)
            {
                return;
            }
            int countBefore = c.Data.Get(StackKey).Count;
            next(node, c);

            if (c.Data.Action != VisitationAction.Recurse)
            {
                return;
            }
            int countAfter = c.Data.Get(StackKey).Count;
            Debug.Assert(countBefore == countAfter, "Popped extra frames?");
            After();

            void ReportChildCountMismatch1(StackFrame frame)
            {
                ReportChildCountMismatch(node, c, frame);
            }

            void Before()
            {
                var stack = c.Data.Get(StackKey);
                IRichTextNode currentNode;
                if (stack.CurrentFrame is { } frame)
                {
                    if (!frame.Children.MoveNext())
                    {
                        ReportChildCountMismatch1(frame);
                        return;
                    }
                    currentNode = frame.Children.Current;
                }
                else
                {
                    currentNode = stack.Root;
                }
                stack.Add(new()
                {
                    Node = currentNode,
                    Children = c.GetChildren(currentNode),
                });
            }
            void After()
            {
                var stack = c.Data.Get(StackKey);
                var frame = stack.CurrentFrame!;
                if (frame.Children.MoveNext())
                {
                    ReportChildCountMismatch1(frame);
                    return;
                }

                stack.Pop();
            }
        })
        .Compare<Href>((a, b) => a.Url.Equals(b.Url))
        .Compare<PlainText>((a, b) => a.Text.Equals(b.Text))
        .Compare<StyledText>((a, b) => a.Text.Equals(b.Text) && a.Style.Equals(b.Style))
        .Default<RichText>()
        .Build();


    extension(RichTextVisitationMapBuilder builder)
    {
        private RichTextVisitationMapBuilder Compare<T>(Func<T, T, bool> compareValue)
            where T : IRichTextNode
        {
            return builder.Override<T>(next => (node, c) =>
            {
                var frame = c.Data.Get(StackKey).CurrentFrame!;
                if (frame.Node is not T currentNode)
                {
                    AddError(c, $"Node types not equal: '{frame.Node.GetType()}' and '{typeof(T)}' with values '{frame.Node}' and '{node}'");
                }
                else if (!compareValue(currentNode, node))
                {
                    AddError(c, $"'{currentNode}' and '{node}' value not equal");
                }

                next(node, c);
            });
        }
    }

    public sealed class StackFrame
    {
        public required IRichTextNode Node;
        public required IEnumerator<IRichTextNode> Children;
    }
    public sealed class Stack : List<StackFrame>
    {
        public required IRichTextNode Root { get; init; }
        public StackFrame? CurrentFrame => Count == 0 ? null : this[Count - 1];
        public IRichTextNode CurrentNode
        {
            get
            {
                if (Count == 0)
                {
                    return Root;
                }
                return CurrentFrame!.Children.Current;
            }
        }

        public void Pop()
        {
            RemoveAt(Count - 1);
        }
    }
    public sealed class ErrorContext : List<string>
    {
    }
    public static readonly Key<ErrorContext> Errors = new(new("Errors"));

    public static readonly Key<Stack> StackKey = new(new("Stack"));
}
