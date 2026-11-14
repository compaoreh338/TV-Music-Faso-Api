using System.Collections.Concurrent;
using Npgsql;
using NpgsqlTypes;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>
/// Arrivée des clips dans PostgreSQL. Chaque source et chaque clip arrivé est un document JSON,
/// avec à côté les colonnes utiles aux recherches (étape, identifiant de plateforme, priorité).
/// </summary>
public sealed class PostgresIngestStore : IIngestStore
{
    private static readonly ConcurrentDictionary<string, bool> SchemaReady = new(StringComparer.Ordinal);

    private readonly string _connectionString;

    public PostgresIngestStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public IReadOnlyList<MonitoredSource> GetSources()
    {
        using var connection = Open();
        using var command = new NpgsqlCommand("SELECT data FROM ingest_sources ORDER BY priority DESC, name;", connection);
        return ReadDocuments<MonitoredSource>(command);
    }

    public MonitoredSource? GetSource(Guid id)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand("SELECT data FROM ingest_sources WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", id);
        return ReadDocuments<MonitoredSource>(command).FirstOrDefault();
    }

    public void SaveSource(MonitoredSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var connection = Open();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO ingest_sources (id, name, priority, data, updated_at)
            VALUES (@id, @name, @priority, @data, now())
            ON CONFLICT (id) DO UPDATE SET name = EXCLUDED.name, priority = EXCLUDED.priority,
                data = EXCLUDED.data, updated_at = now();
            """,
            connection);
        command.Parameters.AddWithValue("id", source.Id);
        command.Parameters.AddWithValue("name", source.Name);
        command.Parameters.AddWithValue("priority", source.Priority);
        command.Parameters.Add(Json("data", source));
        command.ExecuteNonQuery();
    }

    public bool DeleteSource(Guid id)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand("DELETE FROM ingest_sources WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", id);
        return command.ExecuteNonQuery() > 0;
    }

    public IReadOnlyList<IngestItem> GetItems(IReadOnlyCollection<IngestStage>? stages = null, int limit = 500)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            stages is null
                ? "SELECT data FROM ingest_items ORDER BY detected_at DESC LIMIT @limit;"
                : "SELECT data FROM ingest_items WHERE stage = ANY(@stages) ORDER BY detected_at DESC LIMIT @limit;",
            connection);
        command.Parameters.AddWithValue("limit", Math.Clamp(limit, 1, 5000));
        if (stages is not null)
        {
            command.Parameters.AddWithValue("stages", stages.Select(stage => (int)stage).ToArray());
        }

        return ReadDocuments<IngestItem>(command);
    }

    public IngestItem? GetItem(Guid id)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand("SELECT data FROM ingest_items WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", id);
        return ReadDocuments<IngestItem>(command).FirstOrDefault();
    }

    public IngestItem? FindByExternalId(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        using var connection = Open();
        using var command = new NpgsqlCommand("SELECT data FROM ingest_items WHERE external_id = @external LIMIT 1;", connection);
        command.Parameters.AddWithValue("external", externalId);
        return ReadDocuments<IngestItem>(command).FirstOrDefault();
    }

    public void AddItem(IngestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var connection = Open();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO ingest_items (id, stage, external_id, priority, detected_at, next_attempt_at, updated_at, data)
            VALUES (@id, @stage, @external, @priority, @detected, @next, @updated, @data);
            """,
            connection);
        BindItem(command, item, PriorityOf(connection, item.SourceId));
        try
        {
            command.ExecuteNonQuery();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new InvalidOperationException($"Vidéo déjà connue : {item.ExternalId}", exception);
        }
    }

    public void UpdateItem(IngestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        using var connection = Open();
        if (WriteItem(connection, null, item) == 0)
        {
            throw new InvalidOperationException("Clip arrivé introuvable.");
        }
    }

    public IngestItem? ClaimNext(IngestStage from, IngestStage to, DateTimeOffset now)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        IngestItem? item;
        using (var select = new NpgsqlCommand(
                   """
                   SELECT data FROM ingest_items
                   WHERE stage = @from AND (next_attempt_at IS NULL OR next_attempt_at <= @now)
                   ORDER BY priority DESC, detected_at
                   LIMIT 1
                   FOR UPDATE SKIP LOCKED;
                   """,
                   connection,
                   transaction))
        {
            select.Parameters.AddWithValue("from", (int)from);
            select.Parameters.AddWithValue("now", now.ToUniversalTime());
            item = ReadDocuments<IngestItem>(select).FirstOrDefault();
        }

        if (item is null)
        {
            transaction.Commit();
            return null;
        }

        item.Stage = to;
        item.UpdatedAt = now;
        WriteItem(connection, transaction, item);
        transaction.Commit();
        return item;
    }

    public int ResetStage(IngestStage from, IngestStage to)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        List<IngestItem> stuck;
        using (var select = new NpgsqlCommand("SELECT data FROM ingest_items WHERE stage = @from FOR UPDATE;", connection, transaction))
        {
            select.Parameters.AddWithValue("from", (int)from);
            stuck = ReadDocuments<IngestItem>(select);
        }

        foreach (var item in stuck)
        {
            item.Stage = to;
            WriteItem(connection, transaction, item);
        }

        transaction.Commit();
        return stuck.Count;
    }

    public string? GetValue(string key)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand("SELECT value::text FROM ingest_values WHERE key = @key;", connection);
        command.Parameters.AddWithValue("key", key);
        return command.ExecuteScalar() as string;
    }

    public void SetValue(string key, string json)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO ingest_values (key, value, updated_at) VALUES (@key, @value, now())
            ON CONFLICT (key) DO UPDATE SET value = EXCLUDED.value, updated_at = now();
            """,
            connection);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.Add(new NpgsqlParameter("value", NpgsqlDbType.Jsonb) { Value = json });
        command.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, string> GetMemory(string scope)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand("SELECT field, value FROM ingest_memory WHERE scope = @scope;", connection);
        command.Parameters.AddWithValue("scope", scope);
        using var reader = command.ExecuteReader();
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            values[reader.GetString(0)] = reader.GetString(1);
        }

        return values;
    }

    public void Remember(string scope, string field, string value)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO ingest_memory (scope, field, value, updated_at) VALUES (@scope, @field, @value, now())
            ON CONFLICT (scope, field) DO UPDATE SET value = EXCLUDED.value, updated_at = now();
            """,
            connection);
        command.Parameters.AddWithValue("scope", scope);
        command.Parameters.AddWithValue("field", field);
        command.Parameters.AddWithValue("value", value);
        command.ExecuteNonQuery();
    }

    public IngestSummary Summarize(DateTimeOffset since)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            """
            SELECT
                COUNT(*) FILTER (WHERE stage = 0),
                COUNT(*) FILTER (WHERE stage IN (1, 2, 3, 4)),
                COUNT(*) FILTER (WHERE stage = 5),
                COUNT(*) FILTER (WHERE stage IN (6, 7)),
                COUNT(*) FILTER (WHERE stage = 9 AND updated_at >= @since)
            FROM ingest_items;
            """,
            connection);
        command.Parameters.AddWithValue("since", since.ToUniversalTime());
        using var reader = command.ExecuteReader();
        reader.Read();
        return new IngestSummary(
            (int)reader.GetInt64(0),
            (int)reader.GetInt64(1),
            (int)reader.GetInt64(2),
            (int)reader.GetInt64(3),
            (int)reader.GetInt64(4));
    }

    private NpgsqlConnection Open()
    {
        var connection = PostgresDatabase.Open(_connectionString);
        if (!SchemaReady.ContainsKey(_connectionString))
        {
            EnsureSchema(connection);
            SchemaReady[_connectionString] = true;
        }

        return connection;
    }

    private static void EnsureSchema(NpgsqlConnection connection)
    {
        using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS ingest_sources (
                id UUID PRIMARY KEY,
                name TEXT NOT NULL,
                priority INTEGER NOT NULL DEFAULT 3,
                data JSONB NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS ingest_items (
                id UUID PRIMARY KEY,
                stage INTEGER NOT NULL,
                external_id TEXT NOT NULL DEFAULT '',
                priority INTEGER NOT NULL DEFAULT 0,
                detected_at TIMESTAMPTZ NOT NULL,
                next_attempt_at TIMESTAMPTZ NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                data JSONB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_ingest_items_stage ON ingest_items (stage, priority DESC, detected_at);
            CREATE UNIQUE INDEX IF NOT EXISTS idx_ingest_items_external ON ingest_items (external_id) WHERE external_id <> '';
            CREATE TABLE IF NOT EXISTS ingest_values (
                key TEXT PRIMARY KEY,
                value JSONB NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
            );
            CREATE TABLE IF NOT EXISTS ingest_memory (
                scope TEXT NOT NULL,
                field TEXT NOT NULL,
                value TEXT NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL DEFAULT now(),
                PRIMARY KEY (scope, field)
            );
            """,
            connection);
        command.ExecuteNonQuery();
    }

    private static int WriteItem(NpgsqlConnection connection, NpgsqlTransaction? transaction, IngestItem item)
    {
        using var command = new NpgsqlCommand(
            """
            UPDATE ingest_items SET stage = @stage, external_id = @external, detected_at = @detected,
                next_attempt_at = @next, updated_at = @updated, data = @data,
                priority = COALESCE((SELECT priority FROM ingest_sources WHERE id = @source), priority)
            WHERE id = @id;
            """,
            connection,
            transaction);
        BindItem(command, item, 0);
        command.Parameters.AddWithValue("source", (object?)item.SourceId ?? DBNull.Value).NpgsqlDbType = NpgsqlDbType.Uuid;
        return command.ExecuteNonQuery();
    }

    private static void BindItem(NpgsqlCommand command, IngestItem item, int priority)
    {
        command.Parameters.AddWithValue("id", item.Id);
        command.Parameters.AddWithValue("stage", (int)item.Stage);
        command.Parameters.AddWithValue("external", item.ExternalId ?? string.Empty);
        command.Parameters.AddWithValue("priority", priority);
        command.Parameters.AddWithValue("detected", item.DetectedAt.ToUniversalTime());
        command.Parameters.Add(new NpgsqlParameter("next", NpgsqlDbType.TimestampTz)
        {
            Value = item.NextAttemptAt is { } next ? next.ToUniversalTime() : DBNull.Value
        });
        command.Parameters.AddWithValue("updated", item.UpdatedAt.ToUniversalTime());
        command.Parameters.Add(Json("data", item));
    }

    private static int PriorityOf(NpgsqlConnection connection, Guid? sourceId)
    {
        if (sourceId is null)
        {
            return 0;
        }

        using var command = new NpgsqlCommand("SELECT priority FROM ingest_sources WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", sourceId.Value);
        return command.ExecuteScalar() is int priority ? priority : 0;
    }

    private static NpgsqlParameter Json<T>(string name, T value) =>
        new(name, NpgsqlDbType.Jsonb) { Value = IngestJson.Serialize(value) };

    private static List<T> ReadDocuments<T>(NpgsqlCommand command)
    {
        using var reader = command.ExecuteReader();
        var results = new List<T>();
        while (reader.Read())
        {
            if (IngestJson.Deserialize<T>(reader.GetString(0)) is { } document)
            {
                results.Add(document);
            }
        }

        return results;
    }
}
