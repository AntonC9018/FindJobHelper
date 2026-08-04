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
    [Command("example-config", Description = "Print an example JSON CV selection configuration.")]
    public void PrintExampleConfig()
    {
        var examplePath = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "cv-selection.example.json");
        Console.Write(File.ReadAllText(examplePath));
    }

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
        var progressPlan = CreateProgressPlan(artifactPlan);
        var progressDisplay = CvGenerationProgressDisplay.CreateDefault();
        var publishedArtifactPaths = await progressDisplay.RunAsync(
            progressPlan,
            async progress =>
            {
                progress.BeginModule(CvGenerationModule.ComputingHeights);
                var measurementSnapshot = await measurementService.MeasureAsync(
                    experienceDatabase,
                    currentModel,
                    templatePath,
                    progress.Reporter(CvGenerationModule.ComputingHeights),
                    cancellationToken);
                var admissionPolicy = new PageLayoutSelectionAdmissionPolicy(
                    experienceDatabase,
                    measurementSnapshot,
                    searchConfiguration.Sections,
                    searchConfiguration.SectionOrder,
                    searchConfiguration.PageCount,
                    searchConfiguration.PageLayout);
                progress.BeginModule(CvGenerationModule.MatchingExperiences);
                var searchResult = searchConfiguration.Search.Run(
                    experienceDatabase,
                    admissionPolicy,
                    progress.Reporter(CvGenerationModule.MatchingExperiences));
                if (searchConfiguration.PageLayout is null)
                {
                    admissionPolicy.RequireExactPageCount();
                }
                else
                {
                    admissionPolicy.RequireCompletePageLayout();
                }

                searchConfiguration.Sections.Apply(searchResult, currentModel);
                return await GenerateAndPublishArtifactsAsync(
                    artifactPlan,
                    currentModel,
                    templatePath,
                    fullOutputDirectory,
                    searchConfiguration.PageCount,
                    searchConfiguration.PageLayout,
                    progress,
                    cancellationToken);
            },
            cancellationToken);

        foreach (var artifact in artifactPlan.Artifacts)
        {
            Console.WriteLine(
                $"Generated '{publishedArtifactPaths[artifact.Kind]}'.");
        }

        if (openInOs)
        {
            ExplorerHelper.OpenFolderAndSelectFile(
                publishedArtifactPaths[artifactPlan.OpenTarget]);
        }

        return ExitCodes.Success;
    }

    private static async Task<string> StageMarkdownAsync(
        CvDataModel model,
        CvMarkdownRenderMode renderMode,
        string fileName,
        string stagingDirectory,
        IProgressReporter progress,
        CancellationToken cancellationToken)
    {
        var stagedArtifactPath = Path.Combine(stagingDirectory, fileName);
        using var writer = new CodegenTextWriter
        {
            NewLine = "\n",
            PreserveNonWhitespaceIndentBehavior =
                CodegenTextWriter.PreserveNonWhitespaceIndentBehaviorType.PreservePosition,
        };
        CvMarkdownRenderer.Render(model, renderMode, progress, writer);
        await File.WriteAllTextAsync(
            stagedArtifactPath,
            writer.ToString(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
        var workUnits = CvMarkdownRenderer.GetWorkUnitCount(model);
        progress.Report(new(
            CompletedWorkUnits: workUnits,
            TotalWorkUnits: workUnits,
            Detail: "Creating Markdown files"));
        return stagedArtifactPath;
    }

    private static CvGenerationProgressPlan CreateProgressPlan(
        CvArtifactPlan artifactPlan)
    {
        var modules = new List<CvGenerationProgressModule>
        {
            new(
                CvGenerationModule.ComputingHeights,
                "Computing heights"),
            new(
                CvGenerationModule.MatchingExperiences,
                "Matching experiences"),
        };

        if (artifactPlan.Artifacts.Any(
                static artifact => artifact.Kind == CvArtifactKind.Pdf))
        {
            modules.Add(new(
                CvGenerationModule.CreatingTexFile,
                "Creating TeX file"));
            modules.Add(new(
                CvGenerationModule.RenderingPdf,
                "Rendering PDF"));
        }
        else
        {
            modules.Add(new(
                CvGenerationModule.CreatingMarkdownFiles,
                "Creating Markdown files"));
        }

        return new(modules);
    }

    private static async Task<Dictionary<CvArtifactKind, string>>
        GenerateAndPublishArtifactsAsync(
            CvArtifactPlan artifactPlan,
            CvDataModel model,
            string templatePath,
            string outputDirectory,
            CvPageCount pageCount,
            CvPageLayout? pageLayout,
            CvGenerationProgressContext progress,
            CancellationToken cancellationToken)
    {
        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var stagedArtifactPaths =
                new Dictionary<CvArtifactKind, string>(
                    artifactPlan.Artifacts.Length);
            var markdownFileCount = artifactPlan.Artifacts.Count(
                static artifact => artifact.Kind
                    is CvArtifactKind.CleanMarkdown
                    or CvArtifactKind.AnnotatedMarkdown);
            var markdownWorkUnits = CvMarkdownRenderer.GetWorkUnitCount(model);
            var allMarkdownWorkUnits = checked(
                markdownWorkUnits * markdownFileCount);
            var markdownFileIndex = 0;

            foreach (var artifact in artifactPlan.Artifacts)
            {
                switch (artifact.Kind)
                {
                    case CvArtifactKind.Pdf:
                        progress.BeginModule(
                            CvGenerationModule.CreatingTexFile);
                        var artifacts = await CvTemplate.Generate(
                            new()
                            {
                                Model = model,
                                CancellationToken = cancellationToken,
                                ConfigFilePath = templatePath,
                                OutputDirectory = stagingDirectory,
                                PageCount = pageCount,
                                PageLayout = pageLayout,
                            },
                            new(
                                progress.Reporter(
                                    CvGenerationModule.CreatingTexFile),
                                progress.Reporter(
                                    CvGenerationModule.RenderingPdf)));
                        stagedArtifactPaths.Add(
                            artifact.Kind,
                            artifacts.PdfPath);
                        break;
                    case CvArtifactKind.CleanMarkdown:
                    case CvArtifactKind.AnnotatedMarkdown:
                        if (markdownFileIndex == 0)
                        {
                            progress.BeginModule(
                                CvGenerationModule.CreatingMarkdownFiles);
                        }
                        var markdownProgress = new ProgressRangeReporter(
                            progress.Reporter(
                                CvGenerationModule.CreatingMarkdownFiles),
                            offset: markdownFileIndex * markdownWorkUnits,
                            length: markdownWorkUnits,
                            targetTotal: allMarkdownWorkUnits);
                        stagedArtifactPaths.Add(
                            artifact.Kind,
                            await StageMarkdownAsync(
                                model,
                                artifact.Kind == CvArtifactKind.CleanMarkdown
                                    ? CvMarkdownRenderMode.Clean
                                    : CvMarkdownRenderMode.Annotated,
                                artifact.FileName,
                                stagingDirectory,
                                markdownProgress,
                                cancellationToken));
                        markdownFileIndex++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(artifact),
                            artifact,
                            "Unsupported CV artifact kind.");
                }
            }

            Directory.CreateDirectory(outputDirectory);
            var publishedArtifactPaths =
                new Dictionary<CvArtifactKind, string>(
                    artifactPlan.Artifacts.Length);
            foreach (var artifact in artifactPlan.Artifacts)
            {
                var publishedArtifactPath = Path.Combine(
                    outputDirectory,
                    artifact.FileName);
                File.Move(
                    stagedArtifactPaths[artifact.Kind],
                    publishedArtifactPath,
                    overwrite: true);
                publishedArtifactPaths.Add(
                    artifact.Kind,
                    publishedArtifactPath);
            }

            return publishedArtifactPaths;
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
