using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Catalogue des genres musicaux : création, renommage et suppression.</summary>
public interface IGenreCatalog
{
    /// <summary>Tous les genres, supprimés compris (<see cref="GenreDefinition.IsArchived"/>).</summary>
    IReadOnlyList<GenreDefinition> GetAll();

    /// <summary>Crée un genre ; refuse un nom déjà utilisé.</summary>
    GenreDefinition Add(string label);

    /// <summary>Renomme un genre ; les clips gardent leur genre, seul le nom affiché change.</summary>
    GenreDefinition Rename(int id, string label);

    /// <summary>
    /// Supprime un genre. Les clips qui l'utilisent passent au genre de remplacement
    /// (obligatoire s'il y en a). Renvoie le nombre de clips modifiés.
    /// </summary>
    int Delete(int id, int? replacementId);

    /// <summary>Ajoute les genres d'origine absents.</summary>
    void EnsureSeed();
}
