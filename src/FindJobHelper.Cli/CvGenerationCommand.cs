using CommandDotNet;
using FindJobHelper.Configuration;
using FindJobHelper.Configuration.Json;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;
using FindJobHelper.ExperienceProject;
using FindJobHelper.Generation;

public sealed class CvGenerationCommand
{
    [Command("example-config", Description = "Print an example JSON CV selection configuration.")]
    public void PrintExampleConfig(
        [Option("master", Description = "Print the master CV configuration example.")]
        bool master = false)
    {
        Console.Write(File.ReadAllText(ConfigExamplePath(master)));
    }

    [Command("new-config", Description = "Write an example configuration to config.json.")]
    public int NewConfig(
        [Option(
            "output-directory",
            Description = "Destination directory for config.json.")]
        string outputDirectory = ".",
        [Option("master", Description = "Create the master CV configuration example.")]
        bool master = false)
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

        File.Copy(ConfigExamplePath(master), outputPath);
        Console.WriteLine($"Created '{outputPath}'.");
        return ExitCodes.Success;
    }

    [Command("list-tags", Description = "List all tags available for CV selection.")]
    public async Task<int> ListTags(
        ExperienceDatabaseArguments arguments,
        CancellationToken cancellationToken)
    {
        LoadedExperienceDatabaseProvider loadedProvider;
        try
        {
            var database = await ResolveExperienceDatabaseAsync(arguments, cancellationToken);
            loadedProvider = ExperienceDatabaseProviderLoader.Load(database.DllPath);
        }
        catch (ExperienceDatabaseSourceException ex)
        {
            Console.Error.WriteLine($"Experience database error: {ex.Message}");
            return ExitCodes.ValidationError;
        }
        catch (ExperienceProjectBuildException ex)
        {
            Console.Error.WriteLine($"Experience project build failed: {ex.Message}");
            return ExitCodes.Error;
        }
        catch (ExperienceDatabaseProviderLoadException ex)
        {
            Console.Error.WriteLine($"Experience database error: {ex.Message}");
            return ExitCodes.ValidationError;
        }
        catch (CvLayoutException ex)
        {
            Console.Error.WriteLine($"CV layout validation failed: {ex.Message}");
            return ExitCodes.ValidationError;
        }

        using (loadedProvider)
        {
            var tagsDatabase = loadedProvider.Result.TagsDatabase;
            foreach (var tag in tagsDatabase.TagsGraph.Keys
                         .Select(static tag => tag.Name)
                         .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static name => name, StringComparer.Ordinal))
            {
                Console.WriteLine(tag);
            }
        }

        return ExitCodes.Success;
    }

    [DefaultCommand]
    public async Task<int> Generate(
        CvGenerationArguments arguments,
        CancellationToken cancellationToken) =>
        await GenerateAsync(arguments, master: false, cancellationToken);

    [Command("master-cv", Description = "Generate a master CV containing every configured experience section.")]
    public async Task<int> MasterCv(
        CvGenerationArguments arguments,
        CancellationToken cancellationToken) =>
        await GenerateAsync(arguments, master: true, cancellationToken);

    private static async Task<int> GenerateAsync(
        CvGenerationArguments arguments,
        bool master,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = master
                ? await GenerateMasterAsync(arguments, cancellationToken)
                : await GenerateStandardAsync(arguments, cancellationToken);
            if (!result.Success)
            {
                Console.Error.WriteLine(result.Failure!.Message);
                return result.Failure.Disposition == CvFailureDisposition.Validation
                    ? ExitCodes.ValidationError
                    : ExitCodes.Error;
            }

            foreach (var artifact in result.Artifacts)
            {
                Console.WriteLine(
                    $"Generated '{result.PublishedPaths[artifact.Kind]}'.");
            }

            if (arguments.Open)
            {
                ExplorerHelper.OpenFolderAndSelectFile(
                    result.PublishedPaths[result.OpenTarget]);
            }

            return ExitCodes.Success;
        }
        catch (CvConfigurationException ex)
        {
            Console.Error.WriteLine($"Configuration error: {ex.Message}");
            return ExitCodes.ValidationError;
        }
        catch (ExperienceDatabaseSourceException ex)
        {
            Console.Error.WriteLine($"Experience database error: {ex.Message}");
            return ExitCodes.ValidationError;
        }
        catch (ExperienceProjectBuildException ex)
        {
            Console.Error.WriteLine($"Experience project build failed: {ex.Message}");
            return ExitCodes.Error;
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
        catch (CvGenerationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ExitCodes.Error;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"CV generation failed: {ex.Message}");
            return ExitCodes.Error;
        }
    }

    private static async Task<CvGenerationPipelineResult> GenerateStandardAsync(
        CvGenerationArguments arguments,
        CancellationToken cancellationToken)
    {
        var database = await ResolveExperienceDatabaseAsync(arguments, cancellationToken);
        var configuration = await CvSelectionConfigurationLoader.LoadAsync(
            arguments.Config,
            cancellationToken);
        return await CvGenerationPipeline.RunAsync(
            new CvGenerationPipelineRequest
            {
                Config = configuration,
                ExperienceDatabasePath = database.DllPath,
                WorkspaceConfigPath = database.WorkspaceConfigFilePath,
                OutputDirectory = arguments.OutputDirectory,
                OutputFormat = arguments.OutputFormat,
                Debug = arguments.Debug,
                LatexBinDirectory = arguments.LatexBinDirectory,
                Fonts = arguments.FontValues,
                ProgressDisplay = CvGenerationProgressDisplay.CreateDefault(),
            },
            cancellationToken);
    }

    private static async Task<CvGenerationPipelineResult> GenerateMasterAsync(
        CvGenerationArguments arguments,
        CancellationToken cancellationToken)
    {
        var database = await ResolveExperienceDatabaseAsync(arguments, cancellationToken);
        var configuration = await MasterCvConfigurationLoader.LoadAsync(
            arguments.Config,
            cancellationToken);
        return await CvGenerationPipeline.RunMasterAsync(
            new MasterCvGenerationPipelineRequest
            {
                Config = configuration,
                ExperienceDatabasePath = database.DllPath,
                WorkspaceConfigPath = database.WorkspaceConfigFilePath,
                OutputDirectory = arguments.OutputDirectory,
                OutputFormat = arguments.OutputFormat,
                Debug = arguments.Debug,
                LatexBinDirectory = arguments.LatexBinDirectory,
                Fonts = arguments.FontValues,
                ProgressDisplay = CvGenerationProgressDisplay.CreateDefault(),
            },
            cancellationToken);
    }

    private static async Task<ResolvedExperienceDatabase> ResolveExperienceDatabaseAsync(
        ExperienceDatabaseArguments arguments,
        CancellationToken cancellationToken)
    {
        var request = new ExperienceDatabaseRequest
        {
            ExplicitDllPath = NullIfEmpty(arguments.ExperienceDatabase),
            ExplicitProjectPath = NullIfEmpty(arguments.ExperienceProject),
            NoBuild = arguments.NoBuild,
            ConfigFilePath = WorkspaceConfig.Find(Environment.CurrentDirectory),
        };
        return await ExperienceDatabaseSource.ResolveAsync(request, cancellationToken);
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    internal static string ExampleConfigPath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cv-selection.example.json");

    internal static string MasterExampleConfigPath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "master-cv.example.json");

    private static string ConfigExamplePath(bool master) =>
        master ? MasterExampleConfigPath : ExampleConfigPath;
}

public class ExperienceDatabaseArguments : IArgumentModel
{
    [Option(
        "experience-database",
        Description = "Path to a DLL containing exactly one public experience database provider. "
            + "Skips the experience project build; when empty, the project comes from "
            + "--experience-project or findjobhelper.config.json.")]
    public string ExperienceDatabase { get; set; } = string.Empty;

    [Option(
        "experience-project",
        Description = "Path to the experience project csproj; overrides 'experienceProject' "
            + "in findjobhelper.config.json.")]
    public string ExperienceProject { get; set; } = string.Empty;

    [Option(
        "no-build",
        Description = "Skip building the experience project; an existing database DLL is required.")]
    public bool NoBuild { get; set; }
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
        "main-font-size",
        Description = "Positive finite LaTeX Scale factor for the main font. Overrides CV_MAIN_FONT_SIZE; default: no Scale option.")]
    public string? MainFontSize { get; set; }

    [Option(
        "sans-font",
        Description = "Installed LaTeX sans-serif font family. Overrides CV_SANS_FONT; default: Liberation Sans.")]
    public string? SansFont { get; set; }

    [Option(
        "sans-font-size",
        Description = "Positive finite LaTeX Scale factor for the sans-serif font. Overrides CV_SANS_FONT_SIZE; default: no Scale option.")]
    public string? SansFontSize { get; set; }

    [Option(
        "mono-font",
        Description = "Installed LaTeX monospaced font family. Overrides CV_MONO_FONT; default: Liberation Mono.")]
    public string? MonoFont { get; set; }

    [Option(
        "mono-font-size",
        Description = "Positive finite LaTeX Scale factor for the monospaced font. Overrides CV_MONO_FONT_SIZE; default: 0.92.")]
    public string? MonoFontSize { get; set; }

    internal LatexFontConfigurationValues FontValues => new(
        Families: new(
            main: MainFont,
            sans: SansFont,
            monospace: MonoFont),
        Scales: new(
            main: MainFontSize,
            sans: SansFontSize,
            monospace: MonoFontSize));
}
