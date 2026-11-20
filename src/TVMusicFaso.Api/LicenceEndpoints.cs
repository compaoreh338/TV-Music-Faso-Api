using TVMusicFaso.Core.Licensing;

namespace TVMusicFaso.Api;

/// <summary>
/// Licence du serveur : sans licence valide pour cette machine, seules « /api/health » et « /api/licence »
/// répondent. « /api/licence » donne l'empreinte à communiquer au fournisseur et reçoit le fichier de licence.
/// </summary>
public static class LicenceEndpoints
{
    private const string LicencePath = "/api/licence";

    /// <summary>Bloque les routes de l'API tant que la licence est absente ou refusée, ou que le poste est en trop.</summary>
    public static void UseLicence(this WebApplication app)
    {
        app.Use(async (http, next) =>
        {
            var path = http.Request.Path;
            if (path.StartsWithSegments("/api") && !path.StartsWithSegments("/api/health") && !IsLicenceRoute(path))
            {
                var gate = http.RequestServices.GetRequiredService<LicenceGate>();
                var status = gate.Status;
                if (!status.IsValid)
                {
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await http.Response.WriteAsJsonAsync(new
                    {
                        code = "licence",
                        message = status.Message,
                        fingerprint = status.MachineFingerprint
                    });
                    return;
                }

                var seat = gate.AdmitSeat(http.Request);
                if (!seat.Allowed)
                {
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await http.Response.WriteAsJsonAsync(new { code = "licence-postes", message = seat.Message });
                    return;
                }
            }

            await next();
        });
    }

    public static void MapLicence(this WebApplication app)
    {
        // Sans authentification : l'installateur doit pouvoir lire l'empreinte avant toute connexion.
        app.MapGet(LicencePath, (HttpContext http, LicenceGate gate) =>
            Results.Ok(Describe(gate.Status, gate.AdmitSeat(http.Request))));

        app.MapPost(LicencePath, (LicenceInstallRequest body, HttpContext http, LicenceGate gate, ApiComposition composition) =>
        {
            // Première installation ouverte (seule une licence signée pour cette machine est acceptée) ;
            // remplacer une licence valide est réservé à la Direction.
            var session = composition.CurrentSession(http);
            if (gate.Status.IsValid && session?.Policy.CanManageUsers != true)
            {
                return Results.Json(
                    new { message = "Une licence valide est déjà installée : son remplacement est réservé à la Direction." },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(body.Licence) || body.Licence.Length > LicenceCodec.MaxLength)
            {
                return Results.BadRequest(new { message = "Licence absente ou trop volumineuse." });
            }

            var status = gate.Install(body.Licence);
            if (!status.IsValid)
            {
                return Results.BadRequest(new { message = status.Message, fingerprint = status.MachineFingerprint });
            }

            if (session is not null)
            {
                composition.Audit.Write(session, "Licence", status.Message);
            }

            return Results.Ok(Describe(status, gate.AdmitSeat(http.Request)));
        });

        app.MapGet(LicencePath + "/postes", (HttpContext http, LicenceGate gate, ApiComposition composition) =>
        {
            var session = composition.CurrentSession(http);
            if (session is null) return Results.Unauthorized();
            if (!session.Policy.CanManageUsers) return Denied();
            return Results.Ok(new
            {
                max = gate.Status.Licence?.Seats ?? 0,
                seats = gate.Seats.List().Select(seat => new
                {
                    fingerprint = seat.Fingerprint,
                    name = seat.Name,
                    firstSeen = seat.FirstSeen,
                    lastSeen = seat.LastSeen
                })
            });
        });

        app.MapDelete(LicencePath + "/postes/{fingerprint}", (string fingerprint, HttpContext http, LicenceGate gate, ApiComposition composition) =>
        {
            var session = composition.CurrentSession(http);
            if (session is null) return Results.Unauthorized();
            if (!session.Policy.CanManageUsers) return Denied();
            if (!gate.Seats.Remove(fingerprint))
            {
                return Results.NotFound(new { message = "Poste inconnu." });
            }

            composition.Audit.Write(session, "Licence", $"Poste retiré : {MachineFingerprint.Normalize(fingerprint)}");
            return Results.NoContent();
        });
    }

    private static bool IsLicenceRoute(PathString path) =>
        string.Equals(path.Value?.TrimEnd('/'), LicencePath, StringComparison.OrdinalIgnoreCase);

    private static IResult Denied() => Results.Json(new { message = "Accès refusé pour ce rôle." }, statusCode: 403);

    private static object Describe(LicenceStatus status, SeatDecision seat) => new
    {
        valid = status.IsValid,
        message = status.Message,
        fingerprint = status.MachineFingerprint,
        licence = status.IsValid ? status.Token : null,
        customer = status.Licence?.Customer,
        number = status.Licence?.Number,
        seats = status.Licence?.Seats,
        seat = new { allowed = seat.Allowed, message = seat.Message }
    };
}

/// <summary>Fichier de licence envoyé au serveur (texte complet du fichier).</summary>
public sealed record LicenceInstallRequest(string? Licence);
