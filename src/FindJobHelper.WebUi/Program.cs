using System.Diagnostics;
using FindJobHelper.WebUi;

var builder = WebApplication.CreateBuilder(CreateWebApplicationOptions(args));
var options = ParseOptions(args);

builder.Services.ConfigureHttpJsonOptions(static options =>
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<JobStore>();
builder.Services.AddSingleton<ApplicationIngestion>();
builder.Services.AddSingleton<ApplicationCatalog>();
builder.Services.AddSingleton<GenerationJobManager>();
builder.Services.AddSingleton<DatabaseManager>();
builder.Services.AddSingleton<ConfigEditor>();

var app = builder.Build();

try
{
    await app.Services.GetRequiredService<JobStore>().EnsureSchemaAsync(CancellationToken.None);
}
catch (Exception ex)
{
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("JobStore");
    startupLogger.LogWarning(ex, "SQLite job store was not initialized; Refresh will retry on demand.");
}

app.UseDefaultFiles();
// Single-user localhost tool under active development: never cache static
// assets, so the browser always runs the current JS/CSS (stale app.js caused
// real "the fix doesn't work" confusion during the config-editor work).
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = static context =>
    {
        context.Context.Response.Headers.CacheControl = "no-store";
    },
});

app.MapGet("/api/status", async (
    DatabaseManager database,
    JobStore jobs,
    CancellationToken cancellationToken) =>
{
    var jobsDb = await jobs.GetStatusAsync(cancellationToken);
    return Results.Ok(new
    {
        workspaceRoot = options.WorkspaceRoot,
        database = database.GetStatus(),
        jobsDb,
    });
});

app.MapGet("/api/applications", async (
    ApplicationCatalog catalog,
    GenerationJobManager jobs,
    CancellationToken cancellationToken) =>
{
    var applications = await catalog.GetApplicationsAsync(cancellationToken);
    var active = jobs.ActiveJobs();
    return Results.Ok(new { applications, activeGenerations = active });
});

app.MapPost("/api/applications/refresh", async (
    ApplicationIngestion ingestion,
    CancellationToken cancellationToken) =>
{
    var report = await ingestion.RefreshAsync(cancellationToken);
    return Results.Ok(report);
});

app.MapPut("/api/applications/state", async (
    UpdateStateRequest request,
    ApplicationIngestion ingestion,
    ApplicationCatalog catalog,
    CancellationToken cancellationToken) =>
{
    if (!ApplicationStateExtensions.TryParseWireName(request.State, out var state))
    {
        return Results.BadRequest(new
        {
            error = "Unknown state. Use one of: added, generated, sent, followed-up, n/a, other.",
        });
    }

    var updated = await ingestion.TryUpdateStateAsync(
        key: request.Key,
        state: state,
        note: request.Note,
        cancellationToken: cancellationToken);
    if (!updated)
    {
        return Results.NotFound(new { error = $"No application matches key '{request.Key}'." });
    }

    var application = await catalog.FindByKeyAsync(request.Key, cancellationToken);
    return Results.Ok(new { application });
});

app.MapPost("/api/applications/open", async (
    ApplicationKeyRequest request,
    ApplicationCatalog catalog,
    CancellationToken cancellationToken) =>
{
    var folder = await catalog.ResolveFolderAsync(request.Key, cancellationToken);
    if (folder is null)
    {
        return Results.NotFound(new { error = $"No application folder found for key '{request.Key}'." });
    }

    try
    {
        OpenFolderCrossPlatform(folder);
        return Results.Ok(new { opened = folder });
    }
    catch (Exception ex)
    {
        return Results.Conflict(new { error = $"Could not open the folder: {ex.Message}" });
    }
});

app.MapPost("/api/applications/file/open-in-vscode", async (
    OpenInVscodeRequest request,
    ApplicationCatalog catalog,
    CancellationToken cancellationToken) =>
{
    if (IsUnsafeFileName(request.Name))
    {
        return Results.BadRequest(new { error = "Invalid file name." });
    }

    var folder = await catalog.ResolveFolderAsync(request.Key, cancellationToken);
    if (folder is null)
    {
        return Results.NotFound(new { error = $"No application folder found for key '{request.Key}'." });
    }

    var filePath = Path.Combine(folder, request.Name);
    if (!File.Exists(filePath))
    {
        return Results.NotFound(new { error = $"File '{request.Name}' was not found in '{request.Key}'." });
    }

    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "code",
            ArgumentList = { filePath },
            UseShellExecute = false,
        });
        return Results.Ok(new { opened = filePath });
    }
    catch (Exception ex)
    {
        return Results.Conflict(new { error = $"Could not launch VS Code ('code {filePath}'): {ex.Message}" });
    }
});

app.MapGet("/api/applications/file", async (
    string key,
    string name,
    ApplicationCatalog catalog,
    CancellationToken cancellationToken) =>
{
    if (IsUnsafeFileName(name))
    {
        return Results.BadRequest(new { error = "Invalid file name." });
    }

    var folder = await catalog.ResolveFolderAsync(key, cancellationToken);
    if (folder is null)
    {
        return Results.NotFound(new { error = $"No application folder found for key '{key}'." });
    }

    var filePath = Path.Combine(folder, name);
    if (!File.Exists(filePath))
    {
        return Results.NotFound(new { error = $"File '{name}' was not found in '{key}'." });
    }

    var contentType = Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".md" => "text/markdown; charset=utf-8",
        ".txt" => "text/plain; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".tex" => "text/plain; charset=utf-8",
        _ => "application/octet-stream",
    };
    return Results.File(filePath, contentType);
});

// Config editor (fjw-w4u.6): the schema is generated in-process from the
// pinned Configuration.Json model; validate/save use the real loader as the
// ground truth (see ConfigEditor), and tags feed tag-name completion.
app.MapGet("/api/config-schema", (ConfigEditor editor) => Results.Content(
    editor.GetSchemaJson(), "application/json; charset=utf-8"));

app.MapGet("/api/tags", (ConfigEditor editor) =>
{
    var tags = editor.GetTagNames();
    return Results.Ok(new { tags });
});

app.MapPost("/api/applications/config/validate", async (
    ValidateConfigRequest request,
    ApplicationCatalog catalog,
    ConfigEditor editor,
    CancellationToken cancellationToken) =>
{
    // The key is resolved (not just carried): validating against a missing
    // folder is a 404, so editor typos surface instead of validating nowhere.
    var folder = await catalog.ResolveFolderAsync(request.Key, cancellationToken);
    if (folder is null)
    {
        return Results.NotFound(new { error = $"No application folder found for key '{request.Key}'." });
    }

    var errors = await editor.ValidateAsync(request.Content, cancellationToken);
    return Results.Ok(new { valid = errors.Count == 0, errors });
});

app.MapPut("/api/applications/config", async (
    SaveConfigRequest request,
    ApplicationCatalog catalog,
    ConfigEditor editor,
    CancellationToken cancellationToken) =>
{
    var folder = await catalog.ResolveFolderAsync(request.Key, cancellationToken);
    if (folder is null)
    {
        return Results.NotFound(new { error = $"No application folder found for key '{request.Key}'." });
    }

    // Disk failures surface as JSON 409 (like the open endpoints) instead of
    // an empty 500: validation rejects still return 400 below, untouched.
    SaveConfigOutcome outcome;
    try
    {
        outcome = await editor.SaveAsync(folder, request.Content, cancellationToken);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        return Results.Conflict(new { error = $"Could not write config.json: {ex.Message}" });
    }

    if (!outcome.Saved)
    {
        return Results.BadRequest(new { saved = false, errors = outcome.Errors });
    }

    return Results.Ok(new
    {
        saved = true,
        backup = outcome.Backup,
        errors = Array.Empty<string>(),
    });
});

app.MapPost("/api/generations", async (
    StartGenerationRequest request,
    GenerationJobManager jobs,
    CancellationToken cancellationToken) =>
{
    try
    {
        var job = await jobs.StartAsync(
            request.Key,
            request.Debug,
            request.OutputDirectory,
            cancellationToken);
        return Results.Ok(job);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapGet("/api/generations/{id}", (string id, GenerationJobManager jobs) =>
{
    var job = jobs.Find(id);
    return job is null ? Results.NotFound(new { error = "Unknown generation id." }) : Results.Ok(job);
});

app.MapPost("/api/database/rebuild", async (
    DatabaseManager database,
    CancellationToken cancellationToken) =>
{
    try
    {
        var output = await database.RebuildAsync(cancellationToken);
        return Results.Ok(new { rebuilt = true, output, status = database.GetStatus() });
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
});

app.MapFallbackToFile("index.html");

app.Run();

/// <summary>Rejects path separators and parent traversal in UI-supplied file names.</summary>
bool IsUnsafeFileName(string name)
{
    if (name.Contains('/'))
    {
        return true;
    }

    if (name.Contains('\\'))
    {
        return true;
    }

    return name.Contains("..", StringComparison.Ordinal);
}

/// <summary>Opens a folder in the OS file manager, whatever the OS.</summary>
void OpenFolderCrossPlatform(string folder)
{
    if (OperatingSystem.IsWindows())
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { folder },
            UseShellExecute = true,
        });
        return;
    }

    if (OperatingSystem.IsMacOS())
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            ArgumentList = { folder },
            UseShellExecute = false,
        });
        return;
    }

    // Linux: under WSL, prefer the Windows Explorer via interop so the folder
    // opens on the Windows side; otherwise fall back to xdg-open.
    var windowsPath = TryWslWindowsPath(folder);
    if (windowsPath is not null)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            ArgumentList = { windowsPath },
            UseShellExecute = false,
        });
        return;
    }

    Process.Start(new ProcessStartInfo
    {
        FileName = "xdg-open",
        ArgumentList = { folder },
        UseShellExecute = false,
    });
}

string? TryWslWindowsPath(string folder)
{
    if (Environment.GetEnvironmentVariable("WSL_DISTRO_NAME") is null)
    {
        return null;
    }

    try
    {
        using var conversion = Process.Start(new ProcessStartInfo
        {
            FileName = "wslpath",
            ArgumentList = { "-w", folder },
            UseShellExecute = false,
            RedirectStandardOutput = true,
        });
        if (conversion is null)
        {
            return null;
        }

        if (!conversion.WaitForExit(5000))
        {
            try
            {
                conversion.Kill();
            }
            catch
            {
            }

            return null;
        }

        if (conversion.ExitCode != 0)
        {
            return null;
        }

        // Empty conversion output must fall back to xdg-open, never reach
        // explorer.exe as an empty argument.
        var line = conversion.StandardOutput.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(line) ? null : line;
    }
    catch
    {
        return null;
    }
}

WebApplicationOptions CreateWebApplicationOptions(string[] arguments)
{
    // Running the compiled dll from an arbitrary working directory loses the
    // project-relative web root, so probe the usual wwwroot locations.
    string?[] webRootCandidates =
    [
        Path.Combine(Environment.CurrentDirectory, "wwwroot"),
        Path.Combine(AppContext.BaseDirectory, "wwwroot"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot")),
    ];
    var webRoot = webRootCandidates.FirstOrDefault(Directory.Exists);
    return new WebApplicationOptions
    {
        Args = arguments,
        WebRootPath = webRoot,
    };
}

WebUiOptions ParseOptions(string[] arguments)
{
    var parsed = new WebUiOptions();
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        switch (arguments[i])
        {
            case "--workspace":
                parsed.WorkspaceRoot = Path.GetFullPath(arguments[i + 1]);
                break;
            case "--database":
                parsed.DatabasePath = Path.GetFullPath(arguments[i + 1]);
                break;
            case "--experience-database-project":
                parsed.ExperienceDatabaseProjectDir = Path.GetFullPath(arguments[i + 1]);
                break;
            case "--jobs-db":
                parsed.JobsDbPath = Path.GetFullPath(arguments[i + 1]);
                break;
            case "--applications-root":
                parsed.ApplicationsRoot = arguments[i + 1];
                break;
        }
    }

    return parsed;
}

public sealed record UpdateStateRequest(string Key, string State, string? Note);

public sealed record ApplicationKeyRequest(string Key);

public sealed record OpenInVscodeRequest(string Key, string Name);

public sealed record ValidateConfigRequest(string Key, string Content);

public sealed record SaveConfigRequest(string Key, string Content);

public sealed record StartGenerationRequest(
    string Key,
    bool Debug = false,
    string? OutputDirectory = null);
