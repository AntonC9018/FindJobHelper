using FindJobHelper.CVGeneration;

namespace FindJobHelper.Core.Tests;

public sealed class LatexmkProgressParserTests
{
    [Fact]
    public void NormalTwoPlusOnePasses_ReportExpectedMilestones()
    {
        var progress = new ProgressTestReporter();
        var parser = new LatexmkProgressParser(progress);

        parser.ParseLine("Run number 1 of rule 'xelatex'");
        parser.ParseLine("Output written on main.xdv (1 page, 123 bytes).");
        parser.ParseLine("Run number 2 of rule 'xelatex'");
        parser.ParseLine("Output written on main.xdv (1 page, 456 bytes).");
        parser.ParseLine("Run number 1 of rule 'xdvipdfmx'");

        Assert.Equal(2, parser.StartedXeLatexPassCount);
        Assert.Equal(1, parser.StartedPdfConversionPassCount);
        Assert.Contains(
            progress.Reports,
            static report =>
                report.CompletedWorkUnits == 1
                && report.TotalWorkUnits == 3);
        Assert.Contains(
            progress.Reports,
            static report =>
                report.CompletedWorkUnits == 2
                && report.TotalWorkUnits == 3);
        Assert.DoesNotContain(
            progress.Reports,
            static report => report.CompletedWorkUnits == 3);

        parser.CompleteConversionAndValidation();

        Assert.Equal(new ProgressReport(3, 3, "Rendering PDF"), progress.Last);
    }

    [Fact]
    public void AdditionalPasses_KeepMilestoneAndUpdateRetainedWarning()
    {
        var progress = new ProgressTestReporter();
        var parser = new LatexmkProgressParser(progress);
        parser.ParseLine("Run number 1 of rule 'xelatex'");
        parser.ParseLine("Output written on main.xdv");
        parser.ParseLine("Run number 2 of rule 'xelatex'");
        parser.ParseLine("Output written on main.xdv");

        parser.ParseLine("Run number 3 of rule 'xelatex'");
        Assert.Equal(2, progress.Last.CompletedWorkUnits);
        Assert.Equal(
            "Rendering PDF — taking longer than expected: " +
            "XeLaTeX pass 3; expected 2",
            progress.Last.Detail);
        parser.ParseLine("Output written on main.xdv");
        Assert.Equal(2, progress.Last.CompletedWorkUnits);

        parser.ParseLine("Run number 2 of rule 'xdvipdfmx'");
        Assert.Equal(2, progress.Last.CompletedWorkUnits);
        Assert.Equal(
            "Rendering PDF — taking longer than expected: " +
            "xdvipdfmx pass 2; expected 1",
            progress.Last.Detail);

        parser.CompleteConversionAndValidation();

        Assert.Equal(3, progress.Last.CompletedWorkUnits);
        Assert.Equal(
            "Rendering PDF — taking longer than expected: " +
            "xdvipdfmx pass 2; expected 1",
            progress.Last.Detail);
    }

    [Fact]
    public void MalformedAndUnrelatedOutput_IsIgnored()
    {
        var progress = new ProgressTestReporter();
        var parser = new LatexmkProgressParser(progress);

        parser.ParseLine("Run number nope of rule 'xelatex'");
        parser.ParseLine("Run number 1 of rule 'pdflatex'");
        parser.ParseLine("Output written on main.pdf");
        parser.ParseLine("Output written on main.xdv");

        Assert.Single(progress.Reports);
        Assert.Equal(0, progress.Last.CompletedWorkUnits);
        Assert.Equal(0, parser.StartedXeLatexPassCount);
        Assert.Equal(0, parser.StartedPdfConversionPassCount);
    }

    [Fact]
    public void CompilationFailure_DoesNotCreditConversionOrValidation()
    {
        var progress = new ProgressTestReporter();
        var parser = new LatexmkProgressParser(progress);
        parser.ParseLine("Run number 1 of rule 'xelatex'");
        parser.ParseLine("Output written on main.xdv");
        parser.ParseLine("Run number 2 of rule 'xelatex'");
        parser.ParseLine("! Undefined control sequence.");

        Assert.Equal(1, progress.Last.CompletedWorkUnits);
        Assert.Equal(3, progress.Last.TotalWorkUnits);
    }
}
