using TVMusicFaso.Api;
using TVMusicFaso.Core.Domain;
using TVMusicFaso.Core.Rules;
using TVMusicFaso.Core.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ApiComposition>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Spa", policy =>
        policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();
var publicUrl = builder.Configuration["PublicUrl"] ?? "https://localhost:7245";
HttpsTransportPolicy.EnsureSecure(publicUrl);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Spa");
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (ApiComposition composition) =>
    Results.Ok(new
    {
        status = "ok",
        store = "PostgreSQL",
        https = true,
        publicUrl
    }));

app.MapPost("/api/auth/login", (LoginRequest request, ApiComposition composition) =>
{
    var session = composition.Auth.SignIn(request.UserName ?? "", request.Password ?? "");
    if (session is null)
    {
        return Results.Unauthorized();
    }

    composition.Audit.Write(session, "Connexion API", "Tableau de bord web");
    return Results.Ok(ToSessionDto(session));
});

app.MapPost("/api/auth/logout", (HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is not null)
    {
        composition.Auth.SignOut(session.AccessToken);
        composition.Audit.Write(session, "Déconnexion API", "Tableau de bord web");
    }

    return Results.NoContent();
});

app.MapGet("/api/auth/me", (HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    return session is null ? Results.Unauthorized() : Results.Ok(ToSessionDto(session));
});

app.MapGet("/api/dashboard", (HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null)
    {
        return Results.Unauthorized();
    }

    var library = composition.Clips.GetAll();
    var today = composition.Schedules.Load(DateOnly.FromDateTime(DateTime.Today), library);
    var total = library.Count;
    var burkinabe = library.Count(clip => clip.IsBurkinabe);
    var libraryPct = total == 0 ? 0 : 100.0 * burkinabe / total;
    var todayPct = today?.SovereigntyPercent ?? 0;
    var target = ProgrammingRules.SovereigntyTargetPercent;

    return Results.Ok(new
    {
        user = session.User.FullName,
        role = session.User.Role.ToDisplayName(),
        target,
        library = new
        {
            total,
            burkinabe,
            foreign = total - burkinabe,
            premium = library.Count(clip => clip.IsPremium),
            sovereignty = Math.Round(libraryPct, 1),
            alert = libraryPct < target
        },
        today = new
        {
            hasSchedule = today is not null,
            sovereignty = Math.Round(todayPct, 1),
            alert = today is not null && todayPct < target,
            slots = (today?.Playlists ?? []).Select(playlist => new
            {
                slot = playlist.Slot.ToDisplayName(),
                count = playlist.Items.Count,
                sovereignty = Math.Round(playlist.SovereigntyPercent, 1)
            })
        },
        languages = library.GroupBy(clip => clip.Language.ToDisplayName())
            .OrderByDescending(group => group.Count())
            .Select(group => new { label = group.Key, count = group.Count() }),
        genres = library.GroupBy(clip => clip.Genre.ToDisplayName())
            .OrderByDescending(group => group.Count())
            .Select(group => new { label = group.Key, count = group.Count() })
    });
});

app.MapGet("/api/reports/bbda", (int? year, int? month, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null)
    {
        return Results.Unauthorized();
    }

    if (!session.Policy.CanViewReports)
    {
        return Denied();
    }

    var when = DateTime.Today;
    var y = year ?? when.Year;
    var m = month ?? when.Month;
    var entries = composition.Broadcasts.GetByMonth(y, m);
    var sovereignty = entries.Count == 0 ? 0 : 100.0 * entries.Count(item => item.IsBurkinabe) / entries.Count;

    return Results.Ok(new
    {
        year = y,
        month = m,
        count = entries.Count,
        sovereignty = Math.Round(sovereignty, 1),
        canExport = session.Policy.CanExportBbda,
        entries = entries.Select(entry => new
        {
            date = entry.Date.ToString("yyyy-MM-dd"),
            start = entry.StartTime.ToString("HH:mm"),
            slot = entry.Slot.ToDisplayName(),
            title = entry.Title,
            artist = entry.Artist,
            duration = entry.DurationLabel,
            origin = entry.OriginLabel
        })
    });
});

app.MapGet("/api/reports/bbda.csv", (int? year, int? month, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null)
    {
        return Results.Unauthorized();
    }

    if (!session.Policy.CanExportBbda)
    {
        return Denied();
    }

    var when = DateTime.Today;
    var y = year ?? when.Year;
    var m = month ?? when.Month;
    var csv = new BbdaReportService().ToMonthlyCsv(composition.Broadcasts.GetByMonth(y, m), y, m);
    composition.Audit.Write(session, "Export BBDA web", $"{m:00}/{y}");
    return Results.File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"BBDA-{y}-{m:00}.csv");
});

app.MapGet("/api/lookups", (HttpContext http, ApiComposition composition) =>
{
    if (composition.CurrentSession(http) is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        genres = EnumOptions<MusicalGenre>(),
        languages = EnumOptions<ClipLanguage>(),
        themes = EnumOptions<ClipTheme>(),
        audiences = EnumOptions<Audience>(),
        qualities = EnumOptions<VideoQuality>(),
        slots = EnumOptions<TimeSlot>(),
        presets = ThematicPreset.All.Select(preset => new { name = preset.Name })
    });
});

app.MapGet("/api/clips", (string? q, string? genre, string? language, bool? burkinabeOnly, HttpContext http, ApiComposition composition) =>
{
    if (composition.CurrentSession(http) is null) return Results.Unauthorized();
    MusicalGenre? g = Enum.TryParse<MusicalGenre>(genre, out var parsedGenre) ? parsedGenre : null;
    ClipLanguage? l = Enum.TryParse<ClipLanguage>(language, out var parsedLang) ? parsedLang : null;
    return Results.Ok(composition.Clips.Search(q, g, l, burkinabeOnly).Select(ClipDto));
});

app.MapPost("/api/clips", (ClipWriteRequest body, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    if (!session.Policy.CanEditLibrary) return Denied();
    var clip = body.ToClip();
    composition.Clips.Add(clip);
    composition.Audit.Write(session, "Création clip", clip.Title);
    return Results.Created($"/api/clips/{clip.Id}", ClipDto(clip));
});

app.MapPut("/api/clips/{id:guid}", (Guid id, ClipWriteRequest body, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    if (!session.Policy.CanEditLibrary) return Denied();
    var existing = composition.Clips.GetById(id);
    if (existing is null) return Results.NotFound();
    var clip = body.ToClip(id);
    clip.LifetimePlayCount = existing.LifetimePlayCount;
    composition.Clips.Update(clip);
    composition.Audit.Write(session, "Édition clip", clip.Title);
    return Results.Ok(ClipDto(clip));
});

app.MapDelete("/api/clips/{id:guid}", (Guid id, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    if (!session.Policy.CanEditLibrary) return Denied();
    composition.Clips.Remove(id);
    composition.Audit.Write(session, "Suppression clip", id.ToString());
    return Results.NoContent();
});

app.MapGet("/api/schedules", (string? date, HttpContext http, ApiComposition composition) =>
{
    if (composition.CurrentSession(http) is null) return Results.Unauthorized();
    var day = ParseDate(date);
    var library = composition.Clips.GetAll();
    var schedule = composition.Schedules.Load(day, library);
    return Results.Ok(ScheduleDto(schedule, day));
});

app.MapPost("/api/schedules/generate", (GenerateRequest body, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    if (!session.Policy.CanEditPlaylists) return Denied();
    var day = ParseDate(body.Date);
    var library = composition.Clips.GetAll();
    ThematicFilter? theme = null;
    if (body.Thematic == true)
    {
        var preset = ThematicPreset.All.FirstOrDefault(item => item.Name == body.Preset);
        theme = preset?.Filter ?? new ThematicFilter(
            Genre: Enum.TryParse<MusicalGenre>(body.Genre, out var parsed) ? parsed : null,
            BurkinabeOnly: body.BurkinabeOnly == true);
    }

    var existing = composition.Schedules.Load(day, library);
    var schedule = composition.Engine.GenerateDay(library, day, theme, existing);
    composition.Schedules.Save(schedule);
    composition.Audit.Write(session, "Génération grille", day.ToString("yyyy-MM-dd"));
    return Results.Ok(ScheduleDto(schedule, day));
});

app.MapPut("/api/schedules", (SaveScheduleRequest body, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    if (!session.Policy.CanEditPlaylists) return Denied();
    var day = ParseDate(body.Date);
    var library = composition.Clips.GetAll().ToDictionary(clip => clip.Id);
    var schedule = new DaySchedule { Date = day };
    foreach (var playlistBody in body.Playlists ?? [])
    {
        if (!Enum.TryParse<TimeSlot>(playlistBody.Slot, out var slot))
        {
            continue;
        }

        var playlist = new Playlist { Date = day, Slot = slot };
        var position = 1;
        foreach (var item in playlistBody.Items ?? [])
        {
            if (!library.TryGetValue(item.ClipId, out var clip))
            {
                continue;
            }

            playlist.Items.Add(new PlaylistItem
            {
                Position = position++,
                Clip = clip,
                IsLocked = item.IsLocked
            });
        }

        schedule.Playlists.Add(playlist);
    }

    composition.Schedules.Save(schedule);
    composition.Audit.Write(session, "Enregistrement grille", day.ToString("yyyy-MM-dd"));
    return Results.Ok(ScheduleDto(schedule, day));
});

app.MapPost("/api/schedules/lock", (LockRequest body, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    if (!session.Policy.CanEditPlaylists) return Denied();
    var day = ParseDate(body.Date);
    var library = composition.Clips.GetAll();
    var schedule = composition.Schedules.Load(day, library);
    if (schedule is null) return Results.NotFound();
    if (!Enum.TryParse<TimeSlot>(body.Slot, out var slot)) return Results.BadRequest();
    var playlist = schedule.Playlists.FirstOrDefault(item => item.Slot == slot);
    var item = playlist?.Items.FirstOrDefault(row => row.Position == body.Position);
    if (item is null) return Results.NotFound();
    item.IsLocked = !item.IsLocked;
    composition.Schedules.Save(schedule);
    return Results.Ok(ScheduleDto(schedule, day));
});

app.MapPost("/api/broadcasts/validate", (GenerateRequest body, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    if (!session.Policy.CanEditPlaylists) return Denied();
    var day = ParseDate(body.Date);
    var library = composition.Clips.GetAll();
    var schedule = composition.Schedules.Load(day, library);
    if (schedule is null) return Results.NotFound();
    composition.Broadcasts.Record(schedule);
    composition.Audit.Write(session, "Validation diffusion", day.ToString("yyyy-MM-dd"));
    return Results.Ok(new { recorded = schedule.Playlists.Sum(playlist => playlist.Items.Count) });
});

app.MapGet("/api/export/{format}", (string format, string? date, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    if (!session.Policy.CanExport) return Denied();
    var day = ParseDate(date);
    var schedule = composition.Schedules.Load(day, composition.Clips.GetAll());
    if (schedule is null) return Results.NotFound();
    var (content, mime, name) = format.ToLowerInvariant() switch
    {
        "mpl" => (composition.Export.ToMovieJayMpl(schedule), "application/xml", $"TVMusicFaso-{day:yyyyMMdd}.mpl"),
        "m3u" => (composition.Export.ToM3u(schedule), "audio/x-mpegurl", $"TVMusicFaso-{day:yyyyMMdd}.m3u"),
        "csv" => (composition.Export.ToCsv(schedule), "text/csv", $"TVMusicFaso-{day:yyyyMMdd}.csv"),
        "xml" => (composition.Export.ToVmixXml(schedule), "application/xml", $"TVMusicFaso-{day:yyyyMMdd}.xml"),
        _ => (null, null, null)
    };
    if (content is null) return Results.BadRequest();
    composition.Audit.Write(session, "Export playout web", name!);
    return Results.File(System.Text.Encoding.UTF8.GetBytes(content), mime!, name);
});

app.MapGet("/api/security", (HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        fullName = session.User.FullName,
        role = session.User.Role.ToDisplayName(),
        tokenPreview = session.TokenPreview,
        expiresAt = session.ExpiresAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
        lastBackup = composition.Backup.LastBackupPath,
        canManageBackup = session.Policy.CanManageBackup,
        transport = "HTTPS obligatoire. Jeton Bearer, session 8 h. Le rôle n’est pas modifiable ici.",
        entries = composition.Audit.GetRecent().Select(entry => new
        {
            at = entry.AtLabel,
            actor = entry.Actor,
            role = entry.Role.ToDisplayName(),
            action = entry.Action,
            details = entry.Details
        })
    });
});

app.MapPost("/api/backup", (HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    if (!session.Policy.CanManageBackup) return Denied();
    try
    {
        var path = composition.Backup.BackupNow();
        composition.Audit.Write(session, "Sauvegarde", path);
        return Results.Ok(new { path });
    }
    catch (Exception ex)
    {
        return Results.Json(new { message = ex.Message }, statusCode: 500);
    }
});

app.MapGet("/api/constraints", (HttpContext http, ApiComposition composition) =>
{
    if (composition.CurrentSession(http) is null) return Results.Unauthorized();
    return Results.Ok(new
    {
        platform = "PostgreSQL",
        details = "Base unique partagée desktop + web. Index langue, genre, thème, score.",
        clipCount = composition.Clips.GetAll().Count,
        slots = SlotRuleCatalog.CreateDefault().Select(rule => rule.Summary),
        rows = new[]
        {
            new { title = "Volume médiathèque", guarantee = "PostgreSQL — index langue, genre, thème, score." },
            new { title = "Tranches horaires", guarantee = "8 tranches paramétrables. Vendredi = musique musulmane / terroir. Dimanche = gospel / terroir." },
            new { title = "Équité de rotation", guarantee = "Personne n’est sacrifié : les moins diffusés passent d’abord. Un Hit a seulement un plafond plus haut (4), pas la priorité." },
            new { title = "Diversité culturelle", guarantee = "Interdiction de deux clips consécutifs de même langue ou même genre." },
            new { title = "Cible / horaire", guarantee = "Préférence d’audience selon la tranche, sans vider la grille." },
            new { title = "Souveraineté 90 %", guarantee = "Alerte visuelle dès que le ratio burkinabè passe sous 90 %." },
            new { title = "Outils de diffusion", guarantee = "Exports : MPL MovieJaySX, XML/CSV vMix, M3U OBS." },
            new { title = "Conformité BBDA", guarantee = "Rapport mensuel horodaté titre, interprète, durée, passages." }
        }
    });
});

app.MapPut("/api/account/profile", (ProfileRequest body, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    var result = composition.Auth.UpdateProfile(session, body.FullName ?? "", body.UserName ?? "");
    return result.Ok ? Results.Ok(new { message = result.Message, session = ToSessionDto(session) }) : Results.BadRequest(new { message = result.Message });
});

app.MapPut("/api/account/password", (PasswordRequest body, HttpContext http, ApiComposition composition) =>
{
    var session = composition.CurrentSession(http);
    if (session is null) return Results.Unauthorized();
    var result = composition.Auth.ChangePassword(session, body.CurrentPassword ?? "", body.NewPassword ?? "");
    return result.Ok ? Results.Ok(new { message = result.Message }) : Results.BadRequest(new { message = result.Message });
});

app.MapFallback("/api/{**rest}", () =>
    Results.Json(new { message = "Route API inconnue." }, statusCode: 404));
app.Run();

static IResult Denied() => Results.Json(new { message = "Accès refusé pour ce rôle." }, statusCode: 403);

static DateOnly ParseDate(string? value) =>
    DateOnly.TryParse(value, out var date) ? date : DateOnly.FromDateTime(DateTime.Today);

static object[] EnumOptions<T>() where T : struct, Enum =>
    Enum.GetValues<T>().Select(value => new { value = value.ToString(), label = value.ToDisplayName() }).ToArray<object>();

static object ClipDto(Clip clip) => new
{
    id = clip.Id,
    title = clip.Title,
    artist = clip.Artist,
    year = clip.Year,
    originPlace = clip.OriginPlace,
    filmingLocation = clip.FilmingLocation,
    isBurkinabe = clip.IsBurkinabe,
    quality = clip.Quality.ToString(),
    format = clip.Format,
    durationSeconds = (int)clip.Duration.TotalSeconds,
    durationLabel = clip.DurationLabel,
    genre = clip.Genre.ToString(),
    genreLabel = clip.Genre.ToDisplayName(),
    language = clip.Language.ToString(),
    languageLabel = clip.Language.ToDisplayName(),
    theme = clip.Theme.ToString(),
    themeLabel = clip.Theme.ToDisplayName(),
    audience = clip.Audience.ToString(),
    audienceLabel = clip.Audience.ToDisplayName(),
    impactScore = clip.ImpactScore,
    impactLabel = clip.ImpactLabel,
    originLabel = clip.OriginLabel,
    isPremium = clip.IsPremium,
    isMorallyCompliant = clip.IsMorallyCompliant,
    filePath = clip.FilePath,
    committeeRating = clip.CommitteeRating,
    popularityScore = clip.PopularityScore,
    socialScore = clip.SocialScore
};

static object ScheduleDto(DaySchedule? schedule, DateOnly date) => new
{
    date = date.ToString("yyyy-MM-dd"),
    sovereignty = Math.Round(schedule?.SovereigntyPercent ?? 0, 1),
    playlists = (schedule?.Playlists ?? []).Select(playlist => new
    {
        slot = playlist.Slot.ToString(),
        slotLabel = playlist.Slot.ToDisplayName(),
        sovereignty = Math.Round(playlist.SovereigntyPercent, 1),
        items = playlist.Items.Select(item => new
        {
            clipId = item.Clip.Id,
            position = item.Position,
            isLocked = item.IsLocked,
            title = item.Clip.Title,
            artist = item.Clip.Artist,
            origin = item.Clip.OriginLabel
        })
    })
};

static object ToSessionDto(UserSession session) => new
{
    token = session.AccessToken,
    fullName = session.User.FullName,
    userName = session.User.UserName,
    role = session.User.Role.ToDisplayName(),
    roleKey = session.User.Role.ToString(),
    expiresAt = session.ExpiresAt,
    canEditLibrary = session.Policy.CanEditLibrary,
    canEditPlaylists = session.Policy.CanEditPlaylists,
    canExport = session.Policy.CanExport,
    canViewReports = session.Policy.CanViewReports,
    canExportBbda = session.Policy.CanExportBbda,
    canManageBackup = session.Policy.CanManageBackup,
    consultationOnly = session.Policy.IsConsultationOnly
};

public sealed record LoginRequest(string? UserName, string? Password);
public sealed record GenerateRequest(string? Date, bool? Thematic, string? Preset, string? Genre, bool? BurkinabeOnly);
public sealed record LockRequest(string? Date, string? Slot, int Position);
public sealed record SaveScheduleRequest(string? Date, List<SavePlaylistRequest>? Playlists);
public sealed record SavePlaylistRequest(string? Slot, List<SaveItemRequest>? Items);
public sealed record SaveItemRequest(Guid ClipId, bool IsLocked);
public sealed record ProfileRequest(string? FullName, string? UserName);
public sealed record PasswordRequest(string? CurrentPassword, string? NewPassword);
public sealed record ClipWriteRequest(
    string? Title,
    string? Artist,
    decimal? Year,
    string? OriginPlace,
    string? FilmingLocation,
    bool? IsBurkinabe,
    string? Quality,
    string? Format,
    int? DurationSeconds,
    string? Genre,
    string? Language,
    string? Theme,
    string? Audience,
    decimal? CommitteeRating,
    decimal? PopularityScore,
    decimal? SocialScore,
    bool? IsPremium,
    bool? IsMorallyCompliant,
    string? FilePath)
{
    public Clip ToClip(Guid? id = null)
    {
        var clip = new Clip
        {
            Id = id ?? Guid.NewGuid(),
            Title = Title?.Trim() ?? "",
            Artist = Artist?.Trim() ?? "",
            Year = Year ?? DateTime.Today.Year,
            OriginPlace = OriginPlace ?? "Burkina Faso",
            FilmingLocation = FilmingLocation ?? "Ouagadougou",
            IsBurkinabe = IsBurkinabe ?? true,
            Quality = Enum.TryParse<VideoQuality>(Quality, out var quality) ? quality : VideoQuality.Hd,
            Format = string.IsNullOrWhiteSpace(Format) ? "MP4" : Format,
            Duration = TimeSpan.FromSeconds(DurationSeconds ?? 180),
            Genre = Enum.TryParse<MusicalGenre>(Genre, out var genre) ? genre : MusicalGenre.AfroPop,
            Language = Enum.TryParse<ClipLanguage>(Language, out var language) ? language : ClipLanguage.Moore,
            Theme = Enum.TryParse<ClipTheme>(Theme, out var theme) ? theme : ClipTheme.Amour,
            Audience = Enum.TryParse<Audience>(Audience, out var parsedAudience)
                ? parsedAudience
                : TVMusicFaso.Core.Domain.Audience.Famille,
            CommitteeRating = CommitteeRating ?? 3,
            PopularityScore = PopularityScore ?? 3,
            SocialScore = SocialScore ?? 3,
            IsPremium = IsPremium ?? false,
            IsMorallyCompliant = IsMorallyCompliant ?? true,
            FilePath = FilePath ?? ""
        };
        clip.RecalculateImpact();
        return clip;
    }
}
