using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.Json;
using FindJobHelper.CVGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

internal static class AppConfiguration
{
    public static ValueTask<ServiceProvider> CreateApp(
        LatexExecutablePaths latexExecutables,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddUserSecrets(typeof(Program).Assembly);
        configBuilder.AddEnvironmentVariables();

        var config = configBuilder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(latexExecutables);
        services
            .AddOptions<JsonSerializerOptions>()
            .Configure(opts =>
            {
                opts.WriteIndented = true;
            });

        services
            .AddOptions<PersonalInfoOptions>()
            .Bind(config.GetRequiredSection(PersonalInfoOptions.DefaultKey))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<PersonalInfoOptions>(p =>
        {
        });

        services.AddSingleton<LatexMeasurementService>();

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

    [Required]
    public required string FirstName { get; set; }

    [Required]
    public required string LastName { get; set; }

    [Required]
    public required string Profession { get; set; }

    [Required]
    public required string City { get; set; }

    [Required]
    public required string Country { get; set; }

    [Phone]
    [Required]
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public required string Phone { get; set; }

    [EmailAddress]
    [Required]
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public required string Email { get; set; }

    [Url]
    [Required]
    public required string GitHub { get; set; }

    [Url]
    [Required]
    public required string LinkedIn { get; set; }
}
