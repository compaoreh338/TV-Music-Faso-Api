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
        return IssueSession(connection, user);
    }

    private static UserSession IssueSession(NpgsqlConnection connection, AppUser user, AppUser? impersonatedBy = null)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.Now.AddHours(8);
        using var insert = new NpgsqlCommand(
            "INSERT INTO sessions (token, user_id, expires_at) VALUES (@t, @u, @e);",
            connection);
        insert.Parameters.AddWithValue("t", token);
        insert.Parameters.AddWithValue("u", user.Id);
        insert.Parameters.AddWithValue("e", expires);
        insert.ExecuteNonQuery();

        return new UserSession
        {
            User = user,
            AccessToken = token,
            ExpiresAt = expires,
            ImpersonatedBy = impersonatedBy
        };
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

    public IReadOnlyList<AppUser> ListUsers(UserSession actor)
    {
        if (UserAdmin.RejectIfUnauthorized(actor) is not null)
        {
            return [];
        }

        using var connection = PostgresDatabase.Open(_connectionString);
        return ReadUsers(connection);
    }

    public UserAdminResult CreateUser(UserSession actor, string fullName, string userName, string password, UserRole role)
    {
        if (UserAdmin.RejectIfUnauthorized(actor) is { } denied)
        {
            return denied;
        }

        fullName = fullName.Trim();
        userName = userName.Trim();
        if (UserAdmin.RejectIdentity(fullName, userName) is { } identity)
        {
            return identity;
        }

        if (UserAdmin.RejectPassword(password) is { } secret)
        {
            return secret;
        }

        using var connection = PostgresDatabase.Open(_connectionString);
        if (UserNameTaken(connection, userName, Guid.Empty))
        {
            return UserAdminResult.Fail("Cet identifiant est déjà utilisé.");
        }

        using var insert = new NpgsqlCommand(
            """
            INSERT INTO users (id, full_name, user_name, password_hash, role, is_active)
            VALUES (@id, @name, @user, @hash, @role, TRUE);
            """,
            connection);
        insert.Parameters.AddWithValue("id", Guid.NewGuid());
        insert.Parameters.AddWithValue("name", fullName);
        insert.Parameters.AddWithValue("user", userName);
        insert.Parameters.AddWithValue("hash", PasswordHasher.Hash(password));
        insert.Parameters.AddWithValue("role", (int)role);
        insert.ExecuteNonQuery();

        return UserAdminResult.Success($"Compte « {userName} » créé ({role.ToDisplayName()}).");
    }

    public UserAdminResult UpdateUser(UserSession actor, Guid id, string fullName, string userName, UserRole role, bool isActive)
    {
        if (UserAdmin.RejectIfUnauthorized(actor) is { } denied)
        {
            return denied;
        }

        fullName = fullName.Trim();
        userName = userName.Trim();
        if (UserAdmin.RejectIdentity(fullName, userName) is { } identity)
        {
            return identity;
        }

        using var connection = PostgresDatabase.Open(_connectionString);
        var users = ReadUsers(connection);
        if (UserAdmin.RejectDirectoryChange(actor, users, id, role, isActive) is { } directory)
        {
            return directory;
        }

        if (UserNameTaken(connection, userName, id))
        {
            return UserAdminResult.Fail("Cet identifiant est déjà utilisé.");
        }

        using var update = new NpgsqlCommand(
            """
            UPDATE users
            SET full_name = @name, user_name = @user, role = @role, is_active = @active
            WHERE id = @id;
            """,
            connection);
        update.Parameters.AddWithValue("name", fullName);
        update.Parameters.AddWithValue("user", userName);
        update.Parameters.AddWithValue("role", (int)role);
        update.Parameters.AddWithValue("active", isActive);
        update.Parameters.AddWithValue("id", id);
        update.ExecuteNonQuery();

        if (!isActive)
        {
            using var revoke = new NpgsqlCommand("DELETE FROM sessions WHERE user_id = @id;", connection);
            revoke.Parameters.AddWithValue("id", id);
            revoke.ExecuteNonQuery();
        }

        if (id == actor.User.Id)
        {
            actor.User.FullName = fullName;
            actor.User.UserName = userName;
            actor.User.Role = role;
        }

        return UserAdminResult.Success($"Compte « {userName} » mis à jour.");
    }

    public UserAdminResult ResetPassword(UserSession actor, Guid id, string newPassword)
    {
        if (UserAdmin.RejectIfUnauthorized(actor) is { } denied)
        {
            return denied;
        }

        if (UserAdmin.RejectPassword(newPassword) is { } secret)
        {
            return secret;
        }

        using var connection = PostgresDatabase.Open(_connectionString);
        using var update = new NpgsqlCommand("UPDATE users SET password_hash = @hash WHERE id = @id;", connection);
        update.Parameters.AddWithValue("hash", PasswordHasher.Hash(newPassword));
        update.Parameters.AddWithValue("id", id);
        return update.ExecuteNonQuery() == 0
            ? UserAdminResult.Fail("Utilisateur introuvable.")
            : UserAdminResult.Success("Mot de passe réinitialisé.");
    }

    public ImpersonationResult Impersonate(UserSession actor, Guid userId)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        var target = ReadUsers(connection).FirstOrDefault(user => user.Id == userId);
        if (UserAdmin.RejectImpersonation(actor, target) is { } denied)
        {
            return ImpersonationResult.Fail(denied.Message);
        }

        var session = IssueSession(connection, target!, actor.User);
        return ImpersonationResult.Success(
            session,
            $"Vous voyez l’application comme {target!.FullName} ({target.RoleLabel}).");
    }

    private static List<AppUser> ReadUsers(NpgsqlConnection connection)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT id, full_name, user_name, role, is_active
            FROM users
            ORDER BY full_name;
            """,
            connection);
        using var reader = command.ExecuteReader();
        var users = new List<AppUser>();
        while (reader.Read())
        {
            users.Add(new AppUser
            {
                Id = reader.GetGuid(0),
                FullName = reader.GetString(1),
                UserName = reader.GetString(2),
                Role = (UserRole)reader.GetInt32(3),
                IsActive = reader.GetBoolean(4)
            });
        }

        return users;
    }

    private static bool UserNameTaken(NpgsqlConnection connection, string userName, Guid exceptId)
    {
        using var taken = new NpgsqlCommand(
            """
            SELECT COUNT(*) FROM users
            WHERE lower(user_name) = lower(@user) AND id <> @id;
            """,
            connection);
        taken.Parameters.AddWithValue("user", userName);
        taken.Parameters.AddWithValue("id", exceptId);
        return Convert.ToInt64(taken.ExecuteScalar()) > 0;
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
