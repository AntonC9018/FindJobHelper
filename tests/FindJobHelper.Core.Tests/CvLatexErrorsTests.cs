using CliWrap;
using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class CvLatexErrorsTests
{
    [Fact]
    public void SectionOverflowPreservesTheCompleteCapturedName()
    {
        var exception = CvLatexErrors.CreateSectionPageOverflowException(
            "! FJH_SECTION_PAGE_OVERFLOW: WorkExperience.");

        Assert.Equal("WorkExperience", exception.SectionLabel);
    }

    [Fact]
    public void SectionOverflowDoesNotCompletePrefixes()
    {
        var exception = CvLatexErrors.CreateSectionPageOverflowException(
            "! FJH_SECTION_PAGE_OVERFLOW: WorkExper");

        Assert.Equal("WorkExper", exception.SectionLabel);
    }

    [Fact]
    public void LatexProcessesDeclareEffectivelyUnboundedOutputLines()
    {
        Assert.Equal("999", LatexProcessEnvironment.MaxPrintLine);

        var command = Cli.Wrap("xelatex").DisableOutputWrapping();
        if (OperatingSystem.IsWindows())
        {
            Assert.DoesNotContain("max_print_line", command.EnvironmentVariables.Keys);
        }
        else
        {
            Assert.Equal("999", command.EnvironmentVariables["max_print_line"]);
        }
    }
}
