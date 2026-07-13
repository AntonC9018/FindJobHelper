using System.Collections.Immutable;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

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
            new Dictionary<ExperienceListId, LatexHeight>(),
            new Dictionary<Section, LatexHeight>(),
            new Dictionary<Section, LatexHeight>(),
            LatexHeight.Zero,
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
    public void DocumentHeader_RendersMetadataInTwoColumnTable()
    {
        var model = CreateEmptyModel();
        model.CategorizedInfoLists =
        [
            new(Category.Skills, ["API Design"]),
            new(Category.Technologies, [".NET", "PostgreSQL"]),
            new(Category.GitHub, ["https://github.com/example"]),
        ];
        model.CategorizedInfos =
        [
            new(Category.Location, "Example City, Example Country"),
            new(Category.Email, "person@example.test"),
        ];

        var header = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderDocumentHeader(model));

        Assert.Contains(@"\begin{cvmetasectiontable}", header);
        Assert.Contains(@"\end{cvmetasectiontable}", header);
        Assert.Equal(3, CountOccurrences(header, @"\metasection{"));
        Assert.Contains(@"\textbf{Skills:}", header);
        Assert.Contains(@"\textbf{Technologies:}", header);
    }

    [Fact]
    public void DocumentHeader_IncludesFirstSectionSpacing()
    {
        var header = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderDocumentHeader(CreateEmptyModel()));

        Assert.Contains(@"\vspace{\cvflowblockfitskipamount}", header);
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
        Assert.Single(runner.Batches[0].Where(static request => request.CacheKey.Kind == LatexMeasurementKind.ExperienceHeading));
        Assert.Equal(2, cold.ExperienceItems.Count);
        Assert.Single(cold.ExperienceHeadings);
        Assert.Equal(
            cold.GetExperienceItemHeight(new ExperienceItemId(new ExperienceListId(0), 0)),
            cold.GetExperienceItemHeight(new ExperienceItemId(new ExperienceListId(0), 1)));
        Assert.Equal(cold.DocumentChrome, warm.DocumentChrome);
        Assert.Equal(cold.ExperienceItems, warm.ExperienceItems);
        Assert.Equal(cold.ExperienceHeadings, warm.ExperienceHeadings);
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
                LatexMeasurementMode.Box))
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

    [Fact]
    public async Task ProductionEventAndSectionHeights_EqualTheirMeasuredComponents()
    {
        var firstText = CreateRichText(new PlainText { Text = "A short first measured bullet." });
        var secondText = CreateRichText(new PlainText
        {
            Text = "A longer second measured bullet which wraps far enough to exercise the production item width and line spacing consistently.",
        });
        var list = CreateList(firstText, secondText);
        var @event = new Event
        {
            Title = list.Title,
            Place = list.Place,
            DateRange = list.DateRange,
            Text = list.Description,
            Urls = list.Urls,
            SubItems =
            [
                new(0, firstText.ToLatexString()),
                new(0, secondText.ToLatexString()),
            ],
        };
        var linkedList = new ExperienceList
        {
            Title = list.Title,
            Place = list.Place,
            DateRange = list.DateRange,
            Type = list.Type,
            Description = list.Description,
            Items = [list.Items[0]],
            Urls = ["https://example.test/project"],
        };
        var linkedEvent = @event with
        {
            SubItems = [new(0, firstText.ToLatexString())],
            Urls = linkedList.Urls,
        };
        var headingOnlyEvent = new Event
        {
            Title = list.Title,
            Place = list.Place,
            DateRange = list.DateRange,
        };
        var documentModel = CreateEmptyModel();
        documentModel.WorkExperiences = [@event];
        documentModel.SectionOrder = [Section.WorkExperience];
        var completeWorkSection = CvLatexFragmentRenderer.RenderEventsSectionInner(
            documentModel.WorkExperiences,
            "Experience",
            false);
        FormattableString completeDocument = $"{CvLatexFragmentRenderer.RenderDocumentHeader(documentModel)}{CvLatexFragmentRenderer.RenderProductionSection(completeWorkSection)}{CvLatexFragmentRenderer.RenderDocumentFooter(documentModel)}";
        var completeProjectSection = CvLatexFragmentRenderer.RenderEventsSectionInner(
            [@event],
            "Personal Projects",
            false);
        FormattableString twoSectionDocument = $"{CvLatexFragmentRenderer.RenderDocumentHeader(documentModel)}{CvLatexFragmentRenderer.RenderProductionSection(completeWorkSection)}{CvLatexFragmentRenderer.RenderProductionSection(completeProjectSection)}{CvLatexFragmentRenderer.RenderDocumentFooter(documentModel)}";
        var requests = new[]
        {
            Request(1, LatexMeasurementKind.ExperienceChrome, CvLatexFragmentRenderer.RenderExperienceChrome(list), LatexMeasurementMode.ExperienceChromeWithoutPermanentItems),
            Request(2, LatexMeasurementKind.ExperienceItem, CvLatexFragmentRenderer.RenderExperienceItem(list.Items[0]), LatexMeasurementMode.ExperienceItemMarginal),
            Request(3, LatexMeasurementKind.ExperienceItem, CvLatexFragmentRenderer.RenderExperienceItem(list.Items[1]), LatexMeasurementMode.ExperienceItemMarginal),
            Request(4, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEvent(@event, false), LatexMeasurementMode.Box),
            Request(5, LatexMeasurementKind.SectionChrome, CvLatexFragmentRenderer.RenderSectionChrome(Section.WorkExperience), LatexMeasurementMode.SectionChrome),
            Request(6, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEventsSectionInner([@event], "Experience", false), LatexMeasurementMode.FlowBlock),
            Request(7, LatexMeasurementKind.UsablePageHeight, @"\rule{0pt}{\textheight}", LatexMeasurementMode.Box),
            Request(8, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEventsSectionInner([@event, @event], "Experience", false), LatexMeasurementMode.FlowBlock),
            Request(9, LatexMeasurementKind.ExperienceChrome, CvLatexFragmentRenderer.RenderExperienceChrome(linkedList), LatexMeasurementMode.Box),
            Request(10, LatexMeasurementKind.ExperienceItem, CvLatexFragmentRenderer.RenderExperienceItem(linkedList.Items[0]), LatexMeasurementMode.ExperienceItemMarginal),
            Request(11, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEvent(linkedEvent, false), LatexMeasurementMode.Box),
            Request(12, LatexMeasurementKind.DocumentHeader, CvLatexFragmentRenderer.RenderDocumentHeader(documentModel), LatexMeasurementMode.DocumentHeader),
            Request(13, LatexMeasurementKind.DocumentFooter, CvLatexFragmentRenderer.RenderDocumentFooter(documentModel), LatexMeasurementMode.Box),
            Request(14, LatexMeasurementKind.CompleteSection, completeDocument, LatexMeasurementMode.PageStart),
            Request(15, LatexMeasurementKind.CompleteSection, completeProjectSection, LatexMeasurementMode.FlowBlock),
            Request(16, LatexMeasurementKind.CompleteSection, twoSectionDocument, LatexMeasurementMode.PageStart),
            Request(17, LatexMeasurementKind.ExperienceHeading, CvLatexFragmentRenderer.RenderExperienceHeading(list), LatexMeasurementMode.Box),
            Request(18, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEvent(headingOnlyEvent, false), LatexMeasurementMode.Box),
        };
        var runner = new XeLatexMeasurementRunner();

        var measured = await runner.MeasureAsync(ProductionTemplatePath, requests, CancellationToken.None);

        var eventComponents = measured[new(1)].ScaledPoints
            + measured[new(2)].ScaledPoints
            + measured[new(3)].ScaledPoints;
        Assert.True(
            measured[new(4)].ScaledPoints == eventComponents,
            $"chrome={measured[new(1)].ScaledPoints}, item1={measured[new(2)].ScaledPoints}, item2={measured[new(3)].ScaledPoints}, event={measured[new(4)].ScaledPoints}");
        Assert.Equal(
            measured[new(6)].ScaledPoints,
            measured[new(5)].ScaledPoints + measured[new(4)].ScaledPoints);
        Assert.Equal(
            measured[new(8)].ScaledPoints,
            measured[new(5)].ScaledPoints + (2 * measured[new(4)].ScaledPoints));
        Assert.Equal(
            measured[new(11)].ScaledPoints,
            measured[new(9)].ScaledPoints + measured[new(10)].ScaledPoints);
        Assert.Equal(measured[new(17)], measured[new(18)]);
        Assert.True(measured[new(1)].ScaledPoints > measured[new(17)].ScaledPoints);
        Assert.Equal(
            measured[new(14)].ScaledPoints,
            measured[new(12)].ScaledPoints
            + measured[new(13)].ScaledPoints
            + measured[new(6)].ScaledPoints);
        Assert.Equal(
            measured[new(16)].ScaledPoints,
            measured[new(12)].ScaledPoints
            + measured[new(13)].ScaledPoints
            + measured[new(6)].ScaledPoints
            + measured[new(15)].ScaledPoints);
        Assert.True(measured[new(7)].ScaledPoints > measured[new(6)].ScaledPoints);
    }

    [Fact]
    public async Task LatexLog_ReportsOneAndMultiplePageProductionDocuments()
    {
        var shortDirectory = Path.Combine(Path.GetTempPath(), $"fjh-short-pages-{Guid.NewGuid():N}");
        var longDirectory = Path.Combine(Path.GetTempPath(), $"fjh-long-pages-{Guid.NewGuid():N}");
        try
        {
            var shortModel = CreateEmptyModel();
            shortModel.SectionOrder = [];
            await CvTemplate.Generate(new()
            {
                ConfigFilePath = ProductionTemplatePath,
                OutputDirectory = shortDirectory,
                Model = shortModel,
                CancellationToken = CancellationToken.None,
            });

            var longModel = CreateEmptyModel();
            longModel.SectionOrder = [Section.WorkExperience];
            longModel.WorkExperiences = Enumerable.Range(1, 24)
                .Select(position => new Event
                {
                    Title = $"Measured event {position}",
                    Place = Place.Personal,
                    DateRange = DateRange.Completed(new(2020), new(2021)),
                    SubItems =
                    [
                        new(0, new LatexString("A production bullet used to force a genuine multi-page document.")),
                    ],
                })
                .ToImmutableArray();
            await CvTemplate.Generate(new()
            {
                ConfigFilePath = ProductionTemplatePath,
                OutputDirectory = longDirectory,
                Model = longModel,
                CancellationToken = CancellationToken.None,
            });

            Assert.Equal(1, ReadPageCount(shortDirectory));
            Assert.True(ReadPageCount(longDirectory) > 1);
        }
        finally
        {
            if (Directory.Exists(shortDirectory))
            {
                Directory.Delete(shortDirectory, recursive: true);
            }
            if (Directory.Exists(longDirectory))
            {
                Directory.Delete(longDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ProductionGeneration_FailsWhenLeftMetadataExceedsItsColumn()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fjh-wrapped-technologies-{Guid.NewGuid():N}");
        try
        {
            var model = CreateEmptyModel();
            model.SectionOrder = [];
            model.CategorizedInfoLists =
            [
                new(
                    Category.Skills,
                    Enumerable.Repeat<RegularString>("Extremely Long Skill Name", 30).ToImmutableArray()),
            ];
            model.CategorizedInfos = [new(Category.Location, "Example City, Example Country")];

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                CvTemplate.Generate(new()
                {
                    ConfigFilePath = ProductionTemplatePath,
                    OutputDirectory = outputDirectory,
                    Model = model,
                    CancellationToken = CancellationToken.None,
                }));

            Assert.Equal(CvLatexErrors.MetadataLeftOverflowMessage, exception.Message);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    private static IReadOnlyList<LatexMeasurementRequest> CreateProtocolRequests()
    {
        return
        [
            new(
                new MeasurementCorrelationId(1),
                new LatexMeasurementCacheKey(1, LatexMeasurementKind.DocumentHeader, new string('a', 64)),
                "first",
                LatexMeasurementMode.Box),
            new(
                new MeasurementCorrelationId(2),
                new LatexMeasurementCacheKey(1, LatexMeasurementKind.SectionChrome, new string('b', 64)),
                "second",
                LatexMeasurementMode.FlowBlock),
        ];
    }

    private static LatexMeasurementRequest Request(
        int id,
        LatexMeasurementKind kind,
        FormattableString fragment,
        LatexMeasurementMode mode)
        => Request(id, kind, CvLatexFragmentRenderer.Materialize(fragment), mode);

    private static LatexMeasurementRequest Request(
        int id,
        LatexMeasurementKind kind,
        string fragment,
        LatexMeasurementMode mode)
        => new(
            new(id),
            new LatexMeasurementCacheKey(2, kind, id.ToString("x64")),
            fragment,
            mode);

    private static string ProductionTemplatePath => Path.Combine(
        Path.GetDirectoryName(typeof(CvTemplate).Assembly.Location)!,
        "data",
        "cv_template_config.tex");

    private static int ReadPageCount(string outputDirectory)
    {
        var log = File.ReadAllText(Path.Combine(outputDirectory, "main.log"));
        var match = System.Text.RegularExpressions.Regex.Match(
            log,
            @"Output written on main\.(?:pdf|xdv) \((\d+) pages?\b");
        Assert.True(
            match.Success,
            $"LaTeX log did not contain its standard output page-count line. Output lines: {string.Join(" | ", log.Split('\n').Where(static line => line.Contains("Output", StringComparison.OrdinalIgnoreCase)))}");
        return int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string ResultLine(LatexMeasurementRequest request, long height)
        => $"FJH1|corr={request.CorrelationId}|rule={request.CacheKey.RuleVersion}|kind={request.CacheKey.Kind}|sha256={request.CacheKey.ContentHash}|height-sp={height}";

    private static int CountOccurrences(string value, string searchValue)
    {
        var count = 0;
        var startIndex = 0;
        while ((startIndex = value.IndexOf(searchValue, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += searchValue.Length;
        }

        return count;
    }

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
