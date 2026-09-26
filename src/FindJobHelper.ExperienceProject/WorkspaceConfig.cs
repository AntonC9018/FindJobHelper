using System.Text.Json;

namespace FindJobHelper.ExperienceProject;

/// <summary>
/// The <c>findjobhelper.config.json</c> file at a workspace repository root
/// (ADR 0001). Points at the experience project and carries workspace-level
/// settings such as the personal info consumed by CV generation. The file is
/// JSONC: comments and trailing commas are allowed (ADR 0002).
/// </summary>
public sealed record WorkspaceConfig
{
    public const string FileName = "findjobhelper.config.json";

    private const string ExperienceProjectKey = "experienceProject";

    private WorkspaceConfig(string configFilePath, string? experienceProjectPath)
    {
        ConfigFilePath = configFilePath;
        ExperienceProjectPath = experienceProjectPath;
    }

    /// <summary>Absolute path of the config file itself.</summary>
    public string ConfigFilePath { get; }

    /// <summary>
    /// Absolute path of the experience project csproj, with a relative value
    /// resolved from the directory containing the config file, or
    /// <see langword="null"/> when the file sets no experience project.
    /// </summary>
    public string? ExperienceProjectPath { get; }

    public static string? Find(string seedDirectory)
    {
        var current = Path.GetFullPath(seedDirectory);
        while (true)
        {
            var candidate = Path.Combine(current, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                return null;
            }

            current = parent.FullName;
        }
    }

    public static WorkspaceConfig Load(string configFilePath)
    {
        var fullPath = Path.GetFullPath(configFilePath);
        var jsoncText = File.ReadAllText(fullPath);
        var document = JsonDocument.Parse(
            jsoncText,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

        string? experienceProjectPath = null;
        if (document.RootElement.TryGetProperty(ExperienceProjectKey, out var projectElement)
            && projectElement.ValueKind == JsonValueKind.String)
        {
            var projectValue = projectElement.GetString();
            if (!string.IsNullOrWhiteSpace(projectValue))
            {
                experienceProjectPath = Path.GetFullPath(
                    projectValue,
                    Path.GetDirectoryName(fullPath)!);
            }
        }

        return new WorkspaceConfig(fullPath, experienceProjectPath);
    }
}
