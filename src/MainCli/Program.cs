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
isDebug = true;
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


var educationKey = new ExperienceKey("Education");
var workKey = new ExperienceKey("Work");
var personalProjectsKey = new ExperienceKey("PersonalProjects");

var searchBuilder = new SearchBuilder();
searchBuilder.Tags(weightedTags);
// Also allow it through an extension
// searchBuilder.WeightedTags(tagsDatabase, [
//     (tags.dotnet, 1.0f),
//     (tags.restApi, 0.6f),
//     (tags.sql, 1.0f),
//     (tags.sqlServer, 0.8f),
//     (tags.aws, 0.2f),
//     (tags.azure, 0.2f),
// ]);

searchBuilder.ConfigureDefaults(opts =>
{
    opts.TotalItemBudget = 3;
    opts.ScoreLowerBound = 1f;
});
searchBuilder.Configure(
    educationKey,
    predicate: e => e.Type.IsDegree(),
    opts =>
    {
        opts.TotalItemBudget = 3;
        opts.ScoreLowerBound = 1;
    });
searchBuilder.Configure(
    workKey,
    predicate: e => e.Type == ExperienceType.Job,
    opts =>
    {
        opts.TotalItemBudget = 6;
        opts.ScoreLowerBound = 1;
    });
searchBuilder.Configure(
    personalProjectsKey,
    predicate: e => e.Type == ExperienceType.Project,
    opts =>
    {
        opts.TotalItemBudget = 6;
        opts.ScoreLowerBound = 1;
    });
var search = searchBuilder.Build();
var searchResult = search.Run(experienceDatabase.Experiences);

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
        Educations = searchResult.Get(educationKey),
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
        WorkExperiences = searchResult.Get(workKey),
        PersonalProjects = searchResult.Get(personalProjectsKey),
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
