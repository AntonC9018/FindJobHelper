using Microsoft.Data.Sqlite;

namespace FindJobHelper.CVGeneration;

internal sealed class LatexHeightCache(string databasePath, int ruleVersion)
{
    private const int SchemaVersion = 2;
    private static readonly LatexFontRoleArray<string> FontParameterNames = new(
        main: "$main_font",
        sans: "$sans_font",
        monospace: "$mono_font");
    private readonly string _databasePath = databasePath;
    private readonly int _ruleVersion = ruleVersion;

    public static string DefaultPath
    {
        get
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                throw new InvalidOperationException("The local application-data directory could not be resolved.");
            }
            return Path.Combine(localApplicationData, "FindJobHelper", "latex-height-cache.sqlite3");
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(_databasePath)
            ?? throw new InvalidOperationException($"Cache path '{_databasePath}' has no parent directory.");
        Directory.CreateDirectory(parent);

        await using var connection = await OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);
        var currentSchemaVersion = await ReadUserVersionAsync(connection, cancellationToken);
        if (currentSchemaVersion != 0 && currentSchemaVersion != SchemaVersion)
        {
            await ExecuteNonQueryAsync(connection, "DROP TABLE IF EXISTS latex_height_measurement;", cancellationToken);
            await ExecuteNonQueryAsync(connection, "DROP TABLE IF EXISTS latex_measurement_run;", cancellationToken);
        }

        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE IF NOT EXISTS latex_measurement_run (
                run_id INTEGER PRIMARY KEY,
                rule_version INTEGER NOT NULL,
                main_font TEXT NOT NULL,
                sans_font TEXT NOT NULL,
                mono_font TEXT NOT NULL,
                UNIQUE (rule_version, main_font, sans_font, mono_font)
            );
            CREATE TABLE IF NOT EXISTS latex_height_measurement (
                run_id INTEGER NOT NULL REFERENCES latex_measurement_run(run_id) ON DELETE CASCADE,
                measurement_kind TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                height_sp INTEGER NOT NULL CHECK (height_sp >= 0),
                PRIMARY KEY (run_id, measurement_kind, content_hash)
            );
            """, cancellationToken);
        await ExecuteNonQueryAsync(connection, $"PRAGMA user_version={SchemaVersion};", cancellationToken);
    }

    public async Task<IReadOnlyDictionary<LatexMeasurementCacheKey, LatexHeight>> LoadAsync(
        IReadOnlyCollection<LatexMeasurementCacheKey> requiredKeys,
        LatexFontOptions fonts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        if (requiredKeys.Count == 0)
        {
            return new Dictionary<LatexMeasurementCacheKey, LatexHeight>();
        }

        var required = requiredKeys.ToHashSet();
        var result = new Dictionary<LatexMeasurementCacheKey, LatexHeight>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT height.measurement_kind, height.content_hash, height.height_sp
            FROM latex_height_measurement AS height
            INNER JOIN latex_measurement_run AS run ON run.run_id = height.run_id
            WHERE run.rule_version = $rule_version
              AND run.main_font = $main_font
              AND run.sans_font = $sans_font
              AND run.mono_font = $mono_font;
            """;
        AddContextParameters(command, fonts);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!Enum.TryParse<LatexMeasurementKind>(reader.GetString(0), ignoreCase: false, out var kind))
            {
                continue;
            }
            var key = new LatexMeasurementCacheKey(_ruleVersion, kind, reader.GetString(1));
            if (required.Contains(key))
            {
                result.Add(key, new LatexHeight(reader.GetInt64(2)));
            }
        }
        return result;
    }

    public async Task StoreAsync(
        IReadOnlyDictionary<LatexMeasurementCacheKey, LatexHeight> values,
        LatexFontOptions fonts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        if (values.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var runId = await ResolveRunIdAsync(connection, transaction, fonts, cancellationToken);
            foreach (var (key, height) in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO latex_height_measurement (run_id, measurement_kind, content_hash, height_sp)
                    VALUES ($run_id, $measurement_kind, $content_hash, $height_sp)
                    ON CONFLICT(run_id, measurement_kind, content_hash)
                    DO UPDATE SET height_sp = excluded.height_sp;
                    """;
                command.Parameters.AddWithValue("$run_id", runId);
                command.Parameters.AddWithValue("$measurement_kind", key.Kind.ToString());
                command.Parameters.AddWithValue("$content_hash", key.ContentHash);
                command.Parameters.AddWithValue("$height_sp", height.ScaledPoints);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<long> ResolveRunIdAsync(SqliteConnection connection, SqliteTransaction transaction,
        LatexFontOptions fonts, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO latex_measurement_run (rule_version, main_font, sans_font, mono_font)
            VALUES ($rule_version, $main_font, $sans_font, $mono_font)
            ON CONFLICT(rule_version, main_font, sans_font, mono_font) DO NOTHING;
            SELECT run_id FROM latex_measurement_run
            WHERE rule_version = $rule_version AND main_font = $main_font
              AND sans_font = $sans_font AND mono_font = $mono_font;
            """;
        AddContextParameters(command, fonts);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private void AddContextParameters(SqliteCommand command, LatexFontOptions fonts)
    {
        command.Parameters.AddWithValue("$rule_version", _ruleVersion);
        foreach (var role in LatexFontRoles.All)
        {
            command.Parameters.AddWithValue(FontParameterNames[role], fonts[role].Value);
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath, Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared, Pooling = false, DefaultTimeout = 5,
        }.ToString());
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken);
        return connection;
    }

    private static async Task<int> ReadUserVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
