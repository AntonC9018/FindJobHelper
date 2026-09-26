using FindJobHelper.Configuration;

namespace FindJobHelper.Configuration.Json;

public static class MasterCvConfigurationLoader
{
    public static Task<MasterCvConfiguration> LoadAsync(
        string filePath,
        CancellationToken cancellationToken) =>
        CvConfigurationJsonLoader.LoadAsync<JsonMasterCvConfiguration, MasterCvConfiguration>(
            filePath,
            static json => json.ToDomain(),
            cancellationToken);
}
