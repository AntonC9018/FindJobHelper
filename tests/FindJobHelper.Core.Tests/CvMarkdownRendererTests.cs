using System.Collections.Immutable;
using CodegenCS;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class CvMarkdownRendererTests
{
    [Fact]
    public void SelectionDebugInfo_RawScoreFallsBackToScoreForLegacyDiagnostics()
    {
        var debugInfo = new SelectionDebugInfo
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
        var markdown = Render(CreateModel(), CvMarkdownRenderMode.Annotated);

        AssertMarkdownShape(markdown);
        Assert.Contains(
            "<details>\n<summary>Diagnostics</summary>\n\n```text\nrank:",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            "- <details>\n  <summary>Diagnostics</summary>\n\n  ```text\n  rank:",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains("MMR terms:", markdown, StringComparison.Ordinal);
        Assert.Contains(
            "coverage (unboosted; used for similarity and saturation):\n  C#: 8",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains("rank: -0.056", markdown, StringComparison.Ordinal);
        Assert.Contains("relevance:", markdown, StringComparison.Ordinal);
        Assert.Contains("base: 10", markdown, StringComparison.Ordinal);
        Assert.Contains("direct match: 2", markdown, StringComparison.Ordinal);
        Assert.Contains("recency: 2.5", markdown, StringComparison.Ordinal);
        Assert.Contains("adjusted: 14.5", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("mmr:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("[configured:", markdown, StringComparison.Ordinal);
        Assert.Contains(
            """
            matches:
              Unity:
                base contribution: 6
                direct contribution: 4
                direct match bonus: 2
                final relevance: 8
                best requirement origin (unboosted):
                  C#: 6
                additional requirement origins (unboosted):
                  Playwright: 3 (direct)
              Tooling Development:
                base contribution: 2
                direct contribution: 0
                direct match bonus: 0
                final relevance: 2
                best requirement origins (unboosted):
                  Playwright: 2
                  C#: 2
              Source Generation:
                base contribution: 2
                direct contribution: 0
                direct match bonus: 0
                final relevance: 2
            """.ReplaceLineEndings("\n"),
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            """
              ```
              </details>

              `ASP.NET Core` feature
              with a [**design note**](<https://example.test/details_(one)?x=1&y=2>)
            """.ReplaceLineEndings("\n"),
            markdown,
            StringComparison.Ordinal);

        await Verify(markdown);
    }

    [Fact]
    public async Task Render_WritesCleanCvSnapshotWithLfAndFinalNewline()
    {
        var markdown = Render(CreateModel(), CvMarkdownRenderMode.Clean);

        AssertMarkdownShape(markdown);
        Assert.DoesNotContain("`rank:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("<details>", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("```text", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("raw:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("coverage:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("matches:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("MMR terms:", markdown, StringComparison.Ordinal);

        await Verify(markdown);
    }

    [Fact]
    public void Render_UsesFirstConfiguredAliasEvenWhenLaterAliasHasHigherWeight()
    {
        var markdown = Render(CreateModel(), CvMarkdownRenderMode.Annotated);

        Assert.Contains(
            "coverage (unboosted; used for similarity and saturation):\n  C#: 8",
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "coverage (unboosted; used for similarity and saturation):\n  .NET:",
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("[configured:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_AnnotatedSubItemWithoutMmrBreakdownOmitsOptionalDiagnostics()
    {
        var model = CreateModel();
        var @event = model.WorkExperiences[0];
        @event.DebugInfo = new()
        {
            Score = 2,
            RawScore = 3,
        };
        @event.SubItems =
        [
            new(
                text: new PlainText
                {
                    Text = "First line\nsecond line",
                },
                debugInfo: new()
                {
                    Score = 1.23456f,
                }),
        ];
        @event.Urls = [];
        model.WorkExperiences = [@event];
        model.Educations = [];
        model.Languages = [];
        model.SectionOrder = [Section.WorkExperience];

        var markdown = Render(model, CvMarkdownRenderMode.Annotated);

        AssertMarkdownShape(markdown);
        Assert.Contains(
            """
            ### Backend Developer

            <details>
            <summary>Diagnostics</summary>

            ```text
            rank: 2
            raw: 3
            ```
            </details>
            """.ReplaceLineEndings("\n"),
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(
            """
            - <details>
              <summary>Diagnostics</summary>

              ```text
              rank: 1.235
              raw: 1.235
              ```
              </details>

              First line
              second line
            """.ReplaceLineEndings("\n"),
            markdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain("mmr:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("MMR terms:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("coverage:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("matches:", markdown, StringComparison.Ordinal);
    }

    private static void AssertMarkdownShape(string markdown)
    {
        Assert.DoesNotContain('\r', markdown);
        Assert.EndsWith("\n", markdown, StringComparison.Ordinal);
        Assert.False(markdown.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.DoesNotContain("Personal Projects", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal", markdown, StringComparison.Ordinal);
    }

    private static CvDataModel CreateModel()
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
            BaseRelevance: 10,
            DirectMatchBonus: 2,
            RawRelevance: 12,
            AppliedRecencyBoost: 0.25f,
            RecencyBonus: 2.5f,
            AdjustedPreMmrRelevance: 14.5f,
            NormalizedRelevance: 0.297f,
            MaximumCosineSimilarity: 0.254f,
            Saturation: 1.106f,
            WeightedRelevanceTerm: 0.214f,
            WeightedSimilarityPenalty: 0.071f,
            WeightedSaturationPenalty: 0.199f,
            NormalizedMmrScore: -0.056f);
        var coverage =
            new[]
            {
                new DebugRequirementCoverage(dotnetRequirement, 8),
                new DebugRequirementCoverage(playwrightRequirement, 5),
            }.ToImmutableArray();
        var matches =
            new[]
            {
                new DebugTagMatch(
                    new("Unity"),
                    BaseContribution: 6,
                    DirectContribution: 4,
                    DirectMatchBonus: 2,
                    RelevanceContribution: 8,
                    Origins:
                    [
                        new(dotnetRequirement, 6),
                        new(playwrightRequirement, 3, IsDirect: true),
                    ]),
                UnboostedMatch(
                    new("Tooling Development"),
                    2,
                    [
                        new(playwrightRequirement, 2),
                        new(dotnetRequirement, 2),
                    ]),
                UnboostedMatch(
                    new("Source Generation"),
                    2,
                    []),
            }.ToImmutableArray();
        return new CvDataModel
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
                            UnboostedMatch(
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
                        Score = -0.056f,
                        RawScore = 12,
                        TagScores =
                        [
                            new("Unity", 8),
                            new("Tooling Development", 2),
                            new("Source Generation", 2),
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
                            new()
                            {
                                Score = negativeBreakdown.NormalizedMmrScore,
                                RawScore = negativeBreakdown.RawRelevance,
                                TagScores =
                                [
                                    new("Unity", 8),
                                    new("Tooling Development", 2),
                                    new("Source Generation", 2),
                                ],
                                RequirementCoverage = coverage,
                                TagMatches = matches,
                                MmrScoreBreakdown = negativeBreakdown,
                            }),
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
    }

    private static DebugTagMatch UnboostedMatch(
        Tag targetTag,
        float contribution,
        ImmutableArray<DebugTagMatchOrigin> origins)
    {
        return new(
            targetTag,
            BaseContribution: contribution,
            DirectContribution: 0,
            DirectMatchBonus: 0,
            RelevanceContribution: contribution,
            Origins: origins);
    }

    private static string Render(
        CvDataModel model,
        CvMarkdownRenderMode mode)
    {
        using var writer = new CodegenTextWriter
        {
            NewLine = "\n",
            PreserveNonWhitespaceIndentBehavior =
                CodegenTextWriter.PreserveNonWhitespaceIndentBehaviorType.PreservePosition,
        };
        CvMarkdownRenderer.Render(
            model,
            mode,
            NoOpProgressReporter.Instance,
            writer);
        return writer.ToString();
    }
}
