using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Courriel de demande du fichier original, prêt à copier ou à ouvrir dans la messagerie.</summary>
public sealed record IngestRequestMessage(string To, string Subject, string Body)
{
    /// <summary>Lien « mailto: » qui ouvre la messagerie avec le courriel pré-rempli.</summary>
    public string MailtoUri =>
        $"mailto:{Uri.EscapeDataString(To)}?subject={Uri.EscapeDataString(Subject)}&body={Uri.EscapeDataString(Body)}";

    public string ClipboardText => string.IsNullOrWhiteSpace(To)
        ? $"Objet : {Subject}\n\n{Body}"
        : $"À : {To}\nObjet : {Subject}\n\n{Body}";

    /// <summary>Remplit le modèle des réglages : {artiste}, {titre}, {lien}, {plateforme}, {station}, {motif}.</summary>
    public static IngestRequestMessage Compose(IngestItem item, MonitoredSource? source, IngestSettings settings)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(settings);
        var artist = FirstNonEmpty(item.Suggestion.Artist, source?.Owner, item.ChannelTitle, "l'artiste");
        var title = FirstNonEmpty(item.Suggestion.Title, item.PlatformTitle, "votre clip");
        var reason = item.Stage switch
        {
            IngestStage.TechnicalRejected when item.Report is { Issues.Count: > 0 } report =>
                "Le fichier disponible ne respecte pas nos critères de diffusion : " + string.Join(" ; ", report.Issues),
            IngestStage.FileRequested => "Nous n'avons pas pu récupérer la vidéo depuis la plateforme.",
            _ => "Nous souhaitons diffuser la meilleure qualité possible."
        };

        string Fill(string template) => (template ?? string.Empty)
            .Replace("{artiste}", artist, StringComparison.OrdinalIgnoreCase)
            .Replace("{titre}", title, StringComparison.OrdinalIgnoreCase)
            .Replace("{lien}", FirstNonEmpty(item.Url, source?.Url, "(lien inconnu)"), StringComparison.OrdinalIgnoreCase)
            .Replace("{plateforme}", IngestStages.PlatformLabel(item.Platform), StringComparison.OrdinalIgnoreCase)
            .Replace("{station}", settings.StationName, StringComparison.OrdinalIgnoreCase)
            .Replace("{motif}", reason, StringComparison.OrdinalIgnoreCase);

        return new IngestRequestMessage(source?.ContactEmail?.Trim() ?? string.Empty, Fill(settings.RequestSubject), Fill(settings.RequestBody));
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
