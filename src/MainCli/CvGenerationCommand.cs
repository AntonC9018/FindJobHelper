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
    private const string FinalPdfFileName = "CurmanchiiAnton.pdf";
    private const string FinalDebugMarkdownFileName =
        "CurmanchiiAnton-debug.md";

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
                arguments.Config,
                arguments.OutputDirectory,
                arguments.Debug,
                arguments.Open,
                cancellationToken);
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
        bool isDebug,
        bool openInOs,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

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
            searchConfiguration.PageCount);
        var searchResult = searchConfiguration.Search.Run(experienceDatabase, admissionPolicy);
        admissionPolicy.RequireExactPageCount();

        searchConfiguration.Sections.Apply(searchResult, currentModel);

        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            string stagedArtifactPath;
            string finalArtifactFileName;
            if (isDebug)
            {
                finalArtifactFileName = FinalDebugMarkdownFileName;
                stagedArtifactPath = Path.Combine(stagingDirectory, finalArtifactFileName);
                using var writer = new CodegenTextWriter
                {
                    NewLine = "\n",
                    PreserveNonWhitespaceIndentBehavior =
                        CodegenTextWriter.PreserveNonWhitespaceIndentBehaviorType.PreservePosition,
                };
                CvMarkdownRenderer.Render(currentModel, writer);
                await File.WriteAllTextAsync(
                    stagedArtifactPath,
                    writer.ToString(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken);
            }
            else
            {
                finalArtifactFileName = FinalPdfFileName;
                var artifacts = await CvTemplate.Generate(new()
                {
                    Model = currentModel,
                    CancellationToken = cancellationToken,
                    ConfigFilePath = templatePath,
                    OutputDirectory = stagingDirectory,
                    PageCount = searchConfiguration.PageCount,
                });
                stagedArtifactPath = artifacts.PdfPath;
            }

            Directory.CreateDirectory(fullOutputDirectory);
            var publishedArtifactPath = Path.Combine(
                fullOutputDirectory,
                finalArtifactFileName);
            File.Move(stagedArtifactPath, publishedArtifactPath, overwrite: true);

            if (openInOs)
            {
                ExplorerHelper.OpenFolderAndSelectFile(publishedArtifactPath);
            }

            Console.WriteLine($"Generated '{publishedArtifactPath}'.");
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

public sealed class CvGenerationArguments : IArgumentModel
{
    [Option("config", Description = "Path to the JSON CV selection configuration.")]
    public string Config { get; set; } = null!;

    [Option("output-directory", Description = "Destination directory for the generated artifact.")]
    public string OutputDirectory { get; set; } = null!;

    [Option("debug", Description = "Publish an annotated Markdown CV instead of compiling a PDF.")]
    public bool Debug { get; set; }

    [Option("open", Description = "Select the generated artifact after a successful generation.")]
    public bool Open { get; set; }
}
