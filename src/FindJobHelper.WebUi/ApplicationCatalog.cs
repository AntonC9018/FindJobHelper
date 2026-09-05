namespace FindJobHelper.WebUi;

public sealed record ApplicationFileSet(
    IReadOnlyList<string> AllFiles)
{
    public string? Pdf => AllFiles.FirstOrDefault(
            static name => string.Equals(name, "CurmanschiiAnton.pdf", StringComparison.OrdinalIgnoreCase))
        ?? AllFiles.FirstOrDefault(static name =>
            name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            && name.Contains("urman", StringComparison.OrdinalIgnoreCase))
        ?? AllFiles.FirstOrDefault(static name => name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

    public string? Config => AllFiles.FirstOrDefault(
        static name => string.Equals(name, "config.json", StringComparison.OrdinalIgnoreCase));

    public string? JobDescription => AllFiles.FirstOrDefault(
        static name => name.StartsWith("job", StringComparison.OrdinalIgnoreCase));

    public string? CompanyResearch => AllFiles.FirstOrDefault(
        static name => name.StartsWith("company", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "research.md", StringComparison.OrdinalIgnoreCase));

    public string? CoverLetter => AllFiles.FirstOrDefault(
        static name => name.StartsWith("cover", StringComparison.OrdinalIgnoreCase));

    public string? AnnotatedMarkdown => AllFiles.FirstOrDefault(
        static name => name.EndsWith("-debug.md", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Recruiter attached to an application, verbatim from the db.</summary>
public sealed record RecruiterView(
    string Name,
    string? Title,
    string? ProfileUrl,
    string? Location,
    string? Notes);

/// <summary>Single event in an application's timeline, verbatim.</summary>
public sealed record ApplicationEventView(
    long Id,
    string Type,
    string OccurredAt,
    string? Note,
    string? Payload);

/// <summary>
/// Another application sharing this recruiter that already has a followed-up
/// event. Dates are backend ISO strings rendered verbatim, never reformatted.
/// </summary>
public sealed record AlreadyTextedInfo(
    string ApplicationKey,
    string Title,
    string Company,
    string FollowedUpAt);

public sealed record ApplicationSummary(
    string Key,
    string? Nr,
    string Title,
    string Company,
    string? CompanyUrl,
    string? JobUrl,
    string State,
    string? StateNote,
    string CreatedAt,
    string? FolderPath,
    bool FolderExists,
    ApplicationFileSet Files,
    RecruiterView? Recruiter,
    IReadOnlyList<ApplicationEventView> Events,
    IReadOnlyList<AlreadyTextedInfo> AlreadyTexted);

/// <summary>
/// Builds the application list purely from sqlite (fjw-w4u.4). No reads from
/// <c>sent/index.csv</c>: the csv stays a frozen archive until the w4u.5
/// migration. Dates render backend strings verbatim; the client never formats
/// them. Contacted awareness comes from shared <c>recruiter_id</c> plus
/// followed-up events.
/// </summary>
public sealed class ApplicationCatalog
{
    private readonly JobStore _store;

    public ApplicationCatalog(JobStore store)
    {
        _store = store;
    }

    public async Task<IReadOnlyList<ApplicationSummary>> GetApplicationsAsync(
        CancellationToken cancellationToken)
    {
        var applications = await _store.ListApplicationsAsync(cancellationToken);
        var recruiters = await _store.ListRecruitersAsync(cancellationToken);
        var events = await _store.ListEventsAsync(cancellationToken);
        var recruiterById = IndexRecruiters(recruiters);
        var eventsByApplication = GroupEvents(events);
        var followedUpByRecruiter = BuildFollowedUpMap(applications, eventsByApplication);
        var root = _store.ResolveApplicationsRoot();
        var summaries = new List<ApplicationSummary>();
        foreach (var application in applications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summary = BuildSummary(application, root, recruiterById, eventsByApplication, followedUpByRecruiter);
            summaries.Add(summary);
        }

        return summaries;
    }

    /// <summary>
    /// Resolves a UI key through <see cref="JobStore.FindApplicationByKeyAsync"/>
    /// (the single owner of canonical plus legacy key forms) and returns the
    /// matching summary. The full list keeps one summary-building path; the
    /// state endpoint just ran a full Refresh, so the extra scan is noise, and
    /// generation completion hits this rarely.
    /// </summary>
    public async Task<ApplicationSummary?> FindByKeyAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var stored = await _store.FindApplicationByKeyAsync(key, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var applications = await GetApplicationsAsync(cancellationToken);
        foreach (var application in applications)
        {
            var isMatch = string.Equals(application.Key, stored.Folder, StringComparison.OrdinalIgnoreCase);
            if (isMatch)
            {
                return application;
            }
        }

        return null;
    }

    /// <summary>Resolves an application key to a full folder path on disk.</summary>
    public async Task<string?> ResolveFolderAsync(
        string key,
        CancellationToken cancellationToken)
    {
        var stored = await _store.FindApplicationByKeyAsync(key, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var root = _store.ResolveApplicationsRoot();
        var relative = stored.Folder.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.Combine(root, relative);
        var fullPath = Path.GetFullPath(combined);
        var exists = Directory.Exists(fullPath);
        if (!exists)
        {
            return null;
        }

        return fullPath;
    }

    private ApplicationSummary BuildSummary(
        StoredApplication application,
        string root,
        IReadOnlyDictionary<long, StoredRecruiter> recruiterById,
        IReadOnlyDictionary<long, IReadOnlyList<StoredEvent>> eventsByApplication,
        IReadOnlyDictionary<long, IReadOnlyList<AlreadyTextedInfo>> followedUpByRecruiter)
    {
        var relative = application.Folder.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.Combine(root, relative);
        var fullPath = Path.GetFullPath(combined);
        var folderExists = Directory.Exists(fullPath);
        var files = ScanFiles(folderExists ? fullPath : null);
        var recruiter = BuildRecruiterView(application, recruiterById);
        var events = BuildEventViews(application, eventsByApplication);
        var alreadyTexted = BuildAlreadyTexted(application, followedUpByRecruiter);
        return new ApplicationSummary(
            Key: application.Folder,
            Nr: application.Nr,
            Title: application.Title,
            Company: application.Company,
            CompanyUrl: application.CompanyUrl,
            JobUrl: application.JobUrl,
            State: application.State,
            StateNote: application.StateNote,
            CreatedAt: application.CreatedAt,
            FolderPath: application.Folder,
            FolderExists: folderExists,
            Files: files,
            Recruiter: recruiter,
            Events: events,
            AlreadyTexted: alreadyTexted);
    }

    private static RecruiterView? BuildRecruiterView(
        StoredApplication application,
        IReadOnlyDictionary<long, StoredRecruiter> recruiterById)
    {
        if (application.RecruiterId is not { } recruiterId)
        {
            return null;
        }

        var hasRecruiter = recruiterById.TryGetValue(recruiterId, out var stored);
        if (!hasRecruiter)
        {
            return null;
        }

        return new RecruiterView(
            Name: stored!.Name,
            Title: stored.Title,
            ProfileUrl: stored.ProfileUrl,
            Location: stored.Location,
            Notes: stored.Notes);
    }

    private static IReadOnlyList<ApplicationEventView> BuildEventViews(
        StoredApplication application,
        IReadOnlyDictionary<long, IReadOnlyList<StoredEvent>> eventsByApplication)
    {
        var hasEvents = eventsByApplication.TryGetValue(application.Id, out var stored);
        if (!hasEvents)
        {
            return [];
        }

        var views = new List<ApplicationEventView>();
        foreach (var storedEvent in stored!)
        {
            var view = new ApplicationEventView(
                Id: storedEvent.Id,
                Type: storedEvent.Type,
                OccurredAt: storedEvent.OccurredAt,
                Note: storedEvent.Note,
                Payload: storedEvent.Payload);
            views.Add(view);
        }

        return views;
    }

    private static IReadOnlyList<AlreadyTextedInfo> BuildAlreadyTexted(
        StoredApplication application,
        IReadOnlyDictionary<long, IReadOnlyList<AlreadyTextedInfo>> followedUpByRecruiter)
    {
        if (application.RecruiterId is not { } recruiterId)
        {
            return [];
        }

        var hasGroup = followedUpByRecruiter.TryGetValue(recruiterId, out var group);
        if (!hasGroup)
        {
            return [];
        }

        var others = new List<AlreadyTextedInfo>();
        foreach (var info in group!)
        {
            var isSelf = string.Equals(info.ApplicationKey, application.Folder, StringComparison.OrdinalIgnoreCase);
            if (isSelf)
            {
                continue;
            }

            others.Add(info);
        }

        return others;
    }

    /// <summary>
    /// Maps each recruiter id to the other applications sharing it that have a
    /// followed-up event, with the latest followed-up date per application.
    /// </summary>
    private static IReadOnlyDictionary<long, IReadOnlyList<AlreadyTextedInfo>> BuildFollowedUpMap(
        IReadOnlyList<StoredApplication> applications,
        IReadOnlyDictionary<long, IReadOnlyList<StoredEvent>> eventsByApplication)
    {
        var followedUpAtByApplication = new Dictionary<long, string>();
        foreach (var application in applications)
        {
            var latest = LatestFollowedUpAt(application.Id, eventsByApplication);
            if (latest is not null)
            {
                followedUpAtByApplication[application.Id] = latest;
            }
        }

        var groups = new Dictionary<long, List<AlreadyTextedInfo>>();
        foreach (var application in applications)
        {
            if (application.RecruiterId is not { } recruiterId)
            {
                continue;
            }

            var hasFollowedUp = followedUpAtByApplication.TryGetValue(application.Id, out var followedUpAt);
            if (!hasFollowedUp)
            {
                continue;
            }

            var info = new AlreadyTextedInfo(
                ApplicationKey: application.Folder,
                Title: application.Title,
                Company: application.Company,
                FollowedUpAt: followedUpAt!);
            var hasGroup = groups.TryGetValue(recruiterId, out var group);
            if (!hasGroup)
            {
                group = [];
                groups[recruiterId] = group;
            }

            group!.Add(info);
        }

        var ordered = new Dictionary<long, IReadOnlyList<AlreadyTextedInfo>>();
        foreach (var pair in groups)
        {
            var sorted = pair.Value
                .OrderByDescending(static info => info.FollowedUpAt, StringComparer.Ordinal)
                .ThenBy(static info => info.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            ordered[pair.Key] = sorted;
        }

        return ordered;
    }

    private static string? LatestFollowedUpAt(
        long applicationId,
        IReadOnlyDictionary<long, IReadOnlyList<StoredEvent>> eventsByApplication)
    {
        var hasEvents = eventsByApplication.TryGetValue(applicationId, out var events);
        if (!hasEvents)
        {
            return null;
        }

        string? latest = null;
        foreach (var stored in events!)
        {
            var isFollowedUp = string.Equals(stored.Type, "followed-up", StringComparison.Ordinal);
            if (!isFollowedUp)
            {
                continue;
            }

            var isLater = latest is null;
            if (!isLater)
            {
                isLater = string.Compare(stored.OccurredAt, latest, StringComparison.Ordinal) >= 0;
            }

            if (isLater)
            {
                latest = stored.OccurredAt;
            }
        }

        return latest;
    }

    private static Dictionary<long, StoredRecruiter> IndexRecruiters(
        IReadOnlyList<StoredRecruiter> recruiters)
    {
        var index = new Dictionary<long, StoredRecruiter>();
        foreach (var recruiter in recruiters)
        {
            index[recruiter.Id] = recruiter;
        }

        return index;
    }

    private static Dictionary<long, IReadOnlyList<StoredEvent>> GroupEvents(
        IReadOnlyList<StoredEvent> events)
    {
        var grouped = new Dictionary<long, List<StoredEvent>>();
        foreach (var stored in events)
        {
            var hasGroup = grouped.TryGetValue(stored.ApplicationId, out var group);
            if (!hasGroup)
            {
                group = [];
                grouped[stored.ApplicationId] = group;
            }

            group!.Add(stored);
        }

        var index = new Dictionary<long, IReadOnlyList<StoredEvent>>();
        foreach (var pair in grouped)
        {
            index[pair.Key] = pair.Value;
        }

        return index;
    }

    private static ApplicationFileSet ScanFiles(string? folderFullPath)
    {
        if (folderFullPath is null || !Directory.Exists(folderFullPath))
        {
            return new ApplicationFileSet([]);
        }

        var files = Directory.EnumerateFiles(folderFullPath)
            .Select(static path => Path.GetFileName(path))
            .Where(static name => !name.EndsWith(".aux", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".out", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".fls", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".fdb_latexmk", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".xdv", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".synctex.gz", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ApplicationFileSet(files);
    }
}
