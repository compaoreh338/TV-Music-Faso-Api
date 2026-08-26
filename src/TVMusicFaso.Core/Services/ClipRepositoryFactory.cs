namespace TVMusicFaso.Core.Services;

public static class ClipRepositoryFactory
{
    public static (IClipRepository Repository, DataPlatformInfo Platform) Create(AppSettings settings)
    {
        var connectionString = settings.PostgresConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Chaîne de connexion PostgreSQL manquante dans settings.json (postgresConnectionString).");
        }

        PostgresDatabase.EnsureReady(connectionString);
        return (
            new PostgresClipRepository(connectionString),
            new DataPlatformInfo(
                "PostgreSQL",
                true,
                true,
                "Base unique (offre §2.2) : clips, comptes, grilles, historique BBDA, audit. Seed automatique au premier démarrage."));
    }
}
