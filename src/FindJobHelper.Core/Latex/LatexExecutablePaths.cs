namespace FindJobHelper.CVGeneration;

public sealed record LatexExecutablePaths(string Latexmk, string XeLatex)
{
    public static LatexExecutablePaths FromPath { get; } = new("latexmk", "xelatex");
}
