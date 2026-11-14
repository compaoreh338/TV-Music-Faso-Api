using System.Text.Json.Serialization;

namespace TVMusicFaso.Core.Domain;

/// <summary>Plateforme d'une source suivie. Seul YouTube est interrogé automatiquement.</summary>
public enum SourcePlatform
{
    YouTubeChannel = 0,
    YouTubePlaylist = 1,
    Boomplay = 2,
    TikTok = 3,
    Facebook = 4,
    Audiomack = 5,

    /// <summary>Fichiers envoyés directement par l'artiste ou le label.</summary>
    ArtistUpload = 6,
    Other = 7
}

/// <summary>Étape d'un clip arrivé, de la détection à l'envoi en validation.</summary>
public enum IngestStage
{
    Detected = 0,
    Queued = 1,
    Downloading = 2,
    Downloaded = 3,
    Checking = 4,

    /// <summary>Contrôlé, converti et pré-rempli : prêt à envoyer en validation.</summary>
    Ready = 5,
    TechnicalRejected = 6,

    /// <summary>Téléchargement impossible : le fichier original est à demander à l'artiste ou au label.</summary>
    FileRequested = 7,
    Ignored = 8,
    SentToValidation = 9
}

/// <summary>Origine d'une information pré-remplie.</summary>
public enum InfoSource
{
    Platform = 0,
    File = 1,
    MonitoredSource = 2,
    ArtistHistory = 3,
    Correction = 4,
    Manual = 5
}

public enum InfoConfidence
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>Chaîne, liste de lecture ou compte suivi (M1).</summary>
public sealed class MonitoredSource
{
    public const int MinCheckIntervalMinutes = 30;

    public const int MaxCheckIntervalMinutes = 7 * 24 * 60;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public SourcePlatform Platform { get; set; } = SourcePlatform.YouTubeChannel;

    public string Url { get; set; } = string.Empty;

    /// <summary>Identifiant résolu sur la plateforme (chaîne UC…, liste PL…), rempli au premier passage.</summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>Artiste ou label propriétaire : sert d'artiste par défaut quand le titre ne le donne pas.</summary>
    public string Owner { get; set; } = string.Empty;

    public string Country { get; set; } = "Burkina Faso";

    /// <summary>Adresse à qui demander le fichier original.</summary>
    public string ContactEmail { get; set; } = string.Empty;

    /// <summary>1 (basse) à 5 (haute) : les sources prioritaires passent en premier.</summary>
    public int Priority { get; set; } = 3;

    public int CheckIntervalMinutes { get; set; } = 360;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastCheckedAt { get; set; }

    public DateTimeOffset? NextCheckAt { get; set; }

    public string LastError { get; set; } = string.Empty;

    public int ConsecutiveFailures { get; set; }

    public int DetectedCount { get; set; }

    [JsonIgnore]
    public bool IsAutomatic => Platform is SourcePlatform.YouTubeChannel or SourcePlatform.YouTubePlaylist;

    [JsonIgnore]
    public bool IsBurkinabe => IsBurkinaCountry(Country);

    public MonitoredSource Clone() => (MonitoredSource)MemberwiseClone();

    public static bool IsBurkinaCountry(string? country)
    {
        var value = (country ?? string.Empty).Trim().ToLowerInvariant();
        return value is "bf" or "bfa" || value.StartsWith("burkina", StringComparison.Ordinal);
    }

    /// <summary>Erreurs bloquantes ; vide si la source peut être enregistrée.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Donnez un nom à la source.");
        }

        if (Platform != SourcePlatform.ArtistUpload)
        {
            if (!Uri.TryCreate(Url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            {
                errors.Add("Indiquez le lien complet de la source (https://…).");
            }
        }

        if (Priority is < 1 or > 5)
        {
            errors.Add("La priorité va de 1 à 5.");
        }

        if (CheckIntervalMinutes is < MinCheckIntervalMinutes or > MaxCheckIntervalMinutes)
        {
            errors.Add("La fréquence de passage va de 30 minutes à 7 jours.");
        }

        if (!string.IsNullOrWhiteSpace(ContactEmail) && !ContactEmail.Contains('@', StringComparison.Ordinal))
        {
            errors.Add("Adresse de contact invalide.");
        }

        return errors;
    }
}

/// <summary>D'où vient une information pré-remplie, et à quel point s'y fier.</summary>
public sealed record FieldOrigin(InfoSource Source, InfoConfidence Confidence, string Detail = "");

/// <summary>Fiche proposée pour un clip arrivé (M5), avec l'origine de chaque information.</summary>
public sealed class ClipSuggestion
{
    public const string ArtistField = "artist";
    public const string TitleField = "title";
    public const string YearField = "year";
    public const string OriginField = "origin";
    public const string LanguageField = "language";
    public const string GenreField = "genre";
    public const string ThemeField = "theme";
    public const string AudienceField = "audience";
    public const string LabelField = "label";
    public const string DurationField = "duration";
    public const string QualityField = "quality";
    public const string NewTalentField = "newTalent";
    public const string PopularityField = "popularity";

    public string Artist { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Year { get; set; }

    public bool IsBurkinabe { get; set; } = true;

    public string OriginPlace { get; set; } = string.Empty;

    public string LanguageName { get; set; } = string.Empty;

    public MusicalGenre? Genre { get; set; }

    public ClipTheme? Theme { get; set; }

    public Audience? Audience { get; set; }

    public string Label { get; set; } = string.Empty;

    public int DurationSeconds { get; set; }

    public VideoQuality? Quality { get; set; }

    public bool IsNewTalent { get; set; }

    /// <summary>1 à 5, d'après les vues sur la plateforme.</summary>
    public int PopularityScore { get; set; }

    public Dictionary<string, FieldOrigin> Origins { get; set; } = new(StringComparer.Ordinal);

    public FieldOrigin? OriginOf(string field) => Origins.TryGetValue(field, out var origin) ? origin : null;

    public void Set(string field, InfoSource source, InfoConfidence confidence, string detail = "") =>
        Origins[field] = new FieldOrigin(source, confidence, detail);

    public ClipSuggestion Clone()
    {
        var copy = (ClipSuggestion)MemberwiseClone();
        copy.Origins = new Dictionary<string, FieldOrigin>(Origins, StringComparer.Ordinal);
        return copy;
    }
}

/// <summary>Résultat du contrôle technique d'un fichier (M4).</summary>
public sealed class TechnicalReport
{
    public double DurationSeconds { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public string Container { get; set; } = string.Empty;

    public string VideoCodec { get; set; } = string.Empty;

    public string AudioCodec { get; set; } = string.Empty;

    public string PixelFormat { get; set; } = string.Empty;

    public int VideoBitrateKbps { get; set; }

    public string FrameRate { get; set; } = string.Empty;

    public bool HasAudio { get; set; }

    /// <summary>Niveau sonore intégré mesuré (LUFS), avant normalisation.</summary>
    public double? IntegratedLufs { get; set; }

    public double? TruePeakDb { get; set; }

    /// <summary>Raisons du refus ; vide si le fichier passe les seuils.</summary>
    public List<string> Issues { get; set; } = [];

    public List<string> Warnings { get; set; } = [];

    public bool Normalized { get; set; }

    public bool VideoReencoded { get; set; }

    /// <summary>Image réduite au format d'antenne (source plus grande que 1920×1080).</summary>
    public bool Downscaled { get; set; }

    [JsonIgnore]
    public bool Passed => Issues.Count == 0;

    public TechnicalReport Clone()
    {
        var copy = (TechnicalReport)MemberwiseClone();
        copy.Issues = [.. Issues];
        copy.Warnings = [.. Warnings];
        return copy;
    }

    [JsonIgnore]
    public string Summary
    {
        get
        {
            var parts = new List<string>
            {
                TimeSpan.FromSeconds(Math.Max(0, DurationSeconds)).ToString(@"m\:ss"),
                Width > 0 ? $"{Width}×{Height}" : "sans image",
                string.IsNullOrWhiteSpace(VideoCodec) ? string.Empty : VideoCodec,
                VideoBitrateKbps > 0 ? $"{VideoBitrateKbps} kb/s" : string.Empty,
                HasAudio ? (IntegratedLufs is { } lufs ? $"son {lufs:0.0} LUFS" : "son") : "sans son"
            };
            return string.Join(" · ", parts.Where(part => part.Length > 0));
        }
    }
}

/// <summary>Un clip arrivé par une source suivie ou un dépôt, avant son entrée en médiathèque.</summary>
public sealed class IngestItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? SourceId { get; set; }

    public string SourceName { get; set; } = string.Empty;

    public SourcePlatform Platform { get; set; } = SourcePlatform.YouTubeChannel;

    /// <summary>Identifiant de la vidéo sur la plateforme (11 caractères pour YouTube).</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string PlatformTitle { get; set; } = string.Empty;

    public string ChannelTitle { get; set; } = string.Empty;

    public DateTimeOffset? PublishedAt { get; set; }

    public long? Views { get; set; }

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;

    public IngestStage Stage { get; set; } = IngestStage.Detected;

    /// <summary>Dernier message utile : erreur de téléchargement, motif d'abandon, refus technique…</summary>
    public string StageNote { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTimeOffset? NextAttemptAt { get; set; }

    /// <summary>Fichier téléchargé ou déposé, jamais modifié.</summary>
    public string OriginalFilePath { get; set; } = string.Empty;

    /// <summary>Fichier au format de diffusion (son normalisé), utilisé par la fiche clip.</summary>
    public string BroadcastFilePath { get; set; } = string.Empty;

    public DateTimeOffset? DownloadedAt { get; set; }

    public DateTimeOffset? CheckedAt { get; set; }

    /// <summary>Vrai : le programmateur a accepté le fichier malgré un refus technique.</summary>
    public bool AcceptedDespiteCheck { get; set; }

    public TechnicalReport? Report { get; set; }

    public ClipSuggestion Suggestion { get; set; } = new();

    /// <summary>Clip de la médiathèque probablement identique (même artiste et même titre).</summary>
    public Guid? DuplicateOfClipId { get; set; }

    public string DuplicateNote { get; set; } = string.Empty;

    /// <summary>Fiche créée à l'envoi en validation.</summary>
    public Guid? ClipId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string UpdatedBy { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsBusy => Stage is IngestStage.Queued or IngestStage.Downloading or IngestStage.Downloaded or IngestStage.Checking;

    [JsonIgnore]
    public bool IsClosed => Stage is IngestStage.Ignored or IngestStage.SentToValidation;

    public IngestItem Clone()
    {
        var copy = (IngestItem)MemberwiseClone();
        copy.Suggestion = Suggestion.Clone();
        copy.Report = Report?.Clone();
        return copy;
    }
}

/// <summary>Réglages de l'arrivée des clips, partagés par le serveur et les postes.</summary>
public sealed class IngestSettings
{
    /// <summary>Interrupteur général des tâches automatiques du serveur.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Télécharge tout de suite chaque nouveau clip détecté (sauf doublon probable).</summary>
    public bool AutoDownload { get; set; } = true;

    /// <summary>Dossier de travail, relatif à la médiathèque du serveur.</summary>
    public string IngestFolderName { get; set; } = "_Arrivees";

    /// <summary>yt-dlp sur le serveur ; vide = recherche automatique.</summary>
    public string YtDlpPath { get; set; } = string.Empty;

    /// <summary>ffmpeg sur le serveur ; vide = recherche automatique.</summary>
    public string FfmpegPath { get; set; } = string.Empty;

    public int MaxConcurrentDownloads { get; set; } = 2;

    public int MaxDownloadAttempts { get; set; } = 3;

    /// <summary>Au premier passage d'une source, les vidéos plus anciennes sont ignorées.</summary>
    public int InitialLookbackDays { get; set; } = 30;

    public int DefaultCheckIntervalMinutes { get; set; } = 360;

    public int MinDurationSeconds { get; set; } = 90;

    public int MaxDurationSeconds { get; set; } = 900;

    public int MinHeight { get; set; } = 720;

    public int MinVideoBitrateKbps { get; set; } = 1500;

    public bool RequireAudio { get; set; } = true;

    /// <summary>Cible EBU R128 de l'antenne.</summary>
    public double TargetLufs { get; set; } = -23;

    public double MaxTruePeakDb { get; set; } = -1;

    public bool NormalizeAudio { get; set; } = true;

    public string StationName { get; set; } = "Filinfo TV";

    public string RequestSubject { get; set; } = "Demande du fichier original : {artiste} – {titre}";

    public string RequestBody { get; set; } =
        "Bonjour,\n\n{station} souhaite diffuser votre clip « {titre} » ({artiste}) repéré sur {plateforme} :\n{lien}\n\n" +
        "{motif}\n\nPourriez-vous nous envoyer le fichier original en meilleure qualité (MP4 ou MOV, 1080p ou plus) ? " +
        "Vous pouvez répondre à ce courriel avec un lien de téléchargement.\n\nMerci,\nLa programmation musicale de {station}";

    public IngestSettings Clone() => (IngestSettings)MemberwiseClone();

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(IngestFolderName)
            || IngestFolderName.IndexOfAny(['/', '\\', ':']) >= 0
            || IngestFolderName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || IngestFolderName is "." or "..")
        {
            errors.Add("Le dossier des arrivées est un simple nom de dossier dans la médiathèque (ex. _Arrivees).");
        }

        if (MaxConcurrentDownloads is < 1 or > 8)
        {
            errors.Add("Téléchargements simultanés : de 1 à 8.");
        }

        if (MaxDownloadAttempts is < 1 or > 10)
        {
            errors.Add("Tentatives de téléchargement : de 1 à 10.");
        }

        if (InitialLookbackDays is < 0 or > 365)
        {
            errors.Add("Recul au premier passage : de 0 à 365 jours.");
        }

        if (DefaultCheckIntervalMinutes is < MonitoredSource.MinCheckIntervalMinutes or > MonitoredSource.MaxCheckIntervalMinutes)
        {
            errors.Add("Fréquence de passage par défaut : de 30 minutes à 7 jours.");
        }

        if (MinDurationSeconds < 0 || MaxDurationSeconds <= MinDurationSeconds || MaxDurationSeconds > 3 * 3600)
        {
            errors.Add("Durées : le minimum doit être inférieur au maximum (3 h au plus).");
        }

        if (MinHeight is < 0 or > 4320 || MinVideoBitrateKbps is < 0 or > 100_000)
        {
            errors.Add("Seuils d'image invalides.");
        }

        if (TargetLufs is < -36 or > -10 || MaxTruePeakDb is < -9 or > 0)
        {
            errors.Add("Son : cible entre -36 et -10 LUFS, crête entre -9 et 0 dBTP.");
        }

        if (string.IsNullOrWhiteSpace(StationName))
        {
            errors.Add("Indiquez le nom de la chaîne pour les courriels.");
        }

        return errors;
    }
}

/// <summary>État des tâches automatiques, écrit par le serveur à chaque cycle.</summary>
public sealed class IngestPipelineStatus
{
    public DateTimeOffset? LastCycleAt { get; set; }

    public string Host { get; set; } = string.Empty;

    public bool YtDlpFound { get; set; }

    public string YtDlpVersion { get; set; } = string.Empty;

    public bool FfmpegFound { get; set; }

    public string IngestFolder { get; set; } = string.Empty;

    public string LastError { get; set; } = string.Empty;

    public int RunningDownloads { get; set; }

    /// <summary>Demande de mise à jour de yt-dlp, prise en compte au cycle suivant.</summary>
    public bool ToolUpdateRequested { get; set; }

    public string ToolUpdateResult { get; set; } = string.Empty;
}

/// <summary>
/// Compteurs pour le menu et le tableau de bord. <paramref name="Total"/> et <paramref name="Revision"/>
/// (augmente à chaque écriture d'une arrivée) permettent aux postes de ne recharger la liste que si elle a changé ;
/// un serveur plus ancien les renvoie à 0.
/// </summary>
public sealed record IngestSummary(int New, int InProgress, int Ready, int NeedsAction, int SentThisWeek, int Total = 0, long Revision = 0)
{
    /// <summary>Clips qui attendent quelqu'un : nouveaux, prêts, ou bloqués (refus technique, fichier à demander).</summary>
    [JsonIgnore]
    public int Pending => New + Ready + NeedsAction;

    /// <summary>Empreinte de l'état des arrivées ; null si le serveur ne la fournit pas (rechargement systématique).</summary>
    [JsonIgnore]
    public string? ChangeStamp => Revision > 0 ? $"{Revision}:{Total}" : null;
}

/// <summary>Activité des arrivées sur une période : clips repérés sur les sources et fichiers téléchargés.</summary>
public sealed record IngestActivity(int Detected, int Downloaded);
