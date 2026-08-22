internal sealed record ResolvedLatexExecutables(
    string SelectionSource,
    string Directory,
    FindJobHelper.CVGeneration.LatexExecutablePaths Paths);

internal static class LatexBinaryDirectoryResolver
{
    public const string EnvironmentVariable = "FINDJOBHELPER_LATEX_BIN_DIRECTORY";
    public const string TeXLiveRootEnvironmentVariable = "FINDJOBHELPER_TEXLIVE_ROOT";
    public const string TeXLiveYear = "2026";

    public static ResolvedLatexExecutables Resolve(string? commandLineDirectory)
    {
        if (!string.IsNullOrWhiteSpace(commandLineDirectory))
        {
            return FromDirectory("--latex-bin-directory", commandLineDirectory);
        }

        var environmentDirectory = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(environmentDirectory))
        {
            return FromDirectory(EnvironmentVariable, environmentDirectory);
        }

        var localRoot = Environment.GetEnvironmentVariable(TeXLiveRootEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            localRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share",
                "findjobhelper",
                "texlive",
                TeXLiveYear);
        }

        var binRoot = Path.Combine(localRoot, "bin");
        if (Directory.Exists(binRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(binRoot)
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                var result = TryResolveFromDirectory(
                    "default local TeX Live 2026 installation",
                    Path.GetFullPath(directory));
                if (result is not null)
                {
                    return result;
                }
            }
        }

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in pathDirectories)
        {
            var fullDirectory = TryNormalizePathDirectory(directory);
            if (fullDirectory is null)
            {
                continue;
            }

            var result = TryResolveFromDirectory(
                "PATH environment variable",
                fullDirectory);
            if (result is not null)
            {
                return result;
            }
        }

        throw new InvalidOperationException(
            "Could not find one directory containing both latexmk and xelatex. Run scripts/setup-latex.sh or specify --latex-bin-directory.");
    }

    internal static string? TryNormalizePathDirectory(string directory)
    {
        try
        {
            return Path.GetFullPath(directory);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or PathTooLongException
                or NotSupportedException)
        {
            return null;
        }
    }

    private static ResolvedLatexExecutables FromDirectory(
        string selectionSource,
        string directory)
    {
        var fullDirectory = Path.GetFullPath(directory);
        var result = TryResolveFromDirectory(selectionSource, fullDirectory);
        if (result is not null)
        {
            return result;
        }

        throw new InvalidOperationException(
            $"LaTeX binary directory '{fullDirectory}' selected by {selectionSource} must contain both latexmk and xelatex.");
    }

    private static ResolvedLatexExecutables? TryResolveFromDirectory(
        string selectionSource,
        string normalizedDirectory)
    {
        var latexmk = ResolveExecutable(normalizedDirectory, "latexmk");
        var xelatex = ResolveExecutable(normalizedDirectory, "xelatex");
        if (latexmk is null)
        {
            return null;
        }
        if (xelatex is null)
        {
            return null;
        }

        var executables =
            new FindJobHelper.CVGeneration.LatexExecutablePaths(latexmk, xelatex);
        return new(
            selectionSource,
            normalizedDirectory,
            executables);
    }

    private static string? ResolveExecutable(string directory, string name)
    {
        foreach (var extension in OperatingSystem.IsWindows()
                     ? new[] { ".exe", ".cmd", ".bat", string.Empty }
                     : new[] { string.Empty })
        {
            var path = Path.Combine(directory, name + extension);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
