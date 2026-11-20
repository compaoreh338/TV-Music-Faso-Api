using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TVMusicFaso.Core.Licensing;

/// <summary>Pourquoi une licence n'est pas acceptée ; <see cref="None"/> = licence valide.</summary>
public enum LicenceProblem
{
    None,
    Missing,
    Unreadable,
    BadSignature,
    WrongMachine,
    FingerprintUnavailable,
    NotSaved
}

/// <summary>
/// Format d'une licence : <c>TVMF1.&lt;contenu JSON en base64url&gt;.&lt;signature ECDSA P-256 / SHA-256 en base64url&gt;</c>.
/// La signature couvre le texte « TVMF1.contenu » tel quel : changer un seul caractère du contenu l'invalide.
/// Dans un fichier, les lignes qui commencent par « # » sont des commentaires et les retours à la ligne sont ignorés.
/// </summary>
public static class LicenceCodec
{
    /// <summary>Version du format ; à changer si le contenu signé change de forme.</summary>
    public const string Prefix = "TVMF1";

    /// <summary>Taille maximale acceptée pour un fichier de licence.</summary>
    public const int MaxLength = 16 * 1024;

    private static readonly JsonSerializerOptions Json = new();

    /// <summary>Signe un contenu avec la clé privée du fournisseur et renvoie le jeton de licence.</summary>
    public static string Sign(LicenceDocument document, ECDsa privateKey)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(privateKey);
        var errors = document.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(document));
        }

        var normalized = document with { Fingerprint = MachineFingerprint.Normalize(document.Fingerprint) };
        var signedText = $"{Prefix}.{ToBase64Url(JsonSerializer.SerializeToUtf8Bytes(normalized, Json))}";
        var signature = privateKey.SignData(Encoding.ASCII.GetBytes(signedText), HashAlgorithmName.SHA256);
        return $"{signedText}.{ToBase64Url(signature)}";
    }

    /// <summary>Texte du fichier remis au client : un en-tête lisible, puis le jeton.</summary>
    public static string ToFileText(LicenceDocument document, string token)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return string.Join('\n',
            $"# {LicenceDocument.ProductName} - licence a vie",
            $"# Client : {document.Customer}",
            $"# Numero : {document.Number}",
            $"# Postes : {document.SeatsLabel}",
            $"# Machine serveur : {MachineFingerprint.Normalize(document.Fingerprint)}",
            $"# Emise le : {document.IssuedOn:dd/MM/yyyy}",
            "# Ne modifiez pas la ligne ci-dessous : toute modification invalide la licence.",
            token,
            string.Empty);
    }

    /// <summary>Jeton sans commentaires ni espaces ; chaîne vide si le texte est absent ou trop long.</summary>
    public static string Compact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength)
        {
            return string.Empty;
        }

        var token = new StringBuilder(text.Length);
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim().TrimStart('\uFEFF');
            if (trimmed.StartsWith('#'))
            {
                continue;
            }

            foreach (var character in trimmed)
            {
                if (!char.IsWhiteSpace(character))
                {
                    token.Append(character);
                }
            }
        }

        return token.ToString();
    }

    /// <summary>
    /// Lit une licence et vérifie sa signature avec la clé publique (SubjectPublicKeyInfo en base64).
    /// Ne vérifie pas la machine : voir <see cref="LicenceCheck.Evaluate"/>.
    /// </summary>
    public static LicenceProblem TryRead(string? text, string publicKey, out LicenceDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return LicenceProblem.Missing;
        }

        var parts = Compact(text).Split('.');
        if (parts.Length != 3 || parts[0] != Prefix)
        {
            return LicenceProblem.Unreadable;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = FromBase64Url(parts[1]);
            signature = FromBase64Url(parts[2]);
        }
        catch (FormatException)
        {
            return LicenceProblem.Unreadable;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), signature, HashAlgorithmName.SHA256))
            {
                return LicenceProblem.BadSignature;
            }
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return LicenceProblem.BadSignature;
        }

        try
        {
            document = JsonSerializer.Deserialize<LicenceDocument>(payload, Json);
        }
        catch (JsonException)
        {
            return LicenceProblem.Unreadable;
        }

        if (document is null || document.Validate().Count > 0)
        {
            document = null;
            return LicenceProblem.Unreadable;
        }

        return LicenceProblem.None;
    }

    /// <summary>Clé publique (SubjectPublicKeyInfo en base64) correspondant à une clé privée.</summary>
    public static string PublicKeyOf(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}
