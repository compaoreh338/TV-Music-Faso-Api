using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed record UserAdminResult(bool Ok, string Message)
{
    public static UserAdminResult Success(string message) => new(true, message);

    public static UserAdminResult Fail(string message) => new(false, message);
}

public sealed record ImpersonationResult(bool Ok, string Message, UserSession? Session)
{
    public static ImpersonationResult Success(UserSession session, string message) => new(true, message, session);

    public static ImpersonationResult Fail(string message) => new(false, message, null);
}

public static class UserAdmin
{
    public static UserAdminResult? RejectIfUnauthorized(UserSession? actor)
    {
        if (actor is null)
        {
            return UserAdminResult.Fail("Session requise.");
        }

        if (actor.IsOffline)
        {
            return UserAdminResult.Fail("Travail local : la gestion des comptes attend le retour du serveur.");
        }

        if (!actor.Policy.CanManageUsers)
        {
            return UserAdminResult.Fail("Seule la Direction peut gérer les utilisateurs.");
        }

        return null;
    }

    public static UserAdminResult? RejectIdentity(string fullName, string userName)
    {
        fullName = fullName.Trim();
        userName = userName.Trim();

        if (fullName.Length < 2)
        {
            return UserAdminResult.Fail("Le nom affiché doit contenir au moins 2 caractères.");
        }

        if (userName.Length < 3)
        {
            return UserAdminResult.Fail("L'identifiant doit contenir au moins 3 caractères.");
        }

        if (userName.Contains(' '))
        {
            return UserAdminResult.Fail("L'identifiant ne doit pas contenir d'espace.");
        }

        return null;
    }

    public static UserAdminResult? RejectPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return UserAdminResult.Fail("Le mot de passe doit contenir au moins 8 caractères.");
        }

        return null;
    }

    public static UserAdminResult? RejectImpersonation(UserSession actor, AppUser? target)
    {
        if (RejectIfUnauthorized(actor) is { } denied)
        {
            return denied;
        }

        if (actor.IsImpersonating)
        {
            return UserAdminResult.Fail("Terminez d’abord l’emprunt d’identité en cours.");
        }

        if (target is null)
        {
            return UserAdminResult.Fail("Utilisateur introuvable.");
        }

        if (!target.IsActive)
        {
            return UserAdminResult.Fail("Ce compte est inactif.");
        }

        if (target.Id == actor.User.Id)
        {
            return UserAdminResult.Fail("Vous êtes déjà connecté avec ce compte.");
        }

        return null;
    }

    public static bool TryParseRole(string? value, out UserRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role);

    public static UserAdminResult? RejectDirectoryChange(
        UserSession actor,
        IReadOnlyList<AppUser> users,
        Guid id,
        UserRole role,
        bool isActive)
    {
        var current = users.FirstOrDefault(user => user.Id == id);
        if (current is null)
        {
            return UserAdminResult.Fail("Utilisateur introuvable.");
        }

        if (id == actor.User.Id && !isActive)
        {
            return UserAdminResult.Fail("Vous ne pouvez pas désactiver votre propre compte.");
        }

        var remainsActiveDirection = role == UserRole.Direction && isActive;
        var otherActiveDirections = users.Count(user =>
            user.Id != id && user.IsActive && user.Role == UserRole.Direction);
        if (current.IsActive && current.Role == UserRole.Direction && !remainsActiveDirection && otherActiveDirections == 0)
        {
            return UserAdminResult.Fail("Impossible : il doit rester au moins un compte Direction actif.");
        }

        return null;
    }
}
