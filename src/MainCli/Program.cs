using System.ClientModel.Primitives;
using System.Text.Json;
using FindJobHelper.Core;
using FindJobHelper.Core.Helper;
using FindJobHelper.CVGeneration;
using MainCli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Models;
using TheirStack;
using Location = FindJobHelper.CVGeneration.Location;

#pragma warning disable CS8321 // Local function is declared but never used

var cancellationToken = CancellationToken.None;
await using var serviceProvider = await AppConfiguration.CreateApp(cancellationToken);
_ = serviceProvider;

var personalInfo = serviceProvider.GetRequiredService<IOptions<PersonalInfoOptions>>().Value;
// ReSharper disable once RedundantAssignment
bool isDebug;
// isDebug = true;
isDebug = false;
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
var (tags, tagsDatabase) = TagsDatabaseFactory.Create();
_ = tagsDatabase;
var experienceDatabase = ExperienceDatabaseFactory.Create(tags);

var weightedTags = tagsDatabase.Weighted([
    (tags.dotnet, 1.0f),
    (tags.restApi, 0.6f),
    (tags.sql, 1.0f),
    (tags.sqlServer, 0.8f),
    (tags.aws, 0.2f),
    (tags.azure, 0.2f),
]);
var searchParams = new SearchParams(
    Tags: weightedTags,
    TotalItemBudget: 8,
    ScoreLowerBound: 4.5f);
var searchParamsPersonal = searchParams with
{
    TotalItemBudget = 3,
    ScoreLowerBound = 1f,
};
string[] technologies = [
    ".NET",
    "ASP.NET Core",
    // "SQL",
    // "EF Core",
    // "Docker",
    "SQL Server",
    // "ADO.NET",
    // "TypeScript",
    // "Git",
    // "Blazor",
    // "EF Core",
    // "ESB",
    // "AWS",
];

_ = searchParams;
_ = searchParamsPersonal;

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
            new(Category.Technologies, [.. technologies]),
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
        Educations = [
            .. experienceDatabase.Experiences
                .Where(x => x.Type.IsDegree())
                .SelectEvents(searchParams),
        ],
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
            .Where(x => x.Type == ExperienceType.Job)
            // .AllEvents()
            .SelectEvents(searchParams)
        ,
        PersonalProjects = experienceDatabase.Experiences
            .Where(x => x.Type == ExperienceType.Project)
            // .Where(x => x.Title == "Dual-database full-stack app in Go")
            // .AllEvents()
            .SelectEvents(searchParamsPersonal)
        ,
        SectionOrder = [
            // Section.Languages,
            Section.WorkExperience,
            Section.PersonalProjects,
            Section.Education,
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
