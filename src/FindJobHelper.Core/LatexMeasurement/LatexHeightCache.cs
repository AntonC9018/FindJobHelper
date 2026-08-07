using Microsoft.Data.Sqlite;

namespace FindJobHelper.CVGeneration;

internal sealed class LatexHeightCache(string databasePath, int ruleVersion)
{
    private const int SchemaVersion = 1;
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
        await ExecuteNonQueryAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken);

        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = """
                CREATE TABLE IF NOT EXISTS latex_height_measurement (
                    rule_version INTEGER NOT NULL,
                    measurement_kind TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    height_sp INTEGER NOT NULL CHECK (height_sp >= 0),
                    PRIMARY KEY (rule_version, measurement_kind, content_hash)
                );
                """;
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        var currentSchemaVersion = await ReadUserVersionAsync(connection, cancellationToken);
        if (currentSchemaVersion == 0)
        {
            await ExecuteNonQueryAsync(
                connection,
                $"PRAGMA user_version={SchemaVersion};",
                cancellationToken);
        }
        else if (currentSchemaVersion != SchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported LaTeX height cache schema version {currentSchemaVersion}; expected {SchemaVersion}.");
        }

        await using var purge = connection.CreateCommand();
        purge.CommandText = "DELETE FROM latex_height_measurement WHERE rule_version <> $rule_version;";
        purge.Parameters.AddWithValue("$rule_version", _ruleVersion);
        await purge.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<LatexMeasurementCacheKey, LatexHeight>> LoadAsync(
        IReadOnlyCollection<LatexMeasurementCacheKey> requiredKeys,
        CancellationToken cancellationToken)
    {
        if (requiredKeys.Count == 0)
        {
            return new Dictionary<LatexMeasurementCacheKey, LatexHeight>();
        }

        var required = requiredKeys.ToHashSet();
        var result = new Dictionary<LatexMeasurementCacheKey, LatexHeight>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT measurement_kind, content_hash, height_sp
            FROM latex_height_measurement
            WHERE rule_version = $rule_version;
            """;
        command.Parameters.AddWithValue("$rule_version", _ruleVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var kindText = reader.GetString(0);
            if (!Enum.TryParse<LatexMeasurementKind>(kindText, ignoreCase: false, out var kind))
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
        CancellationToken cancellationToken)
    {
        if (values.Count == 0)
        {
            return;
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var (key, height) in values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO latex_height_measurement (
                        rule_version,
                        measurement_kind,
                        content_hash,
                        height_sp)
                    VALUES ($rule_version, $measurement_kind, $content_hash, $height_sp)
                    ON CONFLICT(rule_version, measurement_kind, content_hash)
                    DO UPDATE SET height_sp = excluded.height_sp;
                    """;
                command.Parameters.AddWithValue("$rule_version", key.RuleVersion);
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

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 5,
        }.ToString();
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await ExecuteNonQueryAsync(connection, "PRAGMA busy_timeout=5000;", cancellationToken);
        return connection;
    }

    private static async Task<int> ReadUserVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
