using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class SqliteAuditLog : IAuditLog
{
    private readonly string _databasePath;

    public SqliteAuditLog(string databasePath)
    {
        _databasePath = databasePath;
    }

    public void Write(UserSession? session, string action, string details)
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO audit_logs (Id, At, Actor, Role, Action, Details)
            VALUES ($id, $at, $actor, $role, $action, $details);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$at", DateTimeOffset.Now.ToString("o"));
        command.Parameters.AddWithValue("$actor", session?.User.FullName ?? "Système");
        command.Parameters.AddWithValue("$role", (int)(session?.User.Role ?? UserRole.Technicien));
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$details", details);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<AuditEntry> GetRecent(int take = 200)
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT Id, At, Actor, Role, Action, Details
            FROM audit_logs
            ORDER BY At DESC
            LIMIT {Math.Clamp(take, 1, 1000)};
            """;
        using var reader = command.ExecuteReader();
        var entries = new List<AuditEntry>();
        while (reader.Read())
        {
            entries.Add(new AuditEntry
            {
                Id = Guid.Parse(reader.GetString(0)),
                At = DateTimeOffset.Parse(reader.GetString(1)),
                Actor = reader.GetString(2),
                Role = (UserRole)reader.GetInt32(3),
                Action = reader.GetString(4),
                Details = reader.GetString(5)
            });
        }

        return entries;
    }
}

public sealed class NullAuditLog : IAuditLog
{
    public void Write(UserSession? session, string action, string details)
    {
    }

    public IReadOnlyList<AuditEntry> GetRecent(int take = 200) => [];
}
