using Microsoft.Data.Sqlite;

namespace TVMusicFaso.Core.Services;

internal static class SqliteDatabase
{
    public static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        EnsureSchema(connection);
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS clips (
                Id TEXT PRIMARY KEY,
                Title TEXT NOT NULL,
                Artist TEXT NOT NULL,
                Year TEXT NOT NULL,
                OriginPlace TEXT NOT NULL,
                FilmingLocation TEXT NOT NULL,
                IsBurkinabe INTEGER NOT NULL,
                Quality INTEGER NOT NULL,
                Format TEXT NOT NULL,
                DurationTicks INTEGER NOT NULL,
                Genre INTEGER NOT NULL,
                Language INTEGER NOT NULL,
                Theme INTEGER NOT NULL,
                Audience INTEGER NOT NULL,
                ImpactScore TEXT NOT NULL,
                IsPremium INTEGER NOT NULL,
                IsMorallyCompliant INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                LifetimePlayCount INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS day_schedules (
                Date TEXT PRIMARY KEY,
                Json TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS broadcast_logs (
                Id TEXT PRIMARY KEY,
                Date TEXT NOT NULL,
                Slot INTEGER NOT NULL,
                StartTime TEXT NOT NULL,
                ClipId TEXT NOT NULL,
                Title TEXT NOT NULL,
                Artist TEXT NOT NULL,
                Language INTEGER NOT NULL,
                Genre INTEGER NOT NULL,
                IsBurkinabe INTEGER NOT NULL,
                DurationTicks INTEGER NOT NULL,
                FilePath TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_broadcast_logs_date ON broadcast_logs (Date);

            CREATE INDEX IF NOT EXISTS idx_clips_language ON clips (Language);
            CREATE INDEX IF NOT EXISTS idx_clips_genre ON clips (Genre);
            CREATE INDEX IF NOT EXISTS idx_clips_theme ON clips (Theme);
            CREATE INDEX IF NOT EXISTS idx_clips_score ON clips (ImpactScore);
            CREATE INDEX IF NOT EXISTS idx_clips_artist ON clips (Artist);
            CREATE INDEX IF NOT EXISTS idx_clips_title ON clips (Title);

            CREATE TABLE IF NOT EXISTS users (
                Id TEXT PRIMARY KEY,
                FullName TEXT NOT NULL,
                UserName TEXT NOT NULL UNIQUE,
                PasswordHash TEXT NOT NULL,
                Role INTEGER NOT NULL,
                IsActive INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sessions (
                Token TEXT PRIMARY KEY,
                UserId TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS languages (
                Id TEXT PRIMARY KEY,
                Code TEXT NOT NULL UNIQUE,
                Label TEXT NOT NULL,
                EnumValue INTEGER NOT NULL,
                IsSeeded INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS audit_logs (
                Id TEXT PRIMARY KEY,
                At TEXT NOT NULL,
                Actor TEXT NOT NULL,
                Role INTEGER NOT NULL,
                Action TEXT NOT NULL,
                Details TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        AddColumn(connection, "clips", "CommitteeRating", "TEXT NOT NULL DEFAULT '3'");
        AddColumn(connection, "clips", "PopularityScore", "TEXT NOT NULL DEFAULT '3'");
        AddColumn(connection, "clips", "SocialScore", "TEXT NOT NULL DEFAULT '3'");
        AddColumn(connection, "clips", "ThumbnailPath", "TEXT NOT NULL DEFAULT ''");
        AddColumn(connection, "clips", "BroadcastFailureCount", "INTEGER NOT NULL DEFAULT 0");
        AddColumn(connection, "clips", "LastBroadcastFailureNote", "TEXT NOT NULL DEFAULT ''");
        AddColumn(connection, "clips", "ValidationStatus", "INTEGER NOT NULL DEFAULT 1");
        AddColumn(connection, "clips", "ValidationNote", "TEXT NOT NULL DEFAULT ''");
        AddColumn(connection, "clips", "LanguageName", "TEXT NOT NULL DEFAULT ''");
    }

    private static void AddColumn(SqliteConnection connection, string table, string column, string declaration)
    {
        using var info = connection.CreateCommand();
        info.CommandText = $"PRAGMA table_info({table});";
        using var reader = info.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        reader.Dispose();
        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {declaration};";
        alter.ExecuteNonQuery();
    }
}
