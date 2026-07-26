using System.Collections.Immutable;
using System.Text;
using CodegenCS;
using CommandDotNet;
using FindJobHelper.Core;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;
using MainCli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Location = FindJobHelper.CVGeneration.Location;

public sealed class CvGenerationCommand
{
    [Command("list-tags", Description = "List all tags available for CV selection.")]
    public void ListTags()
    {
        var tagsDatabase = TagsDatabaseFactory.Create().TagsDatabase;
        foreach (var tag in tagsDatabase.TagsGraph.Keys
                     .Select(static tag => tag.Name)
                     .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static name => name, StringComparer.Ordinal))
        {
            Console.WriteLine(tag);
        }
    }

    [DefaultCommand]
    public async Task<int> Generate(
        CvGenerationArguments arguments,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GenerateCore(
                configPath: arguments.Config,
                outputDirectory: arguments.OutputDirectory,
                outputFormat: arguments.OutputFormat,
                isDebug: arguments.Debug,
                openInOs: arguments.Open,
                cancellationToken: cancellationToken);
        }
        catch (CvConfigurationException ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            return ExitCodes.ValidationError;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CV generation failed: {ex.Message}");
            return ExitCodes.Error;
        }
    }

    private static async Task<int> GenerateCore(
        string configPath,
        string outputDirectory,
        CvOutputFormat outputFormat,
        bool isDebug,
        bool openInOs,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var artifactPlan = CvArtifactPlan.Create(outputFormat, isDebug);
        var configuration = await CvSelectionConfigurationLoader.LoadAsync(
            configPath,
            cancellationToken);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        var (tags, tagsDatabase) = TagsDatabaseFactory.Create();
        var searchConfiguration = configuration.BuildSearch(tagsDatabase);
        var experienceDatabase = ExperienceDatabaseFactory.Create(tags);
        var templatePath = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "cv_template_config.tex");
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("CV template file was not found.", templatePath);
        }

        await using var serviceProvider = await AppConfiguration.CreateApp(cancellationToken);
        var personalInfo = serviceProvider.GetRequiredService<IOptions<PersonalInfoOptions>>().Value;
        if (isDebug)
        {
            personalInfo.Phone = Miscellanious.BlurPhone(new()
            {
                String = personalInfo.Phone,
                MaxVisibleLen = 6,
                MinVisibleLen = 3,
            });
        }

        var location = new Location(City: "Chișinău", Country: "Moldova");
        var currentModel = new CvDataModel
        {
            Name = new()
            {
                First = "Anton",
                Last = "Curmanschii",
            },
            CategorizedInfoLists = CreateMetadataLists(searchConfiguration),
            CategorizedInfos = [
                new(Category.Location, location.FormatInfo()),
                new(Category.Email, personalInfo.Email),
                new(Category.Phone, personalInfo.Phone),
            ],
            Profession = new("Software Developer"),
            Languages = [
                new(
                    Language.Russian,
                    LanguageProficiencyLevel.Native),
                new(
                    Language.English,
                    LanguageProficiencyLevel.C2,
                    Skills: [
                        new("Technical Writing & Reading"),
                        new("Conversational Fluency"),
                    ]),
                new(
                    Language.Romanian,
                    LanguageProficiencyLevel.B2,
                    Skills: [
                        new("Technical Conversation"),
                        new("Tutoring"),
                    ]),
            ],
            Location = location,
            Summary = null,
            SectionOrder = searchConfiguration.SectionOrder,
        };

        var measurementService = serviceProvider.GetRequiredService<LatexMeasurementService>();
        var measurementSnapshot = await measurementService.MeasureAsync(
            experienceDatabase,
            currentModel,
            templatePath,
            cancellationToken);
        var admissionPolicy = new PageLayoutSelectionAdmissionPolicy(
            experienceDatabase,
            measurementSnapshot,
            searchConfiguration.Sections,
            searchConfiguration.SectionOrder,
            searchConfiguration.PageCount,
            searchConfiguration.PageLayout);
        var searchResult = searchConfiguration.Search.Run(experienceDatabase, admissionPolicy);
        if (searchConfiguration.PageLayout is null)
        {
            admissionPolicy.RequireExactPageCount();
        }
        else
        {
            admissionPolicy.RequireCompletePageLayout();
        }

        searchConfiguration.Sections.Apply(searchResult, currentModel);

        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var stagedArtifactPaths =
                new Dictionary<CvArtifactKind, string>(artifactPlan.Artifacts.Length);
            foreach (var artifact in artifactPlan.Artifacts)
            {
                switch (artifact.Kind)
                {
                    case CvArtifactKind.Pdf:
                        var artifacts = await CvTemplate.Generate(new()
                        {
                            Model = currentModel,
                            CancellationToken = cancellationToken,
                            ConfigFilePath = templatePath,
                            OutputDirectory = stagingDirectory,
                            PageCount = searchConfiguration.PageCount,
                            PageLayout = searchConfiguration.PageLayout,
                        });
                        stagedArtifactPaths.Add(artifact.Kind, artifacts.PdfPath);
                        break;
                    case CvArtifactKind.CleanMarkdown:
                        stagedArtifactPaths.Add(
                            artifact.Kind,
                            await StageMarkdownAsync(
                                currentModel,
                                CvMarkdownRenderMode.Clean,
                                artifact.FileName,
                                stagingDirectory,
                                cancellationToken));
                        break;
                    case CvArtifactKind.AnnotatedMarkdown:
                        stagedArtifactPaths.Add(
                            artifact.Kind,
                            await StageMarkdownAsync(
                                currentModel,
                                CvMarkdownRenderMode.Annotated,
                                artifact.FileName,
                                stagingDirectory,
                                cancellationToken));
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(artifact),
                            artifact,
                            "Unsupported CV artifact kind.");
                }
            }

            Directory.CreateDirectory(fullOutputDirectory);
            var publishedArtifactPaths =
                new Dictionary<CvArtifactKind, string>(artifactPlan.Artifacts.Length);
            foreach (var artifact in artifactPlan.Artifacts)
            {
                var publishedArtifactPath = Path.Combine(
                    fullOutputDirectory,
                    artifact.FileName);
                File.Move(
                    stagedArtifactPaths[artifact.Kind],
                    publishedArtifactPath,
                    overwrite: true);
                publishedArtifactPaths.Add(artifact.Kind, publishedArtifactPath);
                Console.WriteLine($"Generated '{publishedArtifactPath}'.");
            }

            if (openInOs)
            {
                ExplorerHelper.OpenFolderAndSelectFile(
                    publishedArtifactPaths[artifactPlan.OpenTarget]);
            }

            return ExitCodes.Success;
        }
        finally
        {
            try
            {
                Directory.Delete(stagingDirectory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Nothing remains to clean up.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Failed to clean temporary CV generation directory '{stagingDirectory}': {ex.Message}");
            }
        }
    }

    private static async Task<string> StageMarkdownAsync(
        CvDataModel model,
        CvMarkdownRenderMode renderMode,
        string fileName,
        string stagingDirectory,
        CancellationToken cancellationToken)
    {
        var stagedArtifactPath = Path.Combine(stagingDirectory, fileName);
        using var writer = new CodegenTextWriter
        {
            NewLine = "\n",
            PreserveNonWhitespaceIndentBehavior =
                CodegenTextWriter.PreserveNonWhitespaceIndentBehaviorType.PreservePosition,
        };
        CvMarkdownRenderer.Render(model, renderMode, writer);
        await File.WriteAllTextAsync(
            stagedArtifactPath,
            writer.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        return stagedArtifactPath;
    }

    private static ImmutableArray<CategorizedInfoList> CreateMetadataLists(
        ConfiguredCvSearch searchConfiguration) =>
    [
        new(Category.Skills, searchConfiguration.Skills),
        new(Category.Technologies, searchConfiguration.Technologies),
        new(Category.GitHub, ["https://github.com/AntonC9018"]),
        new(Category.LinkedIn, [
            "https://www.linkedin.com/in/anton-curmanschii-647232161",
        ]),
    ];
}

public enum CvOutputFormat
{
    // CommandDotNet treats a zero-valued value-type property as having no default.
    // Starting at 1 makes the Tex property initializer an optional CLI default.
    // None = 0,
    Tex = 1,
    Md = 2,
}

public sealed class CvGenerationArguments : IArgumentModel
{
    [Option("config", Description = "Path to the JSON CV selection configuration.")]
    public string Config { get; set; } = null!;

    [Option("output-directory", Description = "Destination directory for the generated artifact.")]
    public string OutputDirectory { get; set; } = null!;

    [Option(
        "output-format",
        Description =
            "Output format: tex uses the LaTeX renderer and publishes a compiled PDF; md publishes clean Markdown.")]
    public CvOutputFormat OutputFormat { get; set; } = CvOutputFormat.Tex;

    [Option(
        "debug",
        Description =
            "Override --output-format and publish both clean and annotated Markdown without compiling a PDF.")]
    public bool Debug { get; set; }

    [Option("open", Description = "Select the generated artifact after a successful generation.")]
    public bool Open { get; set; }
}
