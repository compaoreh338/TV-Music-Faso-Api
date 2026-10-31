using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>
/// Registre des genres connus du poste : noms affichés et liste proposée dans les écrans.
/// Mis à jour au chargement du catalogue et après chaque création, renommage ou suppression.
/// </summary>
public static class GenreCatalog
{
    public const int MinLabelLength = 2;

    public const int MaxLabelLength = 60;

    private static Snapshot _current = new(Seeds());

    /// <summary>Genres proposés (non supprimés), triés par nom.</summary>
    public static IReadOnlyList<MusicalGenre> All => _current.Active;

    /// <summary>Définitions connues, supprimées comprises.</summary>
    public static IReadOnlyList<GenreDefinition> Definitions => _current.Definitions;

    /// <summary>Les 18 genres livrés avec l'app, avec leurs noms d'origine.</summary>
    public static IReadOnlyList<GenreDefinition> Seeds() =>
        Enum.GetValues<MusicalGenre>()
            .Select(genre => new GenreDefinition
            {
                Id = (int)genre,
                Label = EnumDisplay.OfferedName(genre),
                IsSeeded = true
            })
            .ToList();

    /// <summary>Remplace le registre ; une liste vide garde les genres d'origine.</summary>
    public static void Use(IEnumerable<GenreDefinition>? definitions)
    {
        var list = definitions?.Where(item => !string.IsNullOrWhiteSpace(item.Label)).Select(item => item.Clone()).ToList() ?? [];
        Interlocked.Exchange(ref _current, new Snapshot(list.Count == 0 ? Seeds() : list));
    }

    /// <summary>Nom du genre dans le registre (y compris supprimé), sinon faux.</summary>
    public static bool TryGetLabel(MusicalGenre genre, out string label)
    {
        if (_current.Labels.TryGetValue((int)genre, out var found))
        {
            label = found;
            return true;
        }

        label = string.Empty;
        return false;
    }

    /// <summary>Nom nettoyé, ou exception si vide, trop court ou trop long.</summary>
    public static string ValidateLabel(string? label)
    {
        var trimmed = string.Join(' ', (label ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (trimmed.Length < MinLabelLength)
        {
            throw new ArgumentException($"Le nom du genre doit contenir au moins {MinLabelLength} caractères.");
        }

        if (trimmed.Length > MaxLabelLength)
        {
            throw new ArgumentException($"Le nom du genre ne doit pas dépasser {MaxLabelLength} caractères.");
        }

        return trimmed;
    }

    /// <summary>Genre actif portant déjà ce nom (sans tenir compte des accents ni de la casse).</summary>
    public static GenreDefinition? FindActiveByLabel(IEnumerable<GenreDefinition> definitions, string label, int? exceptId = null)
    {
        var key = LanguageCatalog.Normalize(label);
        return definitions.FirstOrDefault(item =>
            !item.IsArchived && item.Id != exceptId && LanguageCatalog.Normalize(item.Label) == key);
    }

    /// <summary>
    /// Premier numéro des genres créés dans l'app : laisse la place aux genres qu'une future version
    /// ajouterait à <see cref="MusicalGenre"/>, sans collision avec les clips existants.
    /// </summary>
    public const int FirstCustomId = 1000;

    /// <summary>Numéro libre pour un nouveau genre : à partir de <see cref="FirstCustomId"/>, après tous les genres déjà créés.</summary>
    public static int NextId(IEnumerable<GenreDefinition> definitions) =>
        Math.Max(FirstCustomId - 1, definitions.Select(item => item.Id).DefaultIfEmpty(0).Max()) + 1;

    /// <summary>Vrai si le genre est proposé (ni supprimé, ni inconnu du catalogue).</summary>
    public static bool IsActive(MusicalGenre genre) => _current.ActiveSet.Contains(genre);

    /// <summary>
    /// Retire les genres supprimés d'une liste de tranche. Si tous l'étaient, la liste est gardée telle quelle :
    /// une liste vide voudrait dire « tous genres ».
    /// </summary>
    public static List<MusicalGenre> KeepActive(IEnumerable<MusicalGenre> genres)
    {
        var source = genres.ToList();
        var active = source.Where(IsActive).ToList();
        return active.Count == 0 ? source : active;
    }

    /// <summary>
    /// Vérifie une suppression : le genre existe, il en reste au moins un autre, et le genre de
    /// remplacement (obligatoire si des clips l'utilisent) est un autre genre actif.
    /// </summary>
    public static GenreDefinition CheckDeletion(IReadOnlyList<GenreDefinition> definitions, int id, int? replacementId, int usedByClips)
    {
        var target = definitions.FirstOrDefault(item => item.Id == id && !item.IsArchived)
            ?? throw new InvalidOperationException("Genre introuvable ou déjà supprimé.");
        if (definitions.Count(item => !item.IsArchived) <= 1)
        {
            throw new InvalidOperationException("Impossible de supprimer le dernier genre.");
        }

        if (replacementId is { } replacement
            && (replacement == id || definitions.All(item => item.Id != replacement || item.IsArchived)))
        {
            throw new InvalidOperationException("Choisissez un autre genre existant pour le remplacement.");
        }

        if (usedByClips > 0 && replacementId is null)
        {
            throw new InvalidOperationException(
                $"{usedByClips} clip(s) utilisent « {target.Label} » : choisissez le genre qui le remplacera.");
        }

        return target;
    }

    private sealed class Snapshot
    {
        public Snapshot(IReadOnlyList<GenreDefinition> definitions)
        {
            Definitions = definitions;
            Labels = definitions.GroupBy(item => item.Id).ToDictionary(group => group.Key, group => group.Last().Label);
            Active = definitions
                .Where(item => !item.IsArchived)
                .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
                .Select(item => item.Genre)
                .Distinct()
                .ToList();
            ActiveSet = Active.ToHashSet();
        }

        public IReadOnlyList<GenreDefinition> Definitions { get; }

        public IReadOnlySet<MusicalGenre> ActiveSet { get; }

        public IReadOnlyDictionary<int, string> Labels { get; }

        public IReadOnlyList<MusicalGenre> Active { get; }
    }
}
