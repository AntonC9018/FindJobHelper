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
        Console.Write(File.ReadAllText(ExampleConfigPath));
    }

    [Command("new-config", Description = "Write an example configuration to config.json.")]
    public int NewConfig(
        [Option(
            "output-directory",
            Description = "Destination directory for config.json.")]
        string outputDirectory = ".")
    {
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);
        var outputPath = Path.Combine(fullOutputDirectory, "config.json");
        if (File.Exists(outputPath))
        {
            Console.Error.WriteLine(
                $"Cannot create '{outputPath}': the file already exists.");
            return ExitCodes.Error;
        }

        File.Copy(ExampleConfigPath, outputPath);
        Console.WriteLine($"Created '{outputPath}'.");
        return ExitCodes.Success;
    }

    [Command("list-tags", Description = "List all tags available for CV selection.")]
    public int ListTags(ExperienceDatabaseArguments arguments)
    {
        LoadedExperienceDatabaseProvider loadedProvider;
        try
        {
            loadedProvider = ExperienceDatabaseProviderLoader.Load(
                arguments.ExperienceDatabase);
        }
        catch (ExperienceDatabaseProviderLoadException ex)
        {
            Console.Error.WriteLine($"Experience database error: {ex.Message}");
            return ExitCodes.ValidationError;
        }
        catch (ExpectedCliFailure ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (CvLayoutException ex)
        {
            Console.Error.WriteLine($"CV layout validation failed: {ex.Message}");
            return ExitCodes.ValidationError;
        }

        var tagsDatabase = loadedProvider.Result.TagsDatabase;
        foreach (var tag in tagsDatabase.TagsGraph.Keys
                     .Select(static tag => tag.Name)
                     .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static name => name, StringComparer.Ordinal))
        {
            Console.WriteLine(tag);
        }

        return ExitCodes.Success;
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
                experienceDatabasePath: arguments.ExperienceDatabase,
                outputDirectory: arguments.OutputDirectory,
                outputFormat: arguments.OutputFormat,
                isDebug: arguments.Debug,
                openInOs: arguments.Open,
                latexBinDirectory: arguments.LatexBinDirectory,
                fontConfiguration: LatexFontConfigurationResolver.Resolve(
                    flags: arguments.FontFlags,
                    environments: LatexFontConfigurationResolver.GetEnvironmentValues()),
                cancellationToken: cancellationToken);
        }
        catch (CvConfigurationException ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            return ExitCodes.ValidationError;
        }
        catch (ExperienceDatabaseProviderLoadException ex)
        {
            Console.Error.WriteLine($"Experience database error: {ex.Message}");
            return ExitCodes.ValidationError;
        }
        catch (LatexFontConfigurationException ex)
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
        string experienceDatabasePath,
        string outputDirectory,
        CvOutputFormat outputFormat,
        bool isDebug,
        bool openInOs,
        string? latexBinDirectory,
        ResolvedLatexFontConfiguration fontConfiguration,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var configuration = await CvSelectionConfigurationLoader.LoadAsync(
            configPath,
            cancellationToken);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        var loadedProvider = ExperienceDatabaseProviderLoader.Load(
            experienceDatabasePath);
        var providerResult = loadedProvider.Result;
        var searchConfiguration = configuration.BuildSearch(
            providerResult.TagsDatabase);
        var experienceDatabase = providerResult.ExperienceDatabase;
        var templatePath = Path.Combine(
            AppContext.BaseDirectory,
            "data",
            "cv_template_config.tex");
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("CV template file was not found.", templatePath);
        }

        var latexExecutables = LatexBinaryDirectoryResolver.Resolve(latexBinDirectory);
        Console.WriteLine($"LaTeX tools selected by {latexExecutables.SelectionSource}: {latexExecutables.Directory}");
        Console.WriteLine($"latexmk: {latexExecutables.Paths.Latexmk}");
        Console.WriteLine($"xelatex: {latexExecutables.Paths.XeLatex}");
        var latexExecutionOptions = CreateLatexExecutionOptions(fontConfiguration);
        await using var serviceProvider = await AppConfiguration.CreateApp(
            loadedProvider.Assembly,
            latexExecutables.Paths,
            cancellationToken);
        var personalInfo = serviceProvider.GetRequiredService<IOptions<PersonalInfoOptions>>().Value;
        var artifactPlan = CvArtifactPlan.Create(
            outputFormat,
            isDebug,
            $"{personalInfo.LastName}{personalInfo.FirstName}");
        if (isDebug)
        {
            personalInfo.Phone = Miscellanious.BlurPhone(new()
            {
                String = personalInfo.Phone,
                MaxVisibleLen = 6,
                MinVisibleLen = 3,
            });
        }

        var location = new Location(personalInfo.City, personalInfo.Country);
        var currentModel = new CvDataModel
        {
            Name = new()
            {
                First = personalInfo.FirstName,
                Last = personalInfo.LastName,
            },
            CategorizedInfoLists = CreateMetadataLists(searchConfiguration, personalInfo),
            CategorizedInfos = [
                new(Category.Location, location.FormatInfo()),
                new(Category.Email, personalInfo.Email),
                new(Category.Phone, personalInfo.Phone),
            ],
            Profession = new(personalInfo.Profession),
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
        CvFailurePresentation? failurePresentation = null;
        var publishedArtifactPaths = await progressDisplay.RunAsync(
            progressPlan,
            async progress =>
            {
                progress.BeginModule(CvGenerationModule.ComputingHeights);
                var measurementResult = await measurementService.MeasureAsync(
                    experienceDatabase,
                    currentModel,
                    templatePath,
                    progress.Reporter(CvGenerationModule.ComputingHeights),
                    fontConfiguration.Options,
                    latexExecutionOptions,
                    cancellationToken);
                if (measurementResult is not CvMeasurementSnapshot measurementSnapshot)
                {
                    failurePresentation = CvFailurePresenter.Present(measurementResult);
                    return new Dictionary<CvArtifactKind, string>();
                }
                progress.BeginModule(CvGenerationModule.MatchingExperiences);
                var searchResult = searchConfiguration.Run(
                    experienceDatabase,
                    measurementSnapshot,
                    progress.Reporter(CvGenerationModule.MatchingExperiences));

                searchConfiguration.Sections.Apply(searchResult, currentModel);
                var artifactResult = await GenerateAndPublishArtifactsAsync(
                    artifactPlan,
                    currentModel,
                    templatePath,
                    fullOutputDirectory,
                    searchConfiguration.PageCount,
                    searchConfiguration.PageLayout,
                    latexExecutables.Paths,
                    fontConfiguration.Options,
                    latexExecutionOptions,
                    progress,
                    cancellationToken);
                if (artifactResult is ArtifactGenerationFailure failure)
                {
                    failurePresentation = failure.Presentation;
                    return new Dictionary<CvArtifactKind, string>();
                }
                if (artifactResult is PublishedArtifactPaths published)
                {
                    return published.Paths;
                }
                throw new InvalidOperationException(
                    $"Unsupported artifact generation result implementation '{artifactResult.GetType().FullName}'.");
            },
            cancellationToken);

        if (failurePresentation is not null)
        {
            Console.Error.WriteLine(failurePresentation.Message);
            return failurePresentation.Disposition == CvFailureDisposition.Validation
                ? ExitCodes.ValidationError
                : ExitCodes.Error;
        }

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

    private static async Task<IArtifactGenerationResult>
        GenerateAndPublishArtifactsAsync(
            CvArtifactPlan artifactPlan,
            CvDataModel model,
            string templatePath,
            string outputDirectory,
            CvPageCount pageCount,
            CvPageLayout? pageLayout,
            LatexExecutablePaths latexExecutables,
            LatexFontOptions fontOptions,
            LatexExecutionOptions latexExecutionOptions,
            CvGenerationProgressContext progress,
            CancellationToken cancellationToken)
    {
        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        var retainStagingDirectory = false;

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
                                LatexExecutables = latexExecutables,
                                FontOptions = fontOptions,
                                ExecutionOptions = latexExecutionOptions,
                            },
                            new(
                                progress.Reporter(
                                    CvGenerationModule.CreatingTexFile),
                                progress.Reporter(
                                    CvGenerationModule.RenderingPdf)));
                        if (artifacts is not GeneratedCvArtifacts generatedArtifacts)
                        {
                            retainStagingDirectory = true;
                            var presentation = CvFailurePresenter.Present(artifacts);
                            return new ArtifactGenerationFailure(
                                presentation with
                                {
                                    Message = $"{presentation.Message} Retained generation files: '{stagingDirectory}'.",
                                });
                        }
                        stagedArtifactPaths.Add(
                            artifact.Kind,
                            generatedArtifacts.PdfPath);
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
                        var renderMode = artifact.Kind == CvArtifactKind.CleanMarkdown
                            ? CvMarkdownRenderMode.Clean
                            : CvMarkdownRenderMode.Annotated;
                        var stagedMarkdownPath = await StageMarkdownAsync(
                            model,
                            renderMode,
                            artifact.FileName,
                            stagingDirectory,
                            markdownProgress,
                            cancellationToken);
                        stagedArtifactPaths.Add(
                            artifact.Kind,
                            stagedMarkdownPath);
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

            return new PublishedArtifactPaths(publishedArtifactPaths);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            retainStagingDirectory = true;
            Console.Error.WriteLine(
                $"Retained generation files: '{stagingDirectory}'.");
            throw;
        }
        finally
        {
            try
            {
                if (!retainStagingDirectory)
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
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
        ConfiguredCvSearch searchConfiguration,
        PersonalInfoOptions personalInfo) =>
    [
        new(Category.Skills, searchConfiguration.Skills),
        new(Category.Technologies, searchConfiguration.Technologies),
        new(Category.GitHub, [personalInfo.GitHub]),
        new(Category.LinkedIn, [personalInfo.LinkedIn]),
    ];

    internal static string ExampleConfigPath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cv-selection.example.json");

    private static LatexExecutionOptions CreateLatexExecutionOptions(
        ResolvedLatexFontConfiguration fontConfiguration)
    {
        // Keep this declaration synchronized with scripts/setup-latex.sh. A match
        // means the supported installation is incomplete; custom resources that
        // are not declared here remain ordinary LaTeX compilation failures.
        List<ILatexRequirement> requirements =
        [
            new ExecutableLatexRequirement(new("xelatex")),
            new ExecutableLatexRequirement(new("latexmk")),
            new TexFileLatexRequirement(new("babel.sty")),
            new TexFileLatexRequirement(new("xifthen.sty")),
            new TexFileLatexRequirement(new("ifmtarg.sty")),
            new TexFileLatexRequirement(new("moresize.sty")),
            new TexFileLatexRequirement(new("zref-lastpage.sty")),
            new TexFileLatexRequirement(new("needspace.sty")),
            new TexFileLatexRequirement(new("multirow.sty")),
            new TexFileLatexRequirement(new("wrapfig.sty")),
            new TexFileLatexRequirement(new("varwidth.sty")),
            new TexFileLatexRequirement(new("environ.sty")),
            new BabelLanguageLatexRequirement(new("romanian")),
        ];
        requirements.AddRange(LatexFontRoles.All.Select(role => new FontLatexRequirement(
            fontConfiguration.Options[role],
            IsManuallySpecified: fontConfiguration.ManuallySpecified[role])));
        return new(requirements, setupCommandHint: "./scripts/setup-latex.sh");
    }

}

internal interface IArtifactGenerationResult;

internal sealed record PublishedArtifactPaths(Dictionary<CvArtifactKind, string> Paths) : IArtifactGenerationResult;

internal sealed record ArtifactGenerationFailure(CvFailurePresentation Presentation) : IArtifactGenerationResult;

internal sealed class ExpectedCliFailure(string message, int exitCode) : Exception(message)
{
    public int ExitCode { get; } = exitCode;

    public static ExpectedCliFailure Validation(string message) => new(message, ExitCodes.ValidationError);

    public static ExpectedCliFailure General(string message) => new(message, ExitCodes.Error);
}

public enum CvOutputFormat
{
    // CommandDotNet treats a zero-valued value-type property as having no default.
    // Starting at 1 makes the Tex property initializer an optional CLI default.
    // None = 0,
    Tex = 1,
    Md = 2,
}

public class ExperienceDatabaseArguments : IArgumentModel
{
    [Option(
        "experience-database",
        Description = "Path to a DLL containing exactly one public experience database provider.")]
    public string ExperienceDatabase { get; set; } = null!;
}

public sealed class CvGenerationArguments : ExperienceDatabaseArguments
{
    [Option("config", Description = "Path to the JSON CV selection configuration.")]
    public string Config { get; set; } = null!;

    [Option("output-directory", Description = "Destination directory for the generated artifact.")]
    public string OutputDirectory { get; set; } = ".";

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

    [Option(
        "latex-bin-directory",
        Description = "Directory containing both latexmk and xelatex. Overrides FINDJOBHELPER_LATEX_BIN_DIRECTORY and automatic discovery.")]
    public string? LatexBinDirectory { get; set; }

    [Option(
        "main-font",
        Description = "Installed LaTeX main font family. Overrides CV_MAIN_FONT; default: Liberation Serif.")]
    public string? MainFont { get; set; }

    [Option(
        "sans-font",
        Description = "Installed LaTeX sans-serif font family. Overrides CV_SANS_FONT; default: Liberation Sans.")]
    public string? SansFont { get; set; }

    [Option(
        "mono-font",
        Description = "Installed LaTeX monospaced font family. Overrides CV_MONO_FONT; default: Liberation Mono.")]
    public string? MonoFont { get; set; }

}

internal static class CvGenerationArgumentsExtensions
{
    extension(CvGenerationArguments arguments)
    {
        internal LatexFontRoleArray<string?> FontFlags => new(
            main: arguments.MainFont,
            sans: arguments.SansFont,
            monospace: arguments.MonoFont);
    }
}

internal sealed record ResolvedLatexFontConfiguration(
    LatexFontOptions Options,
    LatexFontRoleArray<bool> ManuallySpecified);

internal sealed class LatexFontConfigurationException(string message) : Exception(message);

internal static class LatexFontConfigurationResolver
{
    public static LatexFontRoleArray<LatexFontSetting> Settings { get; } = new(
        main: new(
            Role: LatexFontRole.Main,
            FlagName: "--main-font",
            EnvironmentVariable: "CV_MAIN_FONT"),
        sans: new(
            Role: LatexFontRole.Sans,
            FlagName: "--sans-font",
            EnvironmentVariable: "CV_SANS_FONT"),
        monospace: new(
            Role: LatexFontRole.Mono,
            FlagName: "--mono-font",
            EnvironmentVariable: "CV_MONO_FONT"));

    public static LatexFontRoleArray<string?> GetEnvironmentValues() =>
        Settings.Map(static setting => Environment.GetEnvironmentVariable(setting.EnvironmentVariable));

    public static ResolvedLatexFontConfiguration Resolve(
        LatexFontRoleArray<string?> flags,
        LatexFontRoleArray<string?> environments)
    {
        var main = ResolveRole(
            flag: flags.Main,
            environment: environments.Main,
            defaultValue: LatexFontOptions.Default.Families.Main,
            setting: Settings.Main,
            manuallySpecified: out var mainManuallySpecified);
        var sans = ResolveRole(
            flag: flags.Sans,
            environment: environments.Sans,
            defaultValue: LatexFontOptions.Default.Families.Sans,
            setting: Settings.Sans,
            manuallySpecified: out var sansManuallySpecified);
        var monospace = ResolveRole(
            flag: flags.Monospace,
            environment: environments.Monospace,
            defaultValue: LatexFontOptions.Default.Families.Monospace,
            setting: Settings.Monospace,
            manuallySpecified: out var monospaceManuallySpecified);
        return new(
            new LatexFontOptions(new(
                main: main,
                sans: sans,
                monospace: monospace)),
            new(
                main: mainManuallySpecified,
                sans: sansManuallySpecified,
                monospace: monospaceManuallySpecified));
    }

    private static LatexFontFamilyName ResolveRole(
        string? flag,
        string? environment,
        LatexFontFamilyName defaultValue,
        LatexFontSetting setting,
        out bool manuallySpecified)
    {
        manuallySpecified = false;
        var value = flag ?? environment;
        if (value is null)
        {
            return defaultValue;
        }
        var source = flag is not null ? setting.FlagName : setting.EnvironmentVariable;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LatexFontConfigurationException($"{source} must not be blank.");
        }
        try
        {
            var family = new LatexFontFamilyName(value);
            manuallySpecified = true;
            return family;
        }
        catch (ArgumentException exception)
        {
            throw new LatexFontConfigurationException($"Invalid value for {source}: {exception.Message}");
        }
    }

}

internal sealed record LatexFontSetting(
    LatexFontRole Role,
    string FlagName,
    string EnvironmentVariable);
