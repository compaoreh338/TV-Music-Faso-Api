using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TVMusicFaso.Core.Services;

/// <summary>Artiste et titre lus dans le titre d'une vidéo, avec un indice de fiabilité.</summary>
/// <param name="FromSeparator">Vrai si le titre contenait « Artiste - Titre » ; faux si l'artiste vient d'ailleurs.</param>
public sealed record ParsedTitle(string Artist, string Title, bool FromSeparator);

/// <summary>
/// Découpe les titres de plateforme : « Artiste - Titre (Clip officiel) », « Artiste ft. X – Titre »,
/// « ARTISTE | TITRE [4K] ». Le bruit (clip officiel, lyrics, HD…) est retiré, le « feat. » est gardé.
/// </summary>
public static partial class PlatformTitleParser
{
    private static readonly string[] Separators = [" - ", " – ", " — ", " | ", " ~ ", " // "];

    /// <param name="title">Titre de la vidéo.</param>
    /// <param name="fallbackArtist">Artiste à utiliser si le titre ne le donne pas (propriétaire de la source, nom de la chaîne).</param>
    public static ParsedTitle Parse(string? title, string? fallbackArtist)
    {
        var cleaned = Clean(title);
        var fallback = Tidy(fallbackArtist);
        if (cleaned.Length == 0)
        {
            return new ParsedTitle(fallback, string.Empty, false);
        }

        foreach (var separator in Separators)
        {
            var index = cleaned.IndexOf(separator, StringComparison.Ordinal);
            if (index <= 0 || index + separator.Length >= cleaned.Length)
            {
                continue;
            }

            var left = Tidy(cleaned[..index]);
            var right = Tidy(cleaned[(index + separator.Length)..]);
            if (left.Length == 0 || right.Length == 0)
            {
                continue;
            }

            // « Titre | Artiste » : la partie droite correspond à la chaîne.
            if (fallback.Length > 0 && SameName(right, fallback) && !SameName(left, fallback))
            {
                (left, right) = (right, left);
            }

            var (title2, featuring) = ExtractFeaturing(right);
            var artist = featuring.Length > 0 ? $"{left} feat. {featuring}" : left;
            return new ParsedTitle(NormalizeFeaturing(artist), title2, true);
        }

        var (bare, feat) = ExtractFeaturing(cleaned);
        var fallbackWithFeat = feat.Length > 0 && fallback.Length > 0 ? $"{fallback} feat. {feat}" : fallback;
        return new ParsedTitle(NormalizeFeaturing(fallbackWithFeat), bare, false);
    }

    /// <summary>Artiste principal, sans les invités : sert à retrouver l'historique de l'artiste.</summary>
    public static string MainArtist(string? artist)
    {
        var value = Tidy(artist);
        var match = FeaturingSplit().Match(value);
        return match.Success ? Tidy(value[..match.Index]) : value;
    }

    /// <summary>Clé de comparaison : minuscules, sans accents ni ponctuation.</summary>
    public static string Key(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ');
        }

        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    private static bool SameName(string a, string b) => Key(a) == Key(b);

    private static string Clean(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var value = WebUtilityDecode(title).Replace('\u00A0', ' ');
        value = Emoji().Replace(value, " ");
        value = BracketNoise().Replace(value, " ");
        value = TrailingNoise().Replace(value, string.Empty);
        value = Hashtags().Replace(value, " ");
        return Tidy(value);
    }

    private static (string Title, string Featuring) ExtractFeaturing(string value)
    {
        var bracketed = BracketFeaturing().Match(value);
        if (bracketed.Success)
        {
            var title = Tidy(value.Remove(bracketed.Index, bracketed.Length));
            return (title, Tidy(bracketed.Groups["who"].Value));
        }

        var inline = FeaturingSplit().Match(value);
        if (inline.Success && inline.Index > 0)
        {
            return (Tidy(value[..inline.Index]), Tidy(value[(inline.Index + inline.Length)..]));
        }

        return (value, string.Empty);
    }

    private static string NormalizeFeaturing(string artist) =>
        FeaturingSplit().Replace(artist, " feat. ").Replace("  ", " ").Trim();

    /// <summary>Espaces et guillemets superflus ; un texte tout en majuscules passe en casse de titre.</summary>
    private static string Tidy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = Spaces().Replace(value, " ").Trim().Trim('"', '«', '»', '“', '”', '\'', '-', '–', '—', '|', ':', ' ');
        var letters = trimmed.Where(char.IsLetter).ToList();
        if (letters.Count > 3 && letters.All(char.IsUpper))
        {
            trimmed = CultureInfo.GetCultureInfo("fr-FR").TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
        }

        return trimmed;
    }

    private static string WebUtilityDecode(string value) => System.Net.WebUtility.HtmlDecode(value);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[\p{So}\p{Cs}\uFE0F]+")]
    private static partial Regex Emoji();

    [GeneratedRegex(@"#\w+")]
    private static partial Regex Hashtags();

    /// <summary>(Clip officiel), [Official Video], (Lyrics), [4K], (Prod. by X)…</summary>
    [GeneratedRegex(
        @"[\(\[\{][^\)\]\}]*\b(clip|video|vid[ée]o|officiel(le)?|official|audio|lyrics?|paroles|visuali[sz]er|hd|4k|uhd|1080p?|720p?|prod\.?|produced|remaster(ed)?|live|teaser|exclusi(f|ve|vit[ée]))\b[^\)\]\}]*[\)\]\}]",
        RegexOptions.IgnoreCase)]
    private static partial Regex BracketNoise();

    [GeneratedRegex(@"\s*[-–—|]?\s*\b(clip|video|vid[ée]o)\s+(officiel(le)?|official)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingNoise();

    [GeneratedRegex(@"[\(\[]\s*(?:feat\.?|ft\.?|featuring|avec)\s+(?<who>[^\)\]]+)[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex BracketFeaturing();

    [GeneratedRegex(@"\s+(?:feat\.?|ft\.?|featuring|x)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex FeaturingSplit();
}
