using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Remarque sur une ligne du fichier importé (ligne rejetée ou valeur remplacée par défaut).</summary>
/// <param name="Line">Numéro de ligne dans le fichier (1 = en-tête).</param>
/// <param name="Message">Explication lisible par l'utilisateur.</param>
public sealed record ClipImportIssue(int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"ligne {Line} : {Message}" : Message;
}

/// <summary>Résultat de la lecture d'un CSV de clips, avant enregistrement.</summary>
public sealed class ClipCsvParseResult
{
    /// <summary>Clips prêts à enregistrer (statut « À valider »).</summary>
    public List<Clip> Clips { get; } = [];

    /// <summary>Lignes ignorées : chemin manquant, fichier déjà présent, etc.</summary>
    public List<ClipImportIssue> Rejected { get; } = [];

    /// <summary>Valeurs non reconnues, remplacées par la valeur par défaut.</summary>
    public List<ClipImportIssue> Warnings { get; } = [];

    /// <summary>Lignes ignorées car le fichier vidéo est déjà dans la médiathèque ou répété dans le CSV.</summary>
    public int Duplicates { get; set; }

    /// <summary>Erreur bloquante (fichier vide, colonne du chemin introuvable…).</summary>
    public string? FatalError { get; set; }
}

/// <summary>Bilan d'un import de clips.</summary>
public sealed class ClipImportReport
{
    public int Imported { get; init; }

    public int Duplicates { get; init; }

    public IReadOnlyList<ClipImportIssue> Rejected { get; init; } = [];

    public IReadOnlyList<ClipImportIssue> Warnings { get; init; } = [];

    public string? FatalError { get; init; }

    public static ClipImportReport From(ClipCsvParseResult parsed, int imported) => new()
    {
        Imported = imported,
        Duplicates = parsed.Duplicates,
        Rejected = parsed.Rejected,
        Warnings = parsed.Warnings,
        FatalError = parsed.FatalError
    };
}

/// <summary>
/// Dépôt capable d'enregistrer un lot de clips en une fois
/// (serveur : quelques requêtes au lieu d'une par clip ; cache : une seule écriture).
/// </summary>
public interface IBulkClipImport
{
    /// <summary>Enregistre les clips et renvoie le nombre réellement ajouté.</summary>
    int AddRange(IReadOnlyList<Clip> clips);
}
