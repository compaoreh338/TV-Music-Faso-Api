using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>
/// Pré-remplissage d'une fiche sans IA (M5) : titre de la plateforme, source suivie, historique de
/// l'artiste dans la médiathèque et corrections déjà faites par les valideurs. Chaque champ garde
/// son origine et un indice de confiance pour être coloré à l'écran.
/// </summary>
public sealed class ClipSuggestionBuilder
{
    private const string AliasScope = "alias:";
    private const string ArtistScope = "artist:";
    private const string SourceScope = "source:";

    private static readonly string[] RememberedFields =
    [
        ClipSuggestion.LanguageField,
        ClipSuggestion.GenreField,
        ClipSuggestion.ThemeField,
        ClipSuggestion.AudienceField,
        ClipSuggestion.LabelField
    ];

    private readonly IIngestStore _store;

    public ClipSuggestionBuilder(IIngestStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <param name="library">Médiathèque actuelle (historique des artistes, doublons probables).</param>
    public ClipSuggestion Build(IngestItem item, MonitoredSource? source, IReadOnlyList<Clip> library)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(library);
        var suggestion = new ClipSuggestion();
        var fallback = !string.IsNullOrWhiteSpace(source?.Owner) ? source!.Owner : item.ChannelTitle;
        var parsed = PlatformTitleParser.Parse(item.PlatformTitle, fallback);

        ApplyArtist(suggestion, parsed, source, item);
        suggestion.Title = parsed.Title;
        suggestion.Set(ClipSuggestion.TitleField, InfoSource.Platform,
            parsed.FromSeparator ? InfoConfidence.Medium : InfoConfidence.Low,
            parsed.FromSeparator ? "titre de la vidéo" : "titre de la vidéo, sans séparateur artiste/titre");

        suggestion.Year = item.PublishedAt?.Year ?? DateTime.Today.Year;
        suggestion.Set(ClipSuggestion.YearField, InfoSource.Platform, InfoConfidence.Medium,
            item.PublishedAt is null ? "année en cours" : "date de mise en ligne");

        if (source is not null && !string.IsNullOrWhiteSpace(source.Country))
        {
            suggestion.IsBurkinabe = source.IsBurkinabe;
            suggestion.OriginPlace = source.IsBurkinabe ? "Burkina Faso" : source.Country.Trim();
            suggestion.Set(ClipSuggestion.OriginField, InfoSource.MonitoredSource, InfoConfidence.High, $"pays de « {source.Name} »");
        }
        else
        {
            suggestion.IsBurkinabe = true;
            suggestion.OriginPlace = "Burkina Faso";
            suggestion.Set(ClipSuggestion.OriginField, InfoSource.Manual, InfoConfidence.Low, "valeur par défaut");
        }

        ApplyHistoryAndMemory(suggestion, source, library);

        suggestion.PopularityScore = PopularityFromViews(item.Views);
        if (item.Views is { } views)
        {
            suggestion.Set(ClipSuggestion.PopularityField, InfoSource.Platform, InfoConfidence.Medium, $"{views:N0} vues");
        }

        return suggestion;
    }

    /// <summary>Durée et qualité lues sur le fichier, plus fiables que tout le reste.</summary>
    public static void ApplyFile(ClipSuggestion suggestion, TechnicalReport report)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(report);
        if (report.DurationSeconds > 0)
        {
            suggestion.DurationSeconds = (int)Math.Round(report.DurationSeconds);
            suggestion.Set(ClipSuggestion.DurationField, InfoSource.File, InfoConfidence.High);
        }

        if (report.Height > 0)
        {
            suggestion.Quality = report.Height >= 2160 ? VideoQuality.Uhd4K : VideoQuality.Hd;
            suggestion.Set(ClipSuggestion.QualityField, InfoSource.File, InfoConfidence.High, $"{report.Width}×{report.Height}");
        }
    }

    /// <summary>Note de popularité 1 à 5 d'après les vues ; 0 si inconnues.</summary>
    public static int PopularityFromViews(long? views) => views switch
    {
        null => 0,
        < 5_000 => 1,
        < 20_000 => 2,
        < 100_000 => 3,
        < 500_000 => 4,
        _ => 5
    };

    /// <summary>Clip de la médiathèque avec le même artiste principal et le même titre.</summary>
    public static Clip? FindProbableDuplicate(ClipSuggestion suggestion, IReadOnlyList<Clip> library)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        var artist = PlatformTitleParser.Key(PlatformTitleParser.MainArtist(suggestion.Artist));
        var title = PlatformTitleParser.Key(suggestion.Title);
        if (artist.Length == 0 || title.Length == 0)
        {
            return null;
        }

        return library.FirstOrDefault(clip =>
            PlatformTitleParser.Key(clip.Title) == title
            && PlatformTitleParser.Key(PlatformTitleParser.MainArtist(clip.Artist)) == artist);
    }

    /// <summary>
    /// Mémorise ce que le valideur a corrigé pour les prochains clips : le nom d'artiste lu dans le titre,
    /// et la langue, le genre, le thème, la cible et le label pour l'artiste et pour la source.
    /// </summary>
    public void RememberCorrections(IngestItem item, ClipSuggestion final)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(final);
        var proposed = item.Suggestion;
        var rawArtist = PlatformTitleParser.Key(proposed.Artist);
        if (rawArtist.Length > 0
            && !string.IsNullOrWhiteSpace(final.Artist)
            && PlatformTitleParser.Key(final.Artist) != rawArtist)
        {
            _store.Remember(AliasScope + rawArtist, ClipSuggestion.ArtistField, final.Artist.Trim());
        }

        var artistKey = PlatformTitleParser.Key(PlatformTitleParser.MainArtist(final.Artist));
        foreach (var field in RememberedFields)
        {
            var value = ValueOf(final, field);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (artistKey.Length > 0)
            {
                _store.Remember(ArtistScope + artistKey, field, value);
            }

            if (item.SourceId is { } sourceId && value != ValueOf(proposed, field))
            {
                _store.Remember(SourceScope + sourceId, field, value);
            }
        }
    }

    private void ApplyArtist(ClipSuggestion suggestion, ParsedTitle parsed, MonitoredSource? source, IngestItem item)
    {
        var alias = _store.GetMemory(AliasScope + PlatformTitleParser.Key(parsed.Artist));
        if (alias.TryGetValue(ClipSuggestion.ArtistField, out var corrected) && !string.IsNullOrWhiteSpace(corrected))
        {
            suggestion.Artist = corrected;
            suggestion.Set(ClipSuggestion.ArtistField, InfoSource.Correction, InfoConfidence.High, $"« {parsed.Artist} » corrigé précédemment");
            return;
        }

        suggestion.Artist = parsed.Artist;
        if (parsed.FromSeparator)
        {
            var main = PlatformTitleParser.Key(PlatformTitleParser.MainArtist(parsed.Artist));
            var known = PlatformTitleParser.Key(source?.Owner) == main || PlatformTitleParser.Key(item.ChannelTitle) == main;
            suggestion.Set(ClipSuggestion.ArtistField, InfoSource.Platform,
                known ? InfoConfidence.High : InfoConfidence.Medium,
                known ? "titre de la vidéo, confirmé par la chaîne" : "titre de la vidéo");
        }
        else if (!string.IsNullOrWhiteSpace(source?.Owner))
        {
            suggestion.Set(ClipSuggestion.ArtistField, InfoSource.MonitoredSource, InfoConfidence.Medium, "propriétaire de la source");
        }
        else
        {
            suggestion.Set(ClipSuggestion.ArtistField, InfoSource.Platform, InfoConfidence.Low, "nom de la chaîne");
        }
    }

    private void ApplyHistoryAndMemory(ClipSuggestion suggestion, MonitoredSource? source, IReadOnlyList<Clip> library)
    {
        var mainKey = PlatformTitleParser.Key(PlatformTitleParser.MainArtist(suggestion.Artist));
        var history = mainKey.Length == 0
            ? []
            : library
                .Where(clip => PlatformTitleParser.Key(PlatformTitleParser.MainArtist(clip.Artist)) == mainKey)
                .OrderByDescending(clip => clip.IsValidated)
                .ThenByDescending(clip => clip.Year)
                .ToList();

        suggestion.IsNewTalent = mainKey.Length > 0 && history.Count == 0;
        suggestion.Set(ClipSuggestion.NewTalentField, InfoSource.ArtistHistory,
            mainKey.Length == 0 ? InfoConfidence.Low : InfoConfidence.High,
            history.Count == 0 ? "artiste absent de la médiathèque" : $"{history.Count} clip(s) déjà en médiathèque");

        var artistMemory = mainKey.Length == 0 ? new Dictionary<string, string>() : _store.GetMemory(ArtistScope + mainKey);
        var sourceMemory = source is null ? new Dictionary<string, string>() : _store.GetMemory(SourceScope + source.Id);
        var latest = history.FirstOrDefault();
        foreach (var field in RememberedFields)
        {
            if (artistMemory.TryGetValue(field, out var corrected) && TryAssign(suggestion, field, corrected))
            {
                suggestion.Set(field, InfoSource.Correction, InfoConfidence.High, "corrigé pour cet artiste");
                continue;
            }

            if (latest is not null && TryAssign(suggestion, field, HistoryValue(latest, field)))
            {
                suggestion.Set(field, InfoSource.ArtistHistory, InfoConfidence.Medium, $"repris de « {latest.Title} »");
                continue;
            }

            if (sourceMemory.TryGetValue(field, out var fromSource) && TryAssign(suggestion, field, fromSource))
            {
                suggestion.Set(field, InfoSource.Correction, InfoConfidence.Low, "habituel pour cette source");
            }
        }
    }

    private static string HistoryValue(Clip clip, string field) => field switch
    {
        ClipSuggestion.LanguageField => clip.LanguageLabel,
        ClipSuggestion.GenreField => clip.Genre.ToString(),
        ClipSuggestion.ThemeField => clip.Theme.ToString(),
        ClipSuggestion.AudienceField => clip.Audience.ToString(),
        ClipSuggestion.LabelField => clip.Label,
        _ => string.Empty
    };

    private static string ValueOf(ClipSuggestion suggestion, string field) => field switch
    {
        ClipSuggestion.LanguageField => suggestion.LanguageName.Trim(),
        ClipSuggestion.GenreField => suggestion.Genre?.ToString() ?? string.Empty,
        ClipSuggestion.ThemeField => suggestion.Theme?.ToString() ?? string.Empty,
        ClipSuggestion.AudienceField => suggestion.Audience?.ToString() ?? string.Empty,
        ClipSuggestion.LabelField => suggestion.Label.Trim(),
        _ => string.Empty
    };

    private static bool TryAssign(ClipSuggestion suggestion, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        switch (field)
        {
            case ClipSuggestion.LanguageField:
                suggestion.LanguageName = value.Trim();
                return true;
            case ClipSuggestion.GenreField when Enum.TryParse<MusicalGenre>(value, out var genre):
                suggestion.Genre = genre;
                return true;
            case ClipSuggestion.ThemeField when Enum.TryParse<ClipTheme>(value, out var theme) && Enum.IsDefined(theme):
                suggestion.Theme = theme;
                return true;
            case ClipSuggestion.AudienceField when Enum.TryParse<Audience>(value, out var audience) && Enum.IsDefined(audience):
                suggestion.Audience = audience;
                return true;
            case ClipSuggestion.LabelField:
                suggestion.Label = value.Trim();
                return true;
            default:
                return false;
        }
    }
}
