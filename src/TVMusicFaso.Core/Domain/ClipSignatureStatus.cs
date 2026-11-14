namespace TVMusicFaso.Core.Domain;

/// <summary>
/// État de la version signée aux couleurs de Filinfo (logo et bandeau bas incrustés).
/// </summary>
public enum ClipSignatureStatus
{
    /// <summary>Aucune version signée : l'antenne utilise le fichier d'origine.</summary>
    None = 0,

    /// <summary>Version signée produite, en attente de prévisualisation.</summary>
    ToReview = 1,

    /// <summary>Version signée vérifiée : les listes de diffusion l'utilisent.</summary>
    Approved = 2
}
