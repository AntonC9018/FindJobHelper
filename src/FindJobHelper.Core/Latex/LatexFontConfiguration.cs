using System.Globalization;

namespace FindJobHelper.CVGeneration;

public sealed record LatexFontConfigurationValues(
    LatexFontRoleArray<string?> Families,
    LatexFontRoleArray<string?> Scales);

public sealed record ResolvedLatexFontConfiguration(
    LatexFontOptions Options,
    LatexFontRoleArray<bool> ManuallySpecified);

public sealed class LatexFontConfigurationException(string message) : Exception(message);

public static class LatexFontConfigurationResolver
{
    public static LatexFontRoleArray<LatexFontSetting> FamilySettings { get; } = new(
        main: new(
            Role: LatexFontRole.Main,
            FlagName: "--main-font",
            EnvironmentVariable: "CV_MAIN_FONT"),
        sans: new(
            Role: LatexFontRole.Sans,
            FlagName: "--sans-font",
            EnvironmentVariable: "CV_SANS_FONT"),
        monospace: new(
            Role: LatexFontRole.Mono,
            FlagName: "--mono-font",
            EnvironmentVariable: "CV_MONO_FONT"));

    public static LatexFontRoleArray<LatexFontSetting> ScaleSettings { get; } = new(
        main: new(
            Role: LatexFontRole.Main,
            FlagName: "--main-font-size",
            EnvironmentVariable: "CV_MAIN_FONT_SIZE"),
        sans: new(
            Role: LatexFontRole.Sans,
            FlagName: "--sans-font-size",
            EnvironmentVariable: "CV_SANS_FONT_SIZE"),
        monospace: new(
            Role: LatexFontRole.Mono,
            FlagName: "--mono-font-size",
            EnvironmentVariable: "CV_MONO_FONT_SIZE"));

    public static LatexFontConfigurationValues GetEnvironmentValues()
    {
        var families = FamilySettings.Map(
            static setting => Environment.GetEnvironmentVariable(setting.EnvironmentVariable));
        var scales = ScaleSettings.Map(
            static setting => Environment.GetEnvironmentVariable(setting.EnvironmentVariable));
        return new(Families: families, Scales: scales);
    }

    public static ResolvedLatexFontConfiguration Resolve(
        LatexFontConfigurationValues flags,
        LatexFontConfigurationValues environments)
    {
        var resolvedFamilies = ResolveFamilies(
            flags: flags.Families,
            environments: environments.Families);
        var families = resolvedFamilies.Map(static resolved => resolved.Family);
        var scales = ResolveScales(
            flags: flags.Scales,
            environments: environments.Scales);
        var options = new LatexFontOptions(
            families: families,
            scales: scales);
        var manuallySpecified = resolvedFamilies.Map(
            static resolved => resolved.ManuallySpecified);
        return new(Options: options, ManuallySpecified: manuallySpecified);
    }

    private static LatexFontRoleArray<ResolvedFontFamily> ResolveFamilies(
        LatexFontRoleArray<string?> flags,
        LatexFontRoleArray<string?> environments)
    {
        ResolvedFontFamily ResolveRole(LatexFontRole role)
        {
            var flag = flags[role];
            var environment = environments[role];
            var defaultValue = LatexFontOptions.Default.Families[role];
            var setting = FamilySettings[role];
            return ResolveFamilyRole(
                flag: flag,
                environment: environment,
                defaultValue: defaultValue,
                setting: setting);
        }

        return LatexFontRoleArray<ResolvedFontFamily>.Create(ResolveRole);
    }

    private static LatexFontRoleArray<LatexFontScale?> ResolveScales(
        LatexFontRoleArray<string?> flags,
        LatexFontRoleArray<string?> environments)
    {
        LatexFontScale? ResolveRole(LatexFontRole role)
        {
            var flag = flags[role];
            var environment = environments[role];
            var defaultValue = LatexFontOptions.Default.Scales[role];
            var setting = ScaleSettings[role];
            return ResolveScaleRole(
                flag: flag,
                environment: environment,
                defaultValue: defaultValue,
                setting: setting);
        }

        return LatexFontRoleArray<LatexFontScale?>.Create(ResolveRole);
    }

    private static ResolvedFontFamily ResolveFamilyRole(
        string? flag,
        string? environment,
        LatexFontFamilyName defaultValue,
        LatexFontSetting setting)
    {
        var value = flag ?? environment;
        if (value is null)
        {
            return new(
                Family: defaultValue,
                ManuallySpecified: false);
        }
        var source = flag is not null ? setting.FlagName : setting.EnvironmentVariable;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LatexFontConfigurationException($"{source} must not be blank.");
        }
        try
        {
            var family = new LatexFontFamilyName(value);
            return new(
                Family: family,
                ManuallySpecified: true);
        }
        catch (ArgumentException exception)
        {
            throw new LatexFontConfigurationException($"Invalid value for {source}: {exception.Message}");
        }
    }

    private static LatexFontScale? ResolveScaleRole(
        string? flag,
        string? environment,
        LatexFontScale? defaultValue,
        LatexFontSetting setting)
    {
        var value = flag ?? environment;
        if (value is null)
        {
            return defaultValue;
        }

        var source = flag is not null ? setting.FlagName : setting.EnvironmentVariable;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LatexFontConfigurationException($"{source} must not be blank.");
        }

        var parsedSuccessfully = double.TryParse(
            value,
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsedValue);
        if (!parsedSuccessfully)
        {
            throw new LatexFontConfigurationException($"{source} must be a number using invariant decimal notation.");
        }

        try
        {
            return new(parsedValue);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new LatexFontConfigurationException($"{source} must be positive and finite.");
        }
    }

    private sealed record ResolvedFontFamily(
        LatexFontFamilyName Family,
        bool ManuallySpecified);

}

public sealed record LatexFontSetting(
    LatexFontRole Role,
    string FlagName,
    string EnvironmentVariable);
