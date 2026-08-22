using System.Xml.Linq;

public sealed class LatexBinaryDirectoryResolverTests
{
    [Fact]
    public void CommandLineDirectorySuppliesBothExecutables()
    {
        using var fixture = new ExecutableDirectoryFixture();

        var result = LatexBinaryDirectoryResolver.Resolve(fixture.Directory);

        Assert.Equal("--latex-bin-directory", result.SelectionSource);
        Assert.Equal(Path.GetFullPath(fixture.Directory), result.Directory);
        Assert.Equal(fixture.Latexmk, result.Paths.Latexmk);
        Assert.Equal(fixture.XeLatex, result.Paths.XeLatex);
    }

    [Fact]
    public void CommandLineDirectoryRejectsMixedOrMissingTools()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"fjh-latex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, ExecutableName("latexmk")), string.Empty);

            var exception = Assert.Throws<InvalidOperationException>(
                () => LatexBinaryDirectoryResolver.Resolve(directory));

            Assert.Contains("both latexmk and xelatex", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MalformedPathDirectoryIsSkipped()
    {
        Assert.Null(LatexBinaryDirectoryResolver.TryNormalizePathDirectory("\0"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var solutionPath = Path.Combine(
                directory.FullName,
                "FindJobHelper.slnx");
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root not found.");
    }

    private static string ExecutableName(string name) =>
        OperatingSystem.IsWindows() ? name + ".exe" : name;

    private sealed class ExecutableDirectoryFixture : IDisposable
    {
        public ExecutableDirectoryFixture()
        {
            Directory = Path.Combine(Path.GetTempPath(), $"fjh-latex-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
            Latexmk = Path.Combine(Directory, ExecutableName("latexmk"));
            XeLatex = Path.Combine(Directory, ExecutableName("xelatex"));
            File.WriteAllText(Latexmk, string.Empty);
            File.WriteAllText(XeLatex, string.Empty);
        }

        public string Directory { get; }
        public string Latexmk { get; }
        public string XeLatex { get; }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
