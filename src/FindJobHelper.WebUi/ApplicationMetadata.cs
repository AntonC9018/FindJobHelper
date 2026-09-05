using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FindJobHelper.WebUi;

/// <summary>Recruiter block of <c>metadata.json</c>. Present when name or profile url is set.</summary>
public sealed record RecruiterMetadata(
    string? Name,
    string? Title,
    string? ProfileUrl,
    string? Location,
    string? Notes)
{
    public bool IsPresent =>
        !string.IsNullOrWhiteSpace(Name) || !string.IsNullOrWhiteSpace(ProfileUrl);
}

/// <summary>
/// Agent-written per-folder contract ingested by Refresh (fjw-w4u.3).
/// Agents write this file inside the application folder; the server upserts
/// the sqlite store from it. Unknown properties are ignored so agents can add
/// context freely. Key aliases accept the obvious snake_case/camelCase
/// variants; <see cref="State"/> is always a canonical wire name
/// (<c>added</c>, <c>generated</c>, <c>sent</c>, <c>followed-up</c>,
/// <c>n/a</c>, <c>other</c>), derived with the same rules as the legacy
/// index.csv statuses.
/// </summary>
public sealed record ApplicationMetadata(
    string? Nr,
    string Title,
    string Company,
    string? CompanyUrl,
    string? JobUrl,
    string State,
    string? StateNote,
    RecruiterMetadata? Recruiter)
{
    public static bool TryParse(
        string json,
        [NotNullWhen(true)] out ApplicationMetadata? metadata,
        [NotNullWhen(false)] out string? error)
    {
        metadata = null;
        error = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "metadata.json must contain a JSON object.";
                return false;
            }

            var properties = IndexProperties(document.RootElement);
            var title = ReadText(properties, "title") ?? string.Empty;
            var company = ReadText(properties, "company") ?? string.Empty;
            var companyUrl = ReadText(properties, "company_url", "companyUrl");
            var jobUrl = ReadText(properties, "job_url", "jobUrl");
            var rawState = ReadText(properties, "state", "status");
            var (state, derivedNote) = ApplicationStateExtensions.DeriveFromStatus(rawState);
            var explicitNote = ReadText(properties, "state_note", "stateNote", "note");
            var stateNote = explicitNote ?? derivedNote;
            var recruiter = ReadRecruiter(properties);
            var nr = ReadNr(properties);
            var wireState = state.ToWireName();
            metadata = new ApplicationMetadata(
                Nr: nr,
                Title: title,
                Company: company,
                CompanyUrl: companyUrl,
                JobUrl: jobUrl,
                State: wireState,
                StateNote: stateNote,
                Recruiter: recruiter);
            return true;
        }
    }

    private static RecruiterMetadata? ReadRecruiter(Dictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("recruiter", out var element))
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var recruiterProperties = IndexProperties(element);
        var name = ReadText(recruiterProperties, "name");
        var title = ReadText(recruiterProperties, "title", "headline");
        var profileUrl = ReadText(recruiterProperties, "profile_url", "profileUrl", "url");
        var location = ReadText(recruiterProperties, "location");
        var notes = ReadText(recruiterProperties, "notes", "note");
        return new RecruiterMetadata(
            Name: name,
            Title: title,
            ProfileUrl: profileUrl,
            Location: location,
            Notes: notes);
    }

    private static string? ReadNr(Dictionary<string, JsonElement> properties)
    {
        if (!properties.TryGetValue("nr", out var nrElement))
        {
            properties.TryGetValue("number", out nrElement);
        }

        if (nrElement.ValueKind == JsonValueKind.String)
        {
            return Normalize(nrElement.GetString());
        }

        if (nrElement.ValueKind == JsonValueKind.Number)
        {
            return nrElement.GetRawText();
        }

        return null;
    }

    private static string? ReadText(Dictionary<string, JsonElement> properties, params string[] names)
    {
        foreach (var name in names)
        {
            if (!properties.TryGetValue(name, out var element))
            {
                continue;
            }

            if (element.ValueKind == JsonValueKind.String)
            {
                return Normalize(element.GetString());
            }

            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetRawText();
            }
        }

        return null;
    }

    private static Dictionary<string, JsonElement> IndexProperties(JsonElement element)
    {
        var index = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            index[property.Name] = property.Value;
        }

        return index;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    /// <summary>
    /// Patches the state fields of a <c>metadata.json</c> document in place,
    /// preserving every unknown property. Canonicalizes to <c>state</c> plus
    /// <c>status</c> (kept in sync for readers that only know the legacy key)
    /// and to <c>state_note</c> (aliases removed). Returns the rewritten JSON.
    /// </summary>
    public static string PatchStateJson(string json, string stateWireName, string? note)
    {
        var node = JsonNode.Parse(json);
        var root = node as JsonObject;
        if (root is null)
        {
            throw new InvalidOperationException("metadata.json must contain a JSON object.");
        }

        RemoveKeysCaseInsensitive(root, "state");
        RemoveKeysCaseInsensitive(root, "status");
        root["state"] = stateWireName;
        root["status"] = stateWireName;

        RemoveKeysCaseInsensitive(root, "state_note");
        RemoveKeysCaseInsensitive(root, "stateNote");
        RemoveKeysCaseInsensitive(root, "note");
        var trimmedNote = note?.Trim();
        var hasNote = !string.IsNullOrEmpty(trimmedNote);
        if (hasNote)
        {
            root["state_note"] = trimmedNote;
        }

        var options = new JsonSerializerOptions();
        options.WriteIndented = true;
        var patched = root.ToJsonString(options);
        return patched + Environment.NewLine;
    }

    private static void RemoveKeysCaseInsensitive(JsonObject root, string name)
    {
        var matches = new List<string>();
        foreach (var property in root)
        {
            var isMatch = string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase);
            if (isMatch)
            {
                matches.Add(property.Key);
            }
        }

        foreach (var match in matches)
        {
            root.Remove(match);
        }
    }
}
