using System.Text.Json;
using System.Text.Json.Serialization;
using FindJobHelper.Configuration;

namespace FindJobHelper.Configuration.Json;

public static class CvSelectionConfigurationLoader
{
    public static async Task<CvSelectionConfiguration> LoadAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new CvConfigurationException($"Configuration file was not found: '{fullPath}'.");
        }

        try
        {
            await using var input = File.OpenRead(fullPath);
            var json = await JsonSerializer.DeserializeAsync<JsonCvSelectionConfiguration>(
                input,
                JsonOptions,
                cancellationToken);
            if (json is null)
            {
                throw new CvConfigurationException("The configuration file must contain a JSON object.");
            }

            return json.ToDomain();
        }
        catch (JsonException ex)
        {
            throw new CvConfigurationException(
                $"Configuration file '{fullPath}' is invalid: {ex.Message}",
                ex);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<Section>(allowIntegerValues: false),
        },
    };
}
