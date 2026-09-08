using FindJobHelper.Configuration;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class PageLayoutCalculatorTests
{
    [Fact]
    public void SectionThatDoesNotFitRemainderMovesIntactToSecondPage()
    {
        var result = Calculate(
            pageHeight: 100,
            header: 60,
            footer: 0,
            new PageLayoutSection(Section.WorkExperience, new(50), new(55)));

        Assert.True(result.Fits);
        Assert.Equal(2, result.PageCount);
        var placement = Assert.Single(result.Placements);
        Assert.Equal(2, placement.PageNumber);
        Assert.True(placement.UsesFreshPageRepresentation);
        Assert.Equal(55, placement.Height.ScaledPoints);
    }

    [Fact]
    public void HeaderIsChargedOnlyOnPageOneAndFooterFollowsFinalSection()
    {
        var result = Calculate(
            pageHeight: 100,
            header: 80,
            footer: 10,
            new PageLayoutSection(Section.WorkExperience, new(30), new(35)),
            new PageLayoutSection(Section.PersonalProjects, new(55), new(60)));

        Assert.True(result.Fits);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(
            new[] { (2, true), (2, false) },
            result.Placements.Select(static placement =>
                (placement.PageNumber, placement.UsesFreshPageRepresentation)));
        Assert.Equal(2, result.FooterPageNumber);
    }

    [Fact]
    public void FooterStartsAnotherPageWhenItDoesNotFitAfterFinalSection()
    {
        var result = Calculate(
            pageHeight: 100,
            header: 20,
            footer: 25,
            new PageLayoutSection(Section.WorkExperience, new(60), new(65)));

        Assert.True(result.Fits);
        Assert.Equal(2, result.PageCount);
        Assert.Equal(2, result.FooterPageNumber);
    }

    [Fact]
    public void OrderedSectionsPackAcrossThreePagesWithoutReordering()
    {
        var result = Calculate(
            pageHeight: 100,
            header: 30,
            footer: 5,
            new PageLayoutSection(Section.WorkExperience, new(65), new(70)),
            new PageLayoutSection(Section.PersonalProjects, new(40), new(45)),
            new PageLayoutSection(Section.Education, new(50), new(55)),
            new PageLayoutSection(Section.Languages, new(40), new(45)));

        Assert.True(result.Fits);
        Assert.Equal(3, result.PageCount);
        Assert.Equal(
            new[]
            {
                Section.WorkExperience,
                Section.PersonalProjects,
                Section.Education,
                Section.Languages,
            },
            result.Placements.Select(static placement => placement.Section));
        Assert.Equal(new[] { 1, 2, 2, 3 }, result.Placements.Select(static x => x.PageNumber));
    }

    [Fact]
    public void FreshPageSectionOverUsableHeightIsRejectedEvenWhenCurrentFormFits()
    {
        var result = Calculate(
            pageHeight: 100,
            header: 0,
            footer: 0,
            new PageLayoutSection(Section.WorkExperience, new(90), new(101)));

        Assert.False(result.Fits);
        Assert.Equal(PageLayoutFailureKind.SectionOverflow, result.Failure!.Kind);
        Assert.Equal(Section.WorkExperience, result.Failure.Section);
    }

    [Fact]
    public void ConfiguredPageCapRejectsLayoutThatNeedsAnotherPage()
    {
        var result = PageLayoutCalculator.Calculate(
            new(100),
            LatexHeight.Zero,
            LatexHeight.Zero,
            [
                new(Section.WorkExperience, new(80), new(85)),
                new(Section.Education, new(30), new(35)),
            ],
            pageCount: CvPageCount.OnePage);

        Assert.False(result.Fits);
        Assert.Equal(PageLayoutFailureKind.PageCountExceeded, result.Failure!.Kind);
    }

    private static PageLayoutResult Calculate(
        long pageHeight,
        long header,
        long footer,
        params PageLayoutSection[] sections)
        => PageLayoutCalculator.Calculate(
            new(pageHeight),
            new(header),
            new(footer),
            sections);
}
