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

    [Fact]
    public void TemplatePackageLinksTheCanonicalInstallerDirectly()
    {
        var root = FindRepositoryRoot();
        var projectPath = Path.Combine(
            root,
            "templates",
            "FindJobWorkspace",
            "Anton.FindJobHelper.Templates.csproj");
        var project = XDocument.Load(projectPath);
        var installer = project.Descendants("Content").Single(element =>
            (string?)element.Attribute("PackagePath") == @"content\scripts\setup-latex.sh");

        Assert.Equal(@"..\..\scripts\setup-latex.sh", (string?)installer.Attribute("Include"));
        Assert.False(File.Exists(Path.Combine(root, "templates", "FindJobWorkspace", "content", "scripts", "setup-latex.sh")));
    }

    [Fact]
    public void TemplateProviderSwitchesFromLocalCoreProjectToPackedCorePackage()
    {
        var root = FindRepositoryRoot();
        var templateDirectory = Path.Combine(root, "templates", "FindJobWorkspace");
        var provider = XDocument.Load(Path.Combine(
            templateDirectory,
            "content",
            "src",
            "FindJobWorkspace.Provider",
            "FindJobWorkspace.Provider.csproj"));
        var packageProject = XDocument.Load(Path.Combine(
            templateDirectory,
            "Anton.FindJobHelper.Templates.csproj"));

        var projectReference = provider.Descendants("ProjectReference").Single();
        var versionPoke = packageProject.Descendants("XmlPoke").Single();

        Assert.Equal(
            "FindJobHelperCoreReference",
            (string?)projectReference.Parent?.Attribute("Label"));
        Assert.DoesNotContain(provider.Descendants("PackageReference"), element =>
            (string?)element.Attribute("Include") == "Anton.FindJobHelper.Core");
        Assert.Equal(
            "/Project/ItemGroup[@Label='FindJobHelperCoreReference']",
            (string?)versionPoke.Attribute("Query"));
        Assert.Equal(
            "<PackageReference Include=\"Anton.FindJobHelper.Core\" Version=\"[$(PackageVersion)]\" />",
            (string?)versionPoke.Attribute("Value"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FindJobHelper.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
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
