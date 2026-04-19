using System.ClientModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Models;
using TheirStack;

internal static class AppConfiguration
{
    public static ValueTask<ServiceProvider> CreateApp(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddUserSecrets(typeof(Program).Assembly);

        var config = configBuilder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services
            .AddOptions<OpenApiOptions>()
            .Bind(config.GetRequiredSection(OpenApiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
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

        services
            .AddOptions<TheirStackOptions>()
            .Bind(config.GetRequiredSection(TheirStackOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services
            .AddHttpClient(nameof(TheirStackClient), (sp, client) =>
            {
                var opts = sp.GetRequiredService<IOptions<TheirStackOptions>>().Value;
                var headerValue = $"{opts.SecretKey}";
                client.DefaultRequestHeaders.Authorization = new(scheme: "Bearer", headerValue);
                _ = client;
            })
            .ConfigureAdditionalHttpMessageHandlers((list, sp) =>
            {
                list.Add(new LoggingHandler());
                _ = sp;
            });
        services
            .AddSingleton<TheirStackClient>(s =>
            {
                var httpClient = s.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(TheirStackClient));
                var ret = new TheirStackClient(httpClient);
                return ret;
            });

        services
            .AddOptions<PersonalInfoOptions>()
            .Bind(config.GetRequiredSection(PersonalInfoOptions.DefaultKey))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<PersonalInfoOptions>(p =>
        {
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

file class LoggingHandler : DelegatingHandler
{
    public LoggingHandler() : base()
    {
    }

    public LoggingHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Request:");
        Console.WriteLine(request.ToString());
        if (request.Content != null)
        {
            var r = await request.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine(r);
        }
        Console.WriteLine();

        var response = await base.SendAsync(request, cancellationToken);

        Console.WriteLine("Response:");
        Console.WriteLine(response.ToString());
        if (response.Content != null)
        {
            var r = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine(r);
        }
        Console.WriteLine();

        return response;
    }
}

public sealed class PersonalInfoOptions
{
    public const string DefaultKey = "PersonalInfo";

    [Phone]
    [Required]
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public required string Phone { get; set; }

    [EmailAddress]
    [Required]
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public required string Email { get; set; }
}
