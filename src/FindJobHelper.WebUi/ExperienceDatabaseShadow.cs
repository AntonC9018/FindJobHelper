using System.Security.Cryptography;
using System.Text;

namespace FindJobHelper.WebUi;

/// <summary>
/// Copies the experience database directory to a content-hashed shadow path
/// before loading it. The pipeline maps the DLL into the process, which locks
/// the file on Windows; loading a copy lets the user rebuild the original at
/// any time. The hash covers every file in the source directory, so a rebuilt
/// database or a changed dependency lands in its own shadow directory.
/// Dependency assemblies, the <c>.deps.json</c> and native runtimes are
/// copied alongside the main assembly so
/// <c>ExperienceDatabaseAssemblyLoadContext</c> can resolve them through
/// <c>AssemblyDependencyResolver</c>; only the engine assemblies shared with
/// the host are loaded from the WebUi itself.
/// Shared by generation (<see cref="GenerationJobManager"/>) and tag-name
/// completion (<see cref="ConfigEditor"/>).
/// </summary>
internal static class ExperienceDatabaseShadow
{
    private const string CompleteMarkerName = ".find-job-webui-complete";

    public static string Copy(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var sourceDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException($"Experience database path '{databasePath}' has no directory.");
        var hashPrefix = HashDirectory(sourceDirectory)[..16];
        var shadowDirectory = Path.Combine(
            Path.GetTempPath(),
            "find-job-webui",
            "experience-database",
            hashPrefix);
        var shadowPath = Path.Combine(shadowDirectory, Path.GetFileName(fullPath));
        if (ShadowIsComplete(shadowDirectory, hashPrefix))
        {
            return shadowPath;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                CopyDirectoryContents(sourceDirectory, shadowDirectory);
                WriteCompleteMarker(shadowDirectory, hashPrefix);
                return shadowPath;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>
    /// The directory name already pins the exact source content; the marker
    /// additionally distinguishes a finished copy from one abandoned by a
    /// crash mid-copy, so an incomplete directory is refilled instead of
    /// loaded. Complete directories are reused as-is, which is what keeps
    /// files mapped by an earlier load from being overwritten.
    /// </summary>
    private static bool ShadowIsComplete(string shadowDirectory, string hashPrefix)
    {
        var markerPath = Path.Combine(shadowDirectory, CompleteMarkerName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        try
        {
            return File.ReadAllText(markerPath) == hashPrefix;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void WriteCompleteMarker(string shadowDirectory, string hashPrefix) =>
        File.WriteAllText(Path.Combine(shadowDirectory, CompleteMarkerName), hashPrefix);

    /// <summary>
    /// Hashes every file in the directory; the sorted relative path and the
    /// content length prefix each file's bytes, so different directory trees
    /// cannot produce the same digest.
    /// </summary>
    private static string HashDirectory(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => (RelativePath: Path.GetRelativePath(directory, path), Path: path))
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
        foreach (var (relativePath, path) in files)
        {
            AppendWithLength(hash, Encoding.UTF8.GetBytes(relativePath));
            AppendWithLength(hash, File.ReadAllBytes(path));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendWithLength(IncrementalHash hash, byte[] data)
    {
        hash.AppendData(BitConverter.GetBytes(data.Length));
        hash.AppendData(data);
    }

    private static void CopyDirectoryContents(string sourceDirectory, string shadowDirectory)
    {
        var files = Directory
            .EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(path => Path.GetRelativePath(sourceDirectory, path), StringComparer.Ordinal);
        foreach (var sourceFile in files)
        {
            var destinationFile = Path.Combine(
                shadowDirectory,
                Path.GetRelativePath(sourceDirectory, sourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            if (FilesHaveSameContent(sourceFile, destinationFile))
            {
                continue;
            }

            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }

    /// <summary>
    /// Skips copying when the destination already holds identical bytes, so a
    /// concurrent copy of the same content or a refill after a crash never
    /// overwrites a file the loader may already have mapped.
    /// </summary>
    private static bool FilesHaveSameContent(string sourceFile, string destinationFile)
    {
        if (!File.Exists(destinationFile))
        {
            return false;
        }

        const int bufferSize = 64 * 1024;
        using var source = File.OpenRead(sourceFile);
        using var destination = File.OpenRead(destinationFile);
        if (source.Length != destination.Length)
        {
            return false;
        }

        var sourceBuffer = new byte[bufferSize];
        var destinationBuffer = new byte[bufferSize];
        int read;
        while ((read = source.ReadAtLeast(sourceBuffer, bufferSize, throwOnEndOfStream: false)) > 0)
        {
            destination.ReadExactly(destinationBuffer.AsSpan(0, read));
            if (!sourceBuffer.AsSpan(0, read).SequenceEqual(destinationBuffer.AsSpan(0, read)))
            {
                return false;
            }
        }

        return true;
    }
}
