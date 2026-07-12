using System.Collections.Immutable;
using FindJobHelper.CVGeneration;
using FindJobHelper.Core.Helper;

namespace FindJobHelper.Core.Tests;

public sealed class LatexMeasurementTests
{
    [Fact]
    public void Enumeration_AssignsDeterministicPositionBasedIds()
    {
        var database = CreateDatabase(
            CreateRichText(new PlainText { Text = "one" }),
            CreateRichText(new PlainText { Text = "two" }));

        var list = Assert.Single(database.EnumerateExperienceLists());
        Assert.Equal(new ExperienceListId(0), list.Id);

        var items = database.EnumerateExperienceItems().ToArray();
        Assert.Equal(new ExperienceItemId(new ExperienceListId(0), 0), items[0].Id);
        Assert.Equal(new ExperienceItemId(new ExperienceListId(0), 1), items[1].Id);
        Assert.Equal(items.Select(static item => item.Value), database.Experiences[0].Items);
    }

    [Fact]
    public void ItemIds_WithEqualLocalPositionsInDifferentLists_AreDistinct()
    {
        var first = CreateList(CreateRichText(new PlainText { Text = "same" }));
        var second = CreateList(CreateRichText(new PlainText { Text = "same" }));
        var database = new ExperienceDatabase { AllPlaces = [], Experiences = [first, second] };

        var ids = database.EnumerateExperienceItems().Select(static item => item.Id).ToArray();

        Assert.NotEqual(ids[0], ids[1]);
        Assert.Equal(0, ids[0].Position);
        Assert.Equal(0, ids[1].Position);
    }

    [Fact]
    public void SnapshotCheckedAccessors_ReportTheMissingTypedId()
    {
        var snapshot = new CvMeasurementSnapshot(
            new Dictionary<ExperienceItemId, LatexHeight>(),
            new Dictionary<ExperienceListId, LatexHeight>(),
            new Dictionary<Section, LatexHeight>(),
            new Dictionary<Section, LatexHeight>(),
            LatexHeight.Zero);
        var missing = new ExperienceItemId(new ExperienceListId(3), 4);

        var exception = Assert.Throws<KeyNotFoundException>(() => snapshot.GetExperienceItemHeight(missing));

        Assert.Contains(missing.ToString(), exception.Message);
    }

    [Fact]
    public void RichTextHash_IsCanonicalAndStructureSensitive()
    {
        var equivalentA = CreateRichText(
            new PlainText { Text = "text" },
            new StyledText { Text = "styled", Style = StyleFlags.Bold },
            new Href { Url = new Uri("https://example.test/a"), Text = new PlainText { Text = "link" } });
        var equivalentB = CreateRichText(
            new PlainText { Text = "text" },
            new StyledText { Text = "styled", Style = StyleFlags.Bold },
            new Href { Url = new Uri("https://example.test/a"), Text = new PlainText { Text = "link" } });
        var changedStyle = CreateRichText(
            new PlainText { Text = "text" },
            new StyledText { Text = "styled", Style = StyleFlags.Italic },
            new Href { Url = new Uri("https://example.test/a"), Text = new PlainText { Text = "link" } });
        var changedStructure = CreateRichText(
            new RichText { Items = [new PlainText { Text = "text" }] },
            new StyledText { Text = "styled", Style = StyleFlags.Bold },
            new Href { Url = new Uri("https://example.test/a"), Text = new PlainText { Text = "link" } });

        Assert.Equal(
            RichTextCanonicalHasher.ComputeHash(equivalentA),
            RichTextCanonicalHasher.ComputeHash(equivalentB));
        Assert.NotEqual(
            RichTextCanonicalHasher.ComputeHash(equivalentA),
            RichTextCanonicalHasher.ComputeHash(changedStyle));
        Assert.NotEqual(
            RichTextCanonicalHasher.ComputeHash(equivalentA),
            RichTextCanonicalHasher.ComputeHash(changedStructure));
    }

    [Fact]
    public void Protocol_MapsShuffledRowsByCorrelationAndRejectsBadMetadata()
    {
        var requests = CreateProtocolRequests();
        var lines = requests.Reverse().Select((request, index) => ResultLine(request, 100 + index)).ToArray();

        var result = LatexMeasurementResultParser.ParseAndValidate(lines, requests);

        Assert.Equal(101, result[new MeasurementCorrelationId(1)].ScaledPoints);
        Assert.Equal(100, result[new MeasurementCorrelationId(2)].ScaledPoints);
        Assert.Throws<InvalidOperationException>(() =>
            LatexMeasurementResultParser.ParseAndValidate([lines[0], lines[0]], requests));
        Assert.Throws<InvalidOperationException>(() =>
            LatexMeasurementResultParser.ParseAndValidate(
                [lines[0].Replace("corr=M00000002", "corr=M00000009"), lines[1]],
                requests));
        Assert.Throws<InvalidOperationException>(() =>
            LatexMeasurementResultParser.ParseAndValidate(
                [lines[0].Replace("kind=SectionChrome", "kind=DocumentChrome"), lines[1]],
                requests));
    }

    [Fact]
    public void MeasurementDocument_UsesOnlySharedTemplateMeasurementPrimitives()
    {
        var requests = CreateProtocolRequests();

        var source = LatexMeasurementDocument.Generate("C:/template.tex", "results.txt", requests);

        Assert.Contains(@"\input{C:/template.tex}", source);
        Assert.Contains(@"\cvsetmeasurementbox{", source);
        Assert.Contains(@"\cvsetmeasurementsectionbox{", source);
        Assert.DoesNotContain(@"\begin{flowblock}", source);
        Assert.DoesNotContain(@"\pagegoal", source);
        Assert.DoesNotContain(@"\newpage", source);
        Assert.DoesNotContain(@"\usebox", source);
        Assert.DoesNotContain("enumitem", source);
        Assert.DoesNotContain("geometry", source);
        Assert.DoesNotContain("setmainfont", source);
    }

    [Fact]
    public async Task Service_DeduplicatesDuplicateItemsAndWarmCacheSkipsRunner()
    {
        using var fixture = new CacheFixture();
        var sameA = CreateRichText(new PlainText { Text = "duplicate" });
        var sameB = CreateRichText(new PlainText { Text = "duplicate" });
        var database = CreateDatabase(sameA, sameB);
        var model = CreateEmptyModel();
        var runner = new RecordingRunner();
        var service = new LatexMeasurementService(fixture.CachePath, runner, ruleVersion: 17);

        var cold = await service.MeasureAsync(database, model, fixture.TemplatePath, CancellationToken.None);
        var warm = await service.MeasureAsync(database, model, fixture.TemplatePath, CancellationToken.None);

        Assert.Equal(1, runner.CallCount);
        Assert.Single(runner.Batches[0].Where(static request => request.CacheKey.Kind == LatexMeasurementKind.ExperienceItem));
        Assert.Equal(2, cold.ExperienceItems.Count);
        Assert.Equal(
            cold.GetExperienceItemHeight(new ExperienceItemId(new ExperienceListId(0), 0)),
            cold.GetExperienceItemHeight(new ExperienceItemId(new ExperienceListId(0), 1)));
        Assert.Equal(cold.DocumentChrome, warm.DocumentChrome);
        Assert.Equal(cold.ExperienceItems, warm.ExperienceItems);
        Assert.Equal(cold.ExperienceChrome, warm.ExperienceChrome);
        Assert.Equal(cold.CompleteSections, warm.CompleteSections);
        Assert.Equal(cold.SectionChrome, warm.SectionChrome);
    }

    [Fact]
    public async Task ChangedRuleVersionPurgesAndRecomputesAllRequiredKeys()
    {
        using var fixture = new CacheFixture();
        var database = CreateDatabase(CreateRichText(new PlainText { Text = "item" }));
        var model = CreateEmptyModel();
        var firstRunner = new RecordingRunner();
        await new LatexMeasurementService(fixture.CachePath, firstRunner, 1)
            .MeasureAsync(database, model, fixture.TemplatePath, CancellationToken.None);
        var secondRunner = new RecordingRunner();

        await new LatexMeasurementService(fixture.CachePath, secondRunner, 2)
            .MeasureAsync(database, model, fixture.TemplatePath, CancellationToken.None);

        Assert.Equal(1, firstRunner.CallCount);
        Assert.Equal(1, secondRunner.CallCount);
        Assert.Equal(firstRunner.Batches[0].Count, secondRunner.Batches[0].Count);
    }

    [Fact]
    public async Task PartialCache_MeasuresOnlyTheNewContentKey()
    {
        using var fixture = new CacheFixture();
        var model = CreateEmptyModel();
        var firstDatabase = CreateDatabase(CreateRichText(new PlainText { Text = "first" }));
        await new LatexMeasurementService(fixture.CachePath, new RecordingRunner(), 1)
            .MeasureAsync(firstDatabase, model, fixture.TemplatePath, CancellationToken.None);
        var expandedDatabase = CreateDatabase(
            CreateRichText(new PlainText { Text = "first" }),
            CreateRichText(new PlainText { Text = "new" }));
        var partialRunner = new RecordingRunner();

        await new LatexMeasurementService(fixture.CachePath, partialRunner, 1)
            .MeasureAsync(expandedDatabase, model, fixture.TemplatePath, CancellationToken.None);

        var request = Assert.Single(Assert.Single(partialRunner.Batches));
        Assert.Equal(LatexMeasurementKind.ExperienceItem, request.CacheKey.Kind);
    }

    [Fact]
    public async Task FailedCompilation_DoesNotCommitAnyMissRows()
    {
        using var fixture = new CacheFixture();
        var database = CreateDatabase(CreateRichText(new PlainText { Text = "item" }));
        var model = CreateEmptyModel();
        var failing = new ThrowingRunner();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LatexMeasurementService(fixture.CachePath, failing, 1)
                .MeasureAsync(database, model, fixture.TemplatePath, CancellationToken.None));
        var retry = new RecordingRunner();

        await new LatexMeasurementService(fixture.CachePath, retry, 1)
            .MeasureAsync(database, model, fixture.TemplatePath, CancellationToken.None);

        Assert.Equal(failing.RequestCount, Assert.Single(retry.Batches).Count);
    }

    [Fact]
    public async Task CancellationAfterCompilation_DoesNotCommitAnyMissRows()
    {
        using var fixture = new CacheFixture();
        var database = CreateDatabase(CreateRichText(new PlainText { Text = "item" }));
        var model = CreateEmptyModel();
        using var cancellation = new CancellationTokenSource();
        var cancellingRunner = new CancellingRunner(cancellation);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LatexMeasurementService(fixture.CachePath, cancellingRunner, 1)
                .MeasureAsync(database, model, fixture.TemplatePath, cancellation.Token));
        var retry = new RecordingRunner();

        await new LatexMeasurementService(fixture.CachePath, retry, 1)
            .MeasureAsync(database, model, fixture.TemplatePath, CancellationToken.None);

        Assert.Equal(cancellingRunner.RequestCount, Assert.Single(retry.Batches).Count);
    }

    [Fact]
    public async Task HiddenBoxBatch_IsPageAndOrderIndependent()
    {
        var templatePath = Path.Combine(
            Path.GetDirectoryName(typeof(CvTemplate).Assembly.Location)!,
            "data",
            "cv_template_config.tex");
        var requests = Enumerable.Range(1, 4)
            .Select(position => new LatexMeasurementRequest(
                new MeasurementCorrelationId(position),
                new LatexMeasurementCacheKey(
                    1,
                    LatexMeasurementKind.ExperienceItem,
                    position.ToString("x64")),
                @"\rule{0pt}{900pt}",
                false))
            .ToArray();
        var runner = new XeLatexMeasurementRunner();

        var batch = await runner.MeasureAsync(templatePath, requests, CancellationToken.None);
        var reversed = await runner.MeasureAsync(templatePath, requests.Reverse().ToArray(), CancellationToken.None);
        var alone = await runner.MeasureAsync(templatePath, [requests[0]], CancellationToken.None);

        foreach (var request in requests)
        {
            Assert.Equal(alone[requests[0].CorrelationId], batch[request.CorrelationId]);
            Assert.Equal(batch[request.CorrelationId], reversed[request.CorrelationId]);
        }
    }

    private static IReadOnlyList<LatexMeasurementRequest> CreateProtocolRequests()
    {
        return
        [
            new(
                new MeasurementCorrelationId(1),
                new LatexMeasurementCacheKey(1, LatexMeasurementKind.DocumentChrome, new string('a', 64)),
                "first",
                false),
            new(
                new MeasurementCorrelationId(2),
                new LatexMeasurementCacheKey(1, LatexMeasurementKind.SectionChrome, new string('b', 64)),
                "second",
                true),
        ];
    }

    private static string ResultLine(LatexMeasurementRequest request, long height)
        => $"FJH1|corr={request.CorrelationId}|rule={request.CacheKey.RuleVersion}|kind={request.CacheKey.Kind}|sha256={request.CacheKey.ContentHash}|height-sp={height}";

    private static ExperienceDatabase CreateDatabase(params RichText[] items)
        => new() { AllPlaces = [], Experiences = [CreateList(items)] };

    private static ExperienceList CreateList(params RichText[] items)
        => new()
        {
            Title = "Title",
            Place = Place.Personal,
            DateRange = DateRange.Completed(new OptionalDateParts(2020), new OptionalDateParts(2021)),
            Type = ExperienceType.Project,
            Items = items.Select(static text => new ExperienceListItem { Text = text }).ToImmutableArray(),
        };

    private static RichText CreateRichText(params IRichTextNode[] items) => new() { Items = [.. items] };

    private static CvDataModel CreateEmptyModel() => new()
    {
        Name = new("First", "Last"),
        Profession = new("Developer"),
        CategorizedInfoLists = [],
        CategorizedInfos = [],
    };

    private sealed class RecordingRunner : ILatexMeasurementRunner
    {
        public int CallCount { get; private set; }
        public List<IReadOnlyList<LatexMeasurementRequest>> Batches { get; } = [];

        public Task<IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight>> MeasureAsync(
            string templatePath,
            IReadOnlyList<LatexMeasurementRequest> requests,
            CancellationToken cancellationToken)
        {
            _ = templatePath;
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Batches.Add(requests.ToArray());
            IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight> result = requests.ToDictionary(
                static request => request.CorrelationId,
                static request => new LatexHeight(10_000 + request.CorrelationId.Value));
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingRunner : ILatexMeasurementRunner
    {
        public int RequestCount { get; private set; }

        public Task<IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight>> MeasureAsync(
            string templatePath,
            IReadOnlyList<LatexMeasurementRequest> requests,
            CancellationToken cancellationToken)
        {
            _ = templatePath;
            _ = cancellationToken;
            RequestCount = requests.Count;
            throw new InvalidOperationException("simulated compilation failure");
        }
    }

    private sealed class CancellingRunner(CancellationTokenSource cancellation) : ILatexMeasurementRunner
    {
        public int RequestCount { get; private set; }

        public Task<IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight>> MeasureAsync(
            string templatePath,
            IReadOnlyList<LatexMeasurementRequest> requests,
            CancellationToken cancellationToken)
        {
            _ = templatePath;
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount = requests.Count;
            IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight> result = requests.ToDictionary(
                static request => request.CorrelationId,
                static request => new LatexHeight(42));
            cancellation.Cancel();
            return Task.FromResult(result);
        }
    }

    private sealed class CacheFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"fjh-measurement-test-{Guid.NewGuid():N}");
        public string CachePath => Path.Combine(_directory, "cache.sqlite3");
        public string TemplatePath => Path.Combine(_directory, "template.tex");

        public CacheFixture()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(TemplatePath, "% test template");
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
