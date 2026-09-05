using Microsoft.Data.Sqlite;

namespace FindJobHelper.WebUi;

/// <summary>Workspace-relative status of the sqlite job store.</summary>
public sealed record JobsDbStatus(
    string Path,
    bool Exists,
    int ApplicationCount,
    int RecruiterCount,
    int EventCount);

/// <summary>Row read back from <c>applications</c>.</summary>
public sealed record StoredApplication(
    long Id,
    string Folder,
    string? Nr,
    string Title,
    string Company,
    string? CompanyUrl,
    string? JobUrl,
    string State,
    string? StateNote,
    string CreatedAt,
    long? RecruiterId);

/// <summary>Row read back from <c>recruiters</c>.</summary>
public sealed record StoredRecruiter(
    long Id,
    string Name,
    string? Title,
    string? ProfileUrl,
    string? Location,
    string? Notes);

/// <summary>Row read back from <c>application_events</c>, verbatim.</summary>
public sealed record StoredEvent(
    long Id,
    long ApplicationId,
    string Type,
    string OccurredAt,
    string? Note,
    string? Payload);

/// <summary>Column values written to <c>applications</c>.</summary>
public sealed record ApplicationValues(
    string Folder,
    string? Nr,
    string Title,
    string Company,
    string? CompanyUrl,
    string? JobUrl,
    string State,
    string? StateNote,
    string CreatedAt,
    long? RecruiterId);

/// <summary>Column values written to <c>recruiters</c>.</summary>
public sealed record RecruiterValues(
    string Name,
    string? Title,
    string? ProfileUrl,
    string? Location,
    string? Notes);

/// <summary>Single row appended to <c>application_events</c>.</summary>
public sealed record PendingEvent(
    string Type,
    string OccurredAt,
    string? Note,
    string? PayloadJson);

/// <summary>Recruiter insert-or-update applied before the application row.</summary>
public sealed record RecruiterWrite(
    bool IsNew,
    long RecruiterId,
    RecruiterValues Values);

/// <summary>
/// All sqlite writes for one ingested folder, applied in a single transaction:
/// the recruiter first (the application row needs its id), then the
/// application row, then the events. When <see cref="Recruiter"/> is present,
/// the application row takes the resulting recruiter id and
/// <see cref="ApplicationValues.RecruiterId"/> is ignored.
/// </summary>
public sealed record FolderWriteBatch(
    bool ApplicationIsNew,
    long ApplicationId,
    ApplicationValues Values,
    RecruiterWrite? Recruiter,
    IReadOnlyList<PendingEvent> Events);

/// <summary>
/// Owns <c>data/jobs.db</c>: schema creation, application-root resolution, and
/// the read/write primitives used by <see cref="ApplicationIngestion"/>.
/// Writers serialize on an async gate; every connection sets a busy timeout so
/// status reads wait out a Refresh instead of failing.
/// </summary>
public sealed class JobStore : IDisposable
{
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS applications (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            folder TEXT NOT NULL UNIQUE,
            nr TEXT NULL,
            title TEXT NOT NULL DEFAULT '',
            company TEXT NOT NULL DEFAULT '',
            company_url TEXT NULL,
            job_url TEXT NULL,
            state TEXT NOT NULL DEFAULT 'added'
                CHECK (state IN ('added', 'generated', 'sent', 'followed-up', 'n/a', 'other')),
            state_note TEXT NULL,
            created_at TEXT NOT NULL,
            recruiter_id INTEGER NULL REFERENCES recruiters (id) ON DELETE SET NULL
        );
        CREATE TABLE IF NOT EXISTS recruiters (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT NOT NULL DEFAULT '',
            title TEXT NULL,
            profile_url TEXT NULL,
            location TEXT NULL,
            notes TEXT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_recruiters_profile_url
            ON recruiters (profile_url)
            WHERE profile_url IS NOT NULL AND profile_url <> '';
        CREATE TABLE IF NOT EXISTS application_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            application_id INTEGER NOT NULL REFERENCES applications (id) ON DELETE CASCADE,
            type TEXT NOT NULL
                CHECK (type IN ('added', 'generated', 'sent', 'followed-up', 'state_changed', 'note', 'recruiter_updated')),
            occurred_at TEXT NOT NULL,
            note TEXT NULL,
            payload TEXT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_application_events_application
            ON application_events (application_id, id);
        """;

    private readonly SemaphoreSlim _writeGate = new(initialCount: 1, maxCount: 1);
    private readonly WebUiOptions _options;
    private readonly ILogger<JobStore> _logger;

    public JobStore(WebUiOptions options, ILogger<JobStore> logger)
    {
        _options = options;
        _logger = logger;
    }

    public void Dispose()
    {
        _writeGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public string DbPath => _options.JobsDbPathOrDefault;

    /// <summary>
    /// Resolves the folder ingestion scans: the explicit override wins, else
    /// <c>data/</c> once it holds application folders (after the sent-to-data
    /// rename, fjw-w4u.5), else the legacy <c>sent/</c>.
    /// </summary>
    public string ResolveApplicationsRoot()
    {
        var configured = _options.ApplicationsRoot.Trim();
        if (configured.Length > 0)
        {
            var combined = Path.Combine(_options.WorkspaceRoot, configured);
            return Path.GetFullPath(combined);
        }

        var dataRoot = Path.Combine(_options.WorkspaceRoot, "data");
        if (Directory.Exists(dataRoot))
        {
            var hasFolders = Directory.EnumerateDirectories(dataRoot).Any();
            if (hasFolders)
            {
                return dataRoot;
            }
        }

        var sentRoot = Path.Combine(_options.WorkspaceRoot, "sent");
        return Path.GetFullPath(sentRoot);
    }

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(DbPath);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<JobsDbStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(DbPath))
        {
            return new JobsDbStatus(
                Path: DbPath,
                Exists: false,
                ApplicationCount: 0,
                RecruiterCount: 0,
                EventCount: 0);
        }

        try
        {
            using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT (SELECT COUNT(*) FROM applications), (SELECT COUNT(*) FROM recruiters), (SELECT COUNT(*) FROM application_events);";
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var applications = reader.GetInt64(0);
            var recruiters = reader.GetInt64(1);
            var events = reader.GetInt64(2);
            return new JobsDbStatus(
                Path: DbPath,
                Exists: true,
                ApplicationCount: (int)applications,
                RecruiterCount: (int)recruiters,
                EventCount: (int)events);
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "Could not read job store counts from '{DbPath}'.", DbPath);
            return new JobsDbStatus(
                Path: DbPath,
                Exists: true,
                ApplicationCount: 0,
                RecruiterCount: 0,
                EventCount: 0);
        }
    }

    /// <summary>Serializes whole Refresh runs; per-folder atomicity comes from the transaction in <see cref="ApplyFolderBatchAsync"/>.</summary>
    public async Task<IDisposable> AcquireWriteGateAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        return new GateReleaser(_writeGate);
    }

    public async Task<StoredApplication?> FindApplicationByFolderAsync(
        string folder,
        CancellationToken cancellationToken)
    {
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, folder, nr, title, company, company_url, job_url, state, state_note, created_at, recruiter_id FROM applications WHERE folder = $folder COLLATE NOCASE;";
        AddParameter(command, "$folder", folder);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var hasRow = await reader.ReadAsync(cancellationToken);
        if (!hasRow)
        {
            return null;
        }

        return ReadApplication(reader);
    }

    /// <summary>
    /// Lists every application ordered by folder. The UI sorts by nr itself;
    /// the store only guarantees a stable order.
    /// </summary>
    public async Task<IReadOnlyList<StoredApplication>> ListApplicationsAsync(
        CancellationToken cancellationToken)
    {
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, folder, nr, title, company, company_url, job_url, state, state_note, created_at, recruiter_id FROM applications ORDER BY folder COLLATE NOCASE;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var applications = new List<StoredApplication>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var application = ReadApplication(reader);
            applications.Add(application);
        }

        return applications;
    }

    public async Task<IReadOnlyList<StoredRecruiter>> ListRecruitersAsync(
        CancellationToken cancellationToken)
    {
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, title, profile_url, location, notes FROM recruiters ORDER BY id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var recruiters = new List<StoredRecruiter>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var recruiter = ReadRecruiter(reader);
            recruiters.Add(recruiter);
        }

        return recruiters;
    }

    /// <summary>Lists every event ordered chronologically per application.</summary>
    public async Task<IReadOnlyList<StoredEvent>> ListEventsAsync(
        CancellationToken cancellationToken)
    {
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, application_id, type, occurred_at, note, payload FROM application_events ORDER BY application_id, id;";
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<StoredEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var stored = ReadEvent(reader);
            events.Add(stored);
        }

        return events;
    }

    public async Task<StoredApplication?> FindApplicationByNrAsync(
        string nr,
        CancellationToken cancellationToken)
    {
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, folder, nr, title, company, company_url, job_url, state, state_note, created_at, recruiter_id FROM applications WHERE nr = $nr LIMIT 1;";
        AddParameter(command, "$nr", nr);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var hasRow = await reader.ReadAsync(cancellationToken);
        if (!hasRow)
        {
            return null;
        }

        return ReadApplication(reader);
    }

    /// <summary>
    /// Resolves a UI key to an application row. Accepts the canonical folder
    /// relative path, legacy <c>sent/</c> or <c>data/</c> prefixed keys,
    /// <c>nr:123</c>, and bare <c>123</c> numbers. The bare-number fallback
    /// only runs for keys without a slash (folder paths never match an nr).
    /// </summary>
    public async Task<StoredApplication?> FindApplicationByKeyAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var normalized = key.Replace('\\', '/').Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        var byFolder = await FindApplicationByFolderAsync(normalized, cancellationToken);
        if (byFolder is not null)
        {
            return byFolder;
        }

        var stripped = StripRootPrefix(normalized);
        if (stripped is not null)
        {
            var byStripped = await FindApplicationByFolderAsync(stripped, cancellationToken);
            if (byStripped is not null)
            {
                return byStripped;
            }
        }

        var nr = ExtractNrKey(normalized);
        if (nr is null)
        {
            return null;
        }

        var byNr = await FindApplicationByNrAsync(nr, cancellationToken);
        return byNr;
    }

    private static string? StripRootPrefix(string normalized)
    {
        var slash = normalized.IndexOf('/');
        if (slash <= 0)
        {
            return null;
        }

        var prefix = normalized[..slash];
        var isSent = string.Equals(prefix, "sent", StringComparison.OrdinalIgnoreCase);
        if (isSent)
        {
            return normalized[(slash + 1)..];
        }

        var isData = string.Equals(prefix, "data", StringComparison.OrdinalIgnoreCase);
        if (isData)
        {
            return normalized[(slash + 1)..];
        }

        return null;
    }

    private static string? ExtractNrKey(string normalized)
    {
        var prefix = "nr:";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var remainder = normalized[prefix.Length..].Trim();
            var hasRemainder = remainder.Length > 0;
            if (!hasRemainder)
            {
                return null;
            }

            return remainder;
        }

        var isBareNumber = normalized.IndexOf('/') < 0;
        if (!isBareNumber)
        {
            return null;
        }

        return normalized;
    }

    public async Task<StoredRecruiter?> FindRecruiterByProfileUrlAsync(
        string profileUrl,
        CancellationToken cancellationToken)
    {
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, title, profile_url, location, notes FROM recruiters WHERE profile_url = $profileUrl;";
        AddParameter(command, "$profileUrl", profileUrl);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var hasRow = await reader.ReadAsync(cancellationToken);
        if (!hasRow)
        {
            return null;
        }

        return ReadRecruiter(reader);
    }

    public async Task<StoredRecruiter?> FindRecruiterByNameAsync(
        string name,
        CancellationToken cancellationToken)
    {
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, title, profile_url, location, notes FROM recruiters WHERE name = $name COLLATE NOCASE AND (profile_url IS NULL OR profile_url = '');";
        AddParameter(command, "$name", name);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var hasRow = await reader.ReadAsync(cancellationToken);
        if (!hasRow)
        {
            return null;
        }

        return ReadRecruiter(reader);
    }

    public async Task ApplyFolderBatchAsync(FolderWriteBatch batch, CancellationToken cancellationToken)
    {
        using var connection = await OpenConfiguredConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var recruiterId = batch.Values.RecruiterId;
        if (batch.Recruiter is not null)
        {
            recruiterId = await ApplyRecruiterWriteAsync(connection, batch.Recruiter, cancellationToken);
        }

        var values = batch.Values with { RecruiterId = recruiterId };
        long applicationId;
        if (batch.ApplicationIsNew)
        {
            applicationId = await InsertApplicationAsync(connection, values, cancellationToken);
        }
        else
        {
            applicationId = batch.ApplicationId;
            await UpdateApplicationAsync(connection, applicationId, values, cancellationToken);
        }

        foreach (var pending in batch.Events)
        {
            await AppendEventAsync(connection, applicationId, pending, cancellationToken);
        }

        transaction.Commit();
        _logger.LogDebug(
            "Ingested folder '{Folder}' (new: {IsNew}, events: {EventCount}).",
            values.Folder,
            batch.ApplicationIsNew,
            batch.Events.Count);
    }

    private static async Task<long> ApplyRecruiterWriteAsync(
        SqliteConnection connection,
        RecruiterWrite write,
        CancellationToken cancellationToken)
    {
        if (write.IsNew)
        {
            return await InsertRecruiterAsync(connection, write.Values, cancellationToken);
        }

        await UpdateRecruiterAsync(connection, write.RecruiterId, write.Values, cancellationToken);
        return write.RecruiterId;
    }

    private static async Task<long> InsertRecruiterAsync(
        SqliteConnection connection,
        RecruiterValues values,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO recruiters (name, title, profile_url, location, notes) VALUES ($name, $title, $profileUrl, $location, $notes); SELECT last_insert_rowid();";
        AddParameter(command, "$name", values.Name);
        AddParameter(command, "$title", values.Title);
        AddParameter(command, "$profileUrl", values.ProfileUrl);
        AddParameter(command, "$location", values.Location);
        AddParameter(command, "$notes", values.Notes);
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return (long)id!;
    }

    private static async Task UpdateRecruiterAsync(
        SqliteConnection connection,
        long recruiterId,
        RecruiterValues values,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE recruiters SET name = $name, title = $title, profile_url = $profileUrl, location = $location, notes = $notes WHERE id = $id;";
        AddParameter(command, "$name", values.Name);
        AddParameter(command, "$title", values.Title);
        AddParameter(command, "$profileUrl", values.ProfileUrl);
        AddParameter(command, "$location", values.Location);
        AddParameter(command, "$notes", values.Notes);
        AddParameter(command, "$id", recruiterId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> InsertApplicationAsync(
        SqliteConnection connection,
        ApplicationValues values,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO applications (folder, nr, title, company, company_url, job_url, state, state_note, created_at, recruiter_id) VALUES ($folder, $nr, $title, $company, $companyUrl, $jobUrl, $state, $stateNote, $createdAt, $recruiterId); SELECT last_insert_rowid();";
        AddApplicationParameters(command, values);
        var id = await command.ExecuteScalarAsync(cancellationToken);
        return (long)id!;
    }

    private static async Task UpdateApplicationAsync(
        SqliteConnection connection,
        long applicationId,
        ApplicationValues values,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE applications SET folder = $folder, nr = $nr, title = $title, company = $company, company_url = $companyUrl, job_url = $jobUrl, state = $state, state_note = $stateNote, created_at = $createdAt, recruiter_id = $recruiterId WHERE id = $id;";
        AddApplicationParameters(command, values);
        AddParameter(command, "$id", applicationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddApplicationParameters(SqliteCommand command, ApplicationValues values)
    {
        AddParameter(command, "$folder", values.Folder);
        AddParameter(command, "$nr", values.Nr);
        AddParameter(command, "$title", values.Title);
        AddParameter(command, "$company", values.Company);
        AddParameter(command, "$companyUrl", values.CompanyUrl);
        AddParameter(command, "$jobUrl", values.JobUrl);
        AddParameter(command, "$state", values.State);
        AddParameter(command, "$stateNote", values.StateNote);
        AddParameter(command, "$createdAt", values.CreatedAt);
        AddParameter(command, "$recruiterId", values.RecruiterId);
    }

    private static async Task AppendEventAsync(
        SqliteConnection connection,
        long applicationId,
        PendingEvent pending,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO application_events (application_id, type, occurred_at, note, payload) VALUES ($applicationId, $type, $occurredAt, $note, $payload);";
        AddParameter(command, "$applicationId", applicationId);
        AddParameter(command, "$type", pending.Type);
        AddParameter(command, "$occurredAt", pending.OccurredAt);
        AddParameter(command, "$note", pending.Note);
        AddParameter(command, "$payload", pending.PayloadJson);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenConfiguredConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder();
        builder.DataSource = DbPath;
        var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static void AddParameter(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static StoredApplication ReadApplication(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var folder = reader.GetString(1);
        var nr = GetNullableText(reader, 2);
        var title = reader.GetString(3);
        var company = reader.GetString(4);
        var companyUrl = GetNullableText(reader, 5);
        var jobUrl = GetNullableText(reader, 6);
        var state = reader.GetString(7);
        var stateNote = GetNullableText(reader, 8);
        var createdAt = reader.GetString(9);
        var recruiterId = GetNullableInt64(reader, 10);
        return new StoredApplication(
            Id: id,
            Folder: folder,
            Nr: nr,
            Title: title,
            Company: company,
            CompanyUrl: companyUrl,
            JobUrl: jobUrl,
            State: state,
            StateNote: stateNote,
            CreatedAt: createdAt,
            RecruiterId: recruiterId);
    }

    private static StoredRecruiter ReadRecruiter(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var name = reader.GetString(1);
        var title = GetNullableText(reader, 2);
        var profileUrl = GetNullableText(reader, 3);
        var location = GetNullableText(reader, 4);
        var notes = GetNullableText(reader, 5);
        return new StoredRecruiter(
            Id: id,
            Name: name,
            Title: title,
            ProfileUrl: profileUrl,
            Location: location,
            Notes: notes);
    }

    private static StoredEvent ReadEvent(SqliteDataReader reader)
    {
        var id = reader.GetInt64(0);
        var applicationId = reader.GetInt64(1);
        var type = reader.GetString(2);
        var occurredAt = reader.GetString(3);
        var note = GetNullableText(reader, 4);
        var payload = GetNullableText(reader, 5);
        return new StoredEvent(
            Id: id,
            ApplicationId: applicationId,
            Type: type,
            OccurredAt: occurredAt,
            Note: note,
            Payload: payload);
    }

    private static string? GetNullableText(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetString(ordinal);
    }

    private static long? GetNullableInt64(SqliteDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        return reader.GetInt64(ordinal);
    }

    private sealed class GateReleaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose()
        {
            gate.Release();
        }
    }
}
