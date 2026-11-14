using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public interface IClipRepository
{
    IReadOnlyList<Clip> GetAll();

    Clip? GetById(Guid id);

    void Add(Clip clip);

    void Update(Clip clip);

    void Remove(Guid id);

    void SetValidation(Guid id, ClipValidationStatus status, string? note = null);

    /// <summary>
    /// Enregistre la revue du clip (grille, BBDA, retrait, fiche de suivi). Les métadonnées ne bougent pas,
    /// sauf la conformité morale qui suit les critères « Morale » d'une grille cochée.
    /// Par défaut : relit la fiche et la réécrit avec la nouvelle revue.
    /// </summary>
    void SaveReview(Guid id, ClipReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        var clip = GetById(id) ?? throw new InvalidOperationException("Clip introuvable : revue non enregistrée.");
        clip.Review = review.Clone();
        if (review.HasCompliance)
        {
            clip.IsMorallyCompliant = review.MoralCriteriaMet;
        }

        Update(clip);
    }

    IReadOnlyList<Clip> Search(string? query, MusicalGenre? genre, ClipLanguage? language, bool? burkinabeOnly);
}
