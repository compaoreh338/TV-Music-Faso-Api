namespace TVMusicFaso.Core.Licensing;

/// <summary>Clé publique du fournisseur : elle vérifie les licences, elle ne permet pas d'en créer.</summary>
public static class LicenceKeys
{
    /// <summary>
    /// Clé publique de production (ECDSA P-256, SubjectPublicKeyInfo en base64).
    /// Elle doit correspondre à la clé privée gardée par le fournisseur, hors du dépôt
    /// (outil « tvmf-licence creer-cles »). La changer invalide toutes les licences déjà émises.
    /// </summary>
    public const string Production =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEds4xlY8ljoM4NtjNf902n/bHClV5pWB/7114Egs0ydJN4e6FExP7vfMxDxwGlvBczShN9KSAzOZU/J/csPyYkA==";
}
