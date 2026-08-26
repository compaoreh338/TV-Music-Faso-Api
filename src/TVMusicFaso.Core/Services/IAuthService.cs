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
}

public interface IAuditLog
{
    void Write(UserSession? session, string action, string details);

    IReadOnlyList<AuditEntry> GetRecent(int take = 200);
}
