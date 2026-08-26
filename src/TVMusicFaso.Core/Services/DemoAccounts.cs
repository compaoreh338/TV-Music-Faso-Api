namespace TVMusicFaso.Core.Services;

/// <summary>Comptes de démonstration semés dans PostgreSQL.</summary>
public static class DemoAccounts
{
    public const string Programmateur = "sara.programmateur";
    public const string Technicien = "ibrahim.technicien";
    public const string Direction = "marie.direction";
    public const string PasswordSuffix = "Faso2026!";

    public static string ProgrammateurPassword => $"Prog{PasswordSuffix}";

    public static string TechnicienPassword => $"Tech{PasswordSuffix}";

    public static string DirectionPassword => $"Dir{PasswordSuffix}";
}
