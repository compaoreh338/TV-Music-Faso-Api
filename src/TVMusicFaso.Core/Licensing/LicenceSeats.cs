namespace TVMusicFaso.Core.Licensing;

/// <summary>Poste de travail enregistré auprès du serveur.</summary>
/// <param name="Fingerprint">Empreinte du poste (voir <see cref="MachineFingerprint"/>).</param>
/// <param name="Name">Nom de la machine, pour la reconnaître dans la liste.</param>
/// <param name="FirstSeen">Première connexion.</param>
/// <param name="LastSeen">Dernière connexion connue (mise à jour au plus une fois par heure).</param>
public sealed record LicenceSeat(string Fingerprint, string Name, DateTimeOffset FirstSeen, DateTimeOffset LastSeen);

/// <summary>En-têtes HTTP par lesquels l'app bureau s'identifie auprès du serveur.</summary>
public static class LicenceHeaders
{
    /// <summary>Empreinte du poste.</summary>
    public const string Seat = "X-TVMF-Poste";

    /// <summary>Nom de la machine, encodé comme une portion d'URL (les en-têtes n'acceptent que l'ASCII).</summary>
    public const string SeatName = "X-TVMF-Poste-Nom";
}

/// <summary>Stockage des postes enregistrés (base du serveur).</summary>
public interface ILicenceSeatStore
{
    /// <summary>Tous les postes enregistrés.</summary>
    IReadOnlyList<LicenceSeat> Load();

    /// <summary>Ajoute le poste ou met à jour son nom et sa dernière connexion.</summary>
    void Save(LicenceSeat seat);

    /// <summary>Retire un poste ; sans effet s'il est inconnu.</summary>
    void Remove(string fingerprint);
}

/// <summary>Réponse à un poste qui se présente.</summary>
/// <param name="Allowed">Vrai si le poste peut travailler.</param>
/// <param name="Message">Raison du refus ; vide si le poste est admis.</param>
public sealed record SeatDecision(bool Allowed, string Message)
{
    /// <summary>Poste admis.</summary>
    public static SeatDecision Admitted { get; } = new(true, string.Empty);
}

/// <summary>
/// Compte les postes de travail d'une licence : un poste connu est toujours admis, un nouveau poste est
/// enregistré tant que le nombre de postes de la licence n'est pas atteint.
/// Une panne du stockage n'empêche jamais de travailler : le poste est admis sans être enregistré.
/// </summary>
public sealed class LicenceSeats
{
    private const int MaxNameLength = 80;

    private static readonly TimeSpan TouchInterval = TimeSpan.FromHours(1);

    private readonly ILicenceSeatStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _lock = new();
    private Dictionary<string, LicenceSeat>? _seats;

    /// <param name="store">Stockage des postes.</param>
    /// <param name="clock">Horloge (tests) ; par défaut l'heure UTC courante.</param>
    public LicenceSeats(ILicenceSeatStore store, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Admet ou refuse un poste. Un poste sans empreinte lisible est admis sans être compté.</summary>
    /// <param name="fingerprint">Empreinte annoncée par le poste.</param>
    /// <param name="name">Nom de la machine.</param>
    /// <param name="maxSeats">Nombre de postes de la licence ; 0 ou moins = illimité.</param>
    public SeatDecision Admit(string? fingerprint, string? name, int maxSeats)
    {
        var key = MachineFingerprint.Normalize(fingerprint);
        if (key.Length == 0)
        {
            return SeatDecision.Admitted;
        }

        var label = CleanName(name);
        lock (_lock)
        {
            try
            {
                var seats = Loaded();
                var now = _clock();
                if (seats.TryGetValue(key, out var known))
                {
                    if (now - known.LastSeen >= TouchInterval || (label.Length > 0 && label != known.Name))
                    {
                        var updated = known with { Name = label.Length > 0 ? label : known.Name, LastSeen = now };
                        _store.Save(updated);
                        seats[key] = updated;
                    }

                    return SeatDecision.Admitted;
                }

                if (maxSeats > 0 && seats.Count >= maxSeats)
                {
                    return new SeatDecision(false,
                        $"Nombre de postes de la licence atteint ({maxSeats}). Ce poste ({key}) n'est pas enregistré : "
                        + "la Direction peut retirer un poste qui ne sert plus, ou demandez une extension de licence au fournisseur.");
                }

                var seat = new LicenceSeat(key, label, now, now);
                _store.Save(seat);
                seats[key] = seat;
                return SeatDecision.Admitted;
            }
            catch (Exception exception) when (IsStoreFailure(exception))
            {
                // Liste relue au prochain appel ; d'ici là, personne n'est bloqué par une panne de base.
                _seats = null;
                return SeatDecision.Admitted;
            }
        }
    }

    /// <summary>Postes enregistrés, du plus récemment vu au plus ancien.</summary>
    public IReadOnlyList<LicenceSeat> List()
    {
        lock (_lock)
        {
            return Loaded().Values.OrderByDescending(seat => seat.LastSeen).ToList();
        }
    }

    /// <summary>Retire un poste (machine remplacée) ; renvoie faux s'il n'était pas enregistré.</summary>
    public bool Remove(string? fingerprint)
    {
        var key = MachineFingerprint.Normalize(fingerprint);
        if (key.Length == 0)
        {
            return false;
        }

        lock (_lock)
        {
            var seats = Loaded();
            if (!seats.ContainsKey(key))
            {
                return false;
            }

            _store.Remove(key);
            seats.Remove(key);
            return true;
        }
    }

    private Dictionary<string, LicenceSeat> Loaded() =>
        _seats ??= _store.Load()
            .Where(seat => MachineFingerprint.IsWellFormed(seat.Fingerprint))
            .GroupBy(seat => MachineFingerprint.Normalize(seat.Fingerprint))
            .ToDictionary(group => group.Key, group => group.First() with { Fingerprint = group.Key }, StringComparer.Ordinal);

    private static string CleanName(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Where(character => !char.IsControl(character)).ToArray()).Trim();
        return cleaned.Length > MaxNameLength ? cleaned[..MaxNameLength] : cleaned;
    }

    private static bool IsStoreFailure(Exception exception) =>
        exception is System.Data.Common.DbException or InvalidOperationException or TimeoutException or IOException;
}
