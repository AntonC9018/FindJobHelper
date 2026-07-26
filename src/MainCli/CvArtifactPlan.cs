using System.Collections.Immutable;

internal enum CvArtifactKind
{
    Pdf,
    CleanMarkdown,
    AnnotatedMarkdown,
}

internal readonly record struct CvPlannedArtifact(
    CvArtifactKind Kind,
    string FileName);

internal sealed record CvArtifactPlan(
    ImmutableArray<CvPlannedArtifact> Artifacts,
    CvArtifactKind OpenTarget)
{
    private static readonly CvPlannedArtifact Pdf =
        new(CvArtifactKind.Pdf, "CurmanchiiAnton.pdf");
    private static readonly CvPlannedArtifact CleanMarkdown =
        new(CvArtifactKind.CleanMarkdown, "CurmanchiiAnton.md");
    private static readonly CvPlannedArtifact AnnotatedMarkdown =
        new(CvArtifactKind.AnnotatedMarkdown, "CurmanchiiAnton-debug.md");

    internal static CvArtifactPlan Create(
        CvOutputFormat outputFormat,
        bool isDebug)
    {
        if (isDebug)
        {
            return new(
                [CleanMarkdown, AnnotatedMarkdown],
                CvArtifactKind.AnnotatedMarkdown);
        }

        return outputFormat switch
        {
            CvOutputFormat.Tex => new(
                [Pdf],
                CvArtifactKind.Pdf),
            CvOutputFormat.Md => new(
                [CleanMarkdown],
                CvArtifactKind.CleanMarkdown),
            _ => throw new ArgumentOutOfRangeException(
                nameof(outputFormat),
                outputFormat,
                "Unsupported CV output format."),
        };
    }
}
