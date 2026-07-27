using CodegenCS;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class CvRendererProgressTests
{
    [Fact]
    public void Markdown_CreditsEmptyConfiguredSectionsAndLeavesDiskWriteUnit()
    {
        var model = CreateEmptyModel();
        model.SectionOrder =
        [
            Section.WorkExperience,
            Section.PersonalProjects,
            Section.Languages,
        ];
        var progress = new ProgressTestReporter();
        using var writer = CreateWriter();

        CvMarkdownRenderer.Render(
            model,
            CvMarkdownRenderMode.Clean,
            progress,
            writer);

        Assert.Equal(8, CvMarkdownRenderer.GetWorkUnitCount(model));
        Assert.Equal(7, progress.Last.CompletedWorkUnits);
        Assert.Equal(8, progress.Last.TotalWorkUnits);
        Assert.Equal(
            3,
            progress.Reports.Count(static report =>
                report.Detail == "Creating Markdown files — section"));

        progress.Report(new(8, 8, "Creating Markdown files"));
        Assert.Equal(
            new ProgressReport(8, 8, "Creating Markdown files"),
            progress.Last);
    }

    [Fact]
    public void DebugMarkdown_TwoFilesSpanOneCombinedProgressTask()
    {
        var model = CreateEmptyModel();
        model.SectionOrder = [Section.WorkExperience];
        var perFileWork = CvMarkdownRenderer.GetWorkUnitCount(model);
        var totalWork = perFileWork * 2;
        var aggregate = new ProgressTestReporter();

        for (var fileIndex = 0; fileIndex < 2; fileIndex++)
        {
            var fileProgress = new ProgressRangeReporter(
                aggregate,
                offset: fileIndex * perFileWork,
                length: perFileWork,
                targetTotal: totalWork);
            using var writer = CreateWriter();
            CvMarkdownRenderer.Render(
                model,
                fileIndex == 0
                    ? CvMarkdownRenderMode.Clean
                    : CvMarkdownRenderMode.Annotated,
                fileProgress,
                writer);
            fileProgress.Report(new(
                perFileWork,
                perFileWork,
                "Creating Markdown files"));
        }

        Assert.Equal(totalWork, aggregate.Last.CompletedWorkUnits);
        Assert.Equal(totalWork, aggregate.Last.TotalWorkUnits);
        Assert.Contains(
            aggregate.Reports,
            report => report.CompletedWorkUnits == perFileWork);
    }

    [Fact]
    public void TexWorkTotal_UsesConfiguredAndExplicitLayoutOccurrences()
    {
        var model = CreateEmptyModel();
        model.SectionOrder =
        [
            Section.Languages,
            Section.WorkExperience,
            Section.PersonalProjects,
        ];
        var explicitLayout = new CvPageLayout([
            new(1, 1, [Section.Languages]),
            new(
                2,
                2,
                [Section.WorkExperience, Section.PersonalProjects]),
        ]);

        Assert.Equal(6, CvTemplate.GetTexWorkUnitCount(model, layout: null));
        Assert.Equal(6, CvTemplate.GetTexWorkUnitCount(model, explicitLayout));
    }

    private static CvDataModel CreateEmptyModel() => new()
    {
        Name = new("First", "Last"),
        Profession = new("Developer"),
        CategorizedInfoLists = [],
        CategorizedInfos = [],
    };

    private static CodegenTextWriter CreateWriter() => new()
    {
        NewLine = "\n",
        PreserveNonWhitespaceIndentBehavior =
            CodegenTextWriter.PreserveNonWhitespaceIndentBehaviorType
                .PreservePosition,
    };
}
