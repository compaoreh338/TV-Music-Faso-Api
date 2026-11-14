using System.Text.Json;
using System.Text.Json.Serialization;

namespace TVMusicFaso.Core.Domain;

/// <summary>Famille d'un critère de la grille de conformité (M6).</summary>
public enum ComplianceCategory
{
    /// <summary>Morale, décence, respect des personnes et des valeurs.</summary>
    Morale = 0,

    /// <summary>Ligne éditoriale de la chaîne (qualité, image, cohérence d'antenne).</summary>
    LigneEditoriale = 1
}

/// <summary>État de la déclaration du clip au BBDA (droits d'auteur), M7.</summary>
public enum BbdaDeclarationStatus
{
    NotDeclared = 0,
    InProgress = 1,
    Declared = 2,
    Exempt = 3
}

/// <summary>Nature d'un événement de la fiche de suivi (M9).</summary>
public enum ClipEventKind
{
    Created = 0,
    Arrived = 1,
    Validated = 2,
    Rejected = 3,
    Signed = 4,
    SignatureWithdrawn = 5,
    Withdrawn = 6,
    Reinstated = 7,
    Compliance = 8,
    Bbda = 9,
    Note = 10,
    Resubmitted = 11
}

/// <summary>Critère de la grille de conformité, modifiable par la Direction ou le programmateur.</summary>
public sealed class ComplianceCriterion
{
    public const int MaxLabelLength = 160;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Label { get; set; } = string.Empty;

    public ComplianceCategory Category { get; set; } = ComplianceCategory.Morale;

    /// <summary>Un critère désactivé n'apparaît plus dans les nouvelles évaluations ; les anciennes le gardent.</summary>
    public bool IsActive { get; set; } = true;

    public ComplianceCriterion Clone() => new() { Id = Id, Label = Label, Category = Category, IsActive = IsActive };
}

/// <summary>Grille de critères partagée par tous les postes.</summary>
public sealed class ComplianceGrid
{
    public const int MaxCriteria = 40;

    public int Revision { get; set; } = 1;

    public DateTimeOffset? UpdatedAt { get; set; }

    public string UpdatedBy { get; set; } = string.Empty;

    public List<ComplianceCriterion> Criteria { get; set; } = [];

    public IReadOnlyList<ComplianceCriterion> ActiveCriteria => Criteria.Where(criterion => criterion.IsActive).ToList();

    /// <summary>
    /// Grille de départ, à adapter par la chaîne. Identifiants fixes : tant qu'aucune grille n'est
    /// enregistrée, tous les postes et toutes les lectures désignent les mêmes critères.
    /// </summary>
    public static ComplianceGrid CreateDefault() => new()
    {
        Criteria =
        [
            Criterion("0c5f6a10-6d1e-4c7a-9a01-000000000001", "Aucune scène ou parole obscène, vulgaire ou à caractère sexuel explicite", ComplianceCategory.Morale),
            Criterion("0c5f6a10-6d1e-4c7a-9a01-000000000002", "Aucune incitation à la violence, à la haine ou à la discrimination", ComplianceCategory.Morale),
            Criterion("0c5f6a10-6d1e-4c7a-9a01-000000000003", "Respect des personnes, des religions et des communautés", ComplianceCategory.Morale),
            Criterion("0c5f6a10-6d1e-4c7a-9a01-000000000004", "Pas d'apologie de l'alcool, du tabac ou de la drogue", ComplianceCategory.Morale),
            Criterion("0c5f6a10-6d1e-4c7a-9a01-000000000005", "Respect des institutions, du drapeau et des symboles nationaux", ComplianceCategory.Morale),
            Criterion("0c5f6a10-6d1e-4c7a-9a01-000000000006", "Qualité d'image et de son suffisante pour l'antenne", ComplianceCategory.LigneEditoriale),
            Criterion("0c5f6a10-6d1e-4c7a-9a01-000000000007", "Pas de marque, logo ou publicité visible non autorisée", ComplianceCategory.LigneEditoriale),
            Criterion("0c5f6a10-6d1e-4c7a-9a01-000000000008", "Cohérent avec la ligne éditoriale et l'image de la chaîne", ComplianceCategory.LigneEditoriale)
        ]
    };

    /// <summary>Erreurs bloquantes, vide si la grille est utilisable.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (ActiveCriteria.Count == 0)
        {
            errors.Add("La grille doit garder au moins un critère actif.");
        }

        if (Criteria.Count > MaxCriteria)
        {
            errors.Add($"{MaxCriteria} critères au plus.");
        }

        if (Criteria.Any(criterion => string.IsNullOrWhiteSpace(criterion.Label)))
        {
            errors.Add("Chaque critère doit avoir un intitulé.");
        }

        if (Criteria.Any(criterion => (criterion.Label?.Length ?? 0) > ComplianceCriterion.MaxLabelLength))
        {
            errors.Add($"Intitulé de {ComplianceCriterion.MaxLabelLength} caractères au plus.");
        }

        if (Criteria.Any(criterion => !Enum.IsDefined(criterion.Category)))
        {
            errors.Add("Famille de critère inconnue.");
        }

        if (Criteria.Select(criterion => criterion.Id).Distinct().Count() != Criteria.Count)
        {
            errors.Add("Deux critères portent le même identifiant.");
        }

        return errors;
    }

    public ComplianceGrid Clone() => new()
    {
        Revision = Revision,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy,
        Criteria = Criteria.Select(criterion => criterion.Clone()).ToList()
    };

    private static ComplianceCriterion Criterion(string id, string label, ComplianceCategory category) =>
        new() { Id = Guid.Parse(id), Label = label, Category = category };
}

/// <summary>Réponse du valideur à un critère, avec l'intitulé du moment (la grille peut changer ensuite).</summary>
public sealed class ComplianceCheck
{
    public Guid CriterionId { get; set; }

    public string Label { get; set; } = string.Empty;

    public ComplianceCategory Category { get; set; }

    public bool IsMet { get; set; }
}

/// <summary>Retrait d'antenne d'un clip (M9) : qui l'a demandé, pourquoi, qui l'a saisi.</summary>
public sealed class ClipWithdrawal
{
    public const int MaxTextLength = 300;

    public DateTimeOffset At { get; set; }

    /// <summary>Demandeur : artiste, label, BBDA, CSC, Direction…</summary>
    public string RequestedBy { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>Compte qui a saisi le retrait.</summary>
    public string RecordedBy { get; set; } = string.Empty;
}

/// <summary>Événement de la fiche de suivi d'un clip.</summary>
public sealed class ClipEvent
{
    public const int MaxDetailsLength = 600;

    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    public ClipEventKind Kind { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    [JsonIgnore]
    public string KindLabel => Kind.ToDisplayName();

    [JsonIgnore]
    public string AtLabel => At.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

/// <summary>
/// Revue du clip : grille cochée et commentaire du valideur (M6), déclaration BBDA (M7),
/// retrait d'antenne et fiche de suivi (M9). Enregistrée à part de la fiche descriptive :
/// une modification des métadonnées ne l'écrase jamais.
/// </summary>
public sealed class ClipReview
{
    public const int MaxCommentLength = 1000;

    public const int MaxHistory = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public List<ComplianceCheck> Checks { get; set; } = [];

    public string ComplianceComment { get; set; } = string.Empty;

    public string ReviewedBy { get; set; } = string.Empty;

    public DateTimeOffset? ReviewedAt { get; set; }

    public BbdaDeclarationStatus BbdaStatus { get; set; } = BbdaDeclarationStatus.NotDeclared;

    /// <summary>Numéro ou référence de la déclaration au BBDA.</summary>
    public string BbdaReference { get; set; } = string.Empty;

    /// <summary>Pièce jointe (récépissé, formulaire) rangée sur le disque partagé.</summary>
    public string BbdaAttachmentPath { get; set; } = string.Empty;

    public ClipWithdrawal? Withdrawal { get; set; }

    public List<ClipEvent> History { get; set; } = [];

    [JsonIgnore]
    public bool IsWithdrawn => Withdrawal is not null;

    [JsonIgnore]
    public bool HasCompliance => Checks.Count > 0;

    /// <summary>Tous les critères cochés lors de la dernière évaluation.</summary>
    [JsonIgnore]
    public bool AllCriteriaMet => Checks.Count > 0 && Checks.All(check => check.IsMet);

    [JsonIgnore]
    public bool MoralCriteriaMet => Checks.Where(check => check.Category == ComplianceCategory.Morale).All(check => check.IsMet);

    [JsonIgnore]
    public string ComplianceSummary => Checks.Count == 0
        ? "Grille non remplie"
        : $"{Checks.Count(check => check.IsMet)}/{Checks.Count} critère(s) respecté(s)";

    [JsonIgnore]
    public string BbdaLabel => BbdaStatus.ToDisplayName();

    /// <summary>Ajoute un événement à la fiche de suivi (les plus anciens partent au-delà de la limite).</summary>
    public ClipEvent Record(ClipEventKind kind, string actor, string details, DateTimeOffset? at = null)
    {
        var entry = new ClipEvent
        {
            Kind = kind,
            Actor = Trim(actor, 120),
            Details = Trim(details, ClipEvent.MaxDetailsLength),
            At = (at ?? DateTimeOffset.UtcNow).ToUniversalTime()
        };
        History.Add(entry);
        if (History.Count > MaxHistory)
        {
            History.RemoveRange(0, History.Count - MaxHistory);
        }

        return entry;
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Revue lue en base ; une valeur vide ou illisible donne une revue vierge.</summary>
    public static ClipReview FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ClipReview();
        }

        try
        {
            return JsonSerializer.Deserialize<ClipReview>(json, JsonOptions) ?? new ClipReview();
        }
        catch (JsonException)
        {
            return new ClipReview();
        }
    }

    public ClipReview Clone() => FromJson(ToJson());

    internal static string Trim(string? value, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}

/// <summary>
/// Règles d'écriture d'une revue envoyée par un poste (route PUT /api/clips/{id}/review) :
/// grille, commentaire et retrait réservés aux valideurs (ignorés pour les autres comptes, dont la copie
/// peut être ancienne) ; BBDA pour valideurs et contributeurs ;
/// fiche de suivi en ajout seul, au nom du compte connecté. Les dates de décision sont celles du serveur.
/// </summary>
public static class ClipReviewPolicy
{
    /// <summary>Événements ajoutés au plus par requête.</summary>
    public const int MaxNewEventsPerRequest = 20;

    public const int MaxReferenceLength = 120;

    public const int MaxAttachmentPathLength = 400;

    /// <summary>Fusionne la revue reçue dans la revue enregistrée et renvoie la revue à enregistrer.</summary>
    /// <exception cref="ArgumentException">Valeur invalide (demandeur ou motif de retrait absent, statut inconnu…).</exception>
    public static ClipReview Merge(ClipReview current, ClipReview incoming, bool canValidate, string actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        var merged = current.Clone();
        incoming.Checks ??= [];
        incoming.History ??= [];

        var checks = incoming.Checks
            .Take(ComplianceGrid.MaxCriteria)
            .Select(check => new ComplianceCheck
            {
                CriterionId = check.CriterionId,
                Label = ClipReview.Trim(check.Label, ComplianceCriterion.MaxLabelLength),
                Category = Enum.IsDefined(check.Category) ? check.Category : ComplianceCategory.Morale,
                IsMet = check.IsMet
            })
            .ToList();
        var comment = ClipReview.Trim(incoming.ComplianceComment, ClipReview.MaxCommentLength);
        if (canValidate && (!SameChecks(current.Checks, checks) || comment != current.ComplianceComment))
        {
            merged.Checks = checks;
            merged.ComplianceComment = comment;
            merged.ReviewedBy = actor;
            merged.ReviewedAt = now;
        }

        if (canValidate && incoming.Withdrawal is null != current.Withdrawal is null)
        {
            if (incoming.Withdrawal is { } withdrawal)
            {
                var requester = ClipReview.Trim(withdrawal.RequestedBy, ClipWithdrawal.MaxTextLength);
                var reason = ClipReview.Trim(withdrawal.Reason, ClipWithdrawal.MaxTextLength);
                if (requester.Length == 0 || reason.Length == 0)
                {
                    throw new ArgumentException("Indiquez qui demande le retrait et pourquoi.");
                }

                merged.Withdrawal = new ClipWithdrawal { At = now, RequestedBy = requester, Reason = reason, RecordedBy = actor };
            }
            else
            {
                merged.Withdrawal = null;
            }
        }

        if (!Enum.IsDefined(incoming.BbdaStatus))
        {
            throw new ArgumentException("Statut BBDA inconnu.");
        }

        merged.BbdaStatus = incoming.BbdaStatus;
        merged.BbdaReference = ClipReview.Trim(incoming.BbdaReference, MaxReferenceLength);
        merged.BbdaAttachmentPath = ClipReview.Trim(incoming.BbdaAttachmentPath, MaxAttachmentPathLength);

        var known = current.History.Select(entry => entry.Id).ToHashSet();
        foreach (var entry in incoming.History
                     .Where(entry => entry.Id != Guid.Empty && !known.Contains(entry.Id) && Enum.IsDefined(entry.Kind))
                     .Take(MaxNewEventsPerRequest))
        {
            var at = entry.At > now.AddMinutes(5) || entry.At < now.AddYears(-50) ? now : entry.At;
            var added = merged.Record(entry.Kind, actor, entry.Details, at);
            added.Id = entry.Id;
        }

        return merged;
    }

    private static bool SameChecks(IReadOnlyList<ComplianceCheck> left, IReadOnlyList<ComplianceCheck> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => pair.First.CriterionId == pair.Second.CriterionId
                                       && pair.First.IsMet == pair.Second.IsMet
                                       && pair.First.Label == pair.Second.Label
                                       && pair.First.Category == pair.Second.Category);
}
