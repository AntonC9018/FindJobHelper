using System.ClientModel.Primitives;
using System.Collections.Immutable;
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

var configFullPath = Path.GetFullPath("data/cv_template_config.tex");
var location = new Location(City: "Chișinău", Country: "Moldova");
var experienceDatabase = ExperienceDatabaseFactory.Create();
await CvTemplate.Generate(new()
{
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
                "Blazor",
                // "EF Core",
                "ESB",
                "AWS",
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
        WorkExperiences = [
            .. experienceDatabase.Experiences
                .Where(x => x.IsJob)
                .OrderByDescending(x => x.DateRange, DateRangeComparer.ByEnd)
                .Select(x =>
                {
                    // var items = x.Items
                    //     .Select(i => (Item: i, ScoreSum: i.Tags.Sum(t => t.Score)))
                    //     // average per tag
                    //     .OrderBy(i => (float) i.ScoreSum / i.Item.Tags.Length)
                    //     // number of tags
                    //     .ThenBy(i => i.Item.Tags.Length)
                    //     .Select(i => i.Item)
                    //     .TopologicalSort(i => i.MustBeAfter)
                    //     .Take(2);
                    // var items = x.Items
                    //     .Select(i => (Item: i, RelevantTags: i.Tags.Where(t =>
                    //     {
                    //         return t.Tag.Is(".NET")
                    //             || t.Tag.Is("AWS")
                    //             || t.Tag.Is("ASP.NET Core");
                    //
                    //     }).ToArray()))
                    //     .Where(i => i.RelevantTags.Length > 0)
                    //     .OrderByDescending(i => i.RelevantTags.Sum(t => t.Score))
                    //     .ThenByDescending(i => i.RelevantTags.Length)
                    //     .ThenByDescending(i => i.Item.Tags.Length)
                    //     .Select(i => i.Item)
                    //     .TopologicalSort(i => i.MustBeAfter)
                    //     .Take(3);

                    var items = x.Items;
                    return new Event
                    {
                        Place = x.Place,
                        Title = x.Title,
                        DateRange = x.DateRange,
                        SubItems = [.. items.Select(i => i.Text)],
                        Text = x.Description,
                        Urls = x.Urls,
                    };
                }),
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
