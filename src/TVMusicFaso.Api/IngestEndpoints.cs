using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using TVMusicFaso.Core.Domain;
using TVMusicFaso.Core.Services;

namespace TVMusicFaso.Api;

/// <summary>Routes de l'arrivée des clips : sources suivies, clips arrivés, réglages et état des tâches.</summary>
public static class IngestEndpoints
{
    /// <summary>Taille maximale d'un clip déposé (8 Go).</summary>
    public const long MaxDepositBytes = 8L * 1024 * 1024 * 1024;

    /// <summary>Taille maximale d'une liste de sources importée (20 Mo).</summary>
    public const long MaxSourceListBytes = 20L * 1024 * 1024;

    private const int MaxJsonBytes = 1024 * 1024;

    public static void MapIngest(this WebApplication app)
    {
        app.MapGet("/api/sources", (HttpContext http, ApiComposition c) =>
            Run(http, c, (_, ingest) => Json(ingest.Sources())));

        app.MapPost("/api/sources", (HttpContext http, ApiComposition c) =>
            RunAsync(http, c, async (session, ingest) =>
            {
                var source = await ReadJson<MonitoredSource>(http);
                source.Id = Guid.NewGuid();
                return Json(ingest.SaveSource(session, source));
            }));

        app.MapPut("/api/sources/{id:guid}", (Guid id, HttpContext http, ApiComposition c) =>
            RunAsync(http, c, async (session, ingest) =>
            {
                var source = await ReadJson<MonitoredSource>(http);
                source.Id = id;
                return Json(ingest.SaveSource(session, source));
            }));

        app.MapDelete("/api/sources/{id:guid}", (Guid id, HttpContext http, ApiComposition c) =>
            Run(http, c, (session, ingest) =>
            {
                ingest.DeleteSource(session, id);
                return Results.NoContent();
            }));

        app.MapPost("/api/sources/{id:guid}/check", (Guid id, HttpContext http, ApiComposition c) =>
            Run(http, c, (session, ingest) => Json(ingest.CheckNow(session, id))));

        app.MapPost("/api/sources/import", (string? fileName, HttpContext http, ApiComposition c) =>
            RunAsync(http, c, async (session, ingest) =>
            {
                if (http.Request.ContentLength is > MaxSourceListBytes)
                {
                    throw new ArgumentException("Liste trop volumineuse (20 Mo au plus).");
                }

                using var buffer = new MemoryStream();
                await CopyLimitedAsync(http.Request.Body, buffer, MaxSourceListBytes, http.RequestAborted);
                buffer.Position = 0;
                var read = SourceListImport.Read(fileName ?? string.Empty, buffer, ingest.Settings().DefaultCheckIntervalMinutes);
                var summary = ingest.ImportSources(session, read.Sources);
                return Json(summary with { Errors = read.Errors.Concat(summary.Errors).ToList() });
            }));

        app.MapGet("/api/arrivals", (string? filter, int? limit, HttpContext http, ApiComposition c) =>
            Run(http, c, (_, ingest) =>
            {
                var parsed = Enum.TryParse<IngestFilter>(filter, ignoreCase: true, out var value) ? value : IngestFilter.ToHandle;
                return Json(ingest.Items(parsed, Math.Clamp(limit ?? 500, 1, 2000)));
            }));

        app.MapGet("/api/arrivals/summary", (HttpContext http, ApiComposition c) =>
            Run(http, c, (_, ingest) => Json(ingest.Summary())));

        app.MapGet("/api/arrivals/activity", (string? from, string? to, HttpContext http, ApiComposition c) =>
            Run(http, c, (_, ingest) =>
            {
                if (!TryParseInstant(from, out var start) || !TryParseInstant(to, out var end))
                {
                    throw new ArgumentException("Période attendue : from et to au format ISO 8601.");
                }

                return Json(ingest.Activity(start, end));
            }));

        app.MapGet("/api/arrivals/{id:guid}", (Guid id, HttpContext http, ApiComposition c) =>
            Run(http, c, (_, ingest) => Json(ingest.Item(id))));

        app.MapGet("/api/arrivals/{id:guid}/request", (Guid id, HttpContext http, ApiComposition c) =>
            Run(http, c, (_, ingest) =>
            {
                var message = ingest.RequestMessage(id);
                return Json(new { message.To, message.Subject, message.Body, message.MailtoUri });
            }));

        app.MapPost("/api/arrivals/{id:guid}/queue", (Guid id, HttpContext http, ApiComposition c) =>
            Run(http, c, (session, ingest) => Json(ingest.Queue(session, id))));

        app.MapPost("/api/arrivals/{id:guid}/ignore", (Guid id, HttpContext http, ApiComposition c) =>
            RunAsync(http, c, async (session, ingest) =>
                Json(ingest.Ignore(session, id, (await ReadJson<NoteWire>(http)).Note))));

        app.MapPost("/api/arrivals/{id:guid}/restore", (Guid id, HttpContext http, ApiComposition c) =>
            Run(http, c, (session, ingest) => Json(ingest.Restore(session, id))));

        app.MapPost("/api/arrivals/{id:guid}/remove", (Guid id, HttpContext http, ApiComposition c) =>
            Run(http, c, (session, ingest) => Json(ingest.Remove(session, id))));

        app.MapPost("/api/arrivals/{id:guid}/accept", (Guid id, HttpContext http, ApiComposition c) =>
            Run(http, c, (session, ingest) => Json(ingest.AcceptDespiteCheck(session, id))));

        app.MapPost("/api/arrivals/{id:guid}/request-file", (Guid id, HttpContext http, ApiComposition c) =>
            RunAsync(http, c, async (session, ingest) =>
                Json(ingest.RequestFile(session, id, (await ReadJson<NoteWire>(http)).Note))));

        app.MapPost("/api/arrivals/{id:guid}/validate", (Guid id, HttpContext http, ApiComposition c) =>
            RunAsync(http, c, async (session, ingest) =>
            {
                var clip = ingest.SendToValidation(session, id, await ReadJson<ClipSuggestion>(http));
                return Json(new { clipId = clip.Id });
            }));

        app.MapPost("/api/arrivals/{id:guid}/deposit", (Guid id, string? fileName, HttpContext http, ApiComposition c) =>
            RunAsync(http, c, async (session, ingest) =>
            {
                RequireSubmit(session);
                ingest.Item(id);
                var stored = await StoreDepositAsync(http, c, fileName);
                try
                {
                    return Json(ingest.Deposit(session, id, stored));
                }
                catch
                {
                    TryDelete(stored);
                    throw;
                }
            }));

        app.MapPost("/api/arrivals/deposit", (string? fileName, string? title, Guid? sourceId, HttpContext http, ApiComposition c) =>
            RunAsync(http, c, async (session, ingest) =>
            {
                RequireSubmit(session);
                var stored = await StoreDepositAsync(http, c, fileName);
                try
                {
                    return Json(ingest.CreateFromDeposit(session, stored, title ?? Path.GetFileNameWithoutExtension(fileName), sourceId));
                }
                catch
                {
                    TryDelete(stored);
                    throw;
                }
            }));

        app.MapGet("/api/pipeline/settings", (HttpContext http, ApiComposition c) =>
            Run(http, c, (_, ingest) => Json(ingest.Settings())));

        app.MapPut("/api/pipeline/settings", (HttpContext http, ApiComposition c) =>
            RunAsync(http, c, async (session, ingest) =>
                Json(ingest.SaveSettings(session, await ReadJson<IngestSettings>(http)))));

        app.MapGet("/api/pipeline/status", (HttpContext http, ApiComposition c) =>
            Run(http, c, (_, ingest) => Json(ingest.Status())));

        app.MapPost("/api/pipeline/update-tool", (HttpContext http, ApiComposition c) =>
            Run(http, c, (session, ingest) =>
            {
                ingest.RequestToolUpdate(session);
                return Results.Accepted();
            }));
    }

    /// <summary>Enregistre le flux reçu sous un nom choisi par le serveur dans « CLIPS BON NEW/Depots ».</summary>
    private static async Task<string> StoreDepositAsync(HttpContext http, ApiComposition composition, string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!IngestService.DepositExtensions.Contains(extension))
        {
            throw new ArgumentException("Fichier vidéo attendu (MP4, MOV, MKV, MXF…).");
        }

        if (string.IsNullOrWhiteSpace(composition.LibraryRoot))
        {
            throw new InvalidOperationException("Médiathèque du serveur non configurée (Playout:LibraryRoot).");
        }

        if (http.Request.ContentLength is > MaxDepositBytes)
        {
            throw new ArgumentException("Fichier trop volumineux (8 Go au plus).");
        }

        var sizeLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = MaxDepositBytes;
        }

        var settings = composition.Ingest.Settings();
        var folder = Path.Combine(composition.LibraryRoot.Trim(), settings.IngestFolderName, IngestPipeline.DepositsFolder);
        var target = Path.Combine(folder, $"{Guid.NewGuid():N}{extension}");
        var partial = target + ".partiel";
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Le serveur n'a pas le droit d'écrire dans le dossier des arrivées.");
        }

        try
        {
            await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                await CopyLimitedAsync(http.Request.Body, output, MaxDepositBytes, http.RequestAborted);
            }

            if (new FileInfo(partial).Length == 0)
            {
                throw new ArgumentException("Fichier vide.");
            }

            File.Move(partial, target);
            return target;
        }
        catch (UnauthorizedAccessException)
        {
            TryDelete(partial);
            throw new InvalidOperationException("Le serveur n'a pas le droit d'écrire dans le dossier des arrivées.");
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static async Task CopyLimitedAsync(Stream input, Stream output, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
            {
                throw new ArgumentException("Fichier trop volumineux.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static async Task<T> ReadJson<T>(HttpContext http) where T : class
    {
        if (http.Request.ContentLength is > MaxJsonBytes)
        {
            throw new ArgumentException("Requête trop volumineuse.");
        }

        try
        {
            return await JsonSerializer.DeserializeAsync<T>(http.Request.Body, IngestJson.Options, http.RequestAborted)
                ?? throw new ArgumentException("Requête vide.");
        }
        catch (JsonException)
        {
            throw new ArgumentException("Requête illisible.");
        }
    }

    private static IResult Json<T>(T value) => Results.Json(value, IngestJson.Options);

    private static bool TryParseInstant(string? value, out DateTimeOffset instant) =>
        DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out instant);

    private static IResult Run(HttpContext http, ApiComposition composition, Func<UserSession, IngestService, IResult> action) =>
        RunAsync(http, composition, (session, ingest) => Task.FromResult(action(session, ingest))).GetAwaiter().GetResult();

    /// <summary>Session obligatoire ; refus de droit → 403, règle métier → 400, base indisponible → 503.</summary>
    private static async Task<IResult> RunAsync(HttpContext http, ApiComposition composition, Func<UserSession, IngestService, Task<IResult>> action)
    {
        var session = composition.CurrentSession(http);
        if (session is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            return await action(session, composition.Ingest);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Results.Json(new { message = exception.Message }, statusCode: 403);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Results.BadRequest(new { message = exception.Message });
        }
        catch (BadHttpRequestException exception)
        {
            return Results.Json(new { message = exception.Message }, statusCode: exception.StatusCode);
        }
        catch (IOException exception)
        {
            return Results.Json(new { message = $"Erreur disque sur le serveur : {exception.Message}" }, statusCode: 500);
        }
        catch (Npgsql.NpgsqlException)
        {
            return Results.Json(new { message = "Base de données indisponible, réessayez." }, statusCode: 503);
        }
    }

    private static void RequireSubmit(UserSession session)
    {
        if (!session.Policy.CanSubmitClips)
        {
            throw new UnauthorizedAccessException("L'arrivée des clips est traitée par le programmateur et le technicien.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Le fichier orphelin reste dans « Depots » ; sans conséquence pour la diffusion.
        }
    }

    private sealed record NoteWire(string? Note);
}
