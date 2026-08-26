namespace TVMusicFaso.Core.Services;

/// <summary>
/// L'offre impose HTTPS/TLS entre Desktop, API et tableau de bord.
/// Toute URL d'API doit être en https.
/// </summary>
public static class HttpsTransportPolicy
{
    public static bool IsSecureApiUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    public static void EnsureSecure(string? url)
    {
        if (!IsSecureApiUrl(url))
        {
            throw new InvalidOperationException(
                "Transport refusé : l'API doit être joignable en HTTPS/TLS (https://...).");
        }
    }
}
