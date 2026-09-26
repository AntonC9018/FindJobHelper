using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using FindJobHelper.CVGeneration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FindJobHelper.Generation;

public static class CvGenerationAppConfiguration
{
    public static ValueTask<ServiceProvider> CreateApp(
        string? userSecretsId,
        string? workspaceConfigPath,
        LatexExecutablePaths latexExecutables,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var configBuilder = new ConfigurationBuilder();
        if (!string.IsNullOrWhiteSpace(userSecretsId))
        {
            configBuilder.AddUserSecrets(userSecretsId, reloadOnChange: false);
        }

        // Personal info resolves as environment variable, then the workspace
        // config file, then user secrets (ADR 0003). Later sources win in the
        // configuration chain, so the file lands between the secrets and the
        // environment variables. The JSON configuration provider tolerates
        // the file's comments and trailing commas (ADR 0002).
        if (workspaceConfigPath is not null && File.Exists(workspaceConfigPath))
        {
            configBuilder.AddJsonFile(workspaceConfigPath, optional: false, reloadOnChange: false);
        }

        configBuilder.AddEnvironmentVariables();

        var config = configBuilder.Build();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(latexExecutables);
        services
            .AddOptions<PersonalInfoOptions>()
            .Bind(config.GetRequiredSection(PersonalInfoOptions.DefaultKey))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<LatexMeasurementService>();

        var serviceProvider = services.BuildServiceProvider();
        return ValueTask.FromResult(serviceProvider);
    }
}

public sealed class PersonalInfoOptions
{
    public const string DefaultKey = "PersonalInfo";

    [Required]
    public required string FirstName { get; set; }

    [Required]
    public required string LastName { get; set; }

    public string? Profession { get; set; }

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

    public string? GitHub { get; set; }

    public string? LinkedIn { get; set; }

    public string? YouTube { get; set; }

    public string? Portfolio { get; set; }
}
