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
        ? Language.ToDisplayName()
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

    public bool IsValidated => ValidationStatus == ClipValidationStatus.Validated;

    public bool IsPendingValidation => ValidationStatus == ClipValidationStatus.Pending;

    public bool IsReadyForProgramming => IsMorallyCompliant && IsValidated;

    public string ValidationLabel => ValidationStatus.ToDisplayName();

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
    }
}
