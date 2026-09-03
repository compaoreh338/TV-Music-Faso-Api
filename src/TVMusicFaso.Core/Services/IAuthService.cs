using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed record ProfileChangeResult(bool Ok, string Message)
{
    public static ProfileChangeResult Success(string message) => new(true, message);

    public static ProfileChangeResult Fail(string message) => new(false, message);
}

public interface IAuthService
{
    UserSession? SignIn(string userName, string password);

    UserSession? GetSession(string accessToken);

    void SignOut(string accessToken);

    void EnsureSeedUsers();

    ProfileChangeResult UpdateProfile(UserSession session, string fullName, string userName);

    ProfileChangeResult ChangePassword(UserSession session, string currentPassword, string newPassword);

    IReadOnlyList<AppUser> ListUsers(UserSession actor);

    UserAdminResult CreateUser(UserSession actor, string fullName, string userName, string password, UserRole role);

    UserAdminResult UpdateUser(UserSession actor, Guid id, string fullName, string userName, UserRole role, bool isActive);

    UserAdminResult ResetPassword(UserSession actor, Guid id, string newPassword);

    ImpersonationResult Impersonate(UserSession actor, Guid userId);
}

public interface IAuditLog
{
    void Write(UserSession? session, string action, string details);

    IReadOnlyList<AuditEntry> GetRecent(int take = 200);
}
