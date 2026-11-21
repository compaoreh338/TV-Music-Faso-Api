namespace TVMusicFaso.Core.Domain;

public enum ClipValidationStatus
{
    Pending = 0,
    Validated = 1,
    Rejected = 2
}

/// <summary>
/// Fiche clip de la médiathèque TV-Music Faso.
/// </summary>
public sealed class Clip
{
    public const decimal HitThreshold = 4.5m;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public decimal Year { get; set; } = DateTime.Today.Year;

    public string OriginPlace { get; set; } = "Burkina Faso";

    public string FilmingLocation { get; set; } = "Ouagadougou";

    public bool IsBurkinabe { get; set; } = true;

    public VideoQuality Quality { get; set; } = VideoQuality.Hd;

    public string Format { get; set; } = "MP4";

    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(3);

    public MusicalGenre Genre { get; set; } = MusicalGenre.AfroPop;

    public ClipLanguage Language { get; set; } = ClipLanguage.Moore;

    public string LanguageName { get; set; } = string.Empty;

    public string LanguageLabel => string.IsNullOrWhiteSpace(LanguageName)
        ? string.Empty
        : LanguageName;

    public ClipTheme Theme { get; set; } = ClipTheme.Amour;

    public Audience Audience { get; set; } = Audience.Famille;

    /// <summary>Note du comité de visionnage, de 1 à 5.</summary>
    public decimal CommitteeRating { get; set; } = 3;

    /// <summary>Popularité constatée (antenne / public), de 1 à 5.</summary>
    public decimal PopularityScore { get; set; } = 3;

    /// <summary>Performance sur les réseaux sociaux, de 1 à 5.</summary>
    public decimal SocialScore { get; set; } = 3;

    /// <summary>Moyenne des trois notes (informatif). Le statut Hit dépend uniquement de <see cref="IsPremium"/>.</summary>
    public decimal ImpactScore { get; set; } = 3;

    /// <summary>Case Premium / Hit : seule source du statut Hit pour la programmation.</summary>
    public bool IsPremium { get; set; }

    public bool IsMorallyCompliant { get; set; } = true;

    public string FilePath { get; set; } = string.Empty;

    public string ThumbnailPath { get; set; } = string.Empty;

    public int LifetimePlayCount { get; set; }

    public int BroadcastFailureCount { get; set; }

    public string LastBroadcastFailureNote { get; set; } = string.Empty;

    public ClipValidationStatus ValidationStatus { get; set; } = ClipValidationStatus.Pending;

    public string ValidationNote { get; set; } = string.Empty;

    /// <summary>Maison de disques ou producteur, repris dans le bandeau de signature.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Version signée (16:9) : dans « Signés » le temps de la vérification, puis le fichier du clip lui-même.</summary>
    public string SignedFilePath { get; set; } = string.Empty;

    public ClipSignatureStatus SignatureStatus { get; set; } = ClipSignatureStatus.None;

    /// <summary>Modèle, révision et textes utilisés pour la version signée : sert à repérer les versions périmées.</summary>
    public string SignatureKey { get; set; } = string.Empty;

    public DateTime? SignedAt { get; set; }

    /// <summary>Grille cochée, déclaration BBDA, retrait et fiche de suivi (enregistrés à part des métadonnées).</summary>
    public ClipReview Review { get; set; } = new();

    /// <summary>Retiré de l'antenne à la demande d'un tiers : jamais programmé tant qu'il n'est pas remis.</summary>
    public bool IsWithdrawn => Review.IsWithdrawn;

    public bool HasSignedVersion => SignatureStatus != ClipSignatureStatus.None
        && !string.IsNullOrWhiteSpace(SignedFilePath);

    /// <summary>Fichier envoyé à l'antenne : la version signée vérifiée, sinon l'original.</summary>
    public string BroadcastFilePath => SignatureStatus == ClipSignatureStatus.Approved
        && !string.IsNullOrWhiteSpace(SignedFilePath)
            ? SignedFilePath
            : FilePath;

    public string SignatureLabel => SignatureStatus.ToDisplayName();

    public bool IsValidated => ValidationStatus == ClipValidationStatus.Validated;

    public bool IsPendingValidation => ValidationStatus == ClipValidationStatus.Pending;

    /// <summary>Clip venu de l'écran « Arrivées » (sources suivies ou dépôt de fichier).</summary>
    public bool IsFromArrivals => Review.History.Any(entry => entry.Kind == ClipEventKind.Arrived);

    /// <summary>
    /// Clip arrivé « à valider » pas encore signé : il passe d'abord par l'écran Signature,
    /// puis par l'écran Validation, où la validation du clip valide aussi sa signature.
    /// </summary>
    public bool IsAwaitingSignature => IsPendingValidation && IsFromArrivals && !IsWithdrawn && !HasSignedVersion;

    /// <summary>Version signée rendue mais pas encore vérifiée : la valider (ou valider le clip) la met à l'antenne.</summary>
    public bool HasSignatureToReview => SignatureStatus == ClipSignatureStatus.ToReview && HasSignedVersion;

    /// <summary>Version signée vérifiée et validée : seule condition de signature pour passer à l'antenne.</summary>
    public bool IsSigned => SignatureStatus == ClipSignatureStatus.Approved
        && !string.IsNullOrWhiteSpace(SignedFilePath);

    /// <summary>
    /// Marque interne d'un clip déjà à l'antenne avant le suivi de signature.
    /// Le fichier en place reste la version diffusée : aucun nouveau rendu n'est demandé.
    /// </summary>
    public const string LegacyOnAirSignatureKey = "antenne-deja-signee";

    /// <summary>
    /// Clip validé de la médiathèque, sans fiche de signature : son fichier actuel est déjà celui de l'antenne.
    /// Ne concerne ni une arrivée, ni un clip dont la signature a été posée ou retirée.
    /// </summary>
    public void RecognizeExistingOnAirFile()
    {
        if (!IsValidated
            || IsFromArrivals
            || SignatureStatus != ClipSignatureStatus.None
            || !string.IsNullOrWhiteSpace(SignedFilePath)
            || string.IsNullOrWhiteSpace(FilePath)
            || Review.History.Any(entry => entry.Kind is ClipEventKind.Signed or ClipEventKind.SignatureWithdrawn))
        {
            return;
        }

        SignatureStatus = ClipSignatureStatus.Approved;
        SignedFilePath = FilePath;
        SignatureKey = LegacyOnAirSignatureKey;
    }

    /// <summary>Programmable : non retiré, conforme, validé et signé (la validation de la signature valide aussi le clip).</summary>
    public bool IsReadyForProgramming => !IsWithdrawn && IsMorallyCompliant && IsValidated && IsSigned;

    /// <summary>Raison pour laquelle le clip n'entre pas dans les grilles, ou vide s'il est programmable.</summary>
    public string ProgrammingBlocker => IsWithdrawn
        ? "Retiré"
        : !IsMorallyCompliant
        ? "Non conforme"
        : ValidationStatus == ClipValidationStatus.Rejected
            ? "Refusé"
            : !IsSigned
                ? "À signer"
                : !IsValidated
                    ? "À valider"
                    : string.Empty;

    public string ProgrammingLabel => ProgrammingBlocker.Length == 0
        ? "Programmable"
        : $"Non programmable : {ProgrammingBlocker.ToLowerInvariant()}";

    public string ValidationLabel => IsWithdrawn
        ? "Retiré de l'antenne"
        : IsAwaitingSignature
            ? "À signer avant validation"
            : ValidationStatus.ToDisplayName();

    public decimal DurationSeconds
    {
        get => (decimal)Duration.TotalSeconds;
        set => Duration = TimeSpan.FromSeconds((double)Math.Max(1, value));
    }

    public string DurationLabel => Duration.ToString(@"mm\:ss");

    public string OriginLabel => IsBurkinabe ? "Burkinabè" : "Étranger";

    /// <summary>Hit uniquement si la case Premium / Hit est cochée (le score ne force plus le statut).</summary>
    public bool IsHit => IsPremium;

    public string ImpactLabel => IsHit ? $"Hit {ImpactScore:0.0}" : $"Score {ImpactScore:0.0}";

    public string FailureLabel => BroadcastFailureCount == 0
        ? "Aucun échec de diffusion"
        : $"{BroadcastFailureCount} échec(s) — {LastBroadcastFailureNote}";

    public void ApplyLanguage(SpokenLanguage language)
    {
        ArgumentNullException.ThrowIfNull(language);
        LanguageName = language.Label;
        Language = language.EnumValue;
    }

    public void RecalculateImpact()
    {
        ImpactScore = Math.Round((CommitteeRating + PopularityScore + SocialScore) / 3m, 1);
    }

    public Clip SubmitForValidation()
    {
        ValidationStatus = ClipValidationStatus.Pending;
        ValidationNote = string.Empty;
        return this;
    }

    public Clip Approve()
    {
        ValidationStatus = ClipValidationStatus.Validated;
        ValidationNote = string.Empty;
        return this;
    }

    public Clip Reject(string note)
    {
        ValidationStatus = ClipValidationStatus.Rejected;
        ValidationNote = string.IsNullOrWhiteSpace(note)
            ? "Refusé par la Direction."
            : note.Trim();
        return this;
    }

    public void ReportBroadcastFailure(string note)
    {
        BroadcastFailureCount++;
        LastBroadcastFailureNote = string.IsNullOrWhiteSpace(note)
            ? $"Échec signalé le {DateTime.Now:dd/MM/yyyy HH:mm}"
            : note.Trim();
    }

    /// <summary>Enregistre une nouvelle version signée, à prévisualiser avant diffusion.</summary>
    public void AttachSignature(string signedFilePath, string signatureKey, DateTime renderedAt, bool keepApproval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signedFilePath);
        SignedFilePath = signedFilePath;
        SignatureKey = signatureKey ?? string.Empty;
        SignedAt = renderedAt;
        SignatureStatus = keepApproval && SignatureStatus == ClipSignatureStatus.Approved
            ? ClipSignatureStatus.Approved
            : ClipSignatureStatus.ToReview;
    }

    /// <summary>Valide la version signée ; un clip encore « à valider » devient validé du même coup.</summary>
    public void ApproveSignature()
    {
        if (string.IsNullOrWhiteSpace(SignedFilePath))
        {
            throw new InvalidOperationException("Aucune version signée à valider pour ce clip.");
        }

        if (ValidationStatus == ClipValidationStatus.Rejected)
        {
            throw new InvalidOperationException("Clip refusé : il doit être corrigé et revalidé avant d'être signé.");
        }

        SignatureStatus = ClipSignatureStatus.Approved;
        if (ValidationStatus == ClipValidationStatus.Pending)
        {
            Approve();
        }
    }

    /// <summary>Retire la signature : l'antenne revient au fichier d'origine.</summary>
    public void WithdrawSignature()
    {
        SignatureStatus = ClipSignatureStatus.None;
        SignedFilePath = string.Empty;
        SignatureKey = string.Empty;
        SignedAt = null;
    }

    public Clip Clone()
    {
        var copy = new Clip { Id = Id };
        copy.RestoreFrom(this);
        return copy;
    }

    public void RestoreFrom(Clip source)
    {
        Title = source.Title;
        Artist = source.Artist;
        Year = source.Year;
        OriginPlace = source.OriginPlace;
        FilmingLocation = source.FilmingLocation;
        IsBurkinabe = source.IsBurkinabe;
        Quality = source.Quality;
        Format = source.Format;
        Duration = source.Duration;
        Genre = source.Genre;
        Language = source.Language;
        LanguageName = source.LanguageName;
        Theme = source.Theme;
        Audience = source.Audience;
        CommitteeRating = source.CommitteeRating;
        PopularityScore = source.PopularityScore;
        SocialScore = source.SocialScore;
        ImpactScore = source.ImpactScore;
        IsPremium = source.IsPremium;
        IsMorallyCompliant = source.IsMorallyCompliant;
        FilePath = source.FilePath;
        ThumbnailPath = source.ThumbnailPath;
        LifetimePlayCount = source.LifetimePlayCount;
        BroadcastFailureCount = source.BroadcastFailureCount;
        LastBroadcastFailureNote = source.LastBroadcastFailureNote;
        ValidationStatus = source.ValidationStatus;
        ValidationNote = source.ValidationNote;
        Label = source.Label;
        SignedFilePath = source.SignedFilePath;
        SignatureStatus = source.SignatureStatus;
        SignatureKey = source.SignatureKey;
        SignedAt = source.SignedAt;
        Review = source.Review.Clone();
    }
}
