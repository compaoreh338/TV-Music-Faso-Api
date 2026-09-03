using Npgsql;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public static class PostgresDatabase
{
    public static NpgsqlConnection Open(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        connection.Open();
        EnsureSchema(connection);
        return connection;
    }

    public static void EnsureReady(string connectionString)
    {
        if (!TryConnect(connectionString, out var error))
        {
            throw new InvalidOperationException(
                "PostgreSQL est requis (offre §2.2). Lancez Docker puis relancez l'application :\n\n" +
                "  docker compose up -d\n\n" +
                error);
        }

        using var connection = Open(connectionString);
        EnsureSeed(connection, connectionString);
    }

    public static bool TryConnect(string connectionString, out string error) =>
        PostgresClipRepository.TryConnect(connectionString, out error);

    public static void EnsureSchema(NpgsqlConnection connection)
    {
        using var command = new NpgsqlCommand(
            """
            CREATE TABLE IF NOT EXISTS clips (
                id UUID PRIMARY KEY,
                title TEXT NOT NULL,
                artist TEXT NOT NULL,
                year NUMERIC NOT NULL,
                origin_place TEXT NOT NULL,
                filming_location TEXT NOT NULL,
                is_burkinabe BOOLEAN NOT NULL,
                quality INTEGER NOT NULL,
                format TEXT NOT NULL,
                duration_ticks BIGINT NOT NULL,
                genre INTEGER NOT NULL,
                language INTEGER NOT NULL,
                theme INTEGER NOT NULL,
                audience INTEGER NOT NULL,
                impact_score NUMERIC NOT NULL,
                is_premium BOOLEAN NOT NULL,
                is_morally_compliant BOOLEAN NOT NULL,
                file_path TEXT NOT NULL,
                lifetime_play_count INTEGER NOT NULL,
                committee_rating NUMERIC NOT NULL DEFAULT 3,
                popularity_score NUMERIC NOT NULL DEFAULT 3,
                social_score NUMERIC NOT NULL DEFAULT 3,
                thumbnail_path TEXT NOT NULL DEFAULT '',
                broadcast_failure_count INTEGER NOT NULL DEFAULT 0,
                last_broadcast_failure_note TEXT NOT NULL DEFAULT '',
                validation_status INTEGER NOT NULL DEFAULT 1,
                validation_note TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS idx_clips_language ON clips (language);
            CREATE INDEX IF NOT EXISTS idx_clips_genre ON clips (genre);
            CREATE INDEX IF NOT EXISTS idx_clips_theme ON clips (theme);
            CREATE INDEX IF NOT EXISTS idx_clips_score ON clips (impact_score);
            CREATE INDEX IF NOT EXISTS idx_clips_artist ON clips (artist);
            CREATE INDEX IF NOT EXISTS idx_clips_title ON clips (title);

            CREATE TABLE IF NOT EXISTS users (
                id UUID PRIMARY KEY,
                full_name TEXT NOT NULL,
                user_name TEXT NOT NULL UNIQUE,
                password_hash TEXT NOT NULL,
                role INTEGER NOT NULL,
                is_active BOOLEAN NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_users_name ON users (user_name);

            CREATE TABLE IF NOT EXISTS sessions (
                token TEXT PRIMARY KEY,
                user_id UUID NOT NULL REFERENCES users (id) ON DELETE CASCADE,
                expires_at TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS day_schedules (
                date DATE PRIMARY KEY,
                json JSONB NOT NULL
            );

            CREATE TABLE IF NOT EXISTS broadcast_logs (
                id UUID PRIMARY KEY,
                date DATE NOT NULL,
                slot INTEGER NOT NULL,
                start_time TIME NOT NULL,
                clip_id UUID NOT NULL,
                title TEXT NOT NULL,
                artist TEXT NOT NULL,
                language INTEGER NOT NULL,
                genre INTEGER NOT NULL,
                is_burkinabe BOOLEAN NOT NULL,
                duration_ticks BIGINT NOT NULL,
                file_path TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_broadcast_logs_date ON broadcast_logs (date);

            CREATE TABLE IF NOT EXISTS audit_logs (
                id UUID PRIMARY KEY,
                at TIMESTAMPTZ NOT NULL,
                actor TEXT NOT NULL,
                role INTEGER NOT NULL,
                action TEXT NOT NULL,
                details TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_audit_logs_at ON audit_logs (at DESC);

            CREATE TABLE IF NOT EXISTS languages (
                id UUID PRIMARY KEY,
                code TEXT NOT NULL UNIQUE,
                label TEXT NOT NULL,
                enum_value INTEGER NOT NULL,
                is_seeded BOOLEAN NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_languages_label ON languages (label);

            ALTER TABLE clips ADD COLUMN IF NOT EXISTS validation_status INTEGER NOT NULL DEFAULT 1;
            ALTER TABLE clips ADD COLUMN IF NOT EXISTS validation_note TEXT NOT NULL DEFAULT '';
            ALTER TABLE clips ADD COLUMN IF NOT EXISTS language_name TEXT NOT NULL DEFAULT '';
            """,
            connection);
        command.ExecuteNonQuery();
    }

    public static void EnsureSeed(NpgsqlConnection connection, string connectionString)
    {
        using (var countUsers = new NpgsqlCommand("SELECT COUNT(*) FROM users;", connection))
        {
            if (Convert.ToInt64(countUsers.ExecuteScalar()) == 0)
            {
                InsertUser(connection, "Sara Kaboré", DemoAccounts.Programmateur, DemoAccounts.ProgrammateurPassword, UserRole.Programmateur);
                InsertUser(connection, "Ibrahim Sawadogo", DemoAccounts.Technicien, DemoAccounts.TechnicienPassword, UserRole.Technicien);
                InsertUser(connection, "Marie Ouédraogo", DemoAccounts.Direction, DemoAccounts.DirectionPassword, UserRole.Direction);
            }
        }

        InstallSeeder.SeedLanguages(new PostgresLanguageCatalog(connectionString));

        var clips = new PostgresClipRepository(connectionString);
        clips.EnsureInitialized(InstallSeeder.SeededLibrary());
        BackfillLanguageNames(connection);
    }

    private static void BackfillLanguageNames(NpgsqlConnection connection)
    {
        foreach (var seed in LanguageCatalog.BurkinaAndFrench)
        {
            using var command = new NpgsqlCommand(
                """
                UPDATE clips SET language_name = @label
                WHERE language_name = '' AND language = @enum;
                """,
                connection);
            command.Parameters.AddWithValue("label", seed.Label);
            command.Parameters.AddWithValue("enum", (int)seed.EnumValue);
            command.ExecuteNonQuery();
        }

        using var fallback = new NpgsqlCommand(
            "UPDATE clips SET language_name = 'Autre' WHERE language_name = '';",
            connection);
        fallback.ExecuteNonQuery();
    }

    private static void InsertUser(
        NpgsqlConnection connection,
        string fullName,
        string userName,
        string password,
        UserRole role)
    {
        using var command = new NpgsqlCommand(
            """
            INSERT INTO users (id, full_name, user_name, password_hash, role, is_active)
            VALUES (@id, @name, @user, @hash, @role, TRUE);
            """,
            connection);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("name", fullName);
        command.Parameters.AddWithValue("user", userName);
        command.Parameters.AddWithValue("hash", PasswordHasher.Hash(password));
        command.Parameters.AddWithValue("role", (int)role);
        command.ExecuteNonQuery();
    }
}
