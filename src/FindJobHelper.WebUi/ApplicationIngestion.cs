using System.Text.Json;

namespace FindJobHelper.WebUi;

/// <summary>Outcome of one manual Refresh run.</summary>
public sealed record RefreshReport(
    string DatabasePath,
    string ApplicationsRoot,
    int ScannedFolders,
    int FoldersWithMetadata,
    int Added,
    int Updated,
    int EventsAppended,
    int Skipped,
    IReadOnlyList<string> Errors);

/// <summary>
/// Ingests per-folder <c>metadata.json</c> files into the sqlite store.
/// Refresh is idempotent: a second run without folder changes writes nothing.
/// Events are appended only when meaningful: state changes get a typed event,
/// recruiter link or data changes get <c>recruiter_updated</c>, a lone note
/// change gets <c>note</c>, and silent scalar syncs (title/company/url fixes)
/// update the row without an event. Deleted folders keep their rows; no live
/// watching exists yet (recorded limitation, fjw-w4u.3).
/// </summary>
public sealed class ApplicationIngestion
{
    private readonly JobStore _store;
    private readonly ILogger<ApplicationIngestion> _logger;

    public ApplicationIngestion(JobStore store, ILogger<ApplicationIngestion> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<RefreshReport> RefreshAsync(CancellationToken cancellationToken)
    {
        await _store.EnsureSchemaAsync(cancellationToken);
        using var gate = await _store.AcquireWriteGateAsync(cancellationToken);
        var refreshedAt = DateTimeOffset.UtcNow.ToString("O");
        var root = _store.ResolveApplicationsRoot();
        if (!Directory.Exists(root))
        {
            _logger.LogWarning(
                "Applications root '{Root}' does not exist; Refresh ingested nothing.",
                root);
            return EmptyReport(root);
        }

        var directories = Directory.EnumerateDirectories(root)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var foldersWithMetadata = 0;
        var added = 0;
        var updated = 0;
        var eventsAppended = 0;
        var skipped = 0;
        var errors = new List<string>();
        foreach (var directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await RefreshFolderAsync(directory, root, refreshedAt, cancellationToken);
            if (outcome.HasMetadata)
            {
                foldersWithMetadata += 1;
            }

            added += outcome.Added;
            updated += outcome.Updated;
            eventsAppended += outcome.EventsAppended;
            if (outcome.Error is not null)
            {
                skipped += 1;
                errors.Add(outcome.Error);
            }
        }

        var report = new RefreshReport(
            DatabasePath: _store.DbPath,
            ApplicationsRoot: root,
            ScannedFolders: directories.Count,
            FoldersWithMetadata: foldersWithMetadata,
            Added: added,
            Updated: updated,
            EventsAppended: eventsAppended,
            Skipped: skipped,
            Errors: errors);
        _logger.LogInformation(
            "Refresh ingested {Scanned} folders from '{Root}': {Added} added, {Updated} updated, {Events} events, {Skipped} skipped.",
            report.ScannedFolders,
            root,
            added,
            updated,
            eventsAppended,
            skipped);
        return report;
    }

    /// <summary>
    /// Applies a UI state transition through the file contract: patches the
    /// folder's <c>metadata.json</c> to the new state, then runs a full
    /// Refresh so the db row plus its event come from the single ingestion
    /// path. The file stays the source of truth, so the change survives later
    /// Refreshes by construction. Returns false when no row matches
    /// <paramref name="key"/> or the folder has no metadata file.
    /// </summary>
    public async Task<bool> TryUpdateStateAsync(
        string key,
        ApplicationState state,
        string? note,
        CancellationToken cancellationToken)
    {
        var stored = await _store.FindApplicationByKeyAsync(key, cancellationToken);
        if (stored is null)
        {
            return false;
        }

        var metadataPath = ResolveMetadataPath(stored.Folder);
        if (metadataPath is null)
        {
            return false;
        }

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        var wireName = state.ToWireName();
        var trimmedNote = NormalizeNote(note);
        var patched = ApplicationMetadata.PatchStateJson(json, wireName, trimmedNote);
        await WriteAtomicallyAsync(metadataPath, patched, cancellationToken);
        _logger.LogInformation(
            "State for '{Folder}' set to '{State}' via metadata.json; re-ingesting.",
            stored.Folder,
            wireName);

        await RefreshAsync(cancellationToken);
        return true;
    }

    private string? ResolveMetadataPath(string folder)
    {
        var root = _store.ResolveApplicationsRoot();
        var relative = folder.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.Combine(root, relative);
        var fullFolder = Path.GetFullPath(combined);
        var folderExists = Directory.Exists(fullFolder);
        if (!folderExists)
        {
            return null;
        }

        var metadataPath = Path.Combine(fullFolder, "metadata.json");
        var metadataExists = File.Exists(metadataPath);
        if (!metadataExists)
        {
            return null;
        }

        return metadataPath;
    }

    private static string? NormalizeNote(string? note)
    {
        var trimmed = note?.Trim();
        var hasValue = !string.IsNullOrEmpty(trimmed);
        if (!hasValue)
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>
    /// Writes through a uniquely-named temp file in the same directory (same
    /// filesystem, so the move stays atomic) and cleans it up when the write
    /// or move fails. A fixed <c>.tmp</c> name would let two concurrent saves
    /// of the same folder clobber each other's temp file.
    /// </summary>
    private static async Task WriteAtomicallyAsync(
        string metadataPath,
        string content,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(metadataPath) ?? ".";
        var fileName = Path.GetFileName(metadataPath);
        var temporaryPath = Path.Combine(directory, $"{fileName}.{Path.GetRandomFileName()}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, metadataPath, overwrite: true);
        }
        catch
        {
            TryDeleteTemporary(temporaryPath);
            throw;
        }
    }

    private static void TryDeleteTemporary(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private RefreshReport EmptyReport(string root)
    {
        return new RefreshReport(
            DatabasePath: _store.DbPath,
            ApplicationsRoot: root,
            ScannedFolders: 0,
            FoldersWithMetadata: 0,
            Added: 0,
            Updated: 0,
            EventsAppended: 0,
            Skipped: 0,
            Errors: []);
    }

    private sealed record FolderOutcome(
        bool HasMetadata,
        int Added,
        int Updated,
        int EventsAppended,
        string? Error);

    private async Task<FolderOutcome> RefreshFolderAsync(
        string directory,
        string root,
        string refreshedAt,
        CancellationToken cancellationToken)
    {
        var relativeFolder = Path.GetRelativePath(root, directory).Replace('\\', '/');
        var metadataPath = Path.Combine(directory, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            return new FolderOutcome(
                HasMetadata: false,
                Added: 0,
                Updated: 0,
                EventsAppended: 0,
                Error: null);
        }

        try
        {
            return await RefreshFileFolderAsync(relativeFolder, metadataPath, refreshedAt, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Refresh skipped folder '{Folder}'.", relativeFolder);
            return new FolderOutcome(
                HasMetadata: true,
                Added: 0,
                Updated: 0,
                EventsAppended: 0,
                Error: $"{relativeFolder}: {ex.Message}");
        }
    }

    private async Task<FolderOutcome> RefreshFileFolderAsync(
        string relativeFolder,
        string metadataPath,
        string refreshedAt,
        CancellationToken cancellationToken)
    {
        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        if (!ApplicationMetadata.TryParse(json, out var metadata, out var error))
        {
            return new FolderOutcome(
                HasMetadata: true,
                Added: 0,
                Updated: 0,
                EventsAppended: 0,
                Error: $"{relativeFolder}: {error}");
        }

        var existing = await _store.FindApplicationByFolderAsync(relativeFolder, cancellationToken);
        if (existing is null)
        {
            var insertedEvents = await InsertFolderAsync(relativeFolder, metadata, refreshedAt, cancellationToken);
            return new FolderOutcome(
                HasMetadata: true,
                Added: 1,
                Updated: 0,
                EventsAppended: insertedEvents,
                Error: null);
        }

        var updatedEvents = await UpdateFolderAsync(existing, metadata, refreshedAt, cancellationToken);
        if (updatedEvents is null)
        {
            return new FolderOutcome(
                HasMetadata: true,
                Added: 0,
                Updated: 0,
                EventsAppended: 0,
                Error: null);
        }

        return new FolderOutcome(
            HasMetadata: true,
            Added: 0,
            Updated: 1,
            EventsAppended: updatedEvents.Value,
            Error: null);
    }

    /// <summary>Inserts a new application. Returns the number of events appended.</summary>
    private async Task<int> InsertFolderAsync(
        string relativeFolder,
        ApplicationMetadata metadata,
        string refreshedAt,
        CancellationToken cancellationToken)
    {
        var candidate = BuildApplicationValues(relativeFolder, metadata, refreshedAt);
        var resolution = await ResolveRecruiterAsync(metadata.Recruiter, cancellationToken);
        candidate = candidate with { RecruiterId = resolution.RecruiterId };
        var events = new List<PendingEvent>();
        events.Add(new PendingEvent(
            Type: "added",
            OccurredAt: refreshedAt,
            Note: null,
            PayloadJson: null));
        if (candidate.State != ApplicationState.Added.ToWireName())
        {
            var state = ParseState(candidate.State);
            var payload = StateChangePayload(from: ApplicationState.Added.ToWireName(), to: candidate.State);
            events.Add(new PendingEvent(
                Type: EventTypeForStateChange(state),
                OccurredAt: refreshedAt,
                Note: candidate.StateNote,
                PayloadJson: payload));
        }

        var batch = new FolderWriteBatch(
            ApplicationIsNew: true,
            ApplicationId: 0,
            Values: candidate,
            Recruiter: resolution.Write,
            Events: events);
        await _store.ApplyFolderBatchAsync(batch, cancellationToken);
        return events.Count;
    }

    /// <summary>
    /// Updates an existing application when anything differs. Returns null
    /// when the run was a no-op, else the number of events appended (scalar
    /// syncs may update the row with zero events).
    /// </summary>
    private async Task<int?> UpdateFolderAsync(
        StoredApplication existing,
        ApplicationMetadata metadata,
        string refreshedAt,
        CancellationToken cancellationToken)
    {
        var candidate = BuildApplicationValues(existing.Folder, metadata, existing.CreatedAt);
        var resolution = await ResolveRecruiterAsync(metadata.Recruiter, cancellationToken);
        candidate = candidate with { RecruiterId = resolution.RecruiterId };
        var current = CurrentValues(existing);
        var rowChanged = candidate != current;
        if (!rowChanged)
        {
            if (resolution.Write is null)
            {
                return null;
            }
        }

        var events = BuildUpdateEvents(candidate, current, resolution, refreshedAt);
        var batch = new FolderWriteBatch(
            ApplicationIsNew: false,
            ApplicationId: existing.Id,
            Values: candidate,
            Recruiter: resolution.Write,
            Events: events);
        await _store.ApplyFolderBatchAsync(batch, cancellationToken);
        return events.Count;
    }

    private List<PendingEvent> BuildUpdateEvents(
        ApplicationValues candidate,
        ApplicationValues current,
        RecruiterResolution resolution,
        string refreshedAt)
    {
        var events = new List<PendingEvent>();
        var stateChanged = !string.Equals(candidate.State, current.State, StringComparison.Ordinal);
        if (stateChanged)
        {
            var state = ParseState(candidate.State);
            var payload = StateChangePayload(from: current.State, to: candidate.State);
            events.Add(new PendingEvent(
                Type: EventTypeForStateChange(state),
                OccurredAt: refreshedAt,
                Note: candidate.StateNote,
                PayloadJson: payload));
            return events;
        }

        var linkChanged = candidate.RecruiterId != current.RecruiterId;
        if (linkChanged)
        {
            events.Add(RecruiterLinkEvent(candidate, resolution, refreshedAt));
            return events;
        }

        if (resolution.Write is not null)
        {
            events.Add(RecruiterDataEvent(resolution.DisplayName, refreshedAt));
            return events;
        }

        var noteChanged = !string.Equals(candidate.StateNote, current.StateNote, StringComparison.Ordinal);
        if (noteChanged)
        {
            events.Add(new PendingEvent(
                Type: "note",
                OccurredAt: refreshedAt,
                Note: candidate.StateNote,
                PayloadJson: null));
        }

        return events;
    }

    /// <summary>
    /// Builds the link-change event. A null recruiter id only means "removed"
    /// when no recruiter write is pending: a brand-new recruiter resolves to a
    /// null id until its row is inserted by the batch, but the folder ends up
    /// linked, so that transition reports "linked".
    /// </summary>
    private static PendingEvent RecruiterLinkEvent(
        ApplicationValues candidate,
        RecruiterResolution resolution,
        string refreshedAt)
    {
        if (candidate.RecruiterId is null)
        {
            if (resolution.Write is null)
            {
                return new PendingEvent(
                    Type: "recruiter_updated",
                    OccurredAt: refreshedAt,
                    Note: "Recruiter removed.",
                    PayloadJson: null);
            }
        }

        var recruiterDisplayName = resolution.DisplayName;
        if (!string.IsNullOrWhiteSpace(recruiterDisplayName))
        {
            return new PendingEvent(
                Type: "recruiter_updated",
                OccurredAt: refreshedAt,
                Note: $"Recruiter linked: {recruiterDisplayName}.",
                PayloadJson: null);
        }

        return new PendingEvent(
            Type: "recruiter_updated",
            OccurredAt: refreshedAt,
            Note: "Recruiter linked.",
            PayloadJson: null);
    }

    private static PendingEvent RecruiterDataEvent(string? recruiterDisplayName, string refreshedAt)
    {
        if (!string.IsNullOrWhiteSpace(recruiterDisplayName))
        {
            return new PendingEvent(
                Type: "recruiter_updated",
                OccurredAt: refreshedAt,
                Note: $"Recruiter updated: {recruiterDisplayName}.",
                PayloadJson: null);
        }

        return new PendingEvent(
            Type: "recruiter_updated",
            OccurredAt: refreshedAt,
            Note: "Recruiter updated.",
            PayloadJson: null);
    }

    /// <summary>
    /// Resolved recruiter linkage for one folder: an optional store write,
    /// the recruiter id the application row takes, and a display name for
    /// event notes. A present block with no stored match inserts; a match
    /// with new details merge-fills the shared row; an absent block unlinks
    /// (null id).
    /// </summary>
    private sealed record RecruiterResolution(RecruiterWrite? Write, long? RecruiterId, string? DisplayName);

    /// <summary>
    /// Resolves the recruiter block. Fill-only merge keeps the stored shared
    /// value whenever it is non-empty, so the first non-empty value wins and
    /// later Refreshes stay no-ops even when sibling folders disagree.
    /// </summary>
    private async Task<RecruiterResolution> ResolveRecruiterAsync(
        RecruiterMetadata? recruiter,
        CancellationToken cancellationToken)
    {
        if (recruiter is not { IsPresent: true })
        {
            return new RecruiterResolution(Write: null, RecruiterId: null, DisplayName: null);
        }

        var values = new RecruiterValues(
            Name: recruiter.Name ?? string.Empty,
            Title: recruiter.Title,
            ProfileUrl: recruiter.ProfileUrl,
            Location: recruiter.Location,
            Notes: recruiter.Notes);
        var displayName = DisplayName(values);
        StoredRecruiter? stored = null;
        if (values.ProfileUrl is not null)
        {
            stored = await _store.FindRecruiterByProfileUrlAsync(values.ProfileUrl, cancellationToken);
        }

        if (stored is null)
        {
            stored = await FindRecruiterByNameMatchAsync(values, cancellationToken);
        }

        if (stored is null)
        {
            var write = new RecruiterWrite(IsNew: true, RecruiterId: 0, Values: values);
            return new RecruiterResolution(Write: write, RecruiterId: null, DisplayName: displayName);
        }

        var current = new RecruiterValues(
            Name: stored.Name,
            Title: stored.Title,
            ProfileUrl: stored.ProfileUrl,
            Location: stored.Location,
            Notes: stored.Notes);
        var merged = MergeRecruiter(current, values);
        if (merged == current)
        {
            return new RecruiterResolution(Write: null, RecruiterId: stored.Id, DisplayName: displayName);
        }

        var update = new RecruiterWrite(IsNew: false, RecruiterId: stored.Id, Values: merged);
        return new RecruiterResolution(Write: update, RecruiterId: stored.Id, DisplayName: displayName);
    }

    /// <summary>
    /// Matches by name among recruiters without a profile url. This both
    /// dedups url-less blocks across sibling folders and upgrades an existing
    /// url-less row when an agent later finds the LinkedIn url: the fill-only
    /// merge fills the url in place instead of inserting a duplicate row.
    /// </summary>
    private async Task<StoredRecruiter?> FindRecruiterByNameMatchAsync(
        RecruiterValues values,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(values.Name))
        {
            return null;
        }

        return await _store.FindRecruiterByNameAsync(values.Name, cancellationToken);
    }

    private static string? DisplayName(RecruiterValues values)
    {
        if (!string.IsNullOrWhiteSpace(values.Name))
        {
            return values.Name;
        }

        return values.ProfileUrl;
    }

    private static RecruiterValues MergeRecruiter(RecruiterValues current, RecruiterValues incoming)
    {
        var name = SelectValue(incoming.Name, current.Name);
        var title = SelectValue(incoming.Title, current.Title);
        var profileUrl = SelectValue(incoming.ProfileUrl, current.ProfileUrl);
        var location = SelectValue(incoming.Location, current.Location);
        var notes = SelectValue(incoming.Notes, current.Notes);
        return new RecruiterValues(
            Name: name ?? string.Empty,
            Title: title,
            ProfileUrl: profileUrl,
            Location: location,
            Notes: notes);
    }

    private static string? SelectValue(string? incoming, string? current)
    {
        if (!string.IsNullOrWhiteSpace(current))
        {
            return current;
        }

        return incoming;
    }

    private static ApplicationValues BuildApplicationValues(
        string relativeFolder,
        ApplicationMetadata metadata,
        string createdAt)
    {
        var nr = metadata.Nr ?? PrefixNumber(Path.GetFileName(relativeFolder));
        return new ApplicationValues(
            Folder: relativeFolder,
            Nr: nr,
            Title: metadata.Title,
            Company: metadata.Company,
            CompanyUrl: metadata.CompanyUrl,
            JobUrl: metadata.JobUrl,
            State: metadata.State,
            StateNote: metadata.StateNote,
            CreatedAt: createdAt,
            RecruiterId: null);
    }

    private static ApplicationValues CurrentValues(StoredApplication existing)
    {
        return new ApplicationValues(
            Folder: existing.Folder,
            Nr: existing.Nr,
            Title: existing.Title,
            Company: existing.Company,
            CompanyUrl: existing.CompanyUrl,
            JobUrl: existing.JobUrl,
            State: existing.State,
            StateNote: existing.StateNote,
            CreatedAt: existing.CreatedAt,
            RecruiterId: existing.RecruiterId);
    }

    private static ApplicationState ParseState(string wireName)
    {
        if (ApplicationStateExtensions.TryParseWireName(wireName, out var state))
        {
            return state;
        }

        return ApplicationState.Added;
    }

    private static string StateChangePayload(string from, string to)
    {
        return JsonSerializer.Serialize(new { from = from, to = to });
    }

    private static string EventTypeForStateChange(ApplicationState state)
    {
        if (state == ApplicationState.Generated)
        {
            return "generated";
        }

        if (state == ApplicationState.Sent)
        {
            return "sent";
        }

        if (state == ApplicationState.FollowedUp)
        {
            return "followed-up";
        }

        return "state_changed";
    }

    private static string? PrefixNumber(string folderName)
    {
        var underscore = folderName.IndexOf('_');
        if (underscore <= 0)
        {
            return null;
        }

        var prefix = folderName[..underscore];
        if (prefix.All(char.IsDigit))
        {
            return prefix;
        }

        return null;
    }
}
