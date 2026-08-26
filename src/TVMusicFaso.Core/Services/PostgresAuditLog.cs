using Npgsql;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class PostgresAuditLog : IAuditLog
{
    private readonly string _connectionString;

    public PostgresAuditLog(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void Write(UserSession? session, string action, string details)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(
            """
            INSERT INTO audit_logs (id, at, actor, role, action, details)
            VALUES (@id, @at, @actor, @role, @action, @details);
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("at", DateTimeOffset.Now);
        command.Parameters.AddWithValue("actor", session?.User.FullName ?? "Système");
        command.Parameters.AddWithValue("role", (int)(session?.User.Role ?? UserRole.Technicien));
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("details", details);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<AuditEntry> GetRecent(int take = 200)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(
            """
            SELECT id, at, actor, role, action, details
            FROM audit_logs
            ORDER BY at DESC
            LIMIT @take;
            """,
            connection);
        command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 1000));
        using var reader = command.ExecuteReader();
        var entries = new List<AuditEntry>();
        while (reader.Read())
        {
            entries.Add(new AuditEntry
            {
                Id = reader.GetGuid(0),
                At = reader.GetFieldValue<DateTimeOffset>(1),
                Actor = reader.GetString(2),
                Role = (UserRole)reader.GetInt32(3),
                Action = reader.GetString(4),
                Details = reader.GetString(5)
            });
        }

        return entries;
    }
}
