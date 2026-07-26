using System.Collections.Immutable;
using CodegenCS;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class CvMarkdownRendererTests
{
    [Fact]
    public void EventDebugInfo_RawScoreFallsBackToScoreForLegacyDiagnostics()
    {
        var debugInfo = new EventDebugInfo
        {
            Score = 5,
        };

        Assert.Equal(5, debugInfo.RawScore);

        debugInfo.RawScore = 3;

        Assert.Equal(3, debugInfo.RawScore);
    }

    [Fact]
    public async Task Render_WritesAnnotatedCvSnapshotWithLfAndFinalNewline()
    {
        var educationRequirement = new RequiredTagGroup(
            new("Education"),
            [new(new("Education"), 1)],
            maximumWeight: 1);
        var dotnetRequirement = new RequiredTagGroup(
            new(".NET"),
            [
                new(new("C#"), 1.2f),
                new(new(".NET"), 1.5f),
            ],
            maximumWeight: 1.5f);
        var playwrightRequirement = new RequiredTagGroup(
            new("Playwright"),
            [new(new("Playwright"), 1)],
            maximumWeight: 1);
        var negativeBreakdown = new MmrScoreBreakdown(
            SelectionOrdinal: 2,
            RawRelevance: 12.85f,
            RecencyMultiplier: 1,
            AdjustedRelevance: 12.85f,
            NormalizedRelevance: 0.297f,
            MaximumCosineSimilarity: 0.254f,
            Saturation: 1.106f,
            WeightedRelevanceTerm: 0.214f,
            WeightedSimilarityPenalty: 0.071f,
            WeightedSaturationPenalty: 0.199f,
            NormalizedMmrScore: -0.056f,
            RawEquivalentRankScore: -2.34f);
        var coverage =
            new[]
            {
                new DebugRequirementCoverage(dotnetRequirement, 9.38f),
                new DebugRequirementCoverage(playwrightRequirement, 4.23f),
            }.ToImmutableArray();
        var matches =
            new[]
            {
                new DebugTagMatch(
                    new("Unity"),
                    8.62f,
                    [new(dotnetRequirement, 8.62f)]),
                new DebugTagMatch(
                    new("Tooling Development"),
                    4.23f,
                    [new(playwrightRequirement, 4.23f)]),
            }.ToImmutableArray();
        var model = new CvDataModel
        {
            Name = new("Anton", "Curmanschii"),
            Profession = new("Software Developer"),
            CategorizedInfos =
            [
                new(Category.Location, "Example City, Example Country"),
                new(Category.Website, "https://profile.test/about_(me)?view=full"),
            ],
            CategorizedInfoLists =
            [
                new(Category.Skills, [".NET", "SQL"]),
                new(Category.GitHub, ["https://github.test/Anton?tab=repositories"]),
            ],
            Website = "https://anton.test",
            GitHub = "https://github.test/Anton",
            Summary = new RichText
            {
                Items =
                [
                    new PlainText { Text = "Builds " },
                    RichTextFactory.Bold("reliable systems"),
                    new PlainText { Text = " with " },
                    RichTextFactory.Href("https://dot.net", RichTextFactory.Code(".NET")),
                    new PlainText { Text = "." },
                ],
            },
            Languages =
            [
                new(Language.English, LanguageProficiencyLevel.C2, [new("Technical Writing")]),
                new(Language.Russian, LanguageProficiencyLevel.Native),
            ],
            Educations =
            [
                new Event
                {
                    Title = "BSc Computer Science",
                    Place = new("University"),
                    DateRange = DateRange.Completed(new(2018), new(2022)),
                    DebugInfo = new()
                    {
                        Score = 5,
                        RawScore = 5,
                        TagScores = [new("Education", 5)],
                        RequirementCoverage =
                        [
                            new(educationRequirement, 5),
                        ],
                        TagMatches =
                        [
                            new(
                                new("Education"),
                                5,
                                [new(educationRequirement, 5)]),
                        ],
                    },
                    Text = new PlainText { Text = "Completed with distinction." },
                },
            ],
            WorkExperiences =
            [
                new Event
                {
                    Title = "Backend Developer",
                    Place = Place.Personal,
                    DateRange = DateRange.Ongoing(new(2022)),
                    DebugInfo = new()
                    {
                        Score = -2.34f,
                        RawScore = 12.85f,
                        TagScores =
                        [
                            new("Unity", 8.62f),
                            new("Tooling Development", 4.23f),
                        ],
                        RequirementCoverage = coverage,
                        TagMatches = matches,
                    },
                    Text = new RichText
                    {
                        Items =
                        [
                            new PlainText { Text = "Owned " },
                            RichTextFactory.Italic("backend delivery"),
                            new PlainText { Text = "." },
                        ],
                    },
                    SubItems =
                    [
                        new(
                            negativeBreakdown,
                            new RichText
                            {
                                Items =
                                [
                                    RichTextFactory.Code("ASP.NET Core"),
                                    new PlainText { Text = " feature\nwith a " },
                                    RichTextFactory.Href(
                                        "https://example.test/details_(one)?x=1&y=2",
                                        RichTextFactory.Bold("design note")),
                                ],
                            },
                            [
                                new("Unity", 8.62f),
                                new("Tooling Development", 4.23f),
                            ],
                            coverage,
                            matches),
                    ],
                    Urls = ["https://event.test/demo_(one)?x=1&y=2"],
                },
            ],
            PersonalProjects = [],
            SectionOrder =
            [
                Section.Education,
                Section.PersonalProjects,
                Section.Languages,
                Section.WorkExperience,
            ],
        };

        using var writer = new CodegenTextWriter
        {
            NewLine = "\n",
            PreserveNonWhitespaceIndentBehavior =
                CodegenTextWriter.PreserveNonWhitespaceIndentBehaviorType.PreservePosition,
        };
        CvMarkdownRenderer.Render(model, writer);
        var markdown = writer.ToString();

        Assert.DoesNotContain('\r', markdown);
        Assert.EndsWith("\n", markdown, StringComparison.Ordinal);
        Assert.False(markdown.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.DoesNotContain("Personal Projects", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal", markdown, StringComparison.Ordinal);

        await Verify(markdown);
    }
}
