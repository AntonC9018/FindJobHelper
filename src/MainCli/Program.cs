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

var configFullPath = Path.GetFullPath("data/cv_template_config.tex");
var location = new Location(City: "Chișinău", Country: "Moldova");
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
                new(".NET"),
                new("ASP.NET Core"),
                new("EF Core"),
                new("AWS"),
            ]),
            new(Category.GitHub, [
                new("https://github.com/AntonC9018"),
            ]),
            new(Category.LinkedIn, [
                new("https://www.linkedin.com/in/anton-curmanschii-647232161"),
            ]),
        ],
        CategorizedInfos = [
            new(Category.Location, location.FormatInfo()),
            new(Category.Email, new(personalInfo.Email)),
            new(Category.Phone, new(Miscellanious.BlurPhone(new()
            {
                String = personalInfo.Phone,
            }))),
        ],
        Profession = new("Backend Software Developer"),
        Educations = [],
        Languages = [],
        Location = location,
        Summary = NullableLatexString.Null,
        WorkExperiences = [],
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
