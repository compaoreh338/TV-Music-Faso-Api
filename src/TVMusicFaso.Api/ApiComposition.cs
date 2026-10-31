using TVMusicFaso.Core.Domain;
using TVMusicFaso.Core.Services;

namespace TVMusicFaso.Api;

public sealed class ApiComposition
{
    public ApiComposition(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Postgres")
            ?? configuration["ConnectionStrings:Postgres"]
            ?? "Host=localhost;Port=5432;Database=tvmusicfaso;Username=tvmusic;Password=tvmusic";
        PostgresDatabase.EnsureReady(connection);
        Clips = new PostgresClipRepository(connection);
        Auth = new PostgresAuthService(connection);
        Auth.EnsureSeedUsers();
        Languages = new PostgresLanguageCatalog(connection);
        InstallSeeder.SeedLanguages(Languages);
        Genres = new PostgresGenreCatalog(connection);
        InstallSeeder.SeedGenres(Genres);
        RefreshGenres();
        Schedules = new PostgresScheduleStore(connection);
        Broadcasts = new PostgresBroadcastLog(connection);
        Audit = new PostgresAuditLog(connection);
        Engine = new TVMusicFaso.Core.Rules.ProgrammingEngine();
        LibraryRoot = configuration["Playout:LibraryRoot"];
        var playoutRoot = configuration["Playout:PlayoutRoot"];
        Export = new PlaylistExportService(new PlayoutPathMapper(LibraryRoot, playoutRoot));
        Backup = new BackupService();
    }

    public string? LibraryRoot { get; }

    public ILanguageCatalog Languages { get; }

    /// <summary>Genres gérés dans l'écran « Genres » ; le registre des noms est rafraîchi après chaque changement.</summary>
    public IGenreCatalog Genres { get; }

    /// <summary>Un poste en connexion Postgres directe peut changer les genres sans passer par l'API.</summary>
    private static readonly TimeSpan GenreRefreshInterval = TimeSpan.FromSeconds(15);

    private readonly object _genreLock = new();
    private DateTimeOffset _genresLoadedAt = DateTimeOffset.MinValue;

    /// <summary>Recharge les noms des genres depuis la base ; renvoie la liste lue.</summary>
    public IReadOnlyList<GenreDefinition> RefreshGenres()
    {
        lock (_genreLock)
        {
            var genres = Genres.GetAll();
            GenreCatalog.Use(genres);
            _genresLoadedAt = DateTimeOffset.UtcNow;
            return genres;
        }
    }

    /// <summary>Recharge les genres s'ils datent de plus de 15 s ; une panne de base garde la dernière liste.</summary>
    public void RefreshGenresIfStale(ILogger logger)
    {
        if (DateTimeOffset.UtcNow - _genresLoadedAt < GenreRefreshInterval)
        {
            return;
        }

        try
        {
            RefreshGenres();
        }
        catch (Exception exception) when (exception is Npgsql.NpgsqlException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(exception, "Genres non rechargés : la dernière liste connue reste utilisée.");
        }
    }

    public IClipRepository Clips { get; }

    public PostgresAuthService Auth { get; }

    public IScheduleStore Schedules { get; }

    public IBroadcastLog Broadcasts { get; }

    public IAuditLog Audit { get; }

    public TVMusicFaso.Core.Rules.ProgrammingEngine Engine { get; }

    public PlaylistExportService Export { get; }

    public BackupService Backup { get; }

    public UserSession? CurrentSession(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Auth.GetSession(header["Bearer ".Length..].Trim());
    }
}
