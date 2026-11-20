using System.Text.Json.Serialization;

namespace TVMusicFaso.Core.Licensing;

/// <summary>
/// Contenu signé d'une licence à vie : un client, la machine serveur autorisée et un nombre de postes.
/// Les noms JSON font partie du format signé : ne pas les changer sans changer <see cref="LicenceCodec.Prefix"/>.
/// </summary>
public sealed record LicenceDocument
{
    /// <summary>Nom du produit attendu dans toute licence.</summary>
    public const string ProductName = "TV-Music Faso";

    /// <summary>Produit couvert ; une licence d'un autre produit est refusée.</summary>
    [JsonPropertyName("produit")]
    public string Product { get; init; } = ProductName;

    /// <summary>Numéro de licence (référence du fournisseur).</summary>
    [JsonPropertyName("numero")]
    public string Number { get; init; } = string.Empty;

    /// <summary>Nom du client titulaire.</summary>
    [JsonPropertyName("client")]
    public string Customer { get; init; } = string.Empty;

    /// <summary>Empreinte de la machine serveur autorisée (voir <see cref="MachineFingerprint"/>).</summary>
    [JsonPropertyName("empreinte")]
    public string Fingerprint { get; init; } = string.Empty;

    /// <summary>Nombre de postes de travail autorisés ; 0 = illimité.</summary>
    [JsonPropertyName("postes")]
    public int Seats { get; init; }

    /// <summary>Date d'émission.</summary>
    [JsonPropertyName("emise")]
    public DateOnly IssuedOn { get; init; }

    /// <summary>Nombre de postes en clair.</summary>
    [JsonIgnore]
    public string SeatsLabel => Seats <= 0 ? "postes illimités" : Seats == 1 ? "1 poste" : $"{Seats} postes";

    /// <summary>Résumé affichable : « Licence à vie · client · n° · postes ».</summary>
    [JsonIgnore]
    public string Summary => $"Licence à vie · {Customer} · n° {Number} · {SeatsLabel}";

    /// <summary>Erreurs bloquantes ; vide si le contenu est cohérent.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (!string.Equals(Product, ProductName, StringComparison.Ordinal))
        {
            errors.Add($"Produit inattendu : « {Product} ».");
        }

        if (string.IsNullOrWhiteSpace(Number))
        {
            errors.Add("Numéro de licence manquant.");
        }

        if (string.IsNullOrWhiteSpace(Customer))
        {
            errors.Add("Nom du client manquant.");
        }

        if (!MachineFingerprint.IsWellFormed(Fingerprint))
        {
            errors.Add("Empreinte de machine invalide : 16 caractères hexadécimaux attendus (ex. A1B2-C3D4-E5F6-0718).");
        }

        if (Seats < 0)
        {
            errors.Add("Le nombre de postes ne peut pas être négatif (0 = illimité).");
        }

        return errors;
    }
}
