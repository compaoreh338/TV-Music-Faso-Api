using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>
/// Données créées à la première installation : langues du Burkina + français,
/// comptes démo, médiathèque de démonstration.
/// </summary>
public static class InstallSeeder
{
    public static void SeedLanguages(ILanguageCatalog languages)
    {
        ArgumentNullException.ThrowIfNull(languages);
        languages.EnsureSeed();
    }

    /// <summary>
    /// Les 18 genres de l'offre (enum <see cref="MusicalGenre"/>) sont créés au premier démarrage, avec leur numéro
    /// d'origine : les clips existants restent liés. Un genre renommé ou supprimé ensuite n'est pas recréé.
    /// </summary>
    public static void SeedGenres(IGenreCatalog genres)
    {
        ArgumentNullException.ThrowIfNull(genres);
        genres.EnsureSeed();
    }

    public static IReadOnlyList<Clip> SeededLibrary()
    {
        var library = SeedData.CreateLibrary();
        foreach (var clip in library)
        {
            if (string.IsNullOrWhiteSpace(clip.LanguageName))
            {
                clip.LanguageName = LanguageCatalog.LabelFor(clip.Language);
            }

            if (clip.ValidationStatus == ClipValidationStatus.Pending)
            {
                clip.ValidationStatus = ClipValidationStatus.Validated;
            }
        }

        return library;
    }
}
