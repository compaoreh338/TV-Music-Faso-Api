using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Courriel de demande du fichier original, prêt à copier ou à ouvrir dans la messagerie.</summary>
public sealed record IngestRequestMessage(string To, string Subject, string Body)
{
    /// <summary>Nombre maximal de destinataires acceptés dans le champ « À ».</summary>
    public const int MaxRecipients = 10;

    /// <summary>Lien « mailto: » qui ouvre la messagerie avec le courriel pré-rempli.</summary>
    public string MailtoUri =>
        $"mailto:{string.Join(",", Recipients(To).Select(Uri.EscapeDataString))}?subject={Uri.EscapeDataString(Subject)}&body={Uri.EscapeDataString(Body)}";

    /// <summary>Fenêtre de rédaction de Gmail dans le navigateur, pré-remplie (pour les postes sans messagerie installée).</summary>
    public string GmailComposeUri =>
        $"https://mail.google.com/mail/?view=cm&fs=1&to={Uri.EscapeDataString(string.Join(",", Recipients(To)))}&su={Uri.EscapeDataString(Subject)}&body={Uri.EscapeDataString(Body)}";

    /// <summary>
    /// Vérifie le champ « À » : une ou plusieurs adresses séparées par une virgule ou un point-virgule.
    /// Renvoie null si tout est correct, sinon le message à afficher.
    /// </summary>
    public static string? ValidateRecipients(string? value)
    {
        var recipients = Recipients(value);
        if (recipients.Count == 0)
        {
            return "Indiquez l'adresse du destinataire.";
        }

        if (recipients.Count > MaxRecipients)
        {
            return $"{MaxRecipients} destinataires au plus.";
        }

        var invalid = recipients.FirstOrDefault(address => !IsAddress(address));
        return invalid is null ? null : $"Adresse invalide : « {invalid} ».";
    }

    /// <summary>Adresses du champ « À », sans espaces ni doublons.</summary>
    public static IReadOnlyList<string> Recipients(string? value) =>
        (value ?? string.Empty)
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsAddress(string address) =>
        address.Length <= 254
        && !address.Any(char.IsWhiteSpace)
        && System.Net.Mail.MailAddress.TryCreate(address, out var parsed)
        && string.Equals(parsed.Address, address, StringComparison.OrdinalIgnoreCase)
        && parsed.Host.Contains('.', StringComparison.Ordinal);

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
