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
