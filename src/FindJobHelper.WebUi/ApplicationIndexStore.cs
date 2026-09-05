using System.Text;

namespace FindJobHelper.WebUi;

public enum ApplicationState
{
    Added,
    Generated,
    Sent,
    FollowedUp,
    NotApplicable,
    Other,
}

public static class ApplicationStateExtensions
{
    public static string ToWireName(this ApplicationState state) => state switch
    {
        ApplicationState.Added => "added",
        ApplicationState.Generated => "generated",
        ApplicationState.Sent => "sent",
        ApplicationState.FollowedUp => "followed-up",
        ApplicationState.NotApplicable => "n/a",
        ApplicationState.Other => "other",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    public static bool TryParseWireName(string value, out ApplicationState state)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "added":
                state = ApplicationState.Added;
                return true;
            case "generated":
                state = ApplicationState.Generated;
                return true;
            case "sent":
                state = ApplicationState.Sent;
                return true;
            case "followed-up":
            case "followed up":
            case "followup":
                state = ApplicationState.FollowedUp;
                return true;
            case "n/a":
            case "na":
            case "not-applicable":
                state = ApplicationState.NotApplicable;
                return true;
            case "other":
                state = ApplicationState.Other;
                return true;
            default:
                state = default;
                return false;
        }
    }

    /// <summary>
    /// Renders the canonical `status` column value: the state token plus an
    /// optional parenthesized note, mirroring the existing `N/A (Closed)` style.
    /// </summary>
    public static string ToStatusText(this ApplicationState state, string? note)
    {
        var wireName = state.ToWireName();
        var trimmedNote = note?.Trim();
        if (string.IsNullOrEmpty(trimmedNote))
        {
            return wireName;
        }

        return $"{wireName} ({trimmedNote})";
    }

    /// <summary>
    /// Derives the display state from the free-form `status` text that agents
    /// and the user have written historically. The raw text is preserved until
    /// the user explicitly assigns a state in the UI.
    /// </summary>
    public static (ApplicationState State, string? Note) DeriveFromStatus(string? rawStatus)
    {
        var raw = rawStatus?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return (ApplicationState.Added, null);
        }

        var lowered = raw.ToLowerInvariant();
        if (TryParseWireName(lowered, out var exactState))
        {
            return (exactState, ExtractNote(raw));
        }
        if (lowered.StartsWith("n/a", StringComparison.Ordinal))
        {
            return (ApplicationState.NotApplicable, raw);
        }
        if (lowered.StartsWith("cv generated", StringComparison.Ordinal)
            || lowered.StartsWith("generated", StringComparison.Ordinal))
        {
            return (ApplicationState.Generated, null);
        }
        if (lowered.StartsWith("sent", StringComparison.Ordinal)
            || lowered.StartsWith("applied", StringComparison.Ordinal)
            || lowered.StartsWith("https://", StringComparison.Ordinal)
            || lowered.StartsWith("mail seems broken", StringComparison.Ordinal))
        {
            return (ApplicationState.Sent, null);
        }
        if (lowered.StartsWith("followed", StringComparison.Ordinal))
        {
            return (ApplicationState.FollowedUp, raw);
        }

        return (ApplicationState.Other, raw);

        static string? ExtractNote(string raw)
        {
            var open = raw.IndexOf('(');
            var close = raw.LastIndexOf(')');
            if (open < 0 || close <= open)
            {
                return null;
            }

            return raw[(open + 1)..close].Trim() is { Length: > 0 } note ? note : null;
        }
    }
}

/// <summary>
/// Owns `sent/index.csv`: reads rows and rewrites the `status` column in
/// place. All file access goes through an exclusive lock so concurrent agent
/// appends stay safe, and updates are applied with an atomic file replace.
///
/// Frozen since fjw-w4u.4: the UI reads and writes sqlite only, so nothing
/// constructs this class anymore; `index.csv` stays an untouched archive
/// until the w4u.5 migration. Kept (not deleted) because the enum and helpers
/// above stay live, and the CSV parsing may inform the local migration.
/// </summary>
public sealed class ApplicationIndexStore
{
    public readonly record struct CsvField(string Value, bool Quoted);

    private static readonly string[] ColumnNames =
    [
        "nr",
        "title",
        "company",
        "job_url",
        "status",
        "date",
        "path",
    ];

    private readonly string _indexPath;

    public ApplicationIndexStore(string workspaceRoot)
    {
        _indexPath = Path.Combine(workspaceRoot, "sent", "index.csv");
    }

    public string IndexPath => _indexPath;

    public sealed class Row
    {
        public required string[] Fields { get; init; }

        public required int LineNumber { get; init; }

        public string? Nr => Field("nr");

        public string Title => Field("title") ?? string.Empty;

        public string Company => Field("company") ?? string.Empty;

        public string? JobUrl => Field("job_url");

        public string? Status => Field("status");

        public string? Date => Field("date");

        public string? Path => Field("path");

        public string? Field(string columnName)
        {
            var index = IndexFor(columnName);
            if (index < 0 || index >= Fields.Length)
            {
                return null;
            }

            var value = Fields[index].Trim();
            return value.Length == 0 ? null : value;
        }
    }

    public async Task<IReadOnlyList<Row>> ReadRowsAsync(CancellationToken cancellationToken)
    {
        var csv = await WithLockedFileAsync(
            async file =>
            {
                var bytes = await ReadAllBytesAsync(file, cancellationToken);
                return Decode(bytes).Text;
            },
            cancellationToken);
        var rows = Parse(csv).Lines;
        if (rows.Count > 0
            && rows[0].FirstOrDefault().Value.Trim() == "nr")
        {
            rows.RemoveAt(0);
        }

        return rows
            .Select((fields, index) => new Row
            {
                Fields = fields.Select(static field => field.Value).ToArray(),
                LineNumber = index + 1,
            })
            .ToList();
    }

    public async Task<bool> TryUpdateRowStatusAsync(
        string key,
        ApplicationState state,
        string? note,
        CancellationToken cancellationToken)
    {
        return await WithLockedFileAsync(
            async file =>
            {
                var bytes = await ReadAllBytesAsync(file, cancellationToken);
                var (text, encoding) = Decode(bytes);
                var (lines, header) = Parse(text);
                var statusIndex = header is null ? -1 : IndexFor(header, "status");
                if (statusIndex < 0)
                {
                    throw new InvalidOperationException(
                        "sent/index.csv does not have a 'status' column.");
                }

                var updated = false;
                for (var i = 1; i < lines.Count; i++)
                {
                    var fields = lines[i];
                    if (fields.Length == 0 || !MatchesKey(fields, key))
                    {
                        continue;
                    }

                    while (fields.Length <= statusIndex)
                    {
                        fields = [.. fields, default];
                    }

                    fields[statusIndex] = new CsvField(state.ToStatusText(note), Quoted: false);
                    lines[i] = fields;
                    updated = true;
                }

                if (!updated)
                {
                    return false;
                }

                var serialized = Serialize(lines, DetectNewline(text));
                await WriteInPlaceAsync(file, serialized, encoding, cancellationToken);
                return true;
            },
            cancellationToken);
    }

    // Rewrites the file through the exclusively-locked handle. File.Replace is
    // not an option here: it rejects a destination that is open, and dropping
    // the lock before the swap would let a concurrent agent append in between.
    private static async Task WriteInPlaceAsync(
        FileStream file,
        string content,
        Encoding encoding,
        CancellationToken cancellationToken)
    {
        file.Seek(0, SeekOrigin.Begin);
        file.SetLength(0);
        await using var writer = new StreamWriter(
            file,
            encoding,
            leaveOpen: true);
        await writer.WriteAsync(content);
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadAllBytesAsync(
        FileStream file,
        CancellationToken cancellationToken)
    {
        var buffer = new MemoryStream();
        file.Seek(0, SeekOrigin.Begin);
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static (string Text, Encoding Encoding) Decode(byte[] bytes)
    {
        var hasBom = bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF;
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom);
        var text = encoding.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        return (text, encoding);
    }

    private static bool MatchesKey(CsvField[] fields, string key)
    {
        var rowPath = GetField(fields, "path");
        if (rowPath is not null
            && string.Equals(
                rowPath.Replace('\\', '/'),
                key.Replace('\\', '/'),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var rowIndex = GetField(fields, "nr");
        if (rowIndex is null)
        {
            return false;
        }

        return string.Equals(rowIndex, key, StringComparison.Ordinal);
    }

    private static string? GetField(CsvField[] fields, string columnName)
    {
        var index = IndexFor(ColumnNames, columnName);
        if (index < 0 || index >= fields.Length)
        {
            return null;
        }

        var value = fields[index].Value.Trim();
        return value.Length == 0 ? null : value;
    }

    private static int IndexFor(string columnName) => IndexFor(ColumnNames, columnName);

    private static int IndexFor(string[] columns, string columnName)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            if (string.Equals(columns[i], columnName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private async Task<T> WithLockedFileAsync<T>(
        Func<FileStream, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_indexPath))
        {
            throw new FileNotFoundException(
                "sent/index.csv was not found. Is the workspace root correct?",
                _indexPath);
        }

        const int maxAttempts = 20;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream? file = null;
            try
            {
                file = new FileStream(
                    _indexPath,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return await action(file);
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                await Task.Delay(100, cancellationToken);
            }
            finally
            {
                file?.Dispose();
            }
        }
    }

    // --- RFC 4180 parsing / serialization -------------------------------------

    public static (List<CsvField[]> Lines, string[]? Header) Parse(string csv)
    {
        var lines = new List<CsvField[]>();
        var current = new List<CsvField>();
        var quotedFields = new List<bool>();
        var currentField = new StringBuilder();
        var inQuotes = false;
        var sawAnyField = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        currentField.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    currentField.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    sawAnyField = true;
                    quotedFields.Add(true);
                    break;
                case ',':
                    current.Add(new CsvField(currentField.ToString(), quotedFields.Count > 0));
                    currentField.Clear();
                    quotedFields.Clear();
                    sawAnyField = true;
                    break;
                case '\r' when i + 1 < csv.Length && csv[i + 1] == '\n':
                case '\n':
                    if (c == '\r')
                    {
                        i++;
                    }

                    current.Add(new CsvField(currentField.ToString(), quotedFields.Count > 0));
                    currentField.Clear();
                    quotedFields.Clear();
                    if (sawAnyField || current.Count > 1)
                    {
                        lines.Add([.. current]);
                    }
                    else
                    {
                        lines.Add([]);
                    }

                    current.Clear();
                    sawAnyField = false;
                    break;
                default:
                    currentField.Append(c);
                    sawAnyField = true;
                    break;
            }
        }

        if (inQuotes)
        {
            throw new InvalidOperationException("sent/index.csv contains an unterminated quoted field.");
        }

        if (sawAnyField || currentField.Length > 0)
        {
            current.Add(new CsvField(currentField.ToString(), quotedFields.Count > 0));
            lines.Add([.. current]);
        }

        string[]? header = null;
        if (lines.Count > 0 && lines[0].Length > 0)
        {
            header = lines[0].Select(static field => field.Value).ToArray();
        }

        return (lines, header);
    }

    public static string Serialize(List<CsvField[]> lines, string newline = "\n")
    {
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            var fields = lines[i];
            if (fields.Length == 0)
            {
                builder.Append(newline);
                continue;
            }

            for (var j = 0; j < fields.Length; j++)
            {
                if (j > 0)
                {
                    builder.Append(',');
                }

                AppendEscaped(builder, fields[j].Value, fields[j].Quoted);
            }

            builder.Append(newline);
        }

        return builder.ToString();
    }

    private static string DetectNewline(string csv) =>
        csv.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static void AppendEscaped(StringBuilder builder, string field, bool wasQuoted)
    {
        var needsQuotes = wasQuoted
            || field.Contains(',')
            || field.Contains('"')
            || field.Contains('\n')
            || field.Contains('\r');
        if (!needsQuotes)
        {
            builder.Append(field);
            return;
        }

        builder.Append('"');
        foreach (var c in field)
        {
            if (c == '"')
            {
                builder.Append("\"\"");
            }
            else
            {
                builder.Append(c);
            }
        }

        builder.Append('"');
    }
}
