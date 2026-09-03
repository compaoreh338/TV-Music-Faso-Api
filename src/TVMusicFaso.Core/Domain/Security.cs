namespace TVMusicFaso.Core.Domain;

public enum UserRole
{
    Programmateur,
    Technicien,
    Direction
}

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string FullName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.Programmateur;

    public bool IsActive { get; set; } = true;

    public string RoleLabel => Role.ToDisplayName();

    public string StatusLabel => IsActive ? "Actif" : "Inactif";
}

public sealed class UserSession
{
    public required AppUser User { get; init; }

    public required string AccessToken { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public bool IsOffline { get; init; }

    public AppUser? ImpersonatedBy { get; init; }

    public bool IsImpersonating => ImpersonatedBy is not null;

    public AccessPolicy Policy => new(User.Role);

    public string TokenPreview => AccessToken.Length <= 10 ? AccessToken : $"{AccessToken[..8]}…";

    public string DisplayName => IsImpersonating
        ? $"{User.FullName} · {User.Role.ToDisplayName()} (via {ImpersonatedBy!.FullName})"
        : $"{User.FullName} · {User.Role.ToDisplayName()}";
}

public sealed class AccessPolicy
{
    public AccessPolicy(UserRole role, bool forceConsultation = false)
    {
        Role = role;
        ForceConsultation = forceConsultation;
    }

    public UserRole Role { get; }

    public bool ForceConsultation { get; }

    public bool CanEditLibrary => !ForceConsultation && Role == UserRole.Programmateur;

    public bool CanSubmitClips => !ForceConsultation && Role is UserRole.Programmateur or UserRole.Technicien;

    public bool CanValidateClips => !ForceConsultation && Role == UserRole.Direction;

    public bool CanEditPlaylists => !ForceConsultation && Role == UserRole.Programmateur;

    public bool CanViewDashboard => true;

    public bool CanExport => Role is UserRole.Programmateur or UserRole.Technicien;

    public bool CanViewReports => true;

    public bool CanExportBbda => Role is UserRole.Direction or UserRole.Programmateur;

    public bool CanManageBackup => !ForceConsultation && Role is UserRole.Technicien or UserRole.Programmateur;

    public bool CanViewAudit => true;

    public bool CanManageUsers => !ForceConsultation && Role == UserRole.Direction;

    public bool CanManageSlots => !ForceConsultation && Role is UserRole.Programmateur or UserRole.Direction;

    public bool IsConsultationOnly => !CanEditLibrary && !CanEditPlaylists && !CanManageUsers && !CanManageSlots;
}

public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;

    public string Actor { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public string AtLabel => At.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
}
