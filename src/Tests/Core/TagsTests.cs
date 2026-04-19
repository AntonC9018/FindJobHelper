namespace FindJobHelper.Core.Tests;

public sealed class TagsTests
{
    [Fact]
    public void TransitiveCBA()
    {
        var (tags, db) = CreateOkTags(tags =>
        {
            tags.b.OverlapsWith(tags.a).By(0.9f).WhichOverlaps().By(0.9f);
            tags.c.OverlapsWith(tags.b).By(0.9f).WhichOverlaps().By(0.9f);
        });
        _ = db;

        Equal(0.8f, tags.c.Relations.GetOverlapWith(tags.a));
    }

    [Fact]
    public void PointedAtTagDoesntLinkBack_IsAnError()
    {
        var (tags, errors) = Errors(tags =>
        {
            tags.a.OverlapsWith(tags.b).By(0.9f);
        });
        var err = Assert.Single(errors);
        Assert.Equal(new NotEnoughInformationToImplyInclusionTransitively
        {
            TagA = tags.a,
            TagB = tags.b,
        }, err);
    }

    private static void Equal(float a, OverlapScore b)
    {
        Assert.Equal(a, b.Value, tolerance: 0.001f);
    }

    private sealed class Tags<T> : KnownTags<T>
    {
        public Tags<U> Map<U>(Func<T, U> f) => (Tags<U>) MapImpl(f);

        public required T a { get; init; }
        public required T b { get; init; }
        public required T c { get; init; }
        public required T d { get; init; }
        public required T e { get; init; }
    }

    private static (TagsDatabaseCreateResult, Tags<TagBuilder>) CreateTestTags(
        Action<Tags<TagBuilder>> configure)
    {
        var builder = new TagsDatabaseBuilder();
        var t = new Tags<TagBuilder>
        {
            a = builder.Tag("a"),
            b = builder.Tag("b"),
            c = builder.Tag("c"),
            d = builder.Tag("d"),
            e = builder.Tag("e"),
        };
        configure(t);

        var tagsResult = builder.Build();
        return (tagsResult, t);
    }

    private (Tags<Tag> Tags, List<TagsDatabaseCreationError> Errors) Errors(
        Action<Tags<TagBuilder>> configure)
    {
        var (tagsResult, t) = CreateTestTags(configure);
        _ = t;
        Assert.NotNull(tagsResult.Errors);
        var tags = t.Map(x => new Tag(x.Name));
        return (tags, tagsResult.Errors);
    }

    private (Tags<TagNode> Tags, TagsDatabase Database) CreateOkTags(
        Action<Tags<TagBuilder>> configure)
    {
        var (tagsResult, t) = CreateTestTags(configure);
        Assert.Empty(tagsResult.Errors ?? []);
        var db = tagsResult.Database!;
        var tags = t.Map(x => db.Find(x.Name));
        return (tags, db);
    }

}
