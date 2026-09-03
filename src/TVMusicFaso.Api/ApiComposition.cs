using TVMusicFaso.Core.Domain;
using TVMusicFaso.Core.Services;

namespace TVMusicFaso.Api;

public sealed class ApiComposition
{
    public ApiComposition(IConfiguration configuration)
    {
        var connection = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres manquant.");
        PostgresDatabase.EnsureReady(connection);
        Clips = new PostgresClipRepository(connection);
        Auth = new PostgresAuthService(connection);
        Auth.EnsureSeedUsers();
        Languages = new PostgresLanguageCatalog(connection);
        InstallSeeder.SeedLanguages(Languages);
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
