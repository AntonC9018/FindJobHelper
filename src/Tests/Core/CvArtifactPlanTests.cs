namespace FindJobHelper.Core.Tests;

public sealed class CvArtifactPlanTests
{
    [Fact]
    public void Create_TexPublishesAndOpensPdf()
    {
        var plan = CvArtifactPlan.Create(CvOutputFormat.Tex, isDebug: false);

        AssertPlan(
            plan,
            CvArtifactKind.Pdf,
            new CvPlannedArtifact(CvArtifactKind.Pdf, "CurmanchiiAnton.pdf"));
    }

    [Fact]
    public void Create_MdPublishesAndOpensCleanMarkdown()
    {
        var plan = CvArtifactPlan.Create(CvOutputFormat.Md, isDebug: false);

        AssertPlan(
            plan,
            CvArtifactKind.CleanMarkdown,
            new CvPlannedArtifact(
                CvArtifactKind.CleanMarkdown,
                "CurmanchiiAnton.md"));
    }

    [Theory]
    [InlineData(CvOutputFormat.Tex)]
    [InlineData(CvOutputFormat.Md)]
    public void Create_DebugOverridesFormatAndPrefersAnnotatedMarkdown(
        CvOutputFormat outputFormat)
    {
        var plan = CvArtifactPlan.Create(outputFormat, isDebug: true);

        AssertPlan(
            plan,
            CvArtifactKind.AnnotatedMarkdown,
            new CvPlannedArtifact(
                CvArtifactKind.CleanMarkdown,
                "CurmanchiiAnton.md"),
            new CvPlannedArtifact(
                CvArtifactKind.AnnotatedMarkdown,
                "CurmanchiiAnton-debug.md"));
    }

    private static void AssertPlan(
        CvArtifactPlan plan,
        CvArtifactKind expectedOpenTarget,
        params CvPlannedArtifact[] expectedArtifacts)
    {
        Assert.Equal(expectedArtifacts, plan.Artifacts.ToArray());
        Assert.Equal(expectedOpenTarget, plan.OpenTarget);
    }
}
