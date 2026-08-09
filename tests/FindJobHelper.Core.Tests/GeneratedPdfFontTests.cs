using System.Text.RegularExpressions;
using FindJobHelper.CVGeneration;
using UglyToad.PdfPig;

namespace FindJobHelper.Core.Tests;

public sealed partial class GeneratedPdfFontTests
{
    private static readonly string[] Sentinels =
        ["MAINROLESENTINEL", "SANSROLESENTINEL", "MONOROLESENTINEL"];

    [Fact]
    public async Task GeneratedPdf_EmbedsEachConfiguredFontForItsFamilyRole()
    {
        using var fixture = new GeneratedPdfFixture();
        var fonts = new LatexFontOptions([
            new("Liberation Sans"),
            new("Liberation Serif"),
            new("Latin Modern Mono"),
        ]);

        var result = await CvTemplate.Generate(new()
        {
            ConfigFilePath = fixture.TemplatePath,
            OutputDirectory = fixture.OutputDirectory,
            Model = CreateEmptyModel(),
            FontOptions = fonts,
            CancellationToken = CancellationToken.None,
        }, new(NoOpProgressReporter.Instance, NoOpProgressReporter.Instance));

        var artifacts = Assert.IsType<GeneratedCvArtifacts>(result);
        using var document = PdfDocument.Open(artifacts.PdfPath);
        var words = document.GetPages().SelectMany(static page => page.GetWords()).ToArray();

        foreach (var role in LatexFontRoles.All)
        {
            AssertSentinelUsesFont(words, Sentinels[(int)role], fonts[role]);
        }
    }

    private static void AssertSentinelUsesFont(
        IEnumerable<UglyToad.PdfPig.Content.Word> words,
        string sentinel,
        LatexFontFamilyName expectedFont)
    {
        var word = Assert.Single(words, word => word.Text == sentinel);
        var observedFonts = word.Letters
            .Select(static letter => NormalizeEmbeddedFontName(
                Assert.IsType<string>(letter.FontName)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            NormalizeEmbeddedFontName(expectedFont.Value),
            Assert.Single(observedFonts));
    }

    private static string NormalizeEmbeddedFontName(string name)
    {
        var withoutSubsetPrefix = SubsetPrefix().Replace(name, string.Empty);
        var withoutEncodingSuffix = withoutSubsetPrefix.EndsWith("-Identity-H", StringComparison.Ordinal)
            ? withoutSubsetPrefix[..^"-Identity-H".Length]
            : withoutSubsetPrefix;
        var withoutSpaces = withoutEncodingSuffix.Replace(" ", string.Empty, StringComparison.Ordinal);
        return withoutSpaces == "LMMono10-Regular"
            ? "LatinModernMono"
            : withoutSpaces;
    }

    private static CvDataModel CreateEmptyModel() => new()
    {
        Name = new("First", "Last"),
        Profession = new("Developer"),
        CategorizedInfoLists = [],
        CategorizedInfos = [],
    };

    [GeneratedRegex(@"^[A-Z]{6}\+")]
    private static partial Regex SubsetPrefix();

    private sealed class GeneratedPdfFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"fjh-pdf-fonts-{Guid.NewGuid():N}");

        public string OutputDirectory => Path.Combine(_directory, "output");
        public string TemplatePath => Path.Combine(_directory, "font-role-test.tex");

        public GeneratedPdfFixture()
        {
            Directory.CreateDirectory(_directory);
            var productionTemplatePath = Path.Combine(
                    Path.GetDirectoryName(typeof(CvTemplate).Assembly.Location)!,
                    "data",
                    "cv_template_config.tex")
                .Replace('\\', '/');
            var sentinelContent = string.Join('\n', LatexFontRoles.All.Select(role =>
                $"  {{\\{LatexFontRoles.FamilyCommands[(int)role]} {Sentinels[(int)role]}\\par}}%"));
            File.WriteAllText(
                TemplatePath,
                $$"""
                \input{{{productionTemplatePath}}}
                \AtBeginDocument{%
                {{sentinelContent}}
                }
                """);
        }

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
