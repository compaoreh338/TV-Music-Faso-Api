using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Bilan d'un import de sources.</summary>
public sealed record SourceImportSummary(int Added, int Updated, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// Règles de l'arrivée des clips (M1 à M5) : droits, enchaînement des étapes, journal d'audit.
/// Utilisé par l'API et par les postes branchés directement sur PostgreSQL.
/// </summary>
public sealed class IngestService
{
    public static readonly string[] DepositExtensions = [".mp4", ".mov", ".mkv", ".m4v", ".mxf", ".avi", ".webm", ".mpg", ".mpeg"];

    private readonly IIngestStore _store;
    private readonly IClipRepository _clips;
    private readonly IAuditLog _audit;
    private readonly ILanguageCatalog? _languages;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ClipSuggestionBuilder _suggestions;
    private readonly object _sendLock = new();

    public IngestService(
        IIngestStore store,
        IClipRepository clips,
        IAuditLog audit,
        ILanguageCatalog? languages = null,
        Func<DateTimeOffset>? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clips = clips ?? throw new ArgumentNullException(nameof(clips));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _languages = languages;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _suggestions = new ClipSuggestionBuilder(store);
    }

    public IIngestStore Store => _store;

    public IReadOnlyList<MonitoredSource> Sources() => _store.GetSources();

    /// <summary>Ajoute ou modifie une source ; un lien YouTube est reconnu et la source repasse au prochain cycle.</summary>
    public MonitoredSource SaveSource(UserSession actor, MonitoredSource input)
    {
        RequireManage(actor);
        ArgumentNullException.ThrowIfNull(input);
        var source = input.Clone();
        source.Name = source.Name?.Trim() ?? string.Empty;
        source.Owner = source.Owner?.Trim() ?? string.Empty;
        source.Country = source.Country?.Trim() ?? string.Empty;
        source.ContactEmail = source.ContactEmail?.Trim() ?? string.Empty;
        PrepareUrl(source);

        var errors = source.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        var all = _store.GetSources();
        var sameUrl = all.FirstOrDefault(other => other.Id != source.Id
            && source.Url.Length > 0
            && string.Equals(other.Url.TrimEnd('/'), source.Url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
        if (sameUrl is not null)
        {
            throw new ArgumentException($"Ce lien est déjà suivi par la source « {sameUrl.Name} ».");
        }

        var existing = all.FirstOrDefault(other => other.Id == source.Id);
        if (existing is null)
        {
            source.CreatedAt = _clock();
            source.ExternalId = YouTubeFeedReader.Classify(source.Url)?.Id ?? string.Empty;
            source.NextCheckAt = source.IsActive ? _clock() : null;
            source.LastCheckedAt = null;
            source.LastError = string.Empty;
            source.ConsecutiveFailures = 0;
            source.DetectedCount = 0;
        }
        else
        {
            // Champs tenus par le serveur : jamais repris de l'écran.
            source.CreatedAt = existing.CreatedAt;
            source.LastCheckedAt = existing.LastCheckedAt;
            source.DetectedCount = existing.DetectedCount;
            source.ConsecutiveFailures = existing.ConsecutiveFailures;
            source.LastError = existing.LastError;
            var urlChanged = !string.Equals(existing.Url, source.Url, StringComparison.OrdinalIgnoreCase);
            source.ExternalId = urlChanged ? YouTubeFeedReader.Classify(source.Url)?.Id ?? string.Empty : existing.ExternalId;
            source.NextCheckAt = !source.IsActive
                ? null
                : urlChanged || !existing.IsActive ? _clock() : existing.NextCheckAt ?? _clock();
            if (urlChanged)
            {
                source.LastCheckedAt = null;
                source.LastError = string.Empty;
                source.ConsecutiveFailures = 0;
            }
        }

        _store.SaveSource(source);
        _audit.Write(actor, existing is null ? "Ajout source suivie" : "Modification source suivie", $"{source.Name} ({IngestStages.PlatformLabel(source.Platform)})");
        return source;
    }

    public void DeleteSource(UserSession actor, Guid id)
    {
        RequireManage(actor);
        var source = _store.GetSource(id) ?? throw new InvalidOperationException("Source introuvable.");
        _store.DeleteSource(id);
        _audit.Write(actor, "Suppression source suivie", source.Name);
    }

    /// <summary>Demande un passage au prochain cycle du serveur (environ une minute).</summary>
    public MonitoredSource CheckNow(UserSession actor, Guid id)
    {
        RequireSubmit(actor);
        var source = _store.GetSource(id) ?? throw new InvalidOperationException("Source introuvable.");
        if (!source.IsAutomatic)
        {
            throw new InvalidOperationException("Seules les chaînes et listes YouTube sont interrogées automatiquement.");
        }

        if (!source.IsActive)
        {
            throw new InvalidOperationException("Source désactivée : réactivez-la d'abord.");
        }

        source.NextCheckAt = _clock();
        _store.SaveSource(source);
        return source;
    }

    /// <summary>Ajoute les sources d'une liste ; un lien déjà suivi met à jour la source existante.</summary>
    public SourceImportSummary ImportSources(UserSession actor, IReadOnlyList<MonitoredSource> sources)
    {
        RequireManage(actor);
        ArgumentNullException.ThrowIfNull(sources);
        int added = 0, updated = 0, skipped = 0;
        var errors = new List<string>();
        var byUrl = new Dictionary<string, MonitoredSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var known in _store.GetSources())
        {
            byUrl.TryAdd(UrlKey(known.Url), known);
        }

        foreach (var candidate in sources.Take(SourceListImport.MaxRows))
        {
            byUrl.TryGetValue(UrlKey(YouTubeFeedReader.Normalize(candidate.Url)), out var match);
            var source = candidate.Clone();
            if (match is not null)
            {
                source.Id = match.Id;
            }

            try
            {
                SaveSourceQuietly(source, match);
                byUrl[UrlKey(source.Url)] = source;
                if (match is null)
                {
                    added++;
                }
                else
                {
                    updated++;
                }
            }
            catch (ArgumentException exception)
            {
                skipped++;
                errors.Add($"{candidate.Name} : {exception.Message}");
            }
        }

        _audit.Write(actor, "Import sources suivies", $"{added} ajoutée(s), {updated} mise(s) à jour, {skipped} écartée(s)");
        return new SourceImportSummary(added, updated, skipped, errors);
    }

    public IReadOnlyList<IngestItem> Items(IngestFilter filter, int limit = 500) =>
        _store.GetItems(IngestStages.For(filter), limit);

    public IngestItem Item(Guid id) => _store.GetItem(id) ?? throw new InvalidOperationException("Clip arrivé introuvable.");

    public IngestSummary Summary() => _store.Summarize(_clock().AddDays(-7));

    /// <summary>Repérés et téléchargés sur une période d'au plus un an (rapport hebdomadaire).</summary>
    public IngestActivity Activity(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new ArgumentException("La fin de la période doit suivre son début.");
        }

        if (to - from > TimeSpan.FromDays(366))
        {
            throw new ArgumentException("Période limitée à un an.");
        }

        return _store.CountActivity(from, to);
    }

    /// <summary>Met le clip en file de téléchargement (nouveau, fichier à demander, ignoré ou refusé).</summary>
    public IngestItem Queue(UserSession actor, Guid id)
    {
        RequireSubmit(actor);
        var item = Item(id);
        if (item.Stage is not (IngestStage.Detected or IngestStage.FileRequested or IngestStage.TechnicalRejected or IngestStage.Ignored))
        {
            throw new InvalidOperationException($"Téléchargement impossible à l'étape « {IngestStages.Label(item.Stage)} ».");
        }

        if (!YouTubeFeedReader.IsVideoId(item.ExternalId))
        {
            throw new InvalidOperationException("Ce clip ne vient pas de YouTube : déposez le fichier reçu à la place.");
        }

        item.Stage = IngestStage.Queued;
        item.Attempts = 0;
        item.NextAttemptAt = null;
        item.StageNote = string.Empty;
        return Save(actor, item, "Téléchargement demandé");
    }

    public IngestItem Ignore(UserSession actor, Guid id, string? reason)
    {
        RequireSubmit(actor);
        var item = Item(id);
        RequireNotBusy(item);
        RequireNotRemoved(item);
        if (item.Stage == IngestStage.SentToValidation)
        {
            throw new InvalidOperationException("Clip déjà envoyé à la signature : traitez-le depuis l'écran Signature.");
        }

        item.Stage = IngestStage.Ignored;
        item.StageNote = string.IsNullOrWhiteSpace(reason) ? "Ignoré par le programmateur." : reason.Trim();
        return Save(actor, item, "Clip arrivé ignoré");
    }

    /// <summary>
    /// Retire le clip des arrivées. Il reste connu (le serveur ne le redétecte pas) et ses fichiers
    /// téléchargés sont effacés par le serveur au cycle suivant.
    /// </summary>
    public IngestItem Remove(UserSession actor, Guid id)
    {
        RequireSubmit(actor);
        var item = Item(id);
        RequireNotBusy(item);
        if (item.Stage == IngestStage.SentToValidation)
        {
            throw new InvalidOperationException("Clip déjà envoyé à la signature : supprimez sa fiche dans la médiathèque.");
        }

        if (item.Stage == IngestStage.Removed)
        {
            return item;
        }

        item.Stage = IngestStage.Removed;
        item.StageNote = $"Supprimé par {actor.User.FullName}.";
        item.NextAttemptAt = null;
        return Save(actor, item, "Clip arrivé supprimé");
    }

    public IngestItem Restore(UserSession actor, Guid id)
    {
        RequireSubmit(actor);
        var item = Item(id);
        if (item.Stage != IngestStage.Ignored)
        {
            throw new InvalidOperationException("Seul un clip ignoré peut être repris.");
        }

        item.Stage = string.IsNullOrWhiteSpace(item.BroadcastFilePath)
            ? string.IsNullOrWhiteSpace(item.OriginalFilePath) ? IngestStage.Detected : IngestStage.Downloaded
            : IngestStage.Ready;
        item.StageNote = string.Empty;
        return Save(actor, item, "Clip arrivé repris");
    }

    /// <summary>Le programmateur garde le fichier malgré le refus technique : conversion et pré-remplissage au cycle suivant.</summary>
    public IngestItem AcceptDespiteCheck(UserSession actor, Guid id)
    {
        RequireSubmit(actor);
        var item = Item(id);
        if (item.Stage != IngestStage.TechnicalRejected || string.IsNullOrWhiteSpace(item.OriginalFilePath))
        {
            throw new InvalidOperationException("Seul un fichier refusé au contrôle technique peut être accepté.");
        }

        item.AcceptedDespiteCheck = true;
        item.Stage = IngestStage.Downloaded;
        item.StageNote = string.Empty;
        return Save(actor, item, "Fichier accepté malgré le contrôle");
    }

    /// <summary>Passe le clip en « fichier à demander » (téléchargement impossible ou meilleure version voulue).</summary>
    public IngestItem RequestFile(UserSession actor, Guid id, string? note)
    {
        RequireSubmit(actor);
        var item = Item(id);
        RequireNotBusy(item);
        if (item.IsClosed)
        {
            throw new InvalidOperationException($"Clip à l'étape « {IngestStages.Label(item.Stage)} ».");
        }

        item.Stage = IngestStage.FileRequested;
        item.StageNote = string.IsNullOrWhiteSpace(note) ? "Fichier original demandé à l'artiste." : note.Trim();
        return Save(actor, item, "Fichier demandé à l'artiste");
    }

    /// <summary>Fichier reçu de l'artiste, déjà enregistré par le serveur dans le dossier des dépôts.</summary>
    public IngestItem Deposit(UserSession actor, Guid id, string storedPath)
    {
        RequireSubmit(actor);
        RequireDepositPath(storedPath);
        var item = Item(id);
        RequireNotBusy(item);
        RequireNotRemoved(item);
        if (item.Stage == IngestStage.SentToValidation)
        {
            throw new InvalidOperationException("Clip déjà envoyé à la signature.");
        }

        item.OriginalFilePath = storedPath;
        item.BroadcastFilePath = string.Empty;
        item.DownloadedAt = _clock();
        item.AcceptedDespiteCheck = false;
        item.Report = null;
        item.Stage = IngestStage.Downloaded;
        item.StageNote = "Fichier déposé, contrôle à venir.";
        return Save(actor, item, "Fichier déposé");
    }

    /// <summary>Nouveau clip à partir d'un fichier reçu directement (espace de dépôt).</summary>
    public IngestItem CreateFromDeposit(UserSession actor, string storedPath, string? title, Guid? sourceId)
    {
        RequireSubmit(actor);
        RequireDepositPath(storedPath);
        var source = sourceId is { } id ? _store.GetSource(id) : null;
        var platformTitle = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(storedPath) : title.Trim();
        var item = new IngestItem
        {
            SourceId = source?.Id,
            SourceName = source?.Name ?? "Dépôt direct",
            Platform = SourcePlatform.ArtistUpload,
            PlatformTitle = platformTitle,
            ChannelTitle = source?.Owner ?? string.Empty,
            DetectedAt = _clock(),
            OriginalFilePath = storedPath,
            DownloadedAt = _clock(),
            Stage = IngestStage.Downloaded,
            StageNote = "Fichier déposé, contrôle à venir.",
            UpdatedAt = _clock(),
            UpdatedBy = actor.User.FullName
        };
        item.Suggestion = _suggestions.Build(item, source, _clips.GetAll());
        _store.AddItem(item);
        _audit.Write(actor, "Dépôt d'un clip", platformTitle);
        return item;
    }

    /// <summary>
    /// Crée la fiche « à valider » à partir de la proposition corrigée, mémorise les corrections
    /// pour les clips suivants et clôt l'arrivée.
    /// </summary>
    public Clip SendToValidation(UserSession actor, Guid id, ClipSuggestion final)
    {
        RequireSubmit(actor);
        ArgumentNullException.ThrowIfNull(final);
        lock (_sendLock)
        {
            return SendToValidationLocked(actor, id, final);
        }
    }

    /// <summary>Relu sous verrou : deux envois simultanés (double clic, deux postes) ne créent qu'une fiche.</summary>
    private Clip SendToValidationLocked(UserSession actor, Guid id, ClipSuggestion final)
    {
        var item = Item(id);
        if (item.Stage != IngestStage.Ready)
        {
            throw new InvalidOperationException("Seul un clip contrôlé et prêt peut être envoyé à la signature.");
        }

        var artist = final.Artist?.Trim() ?? string.Empty;
        var title = final.Title?.Trim() ?? string.Empty;
        if (artist.Length == 0 || title.Length == 0)
        {
            throw new ArgumentException("Indiquez l'artiste et le titre avant l'envoi à la signature.");
        }

        var duration = final.DurationSeconds > 0 ? final.DurationSeconds : (int)Math.Round(item.Report?.DurationSeconds ?? 0);
        if (duration <= 0)
        {
            throw new ArgumentException("Durée inconnue : le contrôle technique n'a pas pu la lire.");
        }

        var clip = new Clip
        {
            Title = title,
            Artist = artist,
            Year = final.Year is > 1900 and < 2200 ? final.Year : _clock().Year,
            IsBurkinabe = final.IsBurkinabe,
            OriginPlace = !string.IsNullOrWhiteSpace(final.OriginPlace) ? final.OriginPlace.Trim() : final.IsBurkinabe ? "Burkina Faso" : string.Empty,
            FilmingLocation = string.Empty,
            Quality = final.Quality ?? VideoQuality.Hd,
            Format = "MP4",
            Duration = TimeSpan.FromSeconds(duration),
            Genre = final.Genre ?? MusicalGenre.Autre,
            Label = final.Label?.Trim() ?? string.Empty,
            PopularityScore = final.PopularityScore is >= 1 and <= 5 ? final.PopularityScore : 3,
            FilePath = !string.IsNullOrWhiteSpace(item.BroadcastFilePath) ? item.BroadcastFilePath : item.OriginalFilePath
        };
        if (final.Theme is { } theme)
        {
            clip.Theme = theme;
        }

        if (final.Audience is { } audience)
        {
            clip.Audience = audience;
        }

        ApplyLanguage(clip, final.LanguageName);
        clip.RecalculateImpact();
        clip.SubmitForValidation();
        var origin = item.SourceName is { Length: > 0 } sourceName ? $"source « {sourceName} »" : "dépôt manuel";
        clip.Review.Record(ClipEventKind.Arrived, actor.DisplayName,
            $"Repéré le {item.DetectedAt.ToLocalTime():dd/MM/yyyy HH:mm} ({origin})", item.DetectedAt);
        if (item.DownloadedAt is { } downloadedAt)
        {
            clip.Review.Record(ClipEventKind.Note, actor.DisplayName,
                $"Fichier téléchargé le {downloadedAt.ToLocalTime():dd/MM/yyyy HH:mm}", downloadedAt);
        }

        clip.Review.Record(ClipEventKind.Created, actor.DisplayName, "Fiche créée et envoyée à la signature (validée dès la signature vérifiée)", _clock());
        _clips.Add(clip);

        _suggestions.RememberCorrections(item, final);
        item.Suggestion = MarkEdits(item.Suggestion, final);
        item.ClipId = clip.Id;
        item.Stage = IngestStage.SentToValidation;
        item.StageNote = string.Empty;
        Save(actor, item, "Clip arrivé envoyé à la signature", $"{clip.Artist} – {clip.Title}");
        return clip;
    }

    public IngestSettings Settings() => _store.LoadSettings();

    public IngestSettings SaveSettings(UserSession actor, IngestSettings settings)
    {
        RequireManage(actor);
        ArgumentNullException.ThrowIfNull(settings);
        var cleaned = settings.Clone();
        cleaned.IngestFolderName = cleaned.IngestFolderName?.Trim() ?? string.Empty;

        // Les programmes lancés par le serveur ne se règlent jamais depuis l'écran : sinon un compte
        // métier pourrait faire exécuter un fichier déposé. Ils viennent de la configuration du serveur.
        var current = _store.LoadSettings();
        cleaned.YtDlpPath = current.YtDlpPath;
        cleaned.FfmpegPath = current.FfmpegPath;
        cleaned.StationName = cleaned.StationName?.Trim() ?? string.Empty;
        var errors = cleaned.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        _store.SaveSettings(cleaned);
        _audit.Write(actor, "Réglages arrivée des clips", cleaned.Enabled ? "tâches automatiques actives" : "tâches automatiques en pause");
        return cleaned;
    }

    public IngestPipelineStatus Status() => _store.LoadStatus();

    public void RequestToolUpdate(UserSession actor)
    {
        RequireManage(actor);
        _store.SetToolUpdateRequested(true);
        _audit.Write(actor, "Mise à jour yt-dlp demandée", string.Empty);
    }

    public IngestRequestMessage RequestMessage(Guid id)
    {
        var item = Item(id);
        var source = item.SourceId is { } sourceId ? _store.GetSource(sourceId) : null;
        return IngestRequestMessage.Compose(item, source, _store.LoadSettings());
    }

    /// <summary>Le fichier doit être dans le dossier des arrivées du serveur, avec une extension vidéo.</summary>
    private static void RequireDepositPath(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath) || !DepositExtensions.Contains(Path.GetExtension(storedPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Fichier vidéo attendu (MP4, MOV, MKV, MXF…).");
        }
    }

    /// <summary>Clé de comparaison d'un lien déjà nettoyé (casse et barre finale ignorées).</summary>
    private static string UrlKey(string? url) => (url ?? string.Empty).Trim().TrimEnd('/');

    private void SaveSourceQuietly(MonitoredSource source, MonitoredSource? existing)
    {
        source.Name = source.Name?.Trim() ?? string.Empty;
        PrepareUrl(source);
        var errors = source.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors));
        }

        if (existing is null)
        {
            source.CreatedAt = _clock();
            source.ExternalId = YouTubeFeedReader.Classify(source.Url)?.Id ?? string.Empty;
            source.NextCheckAt = source.IsActive ? _clock() : null;
        }
        else
        {
            source.CreatedAt = existing.CreatedAt;
            source.ExternalId = existing.ExternalId;
            source.LastCheckedAt = existing.LastCheckedAt;
            source.NextCheckAt = source.IsActive ? existing.NextCheckAt ?? _clock() : null;
            source.DetectedCount = existing.DetectedCount;
            source.LastError = existing.LastError;
            source.ConsecutiveFailures = existing.ConsecutiveFailures;
        }

        _store.SaveSource(source);
    }

    /// <summary>
    /// Lien nettoyé (https ajouté si absent) et plateforme YouTube reconnue d'après le lien.
    /// Seul YouTube est suivi : tout autre lien est refusé tout de suite plutôt qu'au passage du serveur.
    /// </summary>
    private static void PrepareUrl(MonitoredSource source)
    {
        source.Url = YouTubeFeedReader.Normalize(source.Url);
        var youtube = YouTubeFeedReader.Classify(source.Url)
            ?? throw new ArgumentException(YouTubeFeedReader.UnrecognizedMessage);
        source.Platform = youtube.Platform;
    }

    private void ApplyLanguage(Clip clip, string? languageName)
    {
        var label = languageName?.Trim() ?? string.Empty;
        if (label.Length == 0)
        {
            return;
        }

        var known = _languages is null ? null : LanguageCatalog.FindByLabel(_languages.GetAll(), label);
        if (known is not null)
        {
            clip.ApplyLanguage(known);
            return;
        }

        clip.LanguageName = label;
        clip.Language = LanguageCatalog.MapEnum(label);
    }

    /// <summary>Proposition finale : les champs changés par le valideur passent en « saisie, sûr ».</summary>
    private static ClipSuggestion MarkEdits(ClipSuggestion proposed, ClipSuggestion final)
    {
        var result = final.Clone();
        result.Origins = new Dictionary<string, FieldOrigin>(proposed.Origins, StringComparer.Ordinal);
        void Check(string field, bool changed)
        {
            if (changed)
            {
                result.Set(field, InfoSource.Manual, InfoConfidence.High, "corrigé à l'envoi");
            }
        }

        Check(ClipSuggestion.ArtistField, !string.Equals(proposed.Artist, final.Artist, StringComparison.Ordinal));
        Check(ClipSuggestion.TitleField, !string.Equals(proposed.Title, final.Title, StringComparison.Ordinal));
        Check(ClipSuggestion.YearField, proposed.Year != final.Year);
        Check(ClipSuggestion.OriginField, proposed.IsBurkinabe != final.IsBurkinabe);
        Check(ClipSuggestion.LanguageField, !string.Equals(proposed.LanguageName, final.LanguageName, StringComparison.Ordinal));
        Check(ClipSuggestion.GenreField, proposed.Genre != final.Genre);
        Check(ClipSuggestion.ThemeField, proposed.Theme != final.Theme);
        Check(ClipSuggestion.AudienceField, proposed.Audience != final.Audience);
        Check(ClipSuggestion.LabelField, !string.Equals(proposed.Label, final.Label, StringComparison.Ordinal));
        return result;
    }

    private IngestItem Save(UserSession actor, IngestItem item, string action, string? details = null)
    {
        item.UpdatedAt = _clock();
        item.UpdatedBy = actor.User.FullName;
        _store.UpdateItem(item);
        _audit.Write(actor, action, details ?? (string.IsNullOrWhiteSpace(item.PlatformTitle) ? item.Id.ToString() : item.PlatformTitle));
        return item;
    }

    private static void RequireNotBusy(IngestItem item)
    {
        if (item.Stage is IngestStage.Downloading or IngestStage.Checking)
        {
            throw new InvalidOperationException($"Le serveur travaille sur ce clip ({IngestStages.Label(item.Stage)}) : réessayez dans un instant.");
        }
    }

    private static void RequireNotRemoved(IngestItem item)
    {
        if (item.Stage == IngestStage.Removed)
        {
            throw new InvalidOperationException("Ce clip a été supprimé des arrivées.");
        }
    }

    private static void RequireManage(UserSession actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.Policy.CanManageSlots)
        {
            throw new UnauthorizedAccessException("Les sources et les réglages sont gérés par le programmateur et la Direction.");
        }
    }

    private static void RequireSubmit(UserSession actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!actor.Policy.CanSubmitClips)
        {
            throw new UnauthorizedAccessException("L'arrivée des clips est traitée par le programmateur et le technicien.");
        }
    }
}
