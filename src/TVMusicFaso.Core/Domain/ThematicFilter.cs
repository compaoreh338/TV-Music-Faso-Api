namespace TVMusicFaso.Core.Domain;

public enum GenerationMode
{
    Automatic,
    Thematic
}

/// <summary>
/// Filtre de la programmation thématique : seuls les clips qui le respectent entrent dans la grille.
/// </summary>
/// <param name="Genre">Genre unique (préréglages « Spécial … »).</param>
/// <param name="Genres">Plusieurs genres : le clip doit appartenir à l'un d'eux.</param>
/// <param name="YearFrom">Année de sortie minimale du clip (incluse).</param>
/// <param name="YearTo">Année de sortie maximale du clip (incluse).</param>
public sealed record ThematicFilter(
    MusicalGenre? Genre = null,
    ClipTheme? Theme = null,
    ClipLanguage? Language = null,
    bool BurkinabeOnly = false,
    IReadOnlyList<MusicalGenre>? Genres = null,
    int? YearFrom = null,
    int? YearTo = null)
{
    public bool IsEmpty => Genre is null && Theme is null && Language is null && !BurkinabeOnly
        && AllGenres.Count == 0 && YearFrom is null && YearTo is null;

    /// <summary><see cref="Genre"/> et <see cref="Genres"/> réunis, sans doublon.</summary>
    public IReadOnlyList<MusicalGenre> AllGenres =>
        (Genres ?? []).Concat(Genre is { } single ? [single] : []).Distinct().ToList();

    public bool Matches(Clip clip)
    {
        var genres = AllGenres;
        if (genres.Count > 0 && !genres.Contains(clip.Genre))
        {
            return false;
        }

        var year = (int)clip.Year;
        if ((YearFrom is { } from && year < from) || (YearTo is { } to && year > to))
        {
            return false;
        }

        if (Theme is { } theme && clip.Theme != theme)
        {
            return false;
        }

        if (Language is { } language && clip.Language != language)
        {
            return false;
        }

        return !BurkinabeOnly || clip.IsBurkinabe;
    }

    public string Label
    {
        get
        {
            var parts = new List<string>();
            var genres = AllGenres;
            if (genres.Count > 0)
            {
                parts.Add(string.Join(" + ", genres.Select(genre => genre.ToDisplayName())));
            }

            if (YearLabel is { } years)
            {
                parts.Add(years);
            }

            if (Theme is { } theme)
            {
                parts.Add(theme.ToDisplayName());
            }

            if (Language is { } language)
            {
                parts.Add(language.ToDisplayName());
            }

            if (BurkinabeOnly)
            {
                parts.Add("100 % burkinabè");
            }

            return parts.Count == 0 ? "Thématique" : "Spécial " + string.Join(" · ", parts);
        }
    }

    /// <summary>« 2000 », « 2000–2005 », « depuis 2000 », « jusqu'à 2005 » ou null.</summary>
    public string? YearLabel => (YearFrom, YearTo) switch
    {
        (null, null) => null,
        ({ } from, { } to) when from == to => from.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ({ } from, { } to) => $"{from}–{to}",
        ({ } from, null) => $"depuis {from}",
        (null, { } to) => $"jusqu'à {to}"
    };
}

public sealed record ThematicPreset(string Name, ThematicFilter Filter)
{
    public static IReadOnlyList<ThematicPreset> All { get; } =
    [
        new("Spécial 100% Slam", new ThematicFilter(Genre: MusicalGenre.Slam)),
        new("Spécial Musique du Terroir", new ThematicFilter(Genre: MusicalGenre.Traditionnel)),
        new("Spécial Religieux", new ThematicFilter(Genre: MusicalGenre.Gospel)),
        new("Spécial Coupé-décalé", new ThematicFilter(Genre: MusicalGenre.CoupeDecale)),
        new("Spécial Afro-pop", new ThematicFilter(Genre: MusicalGenre.AfroPop)),
        new("Spécial 100% Burkinabè", new ThematicFilter(BurkinabeOnly: true))
    ];
}
