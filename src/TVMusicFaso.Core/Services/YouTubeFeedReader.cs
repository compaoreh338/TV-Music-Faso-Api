using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Vidéo lue dans le flux d'une source.</summary>
public sealed record FeedVideo(string VideoId, string Title, string ChannelTitle, DateTimeOffset? PublishedAt, long? Views)
{
    public string Url => YouTubeFeedReader.WatchUrl(VideoId);
}

/// <summary>Contenu du flux : identifiant résolu de la source et dernières vidéos.</summary>
public sealed record FeedResult(string ResolvedId, string ChannelTitle, IReadOnlyList<FeedVideo> Videos);

/// <summary>Lecture des nouvelles vidéos d'une source suivie.</summary>
public interface IVideoFeedReader
{
    Task<FeedResult> FetchAsync(MonitoredSource source, CancellationToken cancellationToken);
}

/// <summary>
/// Flux publics YouTube (sans clé ni quota) : les 15 dernières vidéos d'une chaîne ou d'une liste.
/// Les liens @nom, /c/ et /user/ sont résolus une fois en identifiant de chaîne UC….
/// </summary>
public sealed partial class YouTubeFeedReader : IVideoFeedReader, IDisposable
{
    private const string FeedBase = "https://www.youtube.com/feeds/videos.xml";

    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Yt = "http://www.youtube.com/xml/schemas/2015";
    private static readonly XNamespace Media = "http://search.yahoo.com/mrss/";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public YouTubeFeedReader(HttpClient? http = null)
    {
        _ownsClient = http is null;
        _http = http ?? CreateClient();
    }

    public static string WatchUrl(string videoId) => $"https://www.youtube.com/watch?v={videoId}";

    public static bool IsVideoId(string? value) => value is not null && VideoIdPattern().IsMatch(value);

    /// <summary>
    /// Type de lien YouTube reconnu, et identifiant si le lien le donne directement (sinon vide : résolu au premier passage).
    /// Un lien de vidéo (watch, youtu.be, shorts) désigne la chaîne qui l'a publiée ; avec « list= », la liste de lecture.
    /// </summary>
    public static (SourcePlatform Platform, string Id)? Classify(string? url)
    {
        if (!TryParse(url, out var uri))
        {
            return null;
        }

        var list = System.Web.HttpUtility.ParseQueryString(uri.Query)["list"];
        if (!string.IsNullOrWhiteSpace(list) && PlaylistIdPattern().IsMatch(list) && !list.StartsWith("RD", StringComparison.Ordinal))
        {
            return (SourcePlatform.YouTubePlaylist, list);
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals("channel", StringComparison.OrdinalIgnoreCase) && ChannelIdPattern().IsMatch(segments[1]))
        {
            return (SourcePlatform.YouTubeChannel, segments[1]);
        }

        if ((segments.Length >= 1 && segments[0].StartsWith('@') && segments[0].Length > 1)
            || (segments.Length >= 2 && segments[0] is "c" or "user"))
        {
            return (SourcePlatform.YouTubeChannel, string.Empty);
        }

        return VideoIdOf(uri) is not null ? (SourcePlatform.YouTubeChannel, string.Empty) : null;
    }

    /// <summary>Lien complet et propre à enregistrer (https ajouté si absent, espaces retirés) ; inchangé si ce n'est pas YouTube.</summary>
    public static string Normalize(string? url)
    {
        var trimmed = url?.Trim() ?? string.Empty;
        return TryParse(trimmed, out var uri) ? uri.GetLeftPart(UriPartial.Query) : trimmed;
    }

    /// <summary>Identifiant de la vidéo d'un lien watch?v=, youtu.be/…, /shorts/…, /live/… ou /embed/….</summary>
    public static string? VideoIdOf(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var candidate = uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)
            ? segments.FirstOrDefault()
            : segments.Length >= 2 && segments[0] is "shorts" or "live" or "embed" or "v"
                ? segments[1]
                : segments.Length >= 1 && segments[0] == "watch"
                    ? System.Web.HttpUtility.ParseQueryString(uri.Query)["v"]
                    : null;
        return IsVideoId(candidate) ? candidate : null;
    }

    private static bool TryParse(string? url, out Uri uri)
    {
        var text = url?.Trim() ?? string.Empty;
        if (text.Length > 0 && !text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var parsed)
            && parsed.Scheme is "https" or "http"
            && IsYouTubeHost(parsed.Host))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    public async Task<FeedResult> FetchAsync(MonitoredSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.IsAutomatic)
        {
            throw new InvalidOperationException("Cette plateforme n'est pas interrogée automatiquement : ajoutez ses clips à la main.");
        }

        var resolved = source.ExternalId;
        if (string.IsNullOrWhiteSpace(resolved))
        {
            resolved = await ResolveAsync(source.Url, cancellationToken).ConfigureAwait(false);
        }

        var parameter = resolved.StartsWith("UC", StringComparison.Ordinal) ? "channel_id" : "playlist_id";
        var feedUrl = $"{FeedBase}?{parameter}={Uri.EscapeDataString(resolved)}";
        using var response = await _http.GetAsync(feedUrl, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("Flux introuvable : la chaîne ou la liste a peut-être été supprimée ou rendue privée.");
        }

        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var (channel, videos) = ParseFeed(xml);
        return new FeedResult(resolved, channel, videos);
    }

    /// <summary>Lit un flux Atom YouTube ; les entrées incomplètes sont ignorées.</summary>
    public static (string ChannelTitle, IReadOnlyList<FeedVideo> Videos) ParseFeed(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException("Réponse de YouTube illisible (flux XML attendu).", exception);
        }

        var root = document.Root ?? throw new InvalidOperationException("Flux vide.");
        var channel = root.Element(Atom + "author")?.Element(Atom + "name")?.Value
            ?? root.Element(Atom + "title")?.Value
            ?? string.Empty;
        var videos = new List<FeedVideo>();
        foreach (var entry in root.Elements(Atom + "entry"))
        {
            var id = entry.Element(Yt + "videoId")?.Value?.Trim();
            var title = entry.Element(Atom + "title")?.Value?.Trim();
            if (!IsVideoId(id) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            DateTimeOffset? published = DateTimeOffset.TryParse(
                entry.Element(Atom + "published")?.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var date)
                ? date
                : null;
            long? views = long.TryParse(
                entry.Element(Media + "group")?.Element(Media + "community")?.Element(Media + "statistics")?.Attribute("views")?.Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var count)
                ? count
                : null;
            var author = entry.Element(Atom + "author")?.Element(Atom + "name")?.Value?.Trim() ?? channel;
            videos.Add(new FeedVideo(id!, title, author, published, views));
        }

        return (channel.Trim(), videos);
    }

    /// <summary>Identifiant UC… d'une chaîne, cherché dans sa page publique.</summary>
    public static string? ExtractChannelId(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        foreach (var pattern in new[] { ExternalIdInPage(), CanonicalInPage(), ChannelIdInPage() })
        {
            var match = pattern.Match(html);
            if (match.Success)
            {
                return match.Groups["id"].Value;
            }
        }

        return null;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

    private async Task<string> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        var classified = Classify(url)
            ?? throw new InvalidOperationException(UnrecognizedMessage);
        if (!string.IsNullOrWhiteSpace(classified.Id))
        {
            return classified.Id;
        }

        TryParse(url, out var uri);
        var videoId = VideoIdOf(uri);
        var page = videoId is null ? uri.GetLeftPart(UriPartial.Path) : WatchUrl(videoId);
        using var response = await _http.GetAsync(page, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException(videoId is null ? "Chaîne introuvable : vérifiez le lien." : "Vidéo introuvable : vérifiez le lien.");
        }

        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return (videoId is null ? ExtractChannelId(html) : ExtractVideoOwnerId(html))
            ?? throw new InvalidOperationException("Identifiant de la chaîne introuvable dans la page YouTube : collez le lien …/channel/UC… à la place.");
    }

    public const string UnrecognizedMessage =
        "Lien YouTube non reconnu : collez le lien de la chaîne (…/@nom ou …/channel/UC…), d'une vidéo de la chaîne ou d'une liste de lecture (…/playlist?list=…).";

    /// <summary>Chaîne qui a publié la vidéo, lue dans la page de la vidéo.</summary>
    public static string? ExtractVideoOwnerId(string html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        foreach (var pattern in new[] { ExternalChannelIdInPage(), ItempropChannelIdInPage(), ChannelIdInPage() })
        {
            var match = pattern.Match(html);
            if (match.Success)
            {
                return match.Groups["id"].Value;
            }
        }

        return null;
    }

    private static bool IsYouTubeHost(string host) =>
        host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)
        || host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("\"externalChannelId\"\\s*:\\s*\"(?<id>UC[A-Za-z0-9_-]{22})\"")]
    private static partial Regex ExternalChannelIdInPage();

    [GeneratedRegex("itemprop=\"channelId\"\\s+content=\"(?<id>UC[A-Za-z0-9_-]{22})\"")]
    private static partial Regex ItempropChannelIdInPage();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; TVMusicFaso/1.0)");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-FR,fr;q=0.9");
        // Évite la page de consentement aux cookies servie en Europe à la place de la chaîne.
        client.DefaultRequestHeaders.Add("Cookie", "CONSENT=YES+1; SOCS=CAI");
        return client;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$")]
    private static partial Regex VideoIdPattern();

    [GeneratedRegex("^UC[A-Za-z0-9_-]{22}$")]
    private static partial Regex ChannelIdPattern();

    [GeneratedRegex("^(PL|UU|OL|FL|RD)[A-Za-z0-9_-]{10,}$")]
    private static partial Regex PlaylistIdPattern();

    [GeneratedRegex("\"externalId\"\\s*:\\s*\"(?<id>UC[A-Za-z0-9_-]{22})\"")]
    private static partial Regex ExternalIdInPage();

    [GeneratedRegex("<link rel=\"canonical\" href=\"https://www\\.youtube\\.com/channel/(?<id>UC[A-Za-z0-9_-]{22})\"")]
    private static partial Regex CanonicalInPage();

    [GeneratedRegex("\"channelId\"\\s*:\\s*\"(?<id>UC[A-Za-z0-9_-]{22})\"")]
    private static partial Regex ChannelIdInPage();
}
