using System.Collections.Immutable;

namespace FindJobHelper.CVGeneration;

public enum CvOutputFormat
{
    // CommandDotNet treats a zero-valued value-type property as having no default.
    // Starting at 1 makes the Tex property initializer an optional CLI default.
    // None = 0,
    Tex = 1,
    Md = 2,
}

public enum CvArtifactKind
{
    Pdf,
    CleanMarkdown,
    AnnotatedMarkdown,
}

public readonly record struct CvPlannedArtifact(
    CvArtifactKind Kind,
    string FileName);

public sealed record CvArtifactPlan(
    ImmutableArray<CvPlannedArtifact> Artifacts,
    CvArtifactKind OpenTarget)
{
    public static CvArtifactPlan Create(
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
