using Npgsql;
using TVMusicFaso.Core.Licensing;
using TVMusicFaso.Core.Services;

namespace TVMusicFaso.Api;

/// <summary>Postes de travail enregistrés pour la licence, dans la base du serveur (table « licence_seats »).</summary>
public sealed class PostgresLicenceSeatStore : ILicenceSeatStore
{
    private readonly string _connectionString;
    private bool _tableReady;

    public PostgresLicenceSeatStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public IReadOnlyList<LicenceSeat> Load()
    {
        using var connection = Open();
        using var command = new NpgsqlCommand(
            "SELECT fingerprint, name, first_seen, last_seen FROM licence_seats ORDER BY last_seen DESC;",
            connection);
        using var reader = command.ExecuteReader();
        var seats = new List<LicenceSeat>();
        while (reader.Read())
        {
            seats.Add(new LicenceSeat(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return seats;
    }

    public void Save(LicenceSeat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        using var connection = Open();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO licence_seats (fingerprint, name, first_seen, last_seen)
            VALUES (@fingerprint, @name, @first, @last)
            ON CONFLICT (fingerprint) DO UPDATE SET name = EXCLUDED.name, last_seen = EXCLUDED.last_seen;
            """,
            connection);
        command.Parameters.AddWithValue("fingerprint", seat.Fingerprint);
        command.Parameters.AddWithValue("name", seat.Name);
        // Npgsql n'écrit un « timestamptz » qu'en UTC.
        command.Parameters.AddWithValue("first", seat.FirstSeen.ToUniversalTime());
        command.Parameters.AddWithValue("last", seat.LastSeen.ToUniversalTime());
        command.ExecuteNonQuery();
    }

    public void Remove(string fingerprint)
    {
        using var connection = Open();
        using var command = new NpgsqlCommand("DELETE FROM licence_seats WHERE fingerprint = @fingerprint;", connection);
        command.Parameters.AddWithValue("fingerprint", fingerprint);
        command.ExecuteNonQuery();
    }

    private NpgsqlConnection Open()
    {
        var connection = PostgresDatabase.Open(_connectionString);
        if (_tableReady)
        {
            return connection;
        }

        try
        {
            using var command = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS licence_seats (
                    fingerprint TEXT PRIMARY KEY,
                    name TEXT NOT NULL DEFAULT '',
                    first_seen TIMESTAMPTZ NOT NULL,
                    last_seen TIMESTAMPTZ NOT NULL
                );
                """,
                connection);
            command.ExecuteNonQuery();
            _tableReady = true;
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
