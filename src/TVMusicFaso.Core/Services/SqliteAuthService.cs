using System.Security.Cryptography;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class SqliteAuthService : IAuthService
{
    public const string DemoProgrammateur = DemoAccounts.Programmateur;
    public const string DemoTechnicien = DemoAccounts.Technicien;
    public const string DemoDirection = DemoAccounts.Direction;
    public const string DemoPasswordSuffix = DemoAccounts.PasswordSuffix;

    private readonly string _databasePath;

    public SqliteAuthService(string databasePath)
    {
        _databasePath = databasePath;
    }

    public void EnsureSeedUsers()
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM users;";
        if (Convert.ToInt32(count.ExecuteScalar()) > 0)
        {
            return;
        }

        Insert(connection, "Sara Kaboré", DemoProgrammateur, $"Prog{DemoPasswordSuffix}", UserRole.Programmateur);
        Insert(connection, "Ibrahim Sawadogo", DemoTechnicien, $"Tech{DemoPasswordSuffix}", UserRole.Technicien);
        Insert(connection, "Marie Ouédraogo", DemoDirection, $"Dir{DemoPasswordSuffix}", UserRole.Direction);
    }

    public UserSession? SignIn(string userName, string password)
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, FullName, UserName, PasswordHash, Role, IsActive
            FROM users WHERE UserName = $user COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$user", userName.Trim());
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(5) != 1 || !PasswordHasher.Verify(password, reader.GetString(3)))
        {
            return null;
        }

        var user = new AppUser
        {
            Id = Guid.Parse(reader.GetString(0)),
            FullName = reader.GetString(1),
            UserName = reader.GetString(2),
            Role = (UserRole)reader.GetInt32(4),
            IsActive = true
        };
        reader.Dispose();
        return IssueSession(connection, user);
    }

    private static UserSession IssueSession(Microsoft.Data.Sqlite.SqliteConnection connection, AppUser user, AppUser? impersonatedBy = null)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.Now.AddHours(8);
        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO sessions (Token, UserId, ExpiresAt) VALUES ($t, $u, $e);";
        insert.Parameters.AddWithValue("$t", token);
        insert.Parameters.AddWithValue("$u", user.Id.ToString());
        insert.Parameters.AddWithValue("$e", expires.ToString("o"));
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

        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT u.Id, u.FullName, u.UserName, u.Role, u.IsActive, s.ExpiresAt
            FROM sessions s
            JOIN users u ON u.Id = s.UserId
            WHERE s.Token = $t;
            """;
        command.Parameters.AddWithValue("$t", accessToken);
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt32(4) != 1)
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(reader.GetString(5), out var expires) || expires <= DateTimeOffset.Now)
        {
            return null;
        }

        return new UserSession
        {
            User = new AppUser
            {
                Id = Guid.Parse(reader.GetString(0)),
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
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sessions WHERE Token = $t;";
        command.Parameters.AddWithValue("$t", accessToken);
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

        using var connection = SqliteDatabase.Open(_databasePath);
        if (!SessionBelongsToUser(connection, session))
        {
            return ProfileChangeResult.Fail("Session invalide. Reconnectez-vous.");
        }

        using var taken = connection.CreateCommand();
        taken.CommandText =
            """
            SELECT COUNT(*) FROM users
            WHERE UserName = $user COLLATE NOCASE AND Id <> $id;
            """;
        taken.Parameters.AddWithValue("$user", userName);
        taken.Parameters.AddWithValue("$id", session.User.Id.ToString());
        if (Convert.ToInt32(taken.ExecuteScalar()) > 0)
        {
            return ProfileChangeResult.Fail("Cet identifiant est déjà utilisé.");
        }

        using var update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE users SET FullName = $name, UserName = $user
            WHERE Id = $id;
            """;
        update.Parameters.AddWithValue("$name", fullName);
        update.Parameters.AddWithValue("$user", userName);
        update.Parameters.AddWithValue("$id", session.User.Id.ToString());
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

        using var connection = SqliteDatabase.Open(_databasePath);
        if (!SessionBelongsToUser(connection, session))
        {
            return ProfileChangeResult.Fail("Session invalide. Reconnectez-vous.");
        }

        using var lookup = connection.CreateCommand();
        lookup.CommandText = "SELECT PasswordHash FROM users WHERE Id = $id;";
        lookup.Parameters.AddWithValue("$id", session.User.Id.ToString());
        var stored = lookup.ExecuteScalar() as string;
        if (stored is null || !PasswordHasher.Verify(currentPassword, stored))
        {
            return ProfileChangeResult.Fail("Le mot de passe actuel est incorrect.");
        }

        if (PasswordHasher.Verify(newPassword, stored))
        {
            return ProfileChangeResult.Fail("Le nouveau mot de passe doit être différent de l'actuel.");
        }

        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE users SET PasswordHash = $hash WHERE Id = $id;";
        update.Parameters.AddWithValue("$hash", PasswordHasher.Hash(newPassword));
        update.Parameters.AddWithValue("$id", session.User.Id.ToString());
        update.ExecuteNonQuery();

        return ProfileChangeResult.Success("Mot de passe mis à jour.");
    }

    public IReadOnlyList<AppUser> ListUsers(UserSession actor)
    {
        if (UserAdmin.RejectIfUnauthorized(actor) is not null)
        {
            return [];
        }

        using var connection = SqliteDatabase.Open(_databasePath);
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

        using var connection = SqliteDatabase.Open(_databasePath);
        if (UserNameTaken(connection, userName, Guid.Empty))
        {
            return UserAdminResult.Fail("Cet identifiant est déjà utilisé.");
        }

        Insert(connection, fullName, userName, password, role);
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

        using var connection = SqliteDatabase.Open(_databasePath);
        var users = ReadUsers(connection);
        if (UserAdmin.RejectDirectoryChange(actor, users, id, role, isActive) is { } directory)
        {
            return directory;
        }

        if (UserNameTaken(connection, userName, id))
        {
            return UserAdminResult.Fail("Cet identifiant est déjà utilisé.");
        }

        using var update = connection.CreateCommand();
        update.CommandText =
            """
            UPDATE users
            SET FullName = $name, UserName = $user, Role = $role, IsActive = $active
            WHERE Id = $id;
            """;
        update.Parameters.AddWithValue("$name", fullName);
        update.Parameters.AddWithValue("$user", userName);
        update.Parameters.AddWithValue("$role", (int)role);
        update.Parameters.AddWithValue("$active", isActive ? 1 : 0);
        update.Parameters.AddWithValue("$id", id.ToString());
        update.ExecuteNonQuery();

        if (!isActive)
        {
            using var revoke = connection.CreateCommand();
            revoke.CommandText = "DELETE FROM sessions WHERE UserId = $id;";
            revoke.Parameters.AddWithValue("$id", id.ToString());
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

        using var connection = SqliteDatabase.Open(_databasePath);
        using var update = connection.CreateCommand();
        update.CommandText = "UPDATE users SET PasswordHash = $hash WHERE Id = $id;";
        update.Parameters.AddWithValue("$hash", PasswordHasher.Hash(newPassword));
        update.Parameters.AddWithValue("$id", id.ToString());
        return update.ExecuteNonQuery() == 0
            ? UserAdminResult.Fail("Utilisateur introuvable.")
            : UserAdminResult.Success("Mot de passe réinitialisé.");
    }

    public ImpersonationResult Impersonate(UserSession actor, Guid userId)
    {
        using var connection = SqliteDatabase.Open(_databasePath);
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

    private static List<AppUser> ReadUsers(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, FullName, UserName, Role, IsActive
            FROM users
            ORDER BY FullName;
            """;
        using var reader = command.ExecuteReader();
        var users = new List<AppUser>();
        while (reader.Read())
        {
            users.Add(new AppUser
            {
                Id = Guid.Parse(reader.GetString(0)),
                FullName = reader.GetString(1),
                UserName = reader.GetString(2),
                Role = (UserRole)reader.GetInt32(3),
                IsActive = reader.GetInt32(4) == 1
            });
        }

        return users;
    }

    private static bool UserNameTaken(Microsoft.Data.Sqlite.SqliteConnection connection, string userName, Guid exceptId)
    {
        using var taken = connection.CreateCommand();
        taken.CommandText =
            """
            SELECT COUNT(*) FROM users
            WHERE UserName = $user COLLATE NOCASE AND Id <> $id;
            """;
        taken.Parameters.AddWithValue("$user", userName);
        taken.Parameters.AddWithValue("$id", exceptId.ToString());
        return Convert.ToInt32(taken.ExecuteScalar()) > 0;
    }

    private static bool SessionBelongsToUser(Microsoft.Data.Sqlite.SqliteConnection connection, UserSession session)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT UserId, ExpiresAt FROM sessions WHERE Token = $t;
            """;
        command.Parameters.AddWithValue("$t", session.AccessToken);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        if (!Guid.TryParse(reader.GetString(0), out var userId) || userId != session.User.Id)
        {
            return false;
        }

        return DateTimeOffset.TryParse(reader.GetString(1), out var expires) && expires > DateTimeOffset.Now;
    }

    private static void Insert(Microsoft.Data.Sqlite.SqliteConnection connection, string name, string userName, string password, UserRole role)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO users (Id, FullName, UserName, PasswordHash, Role, IsActive)
            VALUES ($id, $name, $user, $hash, $role, 1);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$user", userName);
        command.Parameters.AddWithValue("$hash", PasswordHasher.Hash(password));
        command.Parameters.AddWithValue("$role", (int)role);
        command.ExecuteNonQuery();
    }
}
