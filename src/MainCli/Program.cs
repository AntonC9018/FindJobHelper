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
        Educations = [
            new()
            {
                Place = new("Example University"),
                Title = new("Master of Applied Informatics"),
                DateRange = DateRange.Completed(
                    start: new(Year: 2022),
                    end: new(Year: 2024)),
                Text = new("""Thesis: \href{https://github.com/AntonC9018/thesis-png}{\textit{PNG File Format}}"""),
            },
            new()
            {
                Place = new("Example University"),
                Title = new("Bachelor of Applied Informatics"),
                DateRange = DateRange.Completed(
                    start: new(Year: 2019),
                    end: new(Year: 2022)),
                Text = new("""Thesis: \href{https://github.com/AntonC9018/uni_thesis}{\textit{Roslyn Code Generators}}"""),
            }],
        Languages = [],
        Location = location,
        Summary = NullableLatexString.Null,
        WorkExperiences = [
            new()
            {
                Place = new("Example University"),
                Title = new("University Tutor"),
                DateRange = DateRange.Current(
                    start: new(Year: 2023)),
                Text = new("I teach algorithms, data structures, and C++ programming to students. My lessons are public on YouTube."),
                SubItems = [
                ],
                Urls = [
                    "https://www.youtube.com/@antonofka9018/playlists",
                    "https://github.com/AntonC9018/uniCourse_dataStructuresAndAlgorithms",
                ],
            },
            new()
            {
                Title = ".NET Backend Developer",
                Place = new("ExampleCo Beta"),
                DateRange = DateRange.Completed(
                    start: new(Year: 2024, Month: 7),
                    end: new(Year: 2024, Month: 9, Day: 14)),
                SubItems = [
                    new(@"Made REST API's that handle the app's logic"),
                    new(@"Configured its \textbf{\textit{build process}} (\textit{MSBuild, Docker}, custom CLI tool)."),
                    new(@"Integrated \textbf{AWS} services (\textit{Cognito, Secrets Manager})."),
                    new(@"Made a module that wraps all API responses automatically."),
                    new(@"Made a module that propagates metadata all the way from the database model to the DTO validation and Swagger."),
                    new(@"Practiced errors-as-values everywhere."),
                ],
            },
            new()
            {
                Title = ".NET Developer",
                Place = new("Example Foundation"),
                DateRange = DateRange.Completed(
                    start: new(Year: 2023, Month: 12),
                    end: new(Year: 2024, Month: 2)),
                SubItems = [
                    new(@"Worked on \textbf{HotChocolate}. Fixed a couple of old issues, worked on \textbf{Apollo} integration."),
                    new(@"Started developing an internal microservice."),
                ],
            },
            new()
            {
                Title = ".NET Backend Developer",
                Place = new("ExampleCo Alpha"),
                DateRange = DateRange.Completed(
                    start: new(Year: 2023, Month: 4),
                    end: new(Year: 2023, Month: 11)),
                SubItems = [
                    new(@"Worked on the backend of \href{https://ExampleCo Alpha.com/}{ExampleCo Alpha}, a project management application with an ASP.NET Core backend."),
                    new(@"Moved the project back to \textbf{EF Core migrations}, designed a test that checks if the models actually correspond to database tables."),
                    new(@"Received a fair bit of \textbf{SQL experience} by writing complex queries in code and for migrations."),
                    new(@"Implemented \textbf{bulk updates} using \textit{Linq2db}."),
                    new(@"Added \textbf{GraphQL support using HotChocolate}. Used the Code-First approach heavily to generalize configuration across types. Wrote a \href{https://dev.azure.com/ExampleCo Alpha-inc/ExampleCo Alpha.Public/_git/ExampleCo Alpha.HotChocolate.GlobalFilters}{library that automatically adds ownership filters} to all queries."),
                    new(@"Implemented multiple \textbf{source generators} for easier project maintenance."),
                    new(@"Designed and implemented \textbf{multiple application features from scratch}."),
                    new(@"Undertook numerous \textbf{refactorings of the legacy code base}."),
                    new(@"Implemented a \textbf{User Hierarchy module} that automatically updates references to users in hierarchies."),
                    new(@"Implemented an \textbf{Excel Micro-ORM} with a complex column format configurable through a builder."),
                    new(@"Implemented a \textbf{Metel parser} using \textit{System.IO.Pipelines}."),
                ],
            },
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
