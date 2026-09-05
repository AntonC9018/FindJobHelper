using CommandDotNet;
using FindJobHelper.Configuration;
using FindJobHelper.Configuration.Json;
using FindJobHelper.Core;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;

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
            var configuration = await CvSelectionConfigurationLoader.LoadAsync(
                arguments.Config,
                cancellationToken);
            var result = await CvGenerationPipeline.RunAsync(
                new CvGenerationPipelineRequest
                {
                    Config = configuration,
                    ExperienceDatabasePath = arguments.ExperienceDatabase,
                    OutputDirectory = arguments.OutputDirectory,
                    OutputFormat = arguments.OutputFormat,
                    Debug = arguments.Debug,
                    LatexBinDirectory = arguments.LatexBinDirectory,
                    Fonts = arguments.FontValues,
                    ProgressDisplay = CvGenerationProgressDisplay.CreateDefault(),
                },
                cancellationToken);
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

    internal static string ExampleConfigPath => Path.Combine(
        AppContext.BaseDirectory,
        "data",
        "cv-selection.example.json");
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
