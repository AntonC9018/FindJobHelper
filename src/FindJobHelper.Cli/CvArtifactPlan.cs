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
    internal static CvArtifactPlan Create(
        CvOutputFormat outputFormat,
        bool isDebug,
        string baseFileName = "ExampleAlex")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseFileName);
        var pdf = new CvPlannedArtifact(CvArtifactKind.Pdf, $"{baseFileName}.pdf");
        var cleanMarkdown = new CvPlannedArtifact(
            CvArtifactKind.CleanMarkdown,
            $"{baseFileName}.md");
        var annotatedMarkdown = new CvPlannedArtifact(
            CvArtifactKind.AnnotatedMarkdown,
            $"{baseFileName}-debug.md");
        if (isDebug)
        {
            return new(
                [cleanMarkdown, annotatedMarkdown],
                CvArtifactKind.AnnotatedMarkdown);
        }

        return outputFormat switch
        {
            CvOutputFormat.Tex => new(
                [pdf],
                CvArtifactKind.Pdf),
            CvOutputFormat.Md => new(
                [cleanMarkdown],
                CvArtifactKind.CleanMarkdown),
            _ => throw new ArgumentOutOfRangeException(
                nameof(outputFormat),
                outputFormat,
                "Unsupported CV output format."),
        };
    }
}
