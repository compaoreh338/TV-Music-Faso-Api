namespace TVMusicFaso.Core.Domain;

public enum GenerationMode
{
    Automatic,
    Thematic
}

public sealed record ThematicFilter(
    MusicalGenre? Genre = null,
    ClipTheme? Theme = null,
    ClipLanguage? Language = null,
    bool BurkinabeOnly = false)
{
    public bool IsEmpty => Genre is null && Theme is null && Language is null && !BurkinabeOnly;

    public bool Matches(Clip clip)
    {
        if (Genre is { } genre && clip.Genre != genre)
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
            if (Genre is { } genre)
            {
                parts.Add(genre.ToDisplayName());
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
}

public sealed record ThematicPreset(string Name, ThematicFilter Filter)
{
    public static IReadOnlyList<ThematicPreset> All { get; } =
    [
        new("Spécial 100% Slam", new ThematicFilter(Genre: MusicalGenre.Slam)),
        new("Spécial Musique du Terroir", new ThematicFilter(Genre: MusicalGenre.Traditionnel)),
        new("Spécial Gospel", new ThematicFilter(Genre: MusicalGenre.Gospel)),
        new("Spécial Coupé-décalé", new ThematicFilter(Genre: MusicalGenre.CoupeDecale)),
        new("Spécial Afro-pop", new ThematicFilter(Genre: MusicalGenre.AfroPop)),
        new("Spécial 100% Burkinabè", new ThematicFilter(BurkinabeOnly: true))
    ];
}
