using System.Security.Cryptography;
using Npgsql;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class PostgresAuthService : IAuthService
{
    private readonly string _connectionString;

    public PostgresAuthService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void EnsureSeedUsers()
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        PostgresDatabase.EnsureSeed(connection, _connectionString);
    }

    public UserSession? SignIn(string userName, string password)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(
            """
            SELECT id, full_name, user_name, password_hash, role, is_active
            FROM users
            WHERE lower(user_name) = lower(@user);
            """,
            connection);
        command.Parameters.AddWithValue("user", userName.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read() || !reader.GetBoolean(5) || !PasswordHasher.Verify(password, reader.GetString(3)))
        {
            return null;
        }

        var user = new AppUser
        {
            Id = reader.GetGuid(0),
            FullName = reader.GetString(1),
            UserName = reader.GetString(2),
            Role = (UserRole)reader.GetInt32(4),
            IsActive = true
        };
        reader.Close();

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.Now.AddHours(8);
        using var insert = new NpgsqlCommand(
            "INSERT INTO sessions (token, user_id, expires_at) VALUES (@t, @u, @e);",
            connection);
        insert.Parameters.AddWithValue("t", token);
        insert.Parameters.AddWithValue("u", user.Id);
        insert.Parameters.AddWithValue("e", expires);
        insert.ExecuteNonQuery();

        return new UserSession { User = user, AccessToken = token, ExpiresAt = expires };
    }

    public UserSession? GetSession(string accessToken)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(
            """
            SELECT u.id, u.full_name, u.user_name, u.role, u.is_active, s.expires_at
            FROM sessions s
            JOIN users u ON u.id = s.user_id
            WHERE s.token = @t;
            """,
            connection);
        command.Parameters.AddWithValue("t", accessToken);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || !reader.GetBoolean(4))
        {
            return null;
        }

        var expires = reader.GetFieldValue<DateTimeOffset>(5);
        if (expires <= DateTimeOffset.Now)
        {
            return null;
        }

        return new UserSession
        {
            User = new AppUser
            {
                Id = reader.GetGuid(0),
                FullName = reader.GetString(1),
                UserName = reader.GetString(2),
                Role = (UserRole)reader.GetInt32(3),
                IsActive = true
            },
            AccessToken = accessToken,
            ExpiresAt = expires
        };
    }

    public void SignOut(string accessToken)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand("DELETE FROM sessions WHERE token = @t;", connection);
        command.Parameters.AddWithValue("t", accessToken);
        command.ExecuteNonQuery();
    }

    public ProfileChangeResult UpdateProfile(UserSession session, string fullName, string userName)
    {
        fullName = fullName.Trim();
        userName = userName.Trim();

        if (fullName.Length < 2)
        {
            return ProfileChangeResult.Fail("Le nom affiché doit contenir au moins 2 caractères.");
        }

        if (userName.Length < 3)
        {
            return ProfileChangeResult.Fail("L'identifiant doit contenir au moins 3 caractères.");
        }

        if (userName.Contains(' '))
        {
            return ProfileChangeResult.Fail("L'identifiant ne doit pas contenir d'espace.");
        }

        using var connection = PostgresDatabase.Open(_connectionString);
        if (!SessionBelongsToUser(connection, session))
        {
            return ProfileChangeResult.Fail("Session invalide. Reconnectez-vous.");
        }

        using var taken = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM users
            WHERE lower(user_name) = lower(@user) AND id <> @id;
            """,
            connection);
        taken.Parameters.AddWithValue("user", userName);
        taken.Parameters.AddWithValue("id", session.User.Id);
        if (Convert.ToInt64(taken.ExecuteScalar()) > 0)
        {
            return ProfileChangeResult.Fail("Cet identifiant est déjà utilisé.");
        }

        using var update = new NpgsqlCommand(
            "UPDATE users SET full_name = @name, user_name = @user WHERE id = @id;",
            connection);
        update.Parameters.AddWithValue("name", fullName);
        update.Parameters.AddWithValue("user", userName);
        update.Parameters.AddWithValue("id", session.User.Id);
        update.ExecuteNonQuery();

        session.User.FullName = fullName;
        session.User.UserName = userName;
        return ProfileChangeResult.Success("Nom et identifiant enregistrés.");
    }

    public ProfileChangeResult ChangePassword(UserSession session, string currentPassword, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            return ProfileChangeResult.Fail("Le nouveau mot de passe doit contenir au moins 8 caractères.");
        }

        using var connection = PostgresDatabase.Open(_connectionString);
        if (!SessionBelongsToUser(connection, session))
        {
            return ProfileChangeResult.Fail("Session invalide. Reconnectez-vous.");
        }

        using var lookup = new NpgsqlCommand("SELECT password_hash FROM users WHERE id = @id;", connection);
        lookup.Parameters.AddWithValue("id", session.User.Id);
        var stored = lookup.ExecuteScalar() as string;
        if (stored is null || !PasswordHasher.Verify(currentPassword, stored))
        {
            return ProfileChangeResult.Fail("Le mot de passe actuel est incorrect.");
        }

        if (PasswordHasher.Verify(newPassword, stored))
        {
            return ProfileChangeResult.Fail("Le nouveau mot de passe doit être différent de l'actuel.");
        }

        using var update = new NpgsqlCommand("UPDATE users SET password_hash = @hash WHERE id = @id;", connection);
        update.Parameters.AddWithValue("hash", PasswordHasher.Hash(newPassword));
        update.Parameters.AddWithValue("id", session.User.Id);
        update.ExecuteNonQuery();

        return ProfileChangeResult.Success("Mot de passe mis à jour.");
    }

    private static bool SessionBelongsToUser(NpgsqlConnection connection, UserSession session)
    {
        using var command = new NpgsqlCommand(
            "SELECT user_id, expires_at FROM sessions WHERE token = @t;",
            connection);
        command.Parameters.AddWithValue("t", session.AccessToken);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        return reader.GetGuid(0) == session.User.Id && reader.GetFieldValue<DateTimeOffset>(1) > DateTimeOffset.Now;
    }
}
