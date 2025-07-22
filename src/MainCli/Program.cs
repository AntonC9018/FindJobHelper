using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Models;
using TheirStack;
#pragma warning disable CS8321 // Local function is declared but never used

await using var serviceProvider = await AppConfiguration.CreateApp();
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
});



async Task GetModels()
{
    var modelClient = serviceProvider.GetRequiredService<OpenAIModelClient>();
    var models = await modelClient.GetModelsAsync();
    await using var file = File.Open("models", FileMode.Create, FileAccess.Write);
    await using var jsonWriter = new Utf8JsonWriter(file, new()
    {
        Indented = true,
    });
    ((IJsonModel<OpenAIModelCollection>) models.Value).Write(jsonWriter, ModelReaderWriterOptions.Json);
}
