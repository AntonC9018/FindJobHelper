using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CliWrap;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexMeasurementTests
{
    [Fact]
    public void SnapshotExposesOnlyTheCurrentMeasurementContract()
    {
        Assert.Single(typeof(CvMeasurementSnapshot).GetConstructors());
    }

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
            experienceItems: new Dictionary<ExperienceItemId, LatexHeight>(),
            experienceHeadings: new Dictionary<ExperienceListId, LatexHeight>(),
            experienceChrome: new Dictionary<ExperienceListId, LatexHeight>(),
            currentPageCompleteSections: new Dictionary<Section, LatexHeight>(),
            currentPageSectionChrome: new Dictionary<Section, LatexHeight>(),
            freshPageSectionChrome: new Dictionary<Section, LatexHeight>(),
            documentHeader: LatexHeight.Zero,
            documentFooter: LatexHeight.Zero,
            usablePageHeight: LatexHeight.Zero);
        var missing = new ExperienceItemId(new ExperienceListId(3), 4);

        var exception = Assert.Throws<KeyNotFoundException>(() => snapshot.GetExperienceItemHeight(missing));

        Assert.Contains(missing.ToString(), exception.Message);
    }

    [Fact]
    public void SnapshotKeepsDocumentPartsSeparateAndDerivesFreshSectionHeight()
    {
        var snapshot = new CvMeasurementSnapshot(
            experienceItems: new Dictionary<ExperienceItemId, LatexHeight>(),
            experienceHeadings: new Dictionary<ExperienceListId, LatexHeight>(),
            experienceChrome: new Dictionary<ExperienceListId, LatexHeight>(),
            currentPageCompleteSections:
                new Dictionary<Section, LatexHeight> { [Section.WorkExperience] = new(40) },
            currentPageSectionChrome:
                new Dictionary<Section, LatexHeight> { [Section.WorkExperience] = new(10) },
            freshPageSectionChrome:
                new Dictionary<Section, LatexHeight> { [Section.WorkExperience] = new(15) },
            documentHeader: new(5),
            documentFooter: new(6),
            usablePageHeight: new(100));

        Assert.Equal(5, snapshot.DocumentHeader.ScaledPoints);
        Assert.Equal(6, snapshot.DocumentFooter.ScaledPoints);
        Assert.Equal(45, snapshot.GetFreshPageCompleteSectionHeight(Section.WorkExperience).ScaledPoints);
        Assert.Equal(
            45,
            snapshot.DeriveFreshPageSectionHeight(Section.WorkExperience, new(40)).ScaledPoints);
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
        Assert.Throws<CvMeasurementException>(() =>
            LatexMeasurementResultParser.ParseAndValidate([lines[0], lines[0]], requests));
        Assert.Throws<CvMeasurementException>(() =>
            LatexMeasurementResultParser.ParseAndValidate(
                [lines[0].Replace("corr=M00000002", "corr=M00000009"), lines[1]],
                requests));
        Assert.Throws<CvMeasurementException>(() =>
            LatexMeasurementResultParser.ParseAndValidate(
                [lines[0].Replace("corr=M00000002", "corr=B00000002"), lines[1]],
                requests));
        Assert.Throws<CvMeasurementException>(() =>
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
        Assert.Contains(
            "FJH_PROGRESS_COMPLETED:M00000001",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "FJH_MEASUREMENT_COMPLETED:",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("enumitem", source);
        Assert.DoesNotContain("geometry", source);
        Assert.DoesNotContain("setmainfont", source);
    }

    [Fact]
    public void DocumentHeaderMeasurementFlushesPageGlueWithoutFixedSizeCorrection()
    {
        var request = Request(
            1,
            LatexMeasurementKind.DocumentHeader,
            "header",
            LatexMeasurementMode.DocumentHeader);

        var source = LatexMeasurementDocument.Generate(
            "C:/template.tex",
            "results.txt",
            [request]);

        Assert.Contains(@"\nointerlineskip\vbox{}", source, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\cvmeasurementsentinelsection", source, StringComparison.Ordinal);
        Assert.DoesNotContain("2.5pt", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionSectionUsesLabelledAtomicCurrentAndFreshWrappers()
    {
        var rendered = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderProductionSection(
                Section.WorkExperience,
                CvLatexFragmentRenderer.RenderSectionChrome(Section.WorkExperience)));
        var template = File.ReadAllText(ProductionTemplatePath);

        Assert.Contains(@"\begin{flowblock}{ WorkExperience }", rendered, StringComparison.Ordinal);
        Assert.Contains(@"\newcommand{\cvflowblockcurrentcontent}", template, StringComparison.Ordinal);
        Assert.Contains(@"\newcommand{\cvflowblockfreshcontent}", template, StringComparison.Ordinal);
        Assert.Contains(@"\cvsetflowcontentbox{\flowcurrentbox}{\cvflowblockcurrentcontent{\BODY}}", template, StringComparison.Ordinal);
        Assert.Contains(@"\cvsetflowcontentbox{\flowfreshbox}{\cvflowblockfreshcontent{\BODY}}", template, StringComparison.Ordinal);
        Assert.Contains(@"\nointerlineskip\box\flowcurrentbox", template, StringComparison.Ordinal);
        Assert.Contains(@"\nointerlineskip\box\flowfreshbox", template, StringComparison.Ordinal);
        Assert.DoesNotContain("+1pt", template, StringComparison.Ordinal);
        Assert.Contains(CvLatexErrors.SectionPageOverflowMarker, template, StringComparison.Ordinal);
        Assert.DoesNotContain("Large blocks become breakable", template, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentHeader_IncludesFirstSectionSpacing()
    {
        var header = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderDocumentHeader(CreateEmptyModel()));

        Assert.Contains(@"\vspace{\cvsectionspacing}", header);
    }

    [Fact]
    public void Renderer_ConvertsRichTextAtLatexBoundaryAndOmitsNullText()
    {
        var @event = new Event
        {
            Title = "Title",
            Place = Place.Personal,
            DateRange = DateRange.Completed(new(2024), new(2025)),
            Text = new StyledText
            {
                Text = "description_&",
                Style = StyleFlags.Italic,
            },
            SubItems =
            [
                new(0, new PlainText { Text = "bullet_&" }),
            ],
        };

        var renderedEvent = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderEvent(@event));
        var renderedNullEvent = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderEvent(@event with
            {
                Text = null,
                SubItems = [],
            }));

        Assert.Contains(@"\cveventitem{bullet\_\&}", renderedEvent, StringComparison.Ordinal);
        Assert.Contains(@"\textit{description\_\&}", renderedEvent, StringComparison.Ordinal);
        Assert.EndsWith("{}{}{}", renderedNullEvent, StringComparison.Ordinal);

        var model = CreateEmptyModel();
        model.Summary = new PlainText { Text = "summary_&" };
        var renderedHeader = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderDocumentHeader(model));
        model.Summary = null;
        var renderedHeaderWithoutSummary = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderDocumentHeader(model));

        Assert.Contains(@"\cvsection{Summary}", renderedHeader, StringComparison.Ordinal);
        Assert.Contains(@"summary\_\&", renderedHeader, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\cvsection{Summary}", renderedHeaderWithoutSummary, StringComparison.Ordinal);

        var list = new ExperienceList
        {
            Title = "List",
            Place = Place.Personal,
            DateRange = DateRange.Completed(new(2024), new(2025)),
            Type = ExperienceType.Project,
            Description = new PlainText { Text = "list_&" },
            Items =
            [
                new ExperienceListItem
                {
                    Text = new StyledText
                    {
                        Text = "item_&",
                        Style = StyleFlags.Bold,
                    },
                },
            ],
        };
        var renderedChrome = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderExperienceChrome(list));
        var renderedItem = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderExperienceItem(list.Items[0]));

        Assert.Contains(@"list\_\&", renderedChrome, StringComparison.Ordinal);
        Assert.Equal(@"\textbf{item\_\&}", renderedItem);
    }

    [Fact]
    public void Renderer_EscapesAllFormatNeutralStructuralData()
    {
        var model = CreateEmptyModel();
        model.Name = new("First#", "Last%");
        model.Profession = new("R&D_{Lead}");
        model.CategorizedInfos =
        [
            new(new Category("Label#"), @"value\%"),
            new(new Category("URL&", IsUrl: true), "https://example.test/a_b?x=1&y=2"),
        ];
        model.CategorizedInfoLists =
        [
            new(new Category("Skills_"), ["C#", "R&D"]),
        ];
        model.Website = "https://site.test/a_b";
        model.GitHub = @"https://github.test/a\b";

        var header = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderDocumentHeader(model));
        var footer = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderDocumentFooter(model));

        Assert.Contains(@"Last\% First\#", header, StringComparison.Ordinal);
        Assert.Contains(@"R\&D\_\{Lead\}", header, StringComparison.Ordinal);
        Assert.Contains(@"\textbf{Label\#:} value\textbackslash{}\%", header, StringComparison.Ordinal);
        Assert.Contains(@"\textbf{URL\&:} \url{https://example.test/a\_b?x=1\&y=2}", header, StringComparison.Ordinal);
        Assert.Contains(@"\textbf{Skills\_:} C\#, R\&D", header, StringComparison.Ordinal);
        Assert.Contains(@"\url{ https://site.test/a\_b }", footer, StringComparison.Ordinal);
        Assert.Contains(@"\url{ https://github.test/a\textbackslash{}b }", footer, StringComparison.Ordinal);

        var languages = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderLanguagesSectionInner(
            [
                new(
                    new("Lang#", "L%"),
                    new("C&"),
                    [new("read_write")]),
            ]));
        Assert.Contains(@"Lang\# & C\& & read\_write", languages, StringComparison.Ordinal);

        var @event = new Event
        {
            Title = "Title#%&_{",
            Place = new("Place\\{}"),
            DateRange = DateRange.Completed(new(2024), new(2025)),
            Text = new PlainText { Text = "summary#%&_\\" },
            SubItems = [new(0, new PlainText { Text = "bullet#%&_\\" })],
            Urls = ["https://event.test/a_b?x=1&y=2"],
        };
        var renderedEvent = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderEvent(@event));

        Assert.Contains(@"Title\#\%\&\_\{", renderedEvent, StringComparison.Ordinal);
        Assert.Contains(@"Place\textbackslash{}\{\}", renderedEvent, StringComparison.Ordinal);
        Assert.Contains(@"\url{https://event.test/a\_b?x=1\&y=2}", renderedEvent, StringComparison.Ordinal);
        Assert.Contains(@"summary\#\%\&\_\textbackslash{}", renderedEvent, StringComparison.Ordinal);
        Assert.Contains(@"bullet\#\%\&\_\textbackslash{}", renderedEvent, StringComparison.Ordinal);

        var experience = new ExperienceList
        {
            Title = "Experience&Title",
            Place = new("Experience_Place"),
            DateRange = DateRange.Completed(new(2024), new(2025)),
            Type = ExperienceType.Project,
            Description = new PlainText { Text = "description{rich}" },
            Urls = ["https://experience.test/a#b"],
            Items = [],
        };
        var renderedExperience = CvLatexFragmentRenderer.Materialize(
            CvLatexFragmentRenderer.RenderExperienceChrome(experience));

        Assert.Contains(@"Experience\&Title", renderedExperience, StringComparison.Ordinal);
        Assert.Contains(@"Experience\_Place", renderedExperience, StringComparison.Ordinal);
        Assert.Contains(@"\url{https://experience.test/a\#b}", renderedExperience, StringComparison.Ordinal);
        Assert.Contains(@"description\{rich\}", renderedExperience, StringComparison.Ordinal);
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
        var coldProgress = new ProgressTestReporter();
        var warmProgress = new ProgressTestReporter();

        var cold = await service.MeasureAsync(
            database,
            model,
            fixture.TemplatePath,
            coldProgress,
            CancellationToken.None);
        var warm = await service.MeasureAsync(
            database,
            model,
            fixture.TemplatePath,
            warmProgress,
            CancellationToken.None);

        Assert.Equal(1, runner.CallCount);
        Assert.Single(runner.Batches[0].Where(static request => request.CacheKey.Kind == LatexMeasurementKind.ExperienceItem));
        Assert.Single(runner.Batches[0].Where(static request => request.CacheKey.Kind == LatexMeasurementKind.ExperienceHeading));
        Assert.DoesNotContain(
            runner.Batches[0],
            static request => request.CacheKey.Kind == LatexMeasurementKind.DocumentFooter);
        Assert.Equal(2, cold.ExperienceItems.Count);
        Assert.Single(cold.ExperienceHeadings);
        Assert.Equal(LatexHeight.Zero, cold.DocumentFooter);
        Assert.Equal(
            cold.GetExperienceItemHeight(new ExperienceItemId(new ExperienceListId(0), 0)),
            cold.GetExperienceItemHeight(new ExperienceItemId(new ExperienceListId(0), 1)));
        Assert.Equal(cold.DocumentHeader, warm.DocumentHeader);
        Assert.Equal(cold.DocumentFooter, warm.DocumentFooter);
        Assert.Equal(cold.ExperienceItems, warm.ExperienceItems);
        var expectedWorkUnits = service.GetWorkUnitCount(database, model);
        Assert.Equal(
            new ProgressReport(
                expectedWorkUnits,
                expectedWorkUnits,
                "Computing heights"),
            coldProgress.Last);
        Assert.Equal(
            coldProgress.Last.CompletedWorkUnits,
            warmProgress.Last.CompletedWorkUnits);
        Assert.Equal(
            expectedWorkUnits - 1,
            warmProgress.Reports.Count(static report =>
                report.Detail == "Computing heights — cached measurement"));
        Assert.Equal(cold.ExperienceHeadings, warm.ExperienceHeadings);
        Assert.Equal(cold.ExperienceChrome, warm.ExperienceChrome);
        Assert.Equal(cold.CurrentPageCompleteSections, warm.CurrentPageCompleteSections);
        Assert.Equal(cold.DocumentHeader, warm.DocumentHeader);
        Assert.Equal(cold.DocumentFooter, warm.DocumentFooter);
        Assert.Equal(cold.CurrentPageSectionChrome, warm.CurrentPageSectionChrome);
        Assert.Equal(cold.FreshPageSectionChrome, warm.FreshPageSectionChrome);
    }

    [Fact]
    public async Task Service_AllowsOmittedSectionsAndKeepsSpecializedMeasurementsSparse()
    {
        using var fixture = new CacheFixture();
        var database = CreateDatabase(CreateRichText(new PlainText { Text = "item" }));
        var model = CreateEmptyModel();
        model.SectionOrder = [Section.WorkExperience];
        var runner = new RecordingRunner();
        var service = new LatexMeasurementService(fixture.CachePath, runner, ruleVersion: 18);

        var snapshot = await service.MeasureAsync(
            database,
            model,
            fixture.TemplatePath,
            NoOpProgressReporter.Instance,
            CancellationToken.None);

        Assert.Equal(
            Section.WorkExperience,
            Assert.Single(snapshot.CurrentPageCompleteSections).Key);
        Assert.Equal(
            Section.WorkExperience,
            Assert.Single(snapshot.CurrentPageSectionChrome).Key);
        Assert.Equal(
            Section.WorkExperience,
            Assert.Single(snapshot.FreshPageSectionChrome).Key);
        Assert.Equal(
            Section.WorkExperience,
            Assert.Single(snapshot.CurrentPageSplitSectionStart).Key);
        Assert.Equal(
            Section.WorkExperience,
            Assert.Single(snapshot.FreshPageSplitSectionStart).Key);
        Assert.Empty(snapshot.CurrentPageExplicitStaticSections);
        Assert.Empty(snapshot.FreshPageExplicitStaticSections);
        Assert.DoesNotContain(
            Assert.Single(runner.Batches),
            static request =>
                request.CacheKey.Kind
                    is LatexMeasurementKind.ExplicitStaticSection
                    or LatexMeasurementKind.FreshPageExplicitStaticSection);
    }

    [Fact]
    public async Task ChangedRuleVersionPurgesAndRecomputesAllRequiredKeys()
    {
        using var fixture = new CacheFixture();
        var database = CreateDatabase(CreateRichText(new PlainText { Text = "item" }));
        var model = CreateEmptyModel();
        var firstRunner = new RecordingRunner();
        await new LatexMeasurementService(fixture.CachePath, firstRunner, 1)
            .MeasureAsync(database, model, fixture.TemplatePath, NoOpProgressReporter.Instance, CancellationToken.None);
        var secondRunner = new RecordingRunner();

        await new LatexMeasurementService(fixture.CachePath, secondRunner, 2)
            .MeasureAsync(database, model, fixture.TemplatePath, NoOpProgressReporter.Instance, CancellationToken.None);

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
            .MeasureAsync(firstDatabase, model, fixture.TemplatePath, NoOpProgressReporter.Instance, CancellationToken.None);
        var expandedDatabase = CreateDatabase(
            CreateRichText(new PlainText { Text = "first" }),
            CreateRichText(new PlainText { Text = "new" }));
        var partialRunner = new RecordingRunner();

        await new LatexMeasurementService(fixture.CachePath, partialRunner, 1)
            .MeasureAsync(expandedDatabase, model, fixture.TemplatePath, NoOpProgressReporter.Instance, CancellationToken.None);

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
        var failedProgress = new ProgressTestReporter();
        await Assert.ThrowsAsync<CvMeasurementException>(() =>
            new LatexMeasurementService(fixture.CachePath, failing, 1)
                .MeasureAsync(
                    database,
                    model,
                    fixture.TemplatePath,
                    failedProgress,
                    CancellationToken.None));
        var retry = new RecordingRunner();

        await new LatexMeasurementService(fixture.CachePath, retry, 1)
            .MeasureAsync(database, model, fixture.TemplatePath, NoOpProgressReporter.Instance, CancellationToken.None);

        Assert.Equal(failing.RequestCount, Assert.Single(retry.Batches).Count);
        Assert.True(
            failedProgress.Last.CompletedWorkUnits
            < failedProgress.Last.TotalWorkUnits);
    }

    [Fact]
    public async Task CancellationAfterCompilation_DoesNotCommitAnyMissRows()
    {
        using var fixture = new CacheFixture();
        var database = CreateDatabase(CreateRichText(new PlainText { Text = "item" }));
        var model = CreateEmptyModel();
        using var cancellation = new CancellationTokenSource();
        var cancellingRunner = new CancellingRunner(cancellation);
        var cancelledProgress = new ProgressTestReporter();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LatexMeasurementService(fixture.CachePath, cancellingRunner, 1)
                .MeasureAsync(
                    database,
                    model,
                    fixture.TemplatePath,
                    cancelledProgress,
                    cancellation.Token));
        var retry = new RecordingRunner();

        await new LatexMeasurementService(fixture.CachePath, retry, 1)
            .MeasureAsync(database, model, fixture.TemplatePath, NoOpProgressReporter.Instance, CancellationToken.None);

        Assert.Equal(cancellingRunner.RequestCount, Assert.Single(retry.Batches).Count);
        Assert.True(
            cancelledProgress.Last.CompletedWorkUnits
            < cancelledProgress.Last.TotalWorkUnits);
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

        var batch = await runner.MeasureAsync(templatePath, requests, NoOpProgressReporter.Instance, CancellationToken.None);
        var reversed = await runner.MeasureAsync(templatePath, requests.Reverse().ToArray(), NoOpProgressReporter.Instance, CancellationToken.None);
        var alone = await runner.MeasureAsync(templatePath, [requests[0]], NoOpProgressReporter.Instance, CancellationToken.None);

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
                new(0, firstText),
                new(0, secondText),
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
            SubItems = [new(0, firstText)],
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
            "Work Experience");
        FormattableString completeDocument = $"{CvLatexFragmentRenderer.RenderDocumentHeader(documentModel)}{CvLatexFragmentRenderer.RenderProductionSection(Section.WorkExperience, completeWorkSection)}{CvLatexFragmentRenderer.RenderDocumentFooter(documentModel)}";
        var completeProjectSection = CvLatexFragmentRenderer.RenderEventsSectionInner(
            [@event],
            "Personal Projects");
        FormattableString twoSectionDocument = $"{CvLatexFragmentRenderer.RenderDocumentHeader(documentModel)}{CvLatexFragmentRenderer.RenderProductionSection(Section.WorkExperience, completeWorkSection)}{CvLatexFragmentRenderer.RenderProductionSection(Section.PersonalProjects, completeProjectSection)}{CvLatexFragmentRenderer.RenderDocumentFooter(documentModel)}";
        FormattableString explicitCurrentUnit =
            $"{FormattableStringFactory.Create(@"\cvflowblockfitskip")}{CvLatexFragmentRenderer.RenderSectionChrome(Section.WorkExperience)}{CvLatexFragmentRenderer.RenderEvent(@event)}{FormattableStringFactory.Create(@"\cvexplicitsectionend")}";
        FormattableString explicitFreshUnit =
            $"{FormattableStringFactory.Create(@"\cvflowblocknewpageskip\cvflowblockfitskip")}{CvLatexFragmentRenderer.RenderSectionChrome(Section.WorkExperience)}{CvLatexFragmentRenderer.RenderEvent(@event)}{FormattableStringFactory.Create(@"\cvexplicitsectionend")}";
        FormattableString explicitFreshContinuationUnit =
            $"{FormattableStringFactory.Create(@"\cvflowblocknewpageskip")}{CvLatexFragmentRenderer.RenderEvent(@event)}{FormattableStringFactory.Create(@"\cvexplicitsectionend")}";
        var languagesInner = CvLatexFragmentRenderer.RenderLanguagesSectionInner(
            [new(Language.English, LanguageProficiencyLevel.C2)]);
        FormattableString explicitCurrentStaticUnit =
            $"{FormattableStringFactory.Create(@"\cvflowblockfitskip")}{languagesInner}{FormattableStringFactory.Create(@"\cvexplicitsectionend")}";
        FormattableString explicitFreshStaticUnit =
            $"{FormattableStringFactory.Create(@"\cvflowblocknewpageskip\cvflowblockfitskip")}{languagesInner}{FormattableStringFactory.Create(@"\cvexplicitsectionend")}";
        var requests = new[]
        {
            Request(1, LatexMeasurementKind.ExperienceChrome, CvLatexFragmentRenderer.RenderExperienceChrome(list), LatexMeasurementMode.ExperienceChromeWithoutPermanentItems),
            Request(2, LatexMeasurementKind.ExperienceItem, CvLatexFragmentRenderer.RenderExperienceItem(list.Items[0]), LatexMeasurementMode.ExperienceItemMarginal),
            Request(3, LatexMeasurementKind.ExperienceItem, CvLatexFragmentRenderer.RenderExperienceItem(list.Items[1]), LatexMeasurementMode.ExperienceItemMarginal),
            Request(4, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEvent(@event), LatexMeasurementMode.Box),
            Request(5, LatexMeasurementKind.SectionChrome, CvLatexFragmentRenderer.RenderSectionChrome(Section.WorkExperience), LatexMeasurementMode.SectionChrome),
            Request(6, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEventsSectionInner([@event], "Work Experience"), LatexMeasurementMode.FlowBlock),
            Request(7, LatexMeasurementKind.UsablePageHeight, @"\rule{0pt}{\textheight}", LatexMeasurementMode.Box),
            Request(8, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEventsSectionInner([@event, @event], "Work Experience"), LatexMeasurementMode.FlowBlock),
            Request(9, LatexMeasurementKind.ExperienceChrome, CvLatexFragmentRenderer.RenderExperienceChrome(linkedList), LatexMeasurementMode.Box),
            Request(10, LatexMeasurementKind.ExperienceItem, CvLatexFragmentRenderer.RenderExperienceItem(linkedList.Items[0]), LatexMeasurementMode.ExperienceItemMarginal),
            Request(11, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEvent(linkedEvent), LatexMeasurementMode.Box),
            Request(12, LatexMeasurementKind.DocumentHeader, CvLatexFragmentRenderer.RenderDocumentHeader(documentModel), LatexMeasurementMode.DocumentHeader),
            Request(14, LatexMeasurementKind.CompleteSection, completeDocument, LatexMeasurementMode.PageStart),
            Request(15, LatexMeasurementKind.CompleteSection, completeProjectSection, LatexMeasurementMode.FlowBlock),
            Request(16, LatexMeasurementKind.CompleteSection, twoSectionDocument, LatexMeasurementMode.PageStart),
            Request(17, LatexMeasurementKind.ExperienceHeading, CvLatexFragmentRenderer.RenderExperienceHeading(list), LatexMeasurementMode.Box),
            Request(18, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEvent(headingOnlyEvent), LatexMeasurementMode.Box),
            Request(19, LatexMeasurementKind.FreshPageSectionChrome, CvLatexFragmentRenderer.RenderSectionChrome(Section.WorkExperience), LatexMeasurementMode.FreshPageSectionChrome),
            Request(20, LatexMeasurementKind.CompleteSection, CvLatexFragmentRenderer.RenderEventsSectionInner([@event], "Work Experience"), LatexMeasurementMode.FreshPageFlowBlock),
            Request(21, LatexMeasurementKind.SplitSectionStart, CvLatexFragmentRenderer.RenderSectionChrome(Section.WorkExperience), LatexMeasurementMode.SplitSectionStart),
            Request(22, LatexMeasurementKind.FreshPageSplitSectionStart, CvLatexFragmentRenderer.RenderSectionChrome(Section.WorkExperience), LatexMeasurementMode.FreshPageSplitSectionStart),
            Request(23, LatexMeasurementKind.SplitSectionEnd, FormattableStringFactory.Create(string.Empty), LatexMeasurementMode.SplitSectionEnd),
            Request(24, LatexMeasurementKind.FreshPageContinuation, FormattableStringFactory.Create(string.Empty), LatexMeasurementMode.FreshPageContinuation),
            Request(25, LatexMeasurementKind.CompleteSection, explicitCurrentUnit, LatexMeasurementMode.Box),
            Request(26, LatexMeasurementKind.CompleteSection, explicitFreshUnit, LatexMeasurementMode.Box),
            Request(27, LatexMeasurementKind.CompleteSection, explicitFreshContinuationUnit, LatexMeasurementMode.Box),
            Request(28, LatexMeasurementKind.ExplicitStaticSection, explicitCurrentStaticUnit, LatexMeasurementMode.Box),
            Request(29, LatexMeasurementKind.FreshPageExplicitStaticSection, explicitFreshStaticUnit, LatexMeasurementMode.Box),
            Request(30, LatexMeasurementKind.CompleteSection, explicitCurrentStaticUnit, LatexMeasurementMode.Box),
            Request(31, LatexMeasurementKind.CompleteSection, explicitFreshStaticUnit, LatexMeasurementMode.Box),
        };
        var runner = new XeLatexMeasurementRunner();

        var measured = await runner.MeasureAsync(ProductionTemplatePath, requests, NoOpProgressReporter.Instance, CancellationToken.None);

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
        Assert.Equal(measured[new(1)], measured[new(17)]);
        Assert.Equal(
            measured[new(20)].ScaledPoints,
            measured[new(19)].ScaledPoints + measured[new(4)].ScaledPoints);
        Assert.Equal(
            measured[new(20)].ScaledPoints,
            measured[new(6)].ScaledPoints
            - measured[new(5)].ScaledPoints
            + measured[new(19)].ScaledPoints);
        Assert.True(measured[new(19)].ScaledPoints >= measured[new(5)].ScaledPoints);
        Assert.Equal(
            measured[new(25)].ScaledPoints,
            measured[new(21)].ScaledPoints
            + measured[new(4)].ScaledPoints
            + measured[new(23)].ScaledPoints);
        Assert.Equal(
            measured[new(26)].ScaledPoints,
            measured[new(22)].ScaledPoints
            + measured[new(4)].ScaledPoints
            + measured[new(23)].ScaledPoints);
        Assert.True(measured[new(24)].ScaledPoints > 0);
        Assert.Equal(
            measured[new(27)].ScaledPoints,
            measured[new(24)].ScaledPoints
            + measured[new(4)].ScaledPoints
            + measured[new(23)].ScaledPoints);
        Assert.Equal(measured[new(30)], measured[new(28)]);
        Assert.Equal(measured[new(31)], measured[new(29)]);
        Assert.Equal(
            measured[new(14)].ScaledPoints,
            measured[new(12)].ScaledPoints
            + measured[new(6)].ScaledPoints);
        Assert.Equal(
            measured[new(16)].ScaledPoints,
            measured[new(12)].ScaledPoints
            + measured[new(6)].ScaledPoints
            + measured[new(15)].ScaledPoints);
        Assert.True(measured[new(7)].ScaledPoints > measured[new(6)].ScaledPoints);
    }

    [Fact]
    public async Task ProductionRendersControlledAtomicSectionsOnExactlyTwoPages()
    {
        var shortDirectory = Path.Combine(Path.GetTempPath(), $"fjh-short-pages-{Guid.NewGuid():N}");
        var twoPageDirectory = Path.Combine(Path.GetTempPath(), $"fjh-two-pages-{Guid.NewGuid():N}");
        try
        {
            var shortModel = CreateEmptyModel();
            shortModel.SectionOrder = [];
            var shortTexProgress = new ProgressTestReporter();
            var shortPdfProgress = new ProgressTestReporter();
            await CvTemplate.Generate(new()
            {
                ConfigFilePath = ProductionTemplatePath,
                OutputDirectory = shortDirectory,
                Model = shortModel,
                CancellationToken = CancellationToken.None,
                PageCount = CvPageCount.OnePage,
            }, new(shortTexProgress, shortPdfProgress));

            var twoPageModel = CreateEmptyModel();
            twoPageModel.SectionOrder =
            [
                Section.WorkExperience,
                Section.PersonalProjects,
            ];
            twoPageModel.WorkExperiences = CreateRenderedEvents("Work", 12);
            twoPageModel.PersonalProjects = CreateRenderedEvents("Project", 12);
            var twoPageTexProgress = new ProgressTestReporter();
            var twoPagePdfProgress = new ProgressTestReporter();
            await CvTemplate.Generate(new()
            {
                ConfigFilePath = ProductionTemplatePath,
                OutputDirectory = twoPageDirectory,
                Model = twoPageModel,
                CancellationToken = CancellationToken.None,
                PageCount = CvPageCount.Exact(2),
            }, new(twoPageTexProgress, twoPagePdfProgress));

            Assert.Equal(1, ReadPageCount(shortDirectory));
            Assert.Equal(2, ReadPageCount(twoPageDirectory));
            AssertExpectedPdfProgress(
                shortPdfProgress,
                expectedBulletCount: 0);
            AssertExpectedPdfProgress(
                twoPagePdfProgress,
                expectedBulletCount: 24);
            AssertExpectedLatexmkPasses(shortDirectory);
            AssertExpectedLatexmkPasses(twoPageDirectory);
            Assert.Equal(
                shortTexProgress.Last.TotalWorkUnits,
                shortTexProgress.Last.CompletedWorkUnits);
            Assert.Equal(
                twoPageTexProgress.Last.TotalWorkUnits,
                twoPageTexProgress.Last.CompletedWorkUnits);
            Assert.Empty(await ReadPdfFooters(shortDirectory));

            var twoPageFooters = await ReadPdfFooters(twoPageDirectory);
            AssertFooterLayout(
                twoPageFooters,
                expectedPageCount: 2);
        }
        finally
        {
            if (Directory.Exists(shortDirectory))
            {
                Directory.Delete(shortDirectory, recursive: true);
            }
            if (Directory.Exists(twoPageDirectory))
            {
                Directory.Delete(twoPageDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExplicitProductionLayoutRendersControlledFourPagePdfWithMarkers()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fjh-explicit-four-pages-{Guid.NewGuid():N}");
        try
        {
            var model = CreateEmptyModel();
            model.Languages =
            [
                new(Language.English, LanguageProficiencyLevel.C2),
            ];
            model.Educations = CreateRenderedEvents("Education", 1);
            model.WorkExperiences = CreateRenderedEvents("Work", 20);
            model.PersonalProjects = CreateRenderedEvents("Project", 2);
            model.GitHub = "https://github.test/example";
            var layout = new CvPageLayout([
                new(1, 1, [Section.Languages, Section.Education]),
                new(2, 3, [Section.WorkExperience]),
                new(4, 4, [Section.PersonalProjects]),
            ]);
            model.SectionOrder = layout.SectionOrder;
            var texProgress = new ProgressTestReporter();
            var pdfProgress = new ProgressTestReporter();

            await CvTemplate.Generate(new()
            {
                ConfigFilePath = ProductionTemplatePath,
                OutputDirectory = outputDirectory,
                Model = model,
                CancellationToken = CancellationToken.None,
                PageCount = CvPageCount.Exact(layout.PageCount),
                PageLayout = layout,
            }, new(texProgress, pdfProgress));

            Assert.Equal(4, ReadPageCount(outputDirectory));
            AssertExpectedPdfProgress(
                pdfProgress,
                expectedBulletCount: 23);
            AssertExpectedLatexmkPasses(outputDirectory);
            Assert.Equal(
                CvTemplate.GetTexWorkUnitCount(model, layout),
                texProgress.Last.CompletedWorkUnits);
            AssertFooterLayout(
                await ReadPdfFooters(outputDirectory),
                expectedPageCount: 4);
            var log = File.ReadAllText(Path.Combine(outputDirectory, "main.log"));
            var markers = LatexExplicitLayoutMarkerParser.Parse(log);
            Assert.Equal(
                new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 4 },
                markers.BlockStartPages);
            Assert.Equal(
                new Dictionary<int, int> { [1] = 1, [2] = 3, [3] = 4 },
                markers.BlockEndPages);
            Assert.Equal(4, markers.FooterPage);

            var extractedTextPath = Path.Combine(outputDirectory, "main.txt");
            await Cli.Wrap("pdftotext")
                .WithArguments([
                    Path.Combine(outputDirectory, "main.pdf"),
                    extractedTextPath,
                ])
                .ExecuteAsync();
            var extractedText = File.ReadAllText(extractedTextPath);
            Assert.Equal(1, CountOccurrences(extractedText, "Work Experience"));
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task OversizedSingleSectionRaisesNamedOverflowInsteadOfSplitting()
    {
        var outputDirectory = Path.Combine(Path.GetTempPath(), $"fjh-section-overflow-{Guid.NewGuid():N}");
        try
        {
            var model = CreateEmptyModel();
            model.SectionOrder = [Section.WorkExperience];
            model.WorkExperiences = CreateRenderedEvents("Oversized", 24);
            var texProgress = new ProgressTestReporter();
            var pdfProgress = new ProgressTestReporter();

            var exception = await Assert.ThrowsAsync<CvSectionPageOverflowException>(() =>
                CvTemplate.Generate(new()
                {
                    ConfigFilePath = ProductionTemplatePath,
                    OutputDirectory = outputDirectory,
                    Model = model,
                    CancellationToken = CancellationToken.None,
                }, new(texProgress, pdfProgress)));

            Assert.Contains("WorkExperience", exception.Message, StringComparison.Ordinal);
            Assert.Contains("single page", exception.Message, StringComparison.Ordinal);
            Assert.Equal(
                texProgress.Last.TotalWorkUnits,
                texProgress.Last.CompletedWorkUnits);
            Assert.True(
                pdfProgress.Last.CompletedWorkUnits
                < pdfProgress.Last.TotalWorkUnits);
            var log = File.ReadAllText(Path.Combine(outputDirectory, "main.log"));
            Assert.Contains(CvLatexErrors.SectionPageOverflowMarker, log, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExplicitOversizedEventReportsItsSectionAndTitle()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(),
            $"fjh-event-overflow-{Guid.NewGuid():N}");
        try
        {
            var model = CreateEmptyModel();
            model.SectionOrder = [Section.WorkExperience];
            model.WorkExperiences =
            [
                new Event
                {
                    Title = "Indivisible oversized job",
                    Place = Place.Personal,
                    DateRange = DateRange.Completed(new(2020), new(2021)),
                    SubItems = Enumerable.Range(1, 100)
                        .Select(position => new SubEvent(
                            0,
                            new PlainText
                            {
                                Text = $"A complete bullet {position} that belongs to the same atomic event.",
                            }))
                        .ToImmutableArray(),
                },
            ];
            var layout = new CvPageLayout([
                new(1, 1, [Section.WorkExperience]),
            ]);

            var exception = await Assert.ThrowsAsync<CvEventPageOverflowException>(() =>
                CvTemplate.Generate(new()
                {
                    ConfigFilePath = ProductionTemplatePath,
                    OutputDirectory = outputDirectory,
                    Model = model,
                    CancellationToken = CancellationToken.None,
                    PageCount = CvPageCount.OnePage,
                    PageLayout = layout,
                }, new(NoOpProgressReporter.Instance, NoOpProgressReporter.Instance)));

            Assert.Equal("WorkExperience", exception.SectionLabel);
            Assert.Equal("Indivisible oversized job", exception.EventLabel);
            Assert.Contains(
                "Indivisible oversized job",
                exception.Message,
                StringComparison.Ordinal);
            var log = File.ReadAllText(Path.Combine(outputDirectory, "main.log"));
            Assert.Contains(
                CvLatexErrors.EventPageOverflowMarker,
                log,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(outputDirectory))
            {
                Directory.Delete(outputDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExactRenderedPageCountRejectsBothTooFewAndTooManyPages()
    {
        var tooFewDirectory = Path.Combine(Path.GetTempPath(), $"fjh-too-few-pages-{Guid.NewGuid():N}");
        var tooManyDirectory = Path.Combine(Path.GetTempPath(), $"fjh-too-many-pages-{Guid.NewGuid():N}");
        try
        {
            var onePageModel = CreateEmptyModel();
            onePageModel.SectionOrder = [];
            var tooFew = await Assert.ThrowsAsync<RenderedPageCountMismatchException>(() =>
                CvTemplate.Generate(new()
                {
                    ConfigFilePath = ProductionTemplatePath,
                    OutputDirectory = tooFewDirectory,
                    Model = onePageModel,
                    CancellationToken = CancellationToken.None,
                    PageCount = CvPageCount.Exact(2),
                }, new(NoOpProgressReporter.Instance, NoOpProgressReporter.Instance)));
            Assert.Equal(
                "Configured pageCount 2, but the rendered PDF contains 1 pages",
                tooFew.Message);

            var twoPageModel = CreateEmptyModel();
            twoPageModel.SectionOrder =
            [
                Section.WorkExperience,
                Section.PersonalProjects,
            ];
            twoPageModel.WorkExperiences = CreateRenderedEvents("Work", 12);
            twoPageModel.PersonalProjects = CreateRenderedEvents("Project", 12);
            var tooMany = await Assert.ThrowsAsync<RenderedPageCountMismatchException>(() =>
                CvTemplate.Generate(new()
                {
                    ConfigFilePath = ProductionTemplatePath,
                    OutputDirectory = tooManyDirectory,
                    Model = twoPageModel,
                    CancellationToken = CancellationToken.None,
                    PageCount = CvPageCount.OnePage,
                }, new(NoOpProgressReporter.Instance, NoOpProgressReporter.Instance)));
            Assert.Equal(
                "Configured pageCount 1, but the rendered PDF contains 2 pages",
                tooMany.Message);
        }
        finally
        {
            if (Directory.Exists(tooFewDirectory))
            {
                Directory.Delete(tooFewDirectory, recursive: true);
            }
            if (Directory.Exists(tooManyDirectory))
            {
                Directory.Delete(tooManyDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("Output written on main.pdf (1 page, 123 bytes).", 1)]
    [InlineData("Output written on main.xdv (37 pages, 123 bytes).", 37)]
    public void LatexPageCountParserReadsProductionLogLine(string log, int expected)
    {
        Assert.True(LatexLogPageCountParser.TryParse(log, out var actual));
        Assert.Equal(expected, actual);
        Assert.False(LatexLogPageCountParser.TryParse("No pages of output.", out _));
    }

    [Theory]
    [InlineData(
        """
        FJH_LAYOUT_BLOCK_START:1:1
        FJH_LAYOUT_BLOCK_END:1:1
        FJH_LAYOUT_BLOCK_START:2:2
        FJH_LAYOUT_FOOTER:2
        Output written on main.pdf (2 pages, 123 bytes).
        """,
        "end marker for block 2")]
    [InlineData(
        """
        FJH_LAYOUT_BLOCK_START:1:1
        FJH_LAYOUT_BLOCK_END:1:2
        FJH_LAYOUT_BLOCK_START:2:2
        FJH_LAYOUT_BLOCK_END:2:2
        FJH_LAYOUT_FOOTER:2
        Output written on main.pdf (2 pages, 123 bytes).
        """,
        "block 1 ends on physical page 2")]
    [InlineData(
        """
        FJH_LAYOUT_BLOCK_START:1:1
        FJH_LAYOUT_BLOCK_END:1:1
        FJH_LAYOUT_BLOCK_START:2:2
        FJH_LAYOUT_BLOCK_END:2:2
        FJH_LAYOUT_FOOTER:1
        Output written on main.pdf (2 pages, 123 bytes).
        """,
        "footer is on physical page 1")]
    [InlineData(
        """
        FJH_LAYOUT_BLOCK_START:1:1
        FJH_LAYOUT_BLOCK_END:1:1
        FJH_LAYOUT_BLOCK_START:2:2
        FJH_LAYOUT_BLOCK_END:2:2
        FJH_LAYOUT_FOOTER:2
        Output written on main.pdf (3 pages, 123 bytes).
        """,
        "PDF contains 3")]
    public void ExplicitRenderedLayoutRejectsMissingOrMismatchedMarkers(
        string log,
        string expectedMessage)
    {
        var layout = new CvPageLayout([
            new(1, 1, [Section.WorkExperience]),
            new(2, 2, [Section.Education]),
        ]);

        var exception = Assert.Throws<RenderedPageLayoutMismatchException>(() =>
            CvTemplate.VerifyExplicitRenderedLayout(layout, log));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
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

            var exception = await Assert.ThrowsAsync<CvMetadataOverflowException>(() =>
                CvTemplate.Generate(new()
                {
                    ConfigFilePath = ProductionTemplatePath,
                    OutputDirectory = outputDirectory,
                    Model = model,
                    CancellationToken = CancellationToken.None,
                }, new(NoOpProgressReporter.Instance, NoOpProgressReporter.Instance)));

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

    private static void AssertExpectedPdfProgress(
        ProgressTestReporter progress,
        int expectedBulletCount)
    {
        var expectedWorkUnitCount =
            CvTemplate.GetPdfWorkUnitCount(expectedBulletCount);
        Assert.Contains(
            progress.Reports,
            report =>
                report.CompletedWorkUnits == 1
                && report.TotalWorkUnits
                == expectedWorkUnitCount);
        Assert.Contains(
            progress.Reports,
            report =>
                report.CompletedWorkUnits == expectedWorkUnitCount - 1
                && report.TotalWorkUnits
                == expectedWorkUnitCount);
        Assert.Equal(
            expectedWorkUnitCount,
            progress.Last.CompletedWorkUnits);
        Assert.Equal(
            expectedWorkUnitCount,
            progress.Last.TotalWorkUnits);
        if (expectedBulletCount > 0)
        {
            Assert.Contains(
                progress.Reports,
                static report => report.Detail?.Contains(
                    "bullet",
                    StringComparison.OrdinalIgnoreCase) == true);
        }
        Assert.DoesNotContain(
            progress.Reports,
            static report => report.Detail?.Contains(
                "taking longer than expected",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void AssertExpectedLatexmkPasses(string outputDirectory)
    {
        var parser = new LatexmkProgressParser(
            NoOpProgressReporter.Instance);
        foreach (var line in File.ReadLines(
                     Path.Combine(outputDirectory, "log-stdout.txt")))
        {
            parser.ParseLine(line);
        }

        Assert.Equal(
            CvTemplate.ExpectedXeLatexPassCount,
            parser.StartedXeLatexPassCount);
        Assert.Equal(
            CvTemplate.ExpectedPdfConversionPassCount,
            parser.StartedPdfConversionPassCount);
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
        Assert.True(
            LatexLogPageCountParser.TryParse(log, out var pageCount),
            $"LaTeX log did not contain its standard output page-count line. Output lines: {string.Join(" | ", log.Split('\n').Where(static line => line.Contains("Output", StringComparison.OrdinalIgnoreCase)))}");
        return pageCount;
    }

    private static async Task<IReadOnlyList<PdfFooter>> ReadPdfFooters(string outputDirectory)
    {
        var bboxPath = Path.Combine(outputDirectory, "main-bbox.html");
        await Cli.Wrap("pdftotext")
            .WithArguments([
                "-bbox-layout",
                Path.Combine(outputDirectory, "main.pdf"),
                bboxPath,
            ])
            .ExecuteAsync();

        var document = XDocument.Load(bboxPath);
        var footers = new List<PdfFooter>();
        var pageNumber = 0;
        foreach (var page in document.Descendants().Where(static element => element.Name.LocalName == "page"))
        {
            pageNumber++;
            var pageWidth = double.Parse(
                page.Attribute("width")!.Value,
                System.Globalization.CultureInfo.InvariantCulture);
            foreach (var line in page.Descendants().Where(static element => element.Name.LocalName == "line"))
            {
                var words = line.Descendants()
                    .Where(static element => element.Name.LocalName == "word")
                    .ToArray();
                var text = string.Join(" ", words.Select(static word => word.Value));
                if (!Regex.IsMatch(text, @"^Page \d+ of \d+$", RegexOptions.CultureInvariant))
                {
                    continue;
                }

                footers.Add(new PdfFooter(
                    PageNumber: pageNumber,
                    Text: text,
                    PageWidth: pageWidth,
                    XMin: words.Min(static word => ParseCoordinate(word, "xMin")),
                    XMax: words.Max(static word => ParseCoordinate(word, "xMax"))));
            }
        }

        return footers;
    }

    private static double ParseCoordinate(XElement element, string attributeName)
        => double.Parse(
            element.Attribute(attributeName)!.Value,
            System.Globalization.CultureInfo.InvariantCulture);

    private static void AssertFooterLayout(
        IReadOnlyList<PdfFooter> footers,
        int expectedPageCount)
    {
        Assert.Equal(expectedPageCount, footers.Count);
        for (var pageNumber = 1; pageNumber <= expectedPageCount; pageNumber++)
        {
            var footer = Assert.Single(footers, footer => footer.PageNumber == pageNumber);
            Assert.Equal($"Page {pageNumber} of {expectedPageCount}", footer.Text);
            Assert.InRange(
                Math.Abs(((footer.XMin + footer.XMax) / 2) - (footer.PageWidth / 2)),
                low: 0,
                high: 1);
            Assert.InRange(footer.XMin, low: 0, high: footer.PageWidth);
            Assert.InRange(footer.XMax, low: 0, high: footer.PageWidth);
        }
    }

    private sealed record PdfFooter(
        int PageNumber,
        string Text,
        double PageWidth,
        double XMin,
        double XMax);

    private static ImmutableArray<Event> CreateRenderedEvents(string prefix, int count)
        => Enumerable.Range(1, count)
            .Select(position => new Event
            {
                Title = $"{prefix} event {position}",
                Place = Place.Personal,
                DateRange = DateRange.Completed(new(2020), new(2021)),
                SubItems =
                [
                    new(0, new PlainText { Text = "A production bullet used to create a controlled atomic section." }),
                ],
            })
            .ToImmutableArray();

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

    private static ExperienceDatabase CreateDatabase(params IRichTextNode[] items)
        => new() { AllPlaces = [], Experiences = [CreateList(items)] };

    private static ExperienceList CreateList(params IRichTextNode[] items)
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
            IProgressReporter progress,
            CancellationToken cancellationToken)
        {
            _ = templatePath;
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new(0, requests.Count));
            CallCount++;
            Batches.Add(requests.ToArray());
            IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight> result = requests.ToDictionary(
                static request => request.CorrelationId,
                static request => new LatexHeight(10_000 + request.CorrelationId.Value));
            for (var i = 0; i < requests.Count; i++)
            {
                progress.Report(new(i + 1, requests.Count));
            }
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingRunner : ILatexMeasurementRunner
    {
        public int RequestCount { get; private set; }

        public Task<IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight>> MeasureAsync(
            string templatePath,
            IReadOnlyList<LatexMeasurementRequest> requests,
            IProgressReporter progress,
            CancellationToken cancellationToken)
        {
            _ = templatePath;
            _ = cancellationToken;
            progress.Report(new(0, requests.Count));
            RequestCount = requests.Count;
            throw new CvMeasurementException("simulated compilation failure");
        }
    }

    private sealed class CancellingRunner(CancellationTokenSource cancellation) : ILatexMeasurementRunner
    {
        public int RequestCount { get; private set; }

        public Task<IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight>> MeasureAsync(
            string templatePath,
            IReadOnlyList<LatexMeasurementRequest> requests,
            IProgressReporter progress,
            CancellationToken cancellationToken)
        {
            _ = templatePath;
            cancellationToken.ThrowIfCancellationRequested();
            progress.Report(new(0, requests.Count));
            RequestCount = requests.Count;
            IReadOnlyDictionary<MeasurementCorrelationId, LatexHeight> result = requests.ToDictionary(
                static request => request.CorrelationId,
                static request => new LatexHeight(42));
            for (var i = 0; i < requests.Count; i++)
            {
                progress.Report(new(i + 1, requests.Count));
            }
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
