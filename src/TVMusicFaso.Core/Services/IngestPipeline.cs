using System.Text.RegularExpressions;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>
/// Tâches automatiques de l'arrivée des clips, lancées par le serveur : détection des nouvelles vidéos
/// (M2), téléchargement (M3), contrôle technique et conversion (M4), pré-remplissage (M5).
/// Chaque étape prend un clip à la fois dans la base ; un arrêt du serveur est repris au démarrage.
/// </summary>
public sealed partial class IngestPipeline
{
    public const string OriginalsFolder = "Originaux";
    public const string BroadcastFolder = "Diffusion";
    public const string DepositsFolder = "Depots";

    private const int SourcesPerCycle = 10;

    private readonly IIngestStore _store;
    private readonly IClipRepository _clips;
    private readonly IVideoFeedReader _feeds;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Action<string> _log;
    private readonly ClipSuggestionBuilder _suggestions;

    /// <param name="libraryRoot">Médiathèque du serveur : le dossier des arrivées y est créé.</param>
    public IngestPipeline(
        IIngestStore store,
        IClipRepository clips,
        IVideoFeedReader feeds,
        string? libraryRoot,
        Func<DateTimeOffset>? clock = null,
        Action<string>? log = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clips = clips ?? throw new ArgumentNullException(nameof(clips));
        _feeds = feeds ?? throw new ArgumentNullException(nameof(feeds));
        LibraryRoot = libraryRoot;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log = log ?? (_ => { });
        _suggestions = new ClipSuggestionBuilder(store);
    }

    public string? LibraryRoot { get; }

    /// <summary>Dossier des arrivées sur le serveur, ou null si la médiathèque n'est pas configurée.</summary>
    public string? IngestRoot(IngestSettings settings) =>
        string.IsNullOrWhiteSpace(LibraryRoot) ? null : Path.Combine(LibraryRoot.Trim(), settings.IngestFolderName);

    /// <summary>Remet en file les clips interrompus par un arrêt du serveur.</summary>
    public int RecoverInterrupted() =>
        _store.ResetStage(IngestStage.Downloading, IngestStage.Queued)
        + _store.ResetStage(IngestStage.Checking, IngestStage.Downloaded);

    /// <summary>Début de la note posée sur une vidéo écartée parce que publiée avant la période de recul.</summary>
    public const string TooOldNotePrefix = "Publié avant le début du suivi";

    /// <summary>
    /// Remet dans le parcours les vidéos écartées seulement pour leur date, si elles entrent dans le recul
    /// actuel (recul allongé après coup). Les clips ignorés à la main ou pour une autre raison ne bougent pas.
    /// </summary>
    public int ReviveWithinLookback(IngestSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var limit = _clock().AddDays(-settings.InitialLookbackDays);
        var candidates = _store.GetItems([IngestStage.Ignored])
            .Where(item => item.StageNote.StartsWith(TooOldNotePrefix, StringComparison.Ordinal)
                && item.PublishedAt is { } published && published >= limit)
            .ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        var library = _clips.GetAll();
        var revived = 0;
        foreach (var item in candidates)
        {
            var source = item.SourceId is { } sourceId ? _store.GetSource(sourceId) : null;
            if (source is null)
            {
                continue;
            }

            item.StageNote = string.Empty;
            Admit(item, source, settings, library);
            item.UpdatedAt = _clock();
            item.UpdatedBy = "Serveur";
            _store.UpdateItem(item);
            revived++;
        }

        if (revived > 0)
        {
            _log($"{revived} vidéo(s) de moins de {settings.InitialLookbackDays} jours remise(s) dans les arrivées.");
        }

        return revived;
    }

    /// <summary>
    /// Efface les fichiers des clips supprimés des arrivées. Seuls les fichiers rangés dans le dossier des
    /// arrivées (originaux, diffusion, dépôts) sont touchés ; tout autre chemin est laissé en place.
    /// </summary>
    public int PurgeRemovedFiles(IngestSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var root = IngestRoot(settings);
        if (root is null)
        {
            return 0;
        }

        var allowed = new[] { OriginalsFolder, BroadcastFolder, DepositsFolder }
            .Select(folder => Path.GetFullPath(Path.Combine(root, folder)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToArray();
        var purged = 0;
        foreach (var item in _store.GetItems([IngestStage.Removed], 100)
                     .Where(item => item.OriginalFilePath.Length > 0 || item.BroadcastFilePath.Length > 0))
        {
            item.OriginalFilePath = DeleteInside(item.OriginalFilePath, allowed);
            item.BroadcastFilePath = DeleteInside(item.BroadcastFilePath, allowed);
            item.UpdatedAt = _clock();
            item.UpdatedBy = "Serveur";
            _store.UpdateItem(item);
            purged++;
        }

        return purged;
    }

    /// <summary>Efface le fichier s'il est rangé dans un des dossiers permis ; renvoie le chemin à garder (vide une fois effacé).</summary>
    private string DeleteInside(string path, IReadOnlyList<string> allowedFolders)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }

        if (!allowedFolders.Any(folder => full.StartsWith(folder, StringComparison.OrdinalIgnoreCase)))
        {
            _log($"Fichier d'un clip supprimé laissé en place (hors du dossier des arrivées) : {full}");
            return string.Empty;
        }

        try
        {
            if (File.Exists(full))
            {
                File.Delete(full);
                _log($"Fichier d'un clip supprimé effacé : {full}");
            }

            return string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Fichier ouvert ailleurs : nouvel essai au cycle suivant.
            _log($"Fichier d'un clip supprimé non effacé ({exception.Message}) : {full}");
            return path;
        }
    }

    /// <summary>Interroge les sources dont le passage est dû ; renvoie le nombre de nouveaux clips.</summary>
    public async Task<int> DetectDueAsync(IngestSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var revived = ReviveWithinLookback(settings);
        var now = _clock();
        var due = _store.GetSources()
            .Where(source => source.IsActive && source.IsAutomatic && (source.NextCheckAt ?? DateTimeOffset.MinValue) <= now)
            .OrderByDescending(source => source.Priority)
            .ThenBy(source => source.NextCheckAt ?? DateTimeOffset.MinValue)
            .Take(SourcesPerCycle)
            .ToList();
        if (due.Count == 0)
        {
            return revived;
        }

        var library = _clips.GetAll();
        var total = revived;
        foreach (var source in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += await DetectAsync(source, settings, library, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    /// <summary>Télécharge le prochain clip en file ; faux s'il n'y en a pas.</summary>
    public async Task<bool> DownloadNextAsync(IVideoDownloader downloader, IngestSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        var root = IngestRoot(settings);
        if (root is null)
        {
            return false;
        }

        var item = _store.ClaimNext(IngestStage.Queued, IngestStage.Downloading, _clock());
        if (item is null)
        {
            return false;
        }

        DownloadOutcome outcome;
        try
        {
            outcome = await downloader.DownloadAsync(item, Path.Combine(root, OriginalsFolder), settings, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            item.Stage = IngestStage.Queued;
            _store.UpdateItem(item);
            throw;
        }
        catch (Exception exception)
        {
            // Toute erreur compte comme un essai raté : le clip ne reste jamais bloqué « en téléchargement ».
            outcome = new DownloadOutcome(DownloadResultKind.Failed, null, exception.Message);
        }

        var now = _clock();
        switch (outcome.Kind)
        {
            case DownloadResultKind.Success:
                item.OriginalFilePath = outcome.FilePath!;
                item.DownloadedAt = now;
                item.Stage = IngestStage.Downloaded;
                item.StageNote = string.Empty;
                item.Attempts = 0;
                item.NextAttemptAt = null;
                break;
            case DownloadResultKind.Filtered:
                item.Stage = IngestStage.Ignored;
                item.StageNote = outcome.Message;
                break;
            case DownloadResultKind.Blocked:
                item.Stage = IngestStage.FileRequested;
                item.StageNote = $"Téléchargement refusé par la plateforme : {outcome.Message}";
                break;
            default:
                item.Attempts++;
                if (item.Attempts >= settings.MaxDownloadAttempts)
                {
                    item.Stage = IngestStage.FileRequested;
                    item.StageNote = $"Échec après {item.Attempts} essai(s) : {outcome.Message}";
                }
                else
                {
                    item.Stage = IngestStage.Queued;
                    item.NextAttemptAt = now + RetryDelay(item.Attempts);
                    item.StageNote = $"Essai {item.Attempts} échoué, nouvel essai vers {item.NextAttemptAt.Value.ToLocalTime():HH:mm} : {outcome.Message}";
                }

                break;
        }

        item.UpdatedAt = now;
        item.UpdatedBy = "Serveur";
        _store.UpdateItem(item);
        _log($"Téléchargement {item.ExternalId} : {outcome.Kind} {outcome.Message}");
        return true;
    }

    /// <summary>Contrôle et convertit le prochain fichier téléchargé ou déposé ; faux s'il n'y en a pas.</summary>
    public async Task<bool> CheckNextAsync(ITechnicalInspector inspector, IngestSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inspector);
        var root = IngestRoot(settings);
        if (root is null)
        {
            return false;
        }

        var item = _store.ClaimNext(IngestStage.Downloaded, IngestStage.Checking, _clock());
        if (item is null)
        {
            return false;
        }

        try
        {
            await CheckAsync(item, inspector, settings, root, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            item.Stage = IngestStage.Downloaded;
            _store.UpdateItem(item);
            throw;
        }
        catch (Exception exception)
        {
            // Toute erreur refuse le fichier (avec son motif) : le clip ne reste jamais bloqué « en contrôle ».
            item.Stage = IngestStage.TechnicalRejected;
            item.StageNote = $"Contrôle impossible : {exception.Message}";
        }

        item.CheckedAt = _clock();
        item.UpdatedAt = item.CheckedAt.Value;
        item.UpdatedBy = "Serveur";
        _store.UpdateItem(item);
        _log($"Contrôle {item.Id} : {item.Stage} {item.StageNote}");
        return true;
    }

    /// <summary>Délai avant un nouvel essai : 5 min, 15 min, 45 min… plafonné à 6 h.</summary>
    public static TimeSpan RetryDelay(int attempts) =>
        TimeSpan.FromMinutes(Math.Min(360, 5 * Math.Pow(3, Math.Max(0, attempts - 1))));

    /// <summary>Après des échecs répétés sur une source : 15 min, 30 min, 1 h… plafonné à la fréquence prévue et à 24 h.</summary>
    public static TimeSpan SourceBackoff(int failures, int intervalMinutes) =>
        TimeSpan.FromMinutes(Math.Min(Math.Min(intervalMinutes, 24 * 60), 15 * Math.Pow(2, Math.Max(0, failures - 1))));

    /// <summary>Nom du fichier de diffusion : « Artiste - Titre [identifiant].mp4 », sans caractère interdit.</summary>
    public static string BroadcastFileName(IngestItem item)
    {
        var suggestion = item.Suggestion;
        var label = !string.IsNullOrWhiteSpace(suggestion.Artist) && !string.IsNullOrWhiteSpace(suggestion.Title)
            ? $"{suggestion.Artist} - {suggestion.Title}"
            : string.IsNullOrWhiteSpace(item.PlatformTitle) ? "Clip" : item.PlatformTitle;
        var safe = UnsafeFileCharacters().Replace(label, " ");
        safe = string.Join(' ', safe.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ');
        if (safe.Length > 120)
        {
            safe = safe[..120].Trim();
        }

        return $"{(safe.Length == 0 ? "Clip" : safe)} [{item.Id.ToString("N")[..8]}].mp4";
    }

    /// <summary>Pré-remplit la fiche, repère un doublon probable, puis met en file (ou laisse au choix du programmateur).</summary>
    private void Admit(IngestItem item, MonitoredSource source, IngestSettings settings, IReadOnlyList<Clip> library)
    {
        item.Suggestion = _suggestions.Build(item, source, library);
        var duplicate = ClipSuggestionBuilder.FindProbableDuplicate(item.Suggestion, library);
        if (duplicate is not null)
        {
            item.DuplicateOfClipId = duplicate.Id;
            item.DuplicateNote = $"Doublon probable de « {duplicate.Artist} – {duplicate.Title} » déjà en médiathèque.";
        }

        item.Stage = settings.AutoDownload && duplicate is null ? IngestStage.Queued : IngestStage.Detected;
    }

    private async Task<int> DetectAsync(MonitoredSource source, IngestSettings settings, IReadOnlyList<Clip> library, CancellationToken cancellationToken)
    {
        var now = _clock();
        var firstPass = source.LastCheckedAt is null;
        var added = 0;
        string error = string.Empty;
        string resolvedId = source.ExternalId;
        string channelTitle = string.Empty;
        try
        {
            var feed = await _feeds.FetchAsync(source, cancellationToken).ConfigureAwait(false);
            resolvedId = feed.ResolvedId;
            channelTitle = feed.ChannelTitle;
            foreach (var video in feed.Videos.OrderBy(video => video.PublishedAt ?? now))
            {
                if (_store.FindByExternalId(video.VideoId) is not null)
                {
                    continue;
                }

                var item = new IngestItem
                {
                    SourceId = source.Id,
                    SourceName = source.Name,
                    Platform = source.Platform,
                    ExternalId = video.VideoId,
                    Url = video.Url,
                    PlatformTitle = video.Title,
                    ChannelTitle = video.ChannelTitle,
                    PublishedAt = video.PublishedAt,
                    Views = video.Views,
                    DetectedAt = now,
                    UpdatedAt = now,
                    UpdatedBy = "Serveur"
                };

                var tooOld = firstPass && video.PublishedAt is { } published && published < now.AddDays(-settings.InitialLookbackDays);
                if (tooOld)
                {
                    item.Stage = IngestStage.Ignored;
                    item.StageNote = $"{TooOldNotePrefix} (plus de {settings.InitialLookbackDays} jours).";
                }
                else
                {
                    Admit(item, source, settings, library);
                    added++;
                }

                try
                {
                    _store.AddItem(item);
                }
                catch (InvalidOperationException)
                {
                    // Ajouté entre-temps par un autre cycle : déjà connu.
                    if (!tooOld)
                    {
                        added--;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Toute erreur (réseau, flux mal formé…) espace les passages de cette source au lieu de la réinterroger à chaque cycle.
            error = exception is TaskCanceledException ? "YouTube ne répond pas (délai dépassé)." : exception.Message;
        }

        // Relu juste avant l'écriture : une modification faite à l'écran pendant l'interrogation est gardée.
        var fresh = _store.GetSource(source.Id);
        if (fresh is null)
        {
            return added;
        }

        if (error.Length == 0)
        {
            fresh.ExternalId = resolvedId;
            fresh.LastCheckedAt = now;
            fresh.NextCheckAt = now.AddMinutes(fresh.CheckIntervalMinutes);
            fresh.LastError = string.Empty;
            fresh.ConsecutiveFailures = 0;
            fresh.DetectedCount += added;
            if (string.IsNullOrWhiteSpace(fresh.Name) && channelTitle.Length > 0)
            {
                fresh.Name = channelTitle;
            }
        }
        else
        {
            fresh.ConsecutiveFailures++;
            fresh.LastError = error;
            fresh.NextCheckAt = now + SourceBackoff(fresh.ConsecutiveFailures, fresh.CheckIntervalMinutes);
            _log($"Source « {fresh.Name} » : {error}");
        }

        _store.SaveSource(fresh);
        return added;
    }

    private async Task CheckAsync(IngestItem item, ITechnicalInspector inspector, IngestSettings settings, string root, CancellationToken cancellationToken)
    {
        var source = ClipMediaPath.Resolve(item.OriginalFilePath, LibraryRoot);
        if (source is null || !ClipMediaPath.IsInsideLibrary(source, root))
        {
            item.Stage = IngestStage.TechnicalRejected;
            item.StageNote = "Fichier introuvable dans le dossier des arrivées du serveur.";
            return;
        }

        var (report, loudness) = await inspector.InspectAsync(source, settings, cancellationToken).ConfigureAwait(false);
        item.Report = report;
        ClipSuggestionBuilder.ApplyFile(item.Suggestion, report);
        if (!report.Passed && !item.AcceptedDespiteCheck)
        {
            item.Stage = IngestStage.TechnicalRejected;
            item.StageNote = string.Join(" ", report.Issues);
            return;
        }

        var output = Path.Combine(root, BroadcastFolder, BroadcastFileName(item));
        await inspector.ConvertAsync(source, output, report, loudness, settings, cancellationToken).ConfigureAwait(false);
        item.BroadcastFilePath = output;
        item.Stage = IngestStage.Ready;
        item.StageNote = item.AcceptedDespiteCheck && report.Issues.Count > 0
            ? "Accepté malgré : " + string.Join(" ", report.Issues)
            : string.Join(" ", report.Warnings);
    }

    [GeneratedRegex(@"[\\/:*?""<>|\x00-\x1F]")]
    private static partial Regex UnsafeFileCharacters();
}
