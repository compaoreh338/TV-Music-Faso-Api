using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TVMusicFaso.Core.Rules;

/// <summary>
/// Artistes d'un clip pour l'espacement à l'antenne : « Nabalum feat. Smarty » compte pour Nabalum et pour Smarty,
/// « FLOBY » et « Floby » sont le même artiste, et « Artiste inconnu » ne regroupe personne.
/// </summary>
public static partial class ArtistIdentity
{
    private static readonly HashSet<string> Placeholders = new(StringComparer.Ordinal)
    {
        "artiste inconnu", "inconnu", "unknown", "unknown artist", "various", "various artists", "divers", "artistes divers", "sans artiste"
    };

    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> Cache = new(StringComparer.Ordinal);

    /// <summary>Noms normalisés (minuscules, sans accents ni ponctuation) ; vide si l'artiste n'est pas renseigné.</summary>
    public static IReadOnlyList<string> Keys(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist))
        {
            return [];
        }

        if (Cache.Count > 20_000)
        {
            Cache.Clear();
        }

        return Cache.GetOrAdd(artist, Parse);
    }

    private static IReadOnlyList<string> Parse(string artist)
    {
        var whole = Normalize(artist);
        if (whole.Length == 0 || Placeholders.Contains(whole))
        {
            return [];
        }

        return Separators().Split(artist)
            .Select(Normalize)
            .Where(name => name.Length > 0 && !Placeholders.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Vrai si les deux clips ont au moins un artiste en commun.</summary>
    public static bool Shares(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        first.Count > 0 && second.Count > 0 && first.Any(second.Contains);

    private static string Normalize(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                if (space && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(character);
                space = false;
            }
            else
            {
                space = true;
            }
        }

        return builder.ToString();
    }

    [GeneratedRegex(@"\s*(?:,|&|\+|/|;|\bfeat\b\.?|\bft\b\.?|\bfeaturing\b|\bwith\b|\bavec\b|\s[xX×]\s)\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Separators();
}
