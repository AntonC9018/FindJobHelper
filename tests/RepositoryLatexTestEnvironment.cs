using System.Runtime.CompilerServices;

internal static class RepositoryLatexTestEnvironment
{
    [ModuleInitializer]
#pragma warning disable CA2255 // Test assemblies intentionally configure their subprocess environment at load time.
    internal static void Configure()
#pragma warning restore CA2255
    {
        var repositoryRoot = FindRepositoryRoot();
        if (repositoryRoot is null)
        {
            return;
        }

        var texLiveRoot = Path.Combine(repositoryRoot, ".tools", "texlive", "2026");
        if (!Directory.Exists(texLiveRoot))
        {
            return;
        }

        var binRoot = Path.Combine(texLiveRoot, "bin");
        if (!Directory.Exists(binRoot))
        {
            throw IncompleteInstallation(repositoryRoot);
        }
        var platformDirectories = Directory.GetDirectories(binRoot);
        var matchingBinDirectories = platformDirectories
            .Where(IsLatexBinDirectory)
            .ToArray();
        if (matchingBinDirectories.Length != 1)
        {
            throw IncompleteInstallation(repositoryRoot);
        }
        var binDirectory = matchingBinDirectories[0];

        var fontconfigFile = Path.Combine(texLiveRoot, "findjobhelper-fontconfig.conf");
        if (!File.Exists(fontconfigFile))
        {
            throw IncompleteInstallation(repositoryRoot);
        }

        var path = Environment.GetEnvironmentVariable("PATH");
        var updatedPath = binDirectory;
        if (!string.IsNullOrEmpty(path))
        {
            updatedPath = $"{binDirectory}{Path.PathSeparator}{path}";
        }
        Environment.SetEnvironmentVariable("PATH", updatedPath);
        Environment.SetEnvironmentVariable("FONTCONFIG_FILE", fontconfigFile);
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var setupScript = Path.Combine(directory.FullName, "scripts", "setup-tests.sh");
            if (File.Exists(setupScript))
            {
                return directory.FullName;
            }
            directory = directory.Parent;
        }
        return null;
    }

    private static bool IsLatexBinDirectory(string directory)
        => File.Exists(Path.Combine(directory, "latexmk"))
            && File.Exists(Path.Combine(directory, "xelatex"));

    private static InvalidOperationException IncompleteInstallation(string repositoryRoot)
        => new(
            $"The repository-local LaTeX installation is incomplete. "
            + $"Run {Path.Combine(repositoryRoot, "scripts", "setup-tests.sh")} and retry.");
}
