using System.Globalization;
using Microsoft.Data.Sqlite;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class SqliteClipRepository : IClipRepository
{
    private readonly string _databasePath;

    public SqliteClipRepository(string databasePath)
    {
        _databasePath = databasePath;
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

    public IReadOnlyList<Clip> GetAll()
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM clips ORDER BY Artist, Title;";
        using var reader = command.ExecuteReader();
        var clips = new List<Clip>();
        while (reader.Read())
        {
            clips.Add(ReadClip(reader));
        }

        return clips;
    }

    public Clip? GetById(Guid id) => GetAll().FirstOrDefault(clip => clip.Id == id);

    public void Add(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = CreateWriteCommand(connection, insert: true);
        Bind(command, clip);
        command.ExecuteNonQuery();
    }

    public void Update(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = CreateWriteCommand(connection, insert: false);
        Bind(command, clip);
        if (command.ExecuteNonQuery() == 0)
        {
            throw new InvalidOperationException($"Clip introuvable : {clip.Id}");
        }
    }

    public void Remove(Guid id)
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM clips WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<Clip> Search(string? query, MusicalGenre? genre, ClipLanguage? language, bool? burkinabeOnly)
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT * FROM clips
            WHERE ($q = '' OR Title LIKE $like OR Artist LIKE $like)
              AND ($genre < 0 OR Genre = $genre)
              AND ($language < 0 OR Language = $language)
              AND ($burkina < 0 OR IsBurkinabe = $burkina)
            ORDER BY Artist, Title;
            """;
        command.Parameters.AddWithValue("$q", query ?? string.Empty);
        command.Parameters.AddWithValue("$like", $"%{query?.Trim() ?? string.Empty}%");
        command.Parameters.AddWithValue("$genre", genre.HasValue ? (int)genre.Value : -1);
        command.Parameters.AddWithValue("$language", language.HasValue ? (int)language.Value : -1);
        command.Parameters.AddWithValue("$burkina", burkinabeOnly == true ? 1 : -1);
        using var reader = command.ExecuteReader();
        var clips = new List<Clip>();
        while (reader.Read())
        {
            clips.Add(ReadClip(reader));
        }

        return clips;
    }

    private static SqliteCommand CreateWriteCommand(SqliteConnection connection, bool insert)
    {
        var command = connection.CreateCommand();
        command.CommandText = insert
            ? """
              INSERT INTO clips (
                  Id, Title, Artist, Year, OriginPlace, FilmingLocation, IsBurkinabe, Quality, Format,
                  DurationTicks, Genre, Language, Theme, Audience, ImpactScore, IsPremium,
                  IsMorallyCompliant, FilePath, LifetimePlayCount, CommitteeRating, PopularityScore,
                  SocialScore, ThumbnailPath, BroadcastFailureCount, LastBroadcastFailureNote)
              VALUES (
                  $Id, $Title, $Artist, $Year, $OriginPlace, $FilmingLocation, $IsBurkinabe, $Quality, $Format,
                  $DurationTicks, $Genre, $Language, $Theme, $Audience, $ImpactScore, $IsPremium,
                  $IsMorallyCompliant, $FilePath, $LifetimePlayCount, $CommitteeRating, $PopularityScore,
                  $SocialScore, $ThumbnailPath, $BroadcastFailureCount, $LastBroadcastFailureNote);
              """
            : """
              UPDATE clips SET
                  Title = $Title, Artist = $Artist, Year = $Year, OriginPlace = $OriginPlace,
                  FilmingLocation = $FilmingLocation, IsBurkinabe = $IsBurkinabe, Quality = $Quality,
                  Format = $Format, DurationTicks = $DurationTicks, Genre = $Genre, Language = $Language,
                  Theme = $Theme, Audience = $Audience, ImpactScore = $ImpactScore, IsPremium = $IsPremium,
                  IsMorallyCompliant = $IsMorallyCompliant, FilePath = $FilePath,
                  LifetimePlayCount = $LifetimePlayCount, CommitteeRating = $CommitteeRating,
                  PopularityScore = $PopularityScore, SocialScore = $SocialScore,
                  ThumbnailPath = $ThumbnailPath, BroadcastFailureCount = $BroadcastFailureCount,
                  LastBroadcastFailureNote = $LastBroadcastFailureNote
              WHERE Id = $Id;
              """;
        return command;
    }

    private static void Bind(SqliteCommand command, Clip clip)
    {
        command.Parameters.AddWithValue("$Id", clip.Id.ToString());
        command.Parameters.AddWithValue("$Title", clip.Title);
        command.Parameters.AddWithValue("$Artist", clip.Artist);
        command.Parameters.AddWithValue("$Year", clip.Year.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$OriginPlace", clip.OriginPlace);
        command.Parameters.AddWithValue("$FilmingLocation", clip.FilmingLocation);
        command.Parameters.AddWithValue("$IsBurkinabe", clip.IsBurkinabe ? 1 : 0);
        command.Parameters.AddWithValue("$Quality", (int)clip.Quality);
        command.Parameters.AddWithValue("$Format", clip.Format);
        command.Parameters.AddWithValue("$DurationTicks", clip.Duration.Ticks);
        command.Parameters.AddWithValue("$Genre", (int)clip.Genre);
        command.Parameters.AddWithValue("$Language", (int)clip.Language);
        command.Parameters.AddWithValue("$Theme", (int)clip.Theme);
        command.Parameters.AddWithValue("$Audience", (int)clip.Audience);
        command.Parameters.AddWithValue("$ImpactScore", clip.ImpactScore.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$IsPremium", clip.IsPremium ? 1 : 0);
        command.Parameters.AddWithValue("$IsMorallyCompliant", clip.IsMorallyCompliant ? 1 : 0);
        command.Parameters.AddWithValue("$FilePath", clip.FilePath);
        command.Parameters.AddWithValue("$LifetimePlayCount", clip.LifetimePlayCount);
        command.Parameters.AddWithValue("$CommitteeRating", clip.CommitteeRating.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$PopularityScore", clip.PopularityScore.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$SocialScore", clip.SocialScore.ToString(CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ThumbnailPath", clip.ThumbnailPath);
        command.Parameters.AddWithValue("$BroadcastFailureCount", clip.BroadcastFailureCount);
        command.Parameters.AddWithValue("$LastBroadcastFailureNote", clip.LastBroadcastFailureNote);
    }

    private static Clip ReadClip(SqliteDataReader reader) =>
        new()
        {
            Id = Guid.Parse(reader.GetString(reader.GetOrdinal("Id"))),
            Title = reader.GetString(reader.GetOrdinal("Title")),
            Artist = reader.GetString(reader.GetOrdinal("Artist")),
            Year = decimal.Parse(reader.GetString(reader.GetOrdinal("Year")), CultureInfo.InvariantCulture),
            OriginPlace = reader.GetString(reader.GetOrdinal("OriginPlace")),
            FilmingLocation = reader.GetString(reader.GetOrdinal("FilmingLocation")),
            IsBurkinabe = reader.GetInt32(reader.GetOrdinal("IsBurkinabe")) == 1,
            Quality = (VideoQuality)reader.GetInt32(reader.GetOrdinal("Quality")),
            Format = reader.GetString(reader.GetOrdinal("Format")),
            Duration = TimeSpan.FromTicks(reader.GetInt64(reader.GetOrdinal("DurationTicks"))),
            Genre = (MusicalGenre)reader.GetInt32(reader.GetOrdinal("Genre")),
            Language = (ClipLanguage)reader.GetInt32(reader.GetOrdinal("Language")),
            Theme = (ClipTheme)reader.GetInt32(reader.GetOrdinal("Theme")),
            Audience = (Audience)reader.GetInt32(reader.GetOrdinal("Audience")),
            ImpactScore = decimal.Parse(reader.GetString(reader.GetOrdinal("ImpactScore")), CultureInfo.InvariantCulture),
            IsPremium = reader.GetInt32(reader.GetOrdinal("IsPremium")) == 1,
            IsMorallyCompliant = reader.GetInt32(reader.GetOrdinal("IsMorallyCompliant")) == 1,
            FilePath = reader.GetString(reader.GetOrdinal("FilePath")),
            LifetimePlayCount = reader.GetInt32(reader.GetOrdinal("LifetimePlayCount")),
            CommitteeRating = ReadDecimal(reader, "CommitteeRating", 3),
            PopularityScore = ReadDecimal(reader, "PopularityScore", 3),
            SocialScore = ReadDecimal(reader, "SocialScore", 3),
            ThumbnailPath = ReadString(reader, "ThumbnailPath"),
            BroadcastFailureCount = ReadInt(reader, "BroadcastFailureCount"),
            LastBroadcastFailureNote = ReadString(reader, "LastBroadcastFailureNote")
        };

    private static string ReadString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static int ReadInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private static decimal ReadDecimal(SqliteDataReader reader, string column, decimal fallback)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return fallback;
        }

        return decimal.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    }
}
