namespace TVMusicFaso.Core.Licensing;

/// <summary>Résultat d'une vérification de licence pour une machine donnée.</summary>
/// <param name="Problem">Raison du refus ; <see cref="LicenceProblem.None"/> si la licence est valide.</param>
/// <param name="Message">Phrase affichable (résumé de la licence, ou raison du refus).</param>
/// <param name="MachineFingerprint">Empreinte de la machine vérifiée ; vide si elle est illisible.</param>
/// <param name="Licence">Contenu de la licence quand sa signature est correcte.</param>
/// <param name="Token">Jeton de la licence valide (sans commentaires) ; vide sinon.</param>
public sealed record LicenceStatus(
    LicenceProblem Problem,
    string Message,
    string MachineFingerprint,
    LicenceDocument? Licence,
    string Token)
{
    /// <summary>Vrai si la licence est signée par le fournisseur et émise pour cette machine.</summary>
    public bool IsValid => Problem == LicenceProblem.None;
}

/// <summary>Vérifie une licence : signature du fournisseur, puis machine autorisée.</summary>
public static class LicenceCheck
{
    /// <param name="text">Texte du fichier de licence.</param>
    /// <param name="machineFingerprint">Empreinte de la machine à autoriser.</param>
    /// <param name="publicKey">Clé publique du fournisseur ; par défaut celle intégrée à l'application.</param>
    public static LicenceStatus Evaluate(string? text, string? machineFingerprint, string? publicKey = null)
    {
        var fingerprint = MachineFingerprint.Normalize(machineFingerprint);
        var problem = LicenceCodec.TryRead(text, publicKey ?? LicenceKeys.Production, out var document);
        if (problem != LicenceProblem.None || document is null)
        {
            return Refused(problem == LicenceProblem.None ? LicenceProblem.Unreadable : problem, fingerprint, null);
        }

        if (fingerprint.Length == 0)
        {
            return Refused(LicenceProblem.FingerprintUnavailable, fingerprint, document);
        }

        if (!MachineFingerprint.Matches(document.Fingerprint, fingerprint))
        {
            return Refused(LicenceProblem.WrongMachine, fingerprint, document);
        }

        return new LicenceStatus(LicenceProblem.None, document.Summary, fingerprint, document, LicenceCodec.Compact(text));
    }

    /// <summary>Statut de refus avec sa phrase d'explication.</summary>
    public static LicenceStatus Refused(LicenceProblem problem, string fingerprint, LicenceDocument? document, string? detail = null)
    {
        var shown = fingerprint.Length == 0 ? "illisible" : fingerprint;
        var message = problem switch
        {
            LicenceProblem.Missing =>
                $"Aucune licence installée. Empreinte de cette machine : {shown}. Communiquez-la à votre fournisseur pour recevoir le fichier de licence.",
            LicenceProblem.Unreadable =>
                $"Fichier de licence illisible : il est incomplet ou n'est pas une licence {LicenceDocument.ProductName}. Empreinte de cette machine : {shown}.",
            LicenceProblem.BadSignature =>
                $"Licence refusée : sa signature est incorrecte (fichier modifié, ou non émis par le fournisseur). Empreinte de cette machine : {shown}.",
            LicenceProblem.WrongMachine =>
                $"Cette licence a été émise pour une autre machine ({MachineFingerprint.Normalize(document?.Fingerprint)}). Empreinte de cette machine : {shown}. Demandez une licence pour cette machine.",
            LicenceProblem.FingerprintUnavailable =>
                "Empreinte de cette machine illisible : la licence ne peut pas être vérifiée.",
            LicenceProblem.NotSaved =>
                $"Licence valide, mais non enregistrée : {detail}",
            _ => "Licence refusée."
        };
        return new LicenceStatus(problem, message, fingerprint, document, string.Empty);
    }
}

/// <summary>
/// Fichier de licence d'une machine. Par défaut : « licence.lic » dans le dossier de données de TV-Music Faso
/// de l'utilisateur (le même pour l'API et l'app bureau lancées sur le serveur).
/// </summary>
public sealed class LicenceFile
{
    /// <summary>Nom du fichier de licence.</summary>
    public const string FileName = "licence.lic";

    private readonly IReadOnlyList<string> _candidates;

    /// <param name="path">Emplacement où la licence est installée ; par défaut <see cref="DefaultPath"/>.</param>
    /// <param name="alsoRead">Autres emplacements lus si le premier ne donne pas de licence valide.</param>
    public LicenceFile(string? path = null, params string?[] alsoRead)
    {
        Path = string.IsNullOrWhiteSpace(path) ? DefaultPath : System.IO.Path.GetFullPath(path);
        _candidates = new[] { Path }
            .Concat(alsoRead.Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => System.IO.Path.GetFullPath(item!)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Emplacement par défaut : dossier de données de l'application de l'utilisateur.</summary>
    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TVMusicFaso",
        FileName);

    /// <summary>Emplacement où <see cref="Install"/> écrit la licence.</summary>
    public string Path { get; }

    /// <summary>Vérifie la licence installée pour la machine donnée ; la première licence valide trouvée l'emporte.</summary>
    public LicenceStatus Check(string? machineFingerprint, string? publicKey = null)
    {
        LicenceStatus? firstRefusal = null;
        foreach (var candidate in _candidates)
        {
            var text = ReadText(candidate);
            if (text is null)
            {
                continue;
            }

            var status = LicenceCheck.Evaluate(text, machineFingerprint, publicKey);
            if (status.IsValid)
            {
                return status;
            }

            firstRefusal ??= status;
        }

        return firstRefusal
            ?? LicenceCheck.Refused(LicenceProblem.Missing, MachineFingerprint.Normalize(machineFingerprint), null);
    }

    /// <summary>Enregistre une licence si elle est valide pour la machine donnée ; une licence refusée n'est jamais écrite.</summary>
    public LicenceStatus Install(string? text, string? machineFingerprint, string? publicKey = null)
    {
        var status = LicenceCheck.Evaluate(text, machineFingerprint, publicKey);
        if (!status.IsValid)
        {
            return status;
        }

        try
        {
            var folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            // Écriture dans un fichier voisin puis remplacement : jamais de licence à moitié écrite.
            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, text!.Trim() + Environment.NewLine);
            File.Move(temporary, Path, overwrite: true);
            return status;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return LicenceCheck.Refused(LicenceProblem.NotSaved, status.MachineFingerprint, status.Licence, exception.Message);
        }
    }

    private static string? ReadText(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > LicenceCodec.MaxLength)
            {
                return null;
            }

            var text = File.ReadAllText(path);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
