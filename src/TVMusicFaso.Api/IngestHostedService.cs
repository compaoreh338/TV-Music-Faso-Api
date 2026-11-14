using TVMusicFaso.Core.Domain;
using TVMusicFaso.Core.Services;

namespace TVMusicFaso.Api;

/// <summary>
/// Tâches de fond de l'arrivée des clips sur le serveur : un cycle toutes les 20 s interroge les
/// sources dues, garde jusqu'à N téléchargements en cours et contrôle un fichier à la fois.
/// L'état (outils trouvés, dernière erreur…) est écrit dans la base pour l'écran « Sources ».
/// </summary>
public sealed class IngestHostedService : BackgroundService
{
    private static readonly TimeSpan CycleInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan VersionRefresh = TimeSpan.FromHours(6);

    private readonly ApiComposition _composition;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IngestHostedService> _logger;
    private readonly YouTubeFeedReader _feeds = new();
    private readonly List<Task> _downloads = [];
    private Task? _checking;
    private string _ytDlpVersion = string.Empty;
    private string? _versionOf;
    private DateTimeOffset _versionReadAt = DateTimeOffset.MinValue;

    public IngestHostedService(ApiComposition composition, IConfiguration configuration, ILogger<IngestHostedService> logger)
    {
        _composition = composition;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pipeline = new IngestPipeline(
            _composition.IngestStore,
            _composition.Clips,
            _feeds,
            _composition.LibraryRoot,
            log: message => _logger.LogInformation("{Message}", message));

        try
        {
            var recovered = pipeline.RecoverInterrupted();
            if (recovered > 0)
            {
                _logger.LogInformation("{Count} clip(s) interrompu(s) remis en file.", recovered);
            }
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            _logger.LogWarning(exception, "Reprise des clips interrompus impossible pour l'instant.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CycleAsync(pipeline, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                _logger.LogWarning(exception, "Cycle d'arrivée des clips interrompu.");
                TryWriteError(exception.Message);
            }
            catch (Exception exception)
            {
                // Une erreur imprévue ne doit jamais arrêter l'API (comportement par défaut d'un BackgroundService).
                _logger.LogError(exception, "Erreur imprévue dans le cycle d'arrivée des clips.");
                TryWriteError($"Erreur imprévue : {exception.Message}");
            }

            try
            {
                await Task.Delay(CycleInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await Task.WhenAll(_downloads.Concat(_checking is null ? [] : [_checking])).ContinueWith(_ => { }, TaskScheduler.Default);
        _feeds.Dispose();
    }

    private async Task CycleAsync(IngestPipeline pipeline, CancellationToken stoppingToken)
    {
        var store = _composition.IngestStore;
        var settings = store.LoadSettings();
        // Chemins des programmes : configuration du serveur uniquement (jamais la base, modifiable depuis les postes).
        var ytDlp = YtDlpDownloader.Locate(_configuration["Ingest:YtDlpPath"]);
        var inspector = MediaInspector.Locate(_configuration["Ingest:FfmpegPath"]);
        var ingestRoot = pipeline.IngestRoot(settings);
        var status = store.LoadStatus();
        var error = string.Empty;

        if (status.ToolUpdateRequested)
        {
            // Demande effacée avant l'essai : une mise à jour qui échoue n'est pas relancée à chaque cycle.
            store.SetToolUpdateRequested(false);
            status.ToolUpdateRequested = false;
            status.ToolUpdateResult = await UpdateToolAsync(ytDlp, inspector, stoppingToken);
            _versionReadAt = DateTimeOffset.MinValue;
        }

        await RefreshVersionAsync(ytDlp, inspector, stoppingToken);
        _downloads.RemoveAll(task => task.IsCompleted);

        if (ingestRoot is null)
        {
            error = "Médiathèque du serveur non configurée (Playout:LibraryRoot) : téléchargements en attente.";
        }
        else if (settings.Enabled)
        {
            await pipeline.DetectDueAsync(settings, stoppingToken);

            if (ytDlp is null)
            {
                error = YtDlpDownloader.MissingMessage;
            }
            else
            {
                var downloader = new YtDlpDownloader(ytDlp, inspector?.FfmpegPath);
                while (_downloads.Count < settings.MaxConcurrentDownloads)
                {
                    _downloads.Add(Task.Run(() => DrainAsync(() => pipeline.DownloadNextAsync(downloader, settings, stoppingToken), stoppingToken), stoppingToken));
                }
            }

            if (inspector is null)
            {
                error = string.IsNullOrEmpty(error) ? "ffmpeg et ffprobe introuvables sur le serveur : contrôle technique en attente." : error;
            }
            else if (_checking is null || _checking.IsCompleted)
            {
                _checking = Task.Run(() => DrainAsync(() => pipeline.CheckNextAsync(inspector, settings, stoppingToken), stoppingToken), stoppingToken);
            }
        }

        status.LastCycleAt = DateTimeOffset.UtcNow;
        status.Host = Environment.MachineName;
        status.YtDlpFound = ytDlp is not null;
        status.YtDlpVersion = ytDlp is null ? string.Empty : _ytDlpVersion;
        status.FfmpegFound = inspector is not null;
        status.IngestFolder = ingestRoot ?? string.Empty;
        status.RunningDownloads = store.GetItems([IngestStage.Downloading], 50).Count;
        status.LastError = error;
        store.SaveStatus(status);
    }

    /// <summary>Traite les clips en file jusqu'à épuisement ; une erreur arrête ce fil jusqu'au cycle suivant.</summary>
    private async Task DrainAsync(Func<Task<bool>> step, CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested && await step())
            {
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            _logger.LogWarning(exception, "Tâche d'arrivée interrompue.");
            TryWriteError(exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Erreur imprévue dans une tâche d'arrivée.");
            TryWriteError($"Erreur imprévue : {exception.Message}");
        }
    }

    private async Task<string> UpdateToolAsync(string? ytDlp, MediaInspector? inspector, CancellationToken cancellationToken)
    {
        if (ytDlp is null)
        {
            return YtDlpDownloader.MissingMessage;
        }

        try
        {
            var result = await new YtDlpDownloader(ytDlp, inspector?.FfmpegPath).UpdateAsync(cancellationToken);
            return $"{DateTimeOffset.Now:dd/MM HH:mm} : {result}";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return "Mise à jour impossible : délai dépassé.";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return $"Mise à jour impossible : {exception.Message}";
        }
    }

    private async Task RefreshVersionAsync(string? ytDlp, MediaInspector? inspector, CancellationToken cancellationToken)
    {
        if (ytDlp is null || (ytDlp == _versionOf && DateTimeOffset.UtcNow - _versionReadAt < VersionRefresh))
        {
            return;
        }

        try
        {
            _ytDlpVersion = await new YtDlpDownloader(ytDlp, inspector?.FfmpegPath).VersionAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            _ytDlpVersion = "version illisible";
            _logger.LogWarning(exception, "Version de yt-dlp illisible.");
        }

        _versionOf = ytDlp;
        _versionReadAt = DateTimeOffset.UtcNow;
    }

    private void TryWriteError(string message)
    {
        try
        {
            var status = _composition.IngestStore.LoadStatus();
            status.LastError = message;
            _composition.IngestStore.SaveStatus(status);
        }
        catch (Exception exception) when (IsTransient(exception))
        {
            _logger.LogDebug(exception, "État de l'arrivée non enregistré.");
        }
    }

    private static bool IsTransient(Exception exception) =>
        exception is Npgsql.NpgsqlException or InvalidOperationException or TimeoutException or IOException
            or UnauthorizedAccessException or HttpRequestException or System.Text.Json.JsonException
            or System.ComponentModel.Win32Exception;
}
