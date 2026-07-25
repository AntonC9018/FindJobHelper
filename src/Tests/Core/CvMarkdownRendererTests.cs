using System.Collections.Immutable;
using System.Text;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class CvMarkdownRendererTests
{
    [Fact]
    public async Task Render_WritesAnnotatedCvSnapshotWithLfAndFinalNewline()
    {
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
                    DebugScore = 5,
                    DebugTagScores = [new("Education", 5)],
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
                    DebugScore = 18.4f,
                    DebugTagScores = [new(".NET", 12), new("SQL", 6.4f)],
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
                            8,
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
                            [new(".NET", 8)]),
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

        await using var memory = new MemoryStream();
        await using (var writer = new StreamWriter(
                         memory,
                         new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                         leaveOpen: true)
                     {
                         NewLine = "\n",
                     })
        {
            CvMarkdownRenderer.Render(model, writer);
            await writer.FlushAsync();
        }

        var markdown = Encoding.UTF8.GetString(memory.ToArray());

        Assert.Equal(
            """
            # Anton Curmanschii

            Software Developer

            **Location:** Example City, Example Country
            **Skills:** \.NET, SQL
            **Website:** <https://profile.test/about_(me)?view=full>
            **GitHub:** <https://github.test/Anton?tab=repositories>

            ## Summary

            Builds **reliable systems** with [`.NET`](<https://dot.net/>)\.

            ## Education

            ### `score: 5 (Education:5)` BSc Computer Science

            *2018 \- 2022 · University*

            Completed with distinction\.

            ## Languages

            - **English:** C2 · Technical Writing
            - **Russian:** Native

            ## Experience

            ### `score: 18.4 (.NET:12, SQL:6.4)` Backend Developer

            *2022 \- current*

            Owned *backend delivery*\.

            - `score: 8 (.NET:8)` `ASP.NET Core` feature
              with a [**design note**](<https://example.test/details_(one)?x=1&y=2>)
            - **Links:** <https://event.test/demo_(one)?x=1&y=2>

            <https://anton.test/> · <https://github.test/Anton>
            """ + "\n",
            markdown);
        Assert.DoesNotContain('\r', markdown);
        Assert.EndsWith("\n", markdown, StringComparison.Ordinal);
        Assert.False(markdown.EndsWith("\n\n", StringComparison.Ordinal));
        Assert.DoesNotContain("Personal Projects", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Personal", markdown, StringComparison.Ordinal);
    }
}
