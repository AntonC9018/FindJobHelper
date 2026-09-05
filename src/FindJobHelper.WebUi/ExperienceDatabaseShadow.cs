using System.Security.Cryptography;

namespace FindJobHelper.WebUi;

/// <summary>
/// Copies the experience database DLL to a content-hashed shadow path before
/// loading it. The pipeline maps the DLL into the process, which locks the
/// file on Windows; loading a copy lets the user rebuild the original at any
/// time. The hash prefix makes each database version load from a fresh path,
/// so a rebuild is never shadowed by an already-loaded assembly. Shared by
/// generation (<see cref="GenerationJobManager"/>) and tag-name completion
/// (<see cref="ConfigEditor"/>).
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
            "FindJobWorkspace-webui",
            "experience-database",
            hashPrefix);
        Directory.CreateDirectory(shadowDirectory);
        var shadowPath = Path.Combine(shadowDirectory, Path.GetFileName(databasePath));
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
}
