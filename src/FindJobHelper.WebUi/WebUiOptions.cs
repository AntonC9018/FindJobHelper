namespace FindJobHelper.WebUi;

public sealed class WebUiOptions
{
    public const string SectionName = "WebUi";

    /// <summary>Root of the workspace holding `data/` and `ExperienceDatabase/`.</summary>
    public string WorkspaceRoot { get; set; } = Environment.CurrentDirectory;

    /// <summary>Compiled experience database DLL used by CV generation.</summary>
    public string DatabasePath { get; set; } = string.Empty;

    /// <summary>ExperienceDatabase project directory used for rebuilds.</summary>
    public string ExperienceDatabaseProjectDir { get; set; } = string.Empty;

    /// <summary>Publish output directory for rebuilds.</summary>
    public string DatabaseBuildOutputDir { get; set; } = string.Empty;

    /// <summary>SQLite job store file backing Refresh ingestion.</summary>
    public string JobsDbPath { get; set; } = string.Empty;

    /// <summary>
    /// Application folders root override. Empty means <c>data/</c> under the
    /// workspace root, which holds the per-application folders since the
    /// sent-to-data rename (fjw-w4u.5).
    /// </summary>
    public string ApplicationsRoot { get; set; } = string.Empty;

    public string DatabasePathOrDefault => string.IsNullOrWhiteSpace(DatabasePath)
        ? Path.Combine(WorkspaceRoot, "build", "ExperienceDatabase.dll")
        : DatabasePath;

    public string ExperienceDatabaseProjectDirOrDefault =>
        string.IsNullOrWhiteSpace(ExperienceDatabaseProjectDir)
            ? Path.Combine(WorkspaceRoot, "ExperienceDatabase")
            : ExperienceDatabaseProjectDir;

    public string DatabaseBuildOutputDirOrDefault =>
        string.IsNullOrWhiteSpace(DatabaseBuildOutputDir)
            ? Path.Combine(WorkspaceRoot, "build")
            : DatabaseBuildOutputDir;

    public string JobsDbPathOrDefault => string.IsNullOrWhiteSpace(JobsDbPath)
        ? Path.Combine(WorkspaceRoot, "data", "jobs.db")
        : JobsDbPath;
}
