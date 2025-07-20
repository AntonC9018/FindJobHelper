using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Models;

var serviceProvider = await AppConfiguration.CreateApp();
var modelClient = serviceProvider.GetRequiredService<OpenAIModelClient>();
var models = await modelClient.GetModelsAsync();
await using var file = File.Open("models", FileMode.Create, FileAccess.Write);
await using var jsonWriter = new Utf8JsonWriter(file, new()
{
    Indented = true,
});
((IJsonModel<OpenAIModelCollection>) models.Value).Write(jsonWriter, ModelReaderWriterOptions.Json);


