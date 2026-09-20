using System.Security.Cryptography;

namespace FindJobHelper.WebUi;

/// <summary>
/// Copies the experience database DLL to a content-hashed shadow path before
/// loading it. The pipeline maps the DLL into the process, which locks the
/// file on Windows; loading a copy lets the user rebuild the original at any
/// time. The hash prefix gives each database version a stable shadow path;
/// <c>ExperienceDatabaseProviderLoader</c> isolates their assembly identities.
/// Shared by generation (<see cref="GenerationJobManager"/>) and tag-name
/// completion (<see cref="ConfigEditor"/>).
/// </summary>
internal static class ExperienceDatabaseShadow
{
    public static string Copy(string databasePath)
    {
        byte[] hash;
        using (var stream = File.OpenRead(databasePath))
        {
            hash = SHA256.HashData(stream);
        }

        var hashPrefix = Convert.ToHexString(hash)[..16];
        var shadowDirectory = Path.Combine(
            Path.GetTempPath(),
            "find-job-webui",
            "experience-database",
            hashPrefix);
        Directory.CreateDirectory(shadowDirectory);
        var shadowPath = Path.Combine(shadowDirectory, Path.GetFileName(databasePath));
        var isFresh = ShadowIsFresh(shadowPath, hash);
        if (isFresh)
        {
            return shadowPath;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(databasePath, shadowPath, overwrite: true);
                return shadowPath;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(200);
            }
        }
    }

    /// <summary>
    /// Reports whether the shadow copy already holds the same content, so a
    /// path locked by an earlier load is reused instead of overwritten.
    /// </summary>
    private static bool ShadowIsFresh(string shadowPath, byte[] hash)
    {
        if (!File.Exists(shadowPath))
        {
            return false;
        }

        byte[] existing;
        try
        {
            using (var stream = File.OpenRead(shadowPath))
            {
                existing = SHA256.HashData(stream);
            }
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }

        return existing.SequenceEqual(hash);
    }
}
