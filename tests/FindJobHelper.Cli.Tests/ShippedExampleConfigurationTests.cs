using FindJobHelper.Configuration.Json;

namespace MainCli.Tests;

public sealed class ShippedExampleConfigurationTests
{
    [Fact]
    public async Task LoadAsync_ParsesShippedExampleConfiguration()
    {
        var configuration = await CvSelectionConfigurationLoader.LoadAsync(
            CvGenerationCommand.ExampleConfigPath,
            CancellationToken.None);

        Assert.NotNull(configuration);
    }
}
