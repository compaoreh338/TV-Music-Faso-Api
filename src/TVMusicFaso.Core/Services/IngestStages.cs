using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Filtres de l'écran « Arrivées ».</summary>
public enum IngestFilter
{
    ToHandle = 0,
    New = 1,
    InProgress = 2,
    Ready = 3,
    NeedsAction = 4,
    Sent = 5,
    Ignored = 6,
    All = 7
}

/// <summary>Groupes d'étapes, libellés et compteurs partagés par le serveur et les postes.</summary>
public static class IngestStages
{
    public static readonly IReadOnlyList<IngestStage> InProgress =
        [IngestStage.Queued, IngestStage.Downloading, IngestStage.Downloaded, IngestStage.Checking];

    public static readonly IReadOnlyList<IngestStage> NeedsAction =
        [IngestStage.TechnicalRejected, IngestStage.FileRequested];

    /// <summary>Étapes d'un filtre ; null = toutes.</summary>
    public static IReadOnlyCollection<IngestStage>? For(IngestFilter filter) => filter switch
    {
        IngestFilter.ToHandle => [IngestStage.Detected, IngestStage.Ready, .. NeedsAction],
        IngestFilter.New => [IngestStage.Detected],
        IngestFilter.InProgress => InProgress.ToArray(),
        IngestFilter.Ready => [IngestStage.Ready],
        IngestFilter.NeedsAction => NeedsAction.ToArray(),
        IngestFilter.Sent => [IngestStage.SentToValidation],
        IngestFilter.Ignored => [IngestStage.Ignored],
        _ => null
    };

    public static string FilterLabel(IngestFilter filter) => filter switch
    {
        IngestFilter.ToHandle => "À traiter",
        IngestFilter.New => "Nouveaux",
        IngestFilter.InProgress => "En cours",
        IngestFilter.Ready => "Prêts à valider",
        IngestFilter.NeedsAction => "Bloqués (fichier, contrôle)",
        IngestFilter.Sent => "Envoyés en validation",
        IngestFilter.Ignored => "Ignorés",
        _ => "Tous"
    };

    public static string Label(IngestStage stage) => stage switch
    {
        IngestStage.Detected => "Détecté",
        IngestStage.Queued => "En file de téléchargement",
        IngestStage.Downloading => "Téléchargement…",
        IngestStage.Downloaded => "Téléchargé, contrôle à venir",
        IngestStage.Checking => "Contrôle technique…",
        IngestStage.Ready => "Prêt à valider",
        IngestStage.TechnicalRejected => "Refus technique",
        IngestStage.FileRequested => "Fichier à demander",
        IngestStage.Ignored => "Ignoré",
        IngestStage.SentToValidation => "Envoyé en validation",
        _ => stage.ToString()
    };

    public static string PlatformLabel(SourcePlatform platform) => platform switch
    {
        SourcePlatform.YouTubeChannel => "Chaîne YouTube",
        SourcePlatform.YouTubePlaylist => "Liste de lecture YouTube",
        SourcePlatform.Boomplay => "Boomplay",
        SourcePlatform.TikTok => "TikTok",
        SourcePlatform.Facebook => "Facebook",
        SourcePlatform.Audiomack => "Audiomack",
        SourcePlatform.ArtistUpload => "Dépôt de l'artiste",
        _ => "Autre"
    };

    public static string SourceLabel(InfoSource source) => source switch
    {
        InfoSource.Platform => "plateforme",
        InfoSource.File => "fichier",
        InfoSource.MonitoredSource => "source suivie",
        InfoSource.ArtistHistory => "historique de l'artiste",
        InfoSource.Correction => "correction mémorisée",
        _ => "saisie"
    };

    public static string ConfidenceLabel(InfoConfidence confidence) => confidence switch
    {
        InfoConfidence.High => "sûr",
        InfoConfidence.Medium => "à vérifier",
        _ => "incertain"
    };

    public static IngestSummary Summarize(IEnumerable<IngestItem> items, DateTimeOffset since)
    {
        int fresh = 0, running = 0, ready = 0, blocked = 0, sent = 0;
        foreach (var item in items)
        {
            switch (item.Stage)
            {
                case IngestStage.Detected:
                    fresh++;
                    break;
                case IngestStage.Ready:
                    ready++;
                    break;
                case IngestStage.TechnicalRejected or IngestStage.FileRequested:
                    blocked++;
                    break;
                case IngestStage.SentToValidation when item.UpdatedAt >= since:
                    sent++;
                    break;
                case IngestStage.Queued or IngestStage.Downloading or IngestStage.Downloaded or IngestStage.Checking:
                    running++;
                    break;
            }
        }

        return new IngestSummary(fresh, running, ready, blocked, sent);
    }
}
