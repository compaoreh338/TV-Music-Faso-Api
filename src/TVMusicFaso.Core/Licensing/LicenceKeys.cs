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
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE+wzTjgi3NI8vSX2k2p6kGhPVmoKXAaypISzcpnmc+EE2TaEueRbQyDtedX2MQ+EF10koVrfJWVxmKimzSBT1KQ==";
}
