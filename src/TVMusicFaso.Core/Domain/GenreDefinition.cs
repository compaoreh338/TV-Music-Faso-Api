namespace TVMusicFaso.Core.Domain;

/// <summary>
/// Genre musical géré dans l'écran « Genres ». Son numéro est celui stocké dans les clips
/// et les tranches : les 18 genres d'origine gardent le leur, les nouveaux prennent les suivants.
/// </summary>
public sealed class GenreDefinition
{
    /// <summary>Numéro du genre (valeur de <see cref="MusicalGenre"/>).</summary>
    public int Id { get; set; }

    /// <summary>Nom affiché partout dans l'app (fiches, tranches, filtres, rapports).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Vrai pour les genres livrés avec l'app.</summary>
    public bool IsSeeded { get; set; }

    /// <summary>Genre supprimé : il n'est plus proposé, mais son nom reste lisible dans l'historique.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Nombre de clips de ce genre, calculé par le catalogue (évite de charger toute la médiathèque).</summary>
    public int ClipCount { get; set; }

    /// <summary>Valeur utilisée par les clips et les règles de tranches.</summary>
    public MusicalGenre Genre => (MusicalGenre)Id;

    public GenreDefinition Clone() => new()
    {
        Id = Id,
        Label = Label,
        IsSeeded = IsSeeded,
        IsArchived = IsArchived,
        ClipCount = ClipCount
    };
}
