using System.ClientModel;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Models;

internal static class AppConfiguration
{
    public static ValueTask<ServiceProvider> CreateApp()
    {
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddUserSecrets(typeof(Program).Assembly);

        var config = configBuilder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services
            .AddOptions<OpenApiOptions>()
            .Bind(config.GetRequiredSection(OpenApiOptions.SectionName))
            .ValidateDataAnnotations();
        services
            .AddTransient<ApiKeyCredential>(static services =>
            {
                var options = services.GetRequiredService<IOptions<OpenApiOptions>>().Value;
                return new ApiKeyCredential(options.SecretKey);
            });
        services
            .AddOptions<OpenAIClientOptions>(nameof(OpenAIModelClient))
            .Configure(opts =>
            {
                _ = opts;
            })
            .RegisterOptionsValueAsService();

        // Not sure if this can be reused.
        services.AddTransient<OpenAIModelClient>(s =>
        {
            var clientOptions = s.GetRequiredKeyedService<OpenAIClientOptions>(nameof(OpenAIModelClient));
            var apiKeyCredential = s.GetRequiredService<ApiKeyCredential>();
            return new OpenAIModelClient(apiKeyCredential, clientOptions);
        });

        services
            .AddOptions<JsonSerializerOptions>()
            .Configure(opts =>
            {
                opts.WriteIndented = true;
            });

        var ret = services.BuildServiceProvider();
        return ValueTask.FromResult(ret);
    }
}

public sealed class JsonWriterOptionsClass
{
    private JsonWriterOptions _options;

    public JsonWriterOptionsClass(JsonWriterOptions options)
    {
        _options = options;
    }

    public ref JsonWriterOptions Options
    {
        get => ref _options;
    }
}
