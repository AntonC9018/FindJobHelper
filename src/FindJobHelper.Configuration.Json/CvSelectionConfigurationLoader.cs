using System.Text.Json;
using System.Text.Json.Serialization;
using FindJobHelper.Configuration;

namespace FindJobHelper.Configuration.Json;

public static class CvSelectionConfigurationLoader
{
    public static Task<CvSelectionConfiguration> LoadAsync(
        string filePath,
        CancellationToken cancellationToken) =>
        CvConfigurationJsonLoader.LoadAsync<JsonCvSelectionConfiguration, CvSelectionConfiguration>(
            filePath,
            static json => json.ToDomain(),
            cancellationToken);
}

internal static class CvConfigurationJsonLoader
{
    public static async Task<TDomain> LoadAsync<TJson, TDomain>(
        string filePath,
        Func<TJson, TDomain> map,
        CancellationToken cancellationToken)
        where TJson : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(map);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new CvConfigurationException($"Configuration file was not found: '{fullPath}'.");
        }

        try
        {
            await using var input = File.OpenRead(fullPath);
            var json = await JsonSerializer.DeserializeAsync<TJson>(
                input,
                JsonOptions,
                cancellationToken);
            if (json is null)
            {
                throw new CvConfigurationException("The configuration file must contain a JSON object.");
            }

            return map(json);
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
