using CommandDotNet;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;
using MainCli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Location = FindJobHelper.CVGeneration.Location;

public sealed class CvGenerationCommand
{
    private const string FinalPdfFileName = "CurmanchiiAnton.pdf";

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

        var configuration = await CvSelectionConfiguration.LoadAsync(configPath, cancellationToken);
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
            throw new InvalidOperationException($"CV template file was not found: '{templatePath}'.");
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

        var searchResult = searchConfiguration.Search.Run(experienceDatabase.Experiences);
        var location = new Location(City: "Chișinău", Country: "Moldova");
        var currentModel = new CvDataModel
        {
            Name = new()
            {
                First = "Anton",
                Last = "Curmanschii",
            },
            CategorizedInfoLists = [
                new(Category.Technologies, searchConfiguration.Technologies),
                new(Category.GitHub, [
                    "https://github.com/AntonC9018",
                ]),
                new(Category.LinkedIn, [
                    "https://www.linkedin.com/in/anton-curmanschii-647232161",
                ]),
            ],
            CategorizedInfos = [
                new(Category.Location, location.FormatInfo()),
                new(Category.Email, personalInfo.Email),
                new(Category.Phone, personalInfo.Phone),
            ],
            Profession = new("Software Developer"),
            Educations = searchResult.Get(searchConfiguration.EducationKey),
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
            Summary = NullableLatexString.Null,
            WorkExperiences = searchResult.Get(searchConfiguration.WorkKey),
            PersonalProjects = searchResult.Get(searchConfiguration.PersonalProjectsKey),
            SectionOrder = searchConfiguration.SectionOrder,
        };

        var measurementService = serviceProvider.GetRequiredService<LatexMeasurementService>();
        var measurementSnapshot = await measurementService.MeasureAsync(
            experienceDatabase,
            currentModel,
            templatePath,
            cancellationToken);
        _ = measurementSnapshot;

        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"FindJobHelper-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var artifacts = await CvTemplate.Generate(new()
            {
                IsDebug = isDebug,
                Model = currentModel,
                CancellationToken = cancellationToken,
                ConfigFilePath = templatePath,
                OutputDirectory = stagingDirectory,
            });

            Directory.CreateDirectory(fullOutputDirectory);
            var publishedPdfPath = Path.Combine(fullOutputDirectory, FinalPdfFileName);
            File.Move(artifacts.PdfPath, publishedPdfPath, overwrite: true);

            if (openInOs)
            {
                ExplorerHelper.OpenFolderAndSelectFile(publishedPdfPath);
            }

            Console.WriteLine($"Generated '{publishedPdfPath}'.");
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
}

public sealed class CvGenerationArguments : IArgumentModel
{
    [Option("config", Description = "Path to the JSON CV selection configuration.")]
    public string Config { get; set; } = null!;

    [Option("output-directory", Description = "Directory where CurmanchiiAnton.pdf will be published.")]
    public string OutputDirectory { get; set; } = null!;

    [Option("debug", Description = "Include selection-score annotations in the generated CV.")]
    public bool Debug { get; set; }

    [Option("open", Description = "Open the published PDF after a successful generation.")]
    public bool Open { get; set; }
}
