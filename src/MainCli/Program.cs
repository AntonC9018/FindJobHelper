using System.ClientModel.Primitives;
using System.Text.Json;
using MainCli;
using MainCli.Helper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Models;
using TheirStack;
using Location = MainCli.Location;

#pragma warning disable CS8321 // Local function is declared but never used

var cancellationToken = CancellationToken.None;
await using var serviceProvider = await AppConfiguration.CreateApp(cancellationToken);
_ = serviceProvider;

var personalInfo = serviceProvider.GetRequiredService<IOptions<PersonalInfoOptions>>().Value;
// ReSharper disable once RedundantAssignment
bool isDebug = true;
// isDebug = false;
if (isDebug)
{
    personalInfo.Phone = Miscellanious.BlurPhone(new()
    {
        String = personalInfo.Phone,
        MaxVisibleLen = 6,
        MinVisibleLen = 3,
    });
}

var configFullPath = Path.GetFullPath("data/cv_template_config.tex");
var location = new Location(City: "Chișinău", Country: "Moldova");
var experienceDatabase = ExperienceDatabaseFactory.Create();

var tags = experienceDatabase.WeightedTasks([
    (".NET", 1.0f),
    ("ASP.NET Core", 1.0f),
    ("TypeScript", 0.5f),
    ("JavaScript", 0.5f),
    ("Unit Tests", 0.8f),
    ("Tailwind", 0.2f),
    ("frontend", 0.5f),
    ("git", 0.2f),
    ("SqlServer", 0.8f),
    ("Java", 1.0f),
]);
var searchParams = new SearchParams(
    Tags: tags,
    TotalItemBudget: 8,
    ScoreLowerBound: 4);

await CvTemplate.Generate(new()
{
    IsDebug = isDebug,
    Model = new()
    {
        Name = new()
        {
            First = "Anton",
            Last = "Curmanschii",
        },
        CategorizedInfoLists = [
            new(Category.Technologies, [
                ".NET",
                "ASP.NET Core",
                "SQL Server",
                "TypeScript",
                "Git",
                // "Blazor",
                // "EF Core",
                // "ESB",
                // "AWS",
            ]),
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
        Profession = new("Backend Software Developer"),
        Educations = [
            new()
            {
                Place = new("Example University"),
                Title = "Master of Applied Informatics",
                DateRange = DateRange.Completed(
                    start: new(Year: 2022),
                    end: new(Year: 2024)),
                Text = new("""Thesis: \href{https://github.com/AntonC9018/thesis-png}{\textit{PNG File Format}}"""),
            },
            new()
            {
                Place = new("Example University"),
                Title = "Bachelor of Applied Informatics",
                DateRange = DateRange.Completed(
                    start: new(Year: 2019),
                    end: new(Year: 2022)),
                Text = new("""Thesis: \href{https://github.com/AntonC9018/uni_thesis}{\textit{Roslyn Code Generators}}"""),
            }],
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
        WorkExperiences = experienceDatabase.Experiences
            .Where(x => x.IsJob)
            .SelectEvents(searchParams),
        PersonalProjects = experienceDatabase.Experiences
            .Where(x => !x.IsJob)
            .SelectEvents(searchParams with
            {
                // TotalItemBudget = 5,
            }),
        SectionOrder = [
            Section.WorkExperience,
            Section.Education,
            Section.Languages,
            Section.PersonalProjects,
        ],
    },
    CancellationToken = cancellationToken,
    ConfigFilePath = configFullPath,
    OpenInOs = true,
});

return;

async Task SearchJobs(CancellationToken cancellationToken1)
{
    var theirStackClient = serviceProvider.GetRequiredService<TheirStackClient>();
    var result = await theirStackClient.JobsSearch_SearchJobs_V1Async(Format.Json, new()
    {
        // Eats up api tokens if this is not set.
        BlurCompanyData = true,

        Limit = 3,
        // Page = 1,
        Remote = true,
        MinSalaryUsd = 3000,
        JobSeniorityOr = [
            JobSeniorityOr.Junior,
            JobSeniorityOr.MidLevel,
        ],
        JobTitleOr = [
            // "('C#' | '.NET' | 'Unity' | 'C++') & (software | game) & (developer | engineer)",
            "Software Engineer",
            "Software Developer",
            "Game Developer",
            "DevOps Engineer",
            "Backend Engineer",
            "Backend Developer",
        ],
        PostedAtMaxAgeDays = 7,
    }, cancellationToken1);

    _ = result;
}

async Task GetModels(CancellationToken cancellationToken1)
{
    var modelClient = serviceProvider.GetRequiredService<OpenAIModelClient>();
    var models = await modelClient.GetModelsAsync(cancellationToken1);
    await using var file = File.Open("models", FileMode.Create, FileAccess.Write);
    await using var jsonWriter = new Utf8JsonWriter(file, new()
    {
        Indented = true,
    });
    ((IJsonModel<OpenAIModelCollection>) models.Value).Write(jsonWriter, ModelReaderWriterOptions.Json);
}
