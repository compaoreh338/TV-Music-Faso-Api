using TVMusicFaso.Core.Licensing;

namespace TVMusicFaso.Api;

/// <summary>
/// État de la licence du serveur. Le fichier est relu régulièrement : une licence déposée dans le dossier
/// est prise en compte en quelques secondes, sans redémarrer l'API.
/// </summary>
public sealed class LicenceGate
{
    private static readonly TimeSpan ValidFor = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RefusedFor = TimeSpan.FromSeconds(5);

    private readonly LicenceFile _file;
    private readonly ILogger<LicenceGate> _logger;
    private readonly object _lock = new();
    private LicenceStatus? _status;
    private DateTimeOffset _checkedAt = DateTimeOffset.MinValue;

    public LicenceGate(IConfiguration configuration, IHostEnvironment environment, ILogger<LicenceGate> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        _logger = logger;
        // « Licence:Path » si l'installation l'impose ; sinon le dossier de données, puis à côté du programme.
        _file = new LicenceFile(
            configuration["Licence:Path"],
            Path.Combine(AppContext.BaseDirectory, LicenceFile.FileName),
            Path.Combine(environment.ContentRootPath, LicenceFile.FileName));
        Seats = new LicenceSeats(new PostgresLicenceSeatStore(ApiComposition.ConnectionString(configuration)));
    }

    /// <summary>Emplacement où une licence installée est écrite.</summary>
    public string FilePath => _file.Path;

    /// <summary>Postes de travail enregistrés.</summary>
    public LicenceSeats Seats { get; }

    /// <summary>Licence du serveur, vérifiée pour cette machine.</summary>
    public LicenceStatus Status
    {
        get
        {
            lock (_lock)
            {
                var age = DateTimeOffset.UtcNow - _checkedAt;
                if (_status is null || age >= (_status.IsValid ? ValidFor : RefusedFor))
                {
                    Remember(_file.Check(MachineFingerprint.Current));
                }

                return _status!;
            }
        }
    }

    /// <summary>Installe une licence reçue ; une licence refusée ne remplace jamais celle en place.</summary>
    public LicenceStatus Install(string? text)
    {
        lock (_lock)
        {
            var status = _file.Install(text, MachineFingerprint.Current);
            if (status.IsValid)
            {
                Remember(status);
                _logger.LogInformation("Licence installée : {Summary} ({Path}).", status.Message, _file.Path);
            }
            else
            {
                _logger.LogWarning("Licence reçue, non installée : {Message}", status.Message);
            }

            return status;
        }
    }

    /// <summary>Admet ou refuse le poste qui envoie la requête (app bureau identifiée par son empreinte).</summary>
    public SeatDecision AdmitSeat(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var status = Status;
        if (!status.IsValid || status.Licence is null)
        {
            return SeatDecision.Admitted;
        }

        var fingerprint = request.Headers[LicenceHeaders.Seat].ToString();
        if (!MachineFingerprint.IsWellFormed(fingerprint))
        {
            return SeatDecision.Admitted;
        }

        string name;
        try
        {
            name = Uri.UnescapeDataString(request.Headers[LicenceHeaders.SeatName].ToString());
        }
        catch (UriFormatException)
        {
            name = string.Empty;
        }

        return Seats.Admit(fingerprint, name, status.Licence.Seats);
    }

    private void Remember(LicenceStatus status)
    {
        // Changement d'état en cours de route (fichier déposé, retiré ou remplacé) : une ligne au journal.
        if (_status is not null && _status.IsValid != status.IsValid)
        {
            _logger.Log(status.IsValid ? LogLevel.Information : LogLevel.Warning, "Licence : {Message}", status.Message);
        }

        _status = status;
        _checkedAt = DateTimeOffset.UtcNow;
    }
}
