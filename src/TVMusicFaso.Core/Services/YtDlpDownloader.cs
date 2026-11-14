using System.Globalization;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public enum DownloadResultKind
{
    Success = 0,

    /// <summary>Vidéo écartée par le filtre de durée (Short, extrait) ou direct en cours.</summary>
    Filtered = 1,

    /// <summary>La plateforme refuse le téléchargement (connexion exigée, vidéo privée, blocage) : fichier à demander.</summary>
    Blocked = 2,

    /// <summary>Erreur passagère (réseau, coupure) : nouvel essai plus tard.</summary>
    Failed = 3
}

public sealed record DownloadOutcome(DownloadResultKind Kind, string? FilePath, string Message);

/// <summary>Téléchargement d'un clip détecté.</summary>
public interface IVideoDownloader
{
    Task<DownloadOutcome> DownloadAsync(IngestItem item, string folder, IngestSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// yt-dlp, meilleure qualité disponible, fusionnée en MP4. L'adresse est reconstruite à partir de
/// l'identifiant de la vidéo : rien de ce que contient la base n'arrive tel quel dans la commande.
/// Un téléchargement interrompu reprend là où il s'était arrêté (fichier .part).
/// </summary>
public sealed class YtDlpDownloader : IVideoDownloader
{
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromHours(2);

    private static readonly string[] VideoExtensions = [".mp4", ".mkv", ".webm", ".mov", ".m4v"];

    private static readonly string[] BlockedMarkers =
    [
        "Sign in to confirm",
        "Private video",
        "This video is private",
        "Video unavailable",
        "This video is unavailable",
        "copyright",
        "blocked it in your country",
        "not available in your country",
        "age-restricted",
        "Join this channel",
        "members-only",
        "HTTP Error 403",
        "HTTP Error 429"
    ];

    public YtDlpDownloader(string ytDlpPath, string? ffmpegPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ytDlpPath);
        YtDlpPath = ytDlpPath;
        FfmpegPath = ffmpegPath;
    }

    public string YtDlpPath { get; }

    public string? FfmpegPath { get; }

    public static string? Locate(string? configuredPath) => ExternalTool.Find("yt-dlp", configuredPath);

    public static string MissingMessage => OperatingSystem.IsWindows()
        ? "yt-dlp introuvable sur le serveur : copiez yt-dlp.exe dans C:\\Tools ou indiquez son chemin dans les réglages."
        : "yt-dlp introuvable : installez-le (brew install yt-dlp) ou indiquez son chemin dans les réglages.";

    public async Task<DownloadOutcome> DownloadAsync(IngestItem item, string folder, IngestSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!YouTubeFeedReader.IsVideoId(item.ExternalId))
        {
            return new DownloadOutcome(DownloadResultKind.Blocked, null, "Pas de vidéo YouTube à télécharger : le fichier est à demander ou à déposer.");
        }

        Directory.CreateDirectory(folder);
        var stem = item.Id.ToString("N");
        var output = new ExternalTool.Tail(40);
        var errors = new ExternalTool.Tail(12);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DownloadTimeout);
        int exitCode;
        try
        {
            exitCode = await ExternalTool.RunAsync(
                YtDlpPath,
                BuildArguments(item.ExternalId, folder, stem, settings, FfmpegPath),
                folder,
                output.Add,
                errors.Add,
                timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DownloadOutcome(DownloadResultKind.Failed, null, "Téléchargement trop long (plus de 2 h) : nouvel essai plus tard.");
        }

        var file = FindDownloaded(folder, stem);
        if (exitCode == 0 && file is not null)
        {
            return new DownloadOutcome(DownloadResultKind.Success, file, "Téléchargé.");
        }

        var log = output + Environment.NewLine + errors;
        if (exitCode == 0)
        {
            // yt-dlp recopie tout le filtre (durée et direct) dans son message : la cause exacte n'est pas connue.
            return new DownloadOutcome(DownloadResultKind.Filtered, null,
                $"Vidéo de moins de {settings.MinDurationSeconds} s (Short, extrait) ou direct en cours : ignorée.");
        }

        return Classify(log);
    }

    /// <summary>Version installée, ou chaîne vide si yt-dlp ne répond pas.</summary>
    public async Task<string> VersionAsync(CancellationToken cancellationToken)
    {
        var output = new ExternalTool.Tail(2);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var code = await ExternalTool.RunAsync(YtDlpPath, ["--version"], null, output.Add, _ => { }, timeout.Token).ConfigureAwait(false);
            return code == 0 ? output.ToString().Trim() : string.Empty;
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Mise à jour par yt-dlp lui-même (version autonome) ; renvoie son message.</summary>
    public async Task<string> UpdateAsync(CancellationToken cancellationToken)
    {
        var output = new ExternalTool.Tail(6);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var code = await ExternalTool.RunAsync(YtDlpPath, ["-U"], null, output.Add, output.Add, timeout.Token).ConfigureAwait(false);
        var message = output.ToString().Trim();
        return code == 0
            ? (message.Length > 0 ? message : "yt-dlp est à jour.")
            : $"Mise à jour impossible (code {code}). {message} Si yt-dlp a été installé par Homebrew ou pip, mettez-le à jour par ce moyen.".Trim();
    }

    public static IReadOnlyList<string> BuildArguments(string videoId, string folder, string stem, IngestSettings settings, string? ffmpegPath)
    {
        if (!YouTubeFeedReader.IsVideoId(videoId))
        {
            throw new ArgumentException("Identifiant de vidéo YouTube invalide.", nameof(videoId));
        }

        var arguments = new List<string>
        {
            "--no-playlist",
            "--no-progress",
            "--newline",
            "--no-mtime",
            "--no-cache-dir",
            "--continue",
            "--retries", "5",
            "--fragment-retries", "10",
            "-f", "bv*+ba/b",
            "-S", $"res:{MediaInspector.BroadcastMaxHeight.ToString(CultureInfo.InvariantCulture)},vcodec:h264,acodec:aac",
            "--merge-output-format", "mp4",
            "--match-filter", $"duration >= {settings.MinDurationSeconds.ToString(CultureInfo.InvariantCulture)} & !is_live",
            "-o", Path.Combine(folder, stem + ".%(ext)s")
        };
        if (!string.IsNullOrWhiteSpace(ffmpegPath))
        {
            arguments.AddRange(["--ffmpeg-location", ffmpegPath]);
        }

        arguments.AddRange(["--", YouTubeFeedReader.WatchUrl(videoId)]);
        return arguments;
    }

    /// <summary>Erreur de yt-dlp : blocage de la plateforme (fichier à demander) ou erreur passagère.</summary>
    public static DownloadOutcome Classify(string log)
    {
        var lines = (log ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var message = lines.LastOrDefault() ?? (log ?? string.Empty).Trim();
        if (message.Length > 400)
        {
            message = message[..400] + "…";
        }

        var blocked = BlockedMarkers.Any(marker => (log ?? string.Empty).Contains(marker, StringComparison.OrdinalIgnoreCase));
        return new DownloadOutcome(
            blocked ? DownloadResultKind.Blocked : DownloadResultKind.Failed,
            null,
            string.IsNullOrWhiteSpace(message) ? "yt-dlp a échoué sans message." : message);
    }

    private static string? FindDownloaded(string folder, string stem) =>
        Directory.EnumerateFiles(folder, stem + ".*")
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(path => new FileInfo(path).Length)
            .FirstOrDefault();
}
