namespace TVMusicFaso.Core.Services;

/// <summary>Comptes de démonstration semés dans PostgreSQL.</summary>
public static class DemoAccounts
{
    public const string Programmateur = "filinfotv.programmateur";
    public const string Technicien = "filinfotv.technicien";
    public const string Direction = "filinfotv.direction";
    public const string PasswordSuffix = "Faso2026!";

    public static string ProgrammateurPassword => $"Prog{PasswordSuffix}";

    public static string TechnicienPassword => $"Tech{PasswordSuffix}";

    public static string DirectionPassword => $"Dir{PasswordSuffix}";
}
