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
/// the host are loaded from the WebUi itself. The completion marker is only
/// written after a re-hash confirms the source still matches the directory
/// name, so a rebuild racing the copy never marks mixed content as complete.
/// Shared by generation (<see cref="GenerationJobManager"/>) and tag-name
/// completion (<see cref="ConfigEditor"/>).
/// </summary>
internal static class ExperienceDatabaseShadow
{
    private const string CompleteMarkerName = ".find-job-webui-complete";

    public static string Copy(string databasePath)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var mainFileName = Path.GetFileName(fullPath);
        var sourceDirectory = Path.GetDirectoryName(fullPath)
            ?? throw new IOException($"Experience database path '{databasePath}' has no directory.");

        for (var attempt = 1; ; attempt++)
        {
            var hashPrefix = HashDirectory(sourceDirectory)[..16];
            var shadowDirectory = Path.Combine(
                Path.GetTempPath(),
                "find-job-webui",
                "experience-database",
                hashPrefix);
            var shadowPath = Path.Combine(shadowDirectory, mainFileName);
            if (ShadowIsComplete(shadowDirectory, hashPrefix))
            {
                return shadowPath;
            }

            try
            {
                CopyDirectoryContents(sourceDirectory, shadowDirectory);
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(200);
                continue;
            }

            var hashAfterCopy = HashDirectory(sourceDirectory)[..16];
            if (hashAfterCopy == hashPrefix)
            {
                WriteCompleteMarker(shadowDirectory, hashPrefix);
                return shadowPath;
            }

            // The source changed mid-copy, so the directory may hold mixed
            // content. Leave it unmarked, which keeps it from ever being
            // reused as complete, and retry against the new content.
            if (attempt >= 5)
            {
                return shadowPath;
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

        string markerContent;
        try
        {
            markerContent = File.ReadAllText(markerPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (markerContent == hashPrefix)
        {
            return true;
        }

        return false;
    }

    private static void WriteCompleteMarker(string shadowDirectory, string hashPrefix)
    {
        var markerPath = Path.Combine(shadowDirectory, CompleteMarkerName);
        File.WriteAllText(markerPath, hashPrefix);
    }

    /// <summary>
    /// Hashes every file in the directory; the sorted relative path and the
    /// content length prefix each file's bytes, so different directory trees
    /// cannot produce the same digest.
    /// </summary>
    private static string HashDirectory(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = ListFilesOrdered(directory);
        foreach (var (relativePath, fullPath) in files)
        {
            var relativePathBytes = Encoding.UTF8.GetBytes(relativePath);
            AppendWithLength(hash, relativePathBytes);
            var contentBytes = File.ReadAllBytes(fullPath);
            AppendWithLength(hash, contentBytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendWithLength(IncrementalHash hash, byte[] data)
    {
        var lengthBytes = BitConverter.GetBytes(data.Length);
        hash.AppendData(lengthBytes);
        hash.AppendData(data);
    }

    private static List<(string RelativePath, string FullPath)> ListFilesOrdered(string directory)
    {
        return Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(fullPath => (RelativePath: Path.GetRelativePath(directory, fullPath), FullPath: fullPath))
            .OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static void CopyDirectoryContents(string sourceDirectory, string shadowDirectory)
    {
        var files = ListFilesOrdered(sourceDirectory);
        foreach (var (relativePath, sourceFile) in files)
        {
            var destinationFile = Path.Combine(shadowDirectory, relativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationFile)!;
            Directory.CreateDirectory(destinationDirectory);
            var filesMatch = FilesHaveSameContent(sourceFile, destinationFile);
            if (filesMatch)
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
        while (true)
        {
            var bytesRead = source.ReadAtLeast(sourceBuffer, bufferSize, throwOnEndOfStream: false);
            if (bytesRead == 0)
            {
                return true;
            }

            destination.ReadExactly(destinationBuffer.AsSpan(0, bytesRead));
            var sourceChunk = sourceBuffer.AsSpan(0, bytesRead);
            var destinationChunk = destinationBuffer.AsSpan(0, bytesRead);
            var chunksMatch = sourceChunk.SequenceEqual(destinationChunk);
            if (!chunksMatch)
            {
                return false;
            }
        }
    }
}
