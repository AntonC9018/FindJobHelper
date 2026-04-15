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
}
