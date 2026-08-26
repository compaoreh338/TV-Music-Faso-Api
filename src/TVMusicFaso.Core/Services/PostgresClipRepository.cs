using Npgsql;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class PostgresClipRepository : IClipRepository
{
    private readonly string _connectionString;

    public PostgresClipRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void EnsureInitialized(IEnumerable<Clip> seed)
    {
        if (GetAll().Count > 0)
        {
            return;
        }

        foreach (var clip in seed)
        {
            Add(clip);
        }
    }

    public IReadOnlyList<Clip> GetAll() => Query(
        "SELECT * FROM clips ORDER BY artist, title;",
        _ => { });

    public Clip? GetById(Guid id) =>
        Query("SELECT * FROM clips WHERE id = @id;", command => command.Parameters.AddWithValue("id", id))
            .FirstOrDefault();

    public void Add(Clip clip) => Write(clip, insert: true);

    public void Update(Clip clip) => Write(clip, insert: false);

    public void Remove(Guid id)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand("DELETE FROM clips WHERE id = @id;", connection);
        command.Parameters.AddWithValue("id", id);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Clip> Search(string? query, MusicalGenre? genre, ClipLanguage? language, bool? burkinabeOnly) =>
        Query(
            """
            SELECT * FROM clips
            WHERE (@q = '' OR title ILIKE @like OR artist ILIKE @like)
              AND (@genre < 0 OR genre = @genre)
              AND (@language < 0 OR language = @language)
              AND (@filter_burkina = FALSE OR is_burkinabe = TRUE)
            ORDER BY artist, title;
            """,
            command =>
            {
                command.Parameters.AddWithValue("q", query ?? string.Empty);
                command.Parameters.AddWithValue("like", $"%{query?.Trim() ?? string.Empty}%");
                command.Parameters.AddWithValue("genre", genre.HasValue ? (int)genre.Value : -1);
                command.Parameters.AddWithValue("language", language.HasValue ? (int)language.Value : -1);
                command.Parameters.AddWithValue("filter_burkina", burkinabeOnly == true);
            });

    public static bool TryConnect(string connectionString, out string error)
    {
        try
        {
            using var connection = new NpgsqlConnection(WithShortTimeout(connectionString));
            connection.Open();
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private IReadOnlyList<Clip> Query(string sql, Action<NpgsqlCommand> bind)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(sql, connection);
        bind(command);
        using var reader = command.ExecuteReader();
        var clips = new List<Clip>();
        while (reader.Read())
        {
            clips.Add(Read(reader));
        }

        return clips;
    }

    private void Write(Clip clip, bool insert)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(insert ? InsertSql : UpdateSql, connection);
        command.Parameters.AddWithValue("id", clip.Id);
        command.Parameters.AddWithValue("title", clip.Title);
        command.Parameters.AddWithValue("artist", clip.Artist);
        command.Parameters.AddWithValue("year", clip.Year);
        command.Parameters.AddWithValue("origin_place", clip.OriginPlace);
        command.Parameters.AddWithValue("filming_location", clip.FilmingLocation);
        command.Parameters.AddWithValue("is_burkinabe", clip.IsBurkinabe);
        command.Parameters.AddWithValue("quality", (int)clip.Quality);
        command.Parameters.AddWithValue("format", clip.Format);
        command.Parameters.AddWithValue("duration_ticks", clip.Duration.Ticks);
        command.Parameters.AddWithValue("genre", (int)clip.Genre);
        command.Parameters.AddWithValue("language", (int)clip.Language);
        command.Parameters.AddWithValue("theme", (int)clip.Theme);
        command.Parameters.AddWithValue("audience", (int)clip.Audience);
        command.Parameters.AddWithValue("impact_score", clip.ImpactScore);
        command.Parameters.AddWithValue("is_premium", clip.IsPremium);
        command.Parameters.AddWithValue("is_morally_compliant", clip.IsMorallyCompliant);
        command.Parameters.AddWithValue("file_path", clip.FilePath);
        command.Parameters.AddWithValue("lifetime_play_count", clip.LifetimePlayCount);
        command.Parameters.AddWithValue("committee_rating", clip.CommitteeRating);
        command.Parameters.AddWithValue("popularity_score", clip.PopularityScore);
        command.Parameters.AddWithValue("social_score", clip.SocialScore);
        command.Parameters.AddWithValue("thumbnail_path", clip.ThumbnailPath);
        command.Parameters.AddWithValue("broadcast_failure_count", clip.BroadcastFailureCount);
        command.Parameters.AddWithValue("last_broadcast_failure_note", clip.LastBroadcastFailureNote);
        command.ExecuteNonQuery();
    }

    private static string WithShortTimeout(string connectionString) =>
        connectionString.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            ? connectionString
            : connectionString.TrimEnd(';') + ";Timeout=2;Command Timeout=2";

    private static Clip Read(NpgsqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(reader.GetOrdinal("id")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Artist = reader.GetString(reader.GetOrdinal("artist")),
            Year = reader.GetDecimal(reader.GetOrdinal("year")),
            OriginPlace = reader.GetString(reader.GetOrdinal("origin_place")),
            FilmingLocation = reader.GetString(reader.GetOrdinal("filming_location")),
            IsBurkinabe = reader.GetBoolean(reader.GetOrdinal("is_burkinabe")),
            Quality = (VideoQuality)reader.GetInt32(reader.GetOrdinal("quality")),
            Format = reader.GetString(reader.GetOrdinal("format")),
            Duration = TimeSpan.FromTicks(reader.GetInt64(reader.GetOrdinal("duration_ticks"))),
            Genre = (MusicalGenre)reader.GetInt32(reader.GetOrdinal("genre")),
            Language = (ClipLanguage)reader.GetInt32(reader.GetOrdinal("language")),
            Theme = (ClipTheme)reader.GetInt32(reader.GetOrdinal("theme")),
            Audience = (Audience)reader.GetInt32(reader.GetOrdinal("audience")),
            ImpactScore = reader.GetDecimal(reader.GetOrdinal("impact_score")),
            IsPremium = reader.GetBoolean(reader.GetOrdinal("is_premium")),
            IsMorallyCompliant = reader.GetBoolean(reader.GetOrdinal("is_morally_compliant")),
            FilePath = reader.GetString(reader.GetOrdinal("file_path")),
            LifetimePlayCount = reader.GetInt32(reader.GetOrdinal("lifetime_play_count")),
            CommitteeRating = reader.GetDecimal(reader.GetOrdinal("committee_rating")),
            PopularityScore = reader.GetDecimal(reader.GetOrdinal("popularity_score")),
            SocialScore = reader.GetDecimal(reader.GetOrdinal("social_score")),
            ThumbnailPath = reader.GetString(reader.GetOrdinal("thumbnail_path")),
            BroadcastFailureCount = reader.GetInt32(reader.GetOrdinal("broadcast_failure_count")),
            LastBroadcastFailureNote = reader.GetString(reader.GetOrdinal("last_broadcast_failure_note"))
        };

    private const string InsertSql =
        """
        INSERT INTO clips (
            id, title, artist, year, origin_place, filming_location, is_burkinabe, quality, format,
            duration_ticks, genre, language, theme, audience, impact_score, is_premium,
            is_morally_compliant, file_path, lifetime_play_count, committee_rating, popularity_score,
            social_score, thumbnail_path, broadcast_failure_count, last_broadcast_failure_note)
        VALUES (
            @id, @title, @artist, @year, @origin_place, @filming_location, @is_burkinabe, @quality, @format,
            @duration_ticks, @genre, @language, @theme, @audience, @impact_score, @is_premium,
            @is_morally_compliant, @file_path, @lifetime_play_count, @committee_rating, @popularity_score,
            @social_score, @thumbnail_path, @broadcast_failure_count, @last_broadcast_failure_note);
        """;

    private const string UpdateSql =
        """
        UPDATE clips SET
            title = @title, artist = @artist, year = @year, origin_place = @origin_place,
            filming_location = @filming_location, is_burkinabe = @is_burkinabe, quality = @quality,
            format = @format, duration_ticks = @duration_ticks, genre = @genre, language = @language,
            theme = @theme, audience = @audience, impact_score = @impact_score, is_premium = @is_premium,
            is_morally_compliant = @is_morally_compliant, file_path = @file_path,
            lifetime_play_count = @lifetime_play_count, committee_rating = @committee_rating,
            popularity_score = @popularity_score, social_score = @social_score,
            thumbnail_path = @thumbnail_path, broadcast_failure_count = @broadcast_failure_count,
            last_broadcast_failure_note = @last_broadcast_failure_note
        WHERE id = @id;
        """;
}
