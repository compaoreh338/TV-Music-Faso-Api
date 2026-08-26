using Npgsql;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class PostgresBroadcastLog : IBroadcastLog
{
    private readonly string _connectionString;

    public PostgresBroadcastLog(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void Record(DaySchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        using var connection = PostgresDatabase.Open(_connectionString);
        using var delete = new NpgsqlCommand("DELETE FROM broadcast_logs WHERE date = @date;", connection);
        delete.Parameters.AddWithValue("date", schedule.Date.ToDateTime(TimeOnly.MinValue));
        delete.ExecuteNonQuery();

        foreach (var playlist in schedule.Playlists)
        {
            var cursor = TimeSlotInfo.For(playlist.Slot).Start;
            foreach (var item in playlist.Items)
            {
                using var insert = new NpgsqlCommand(
                    """
                    INSERT INTO broadcast_logs (
                        id, date, slot, start_time, clip_id, title, artist, language, genre,
                        is_burkinabe, duration_ticks, file_path)
                    VALUES (
                        @id, @date, @slot, @start, @clip, @title, @artist, @language, @genre,
                        @burkina, @duration, @path);
                    """,
                    connection);
                insert.Parameters.AddWithValue("id", Guid.NewGuid());
                insert.Parameters.AddWithValue("date", schedule.Date.ToDateTime(TimeOnly.MinValue));
                insert.Parameters.AddWithValue("slot", (int)playlist.Slot);
                insert.Parameters.AddWithValue("start", cursor.ToTimeSpan());
                insert.Parameters.AddWithValue("clip", item.Clip.Id);
                insert.Parameters.AddWithValue("title", item.Clip.Title);
                insert.Parameters.AddWithValue("artist", item.Clip.Artist);
                insert.Parameters.AddWithValue("language", (int)item.Clip.Language);
                insert.Parameters.AddWithValue("genre", (int)item.Clip.Genre);
                insert.Parameters.AddWithValue("burkina", item.Clip.IsBurkinabe);
                insert.Parameters.AddWithValue("duration", item.Clip.Duration.Ticks);
                insert.Parameters.AddWithValue("path", item.Clip.FilePath);
                insert.ExecuteNonQuery();
                cursor = cursor.Add(item.Clip.Duration);
            }
        }
    }

    public IReadOnlyList<BroadcastLogEntry> GetByDate(DateOnly date) =>
        Query(
            "WHERE date = @from ORDER BY start_time",
            command => command.Parameters.AddWithValue("from", date.ToDateTime(TimeOnly.MinValue)));

    public IReadOnlyList<BroadcastLogEntry> GetByMonth(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);
        return Query(
            "WHERE date >= @from AND date < @to ORDER BY date, start_time",
            command =>
            {
                command.Parameters.AddWithValue("from", from.ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("to", to.ToDateTime(TimeOnly.MinValue));
            });
    }

    public IReadOnlyList<BroadcastLogEntry> GetRecent(int take = 200) =>
        Query(
            "ORDER BY date DESC, start_time DESC LIMIT @take",
            command => command.Parameters.AddWithValue("take", Math.Clamp(take, 1, 2000)));

    private IReadOnlyList<BroadcastLogEntry> Query(string suffix, Action<NpgsqlCommand> bind)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(
            $"""
            SELECT id, date, slot, start_time, clip_id, title, artist, language, genre,
                   is_burkinabe, duration_ticks, file_path
            FROM broadcast_logs
            {suffix};
            """,
            connection);
        bind(command);
        using var reader = command.ExecuteReader();
        var entries = new List<BroadcastLogEntry>();
        while (reader.Read())
        {
            entries.Add(new BroadcastLogEntry
            {
                Id = reader.GetGuid(0),
                Date = DateOnly.FromDateTime(reader.GetDateTime(1)),
                Slot = (TimeSlot)reader.GetInt32(2),
                StartTime = TimeOnly.FromTimeSpan(reader.GetTimeSpan(3)),
                ClipId = reader.GetGuid(4),
                Title = reader.GetString(5),
                Artist = reader.GetString(6),
                Language = (ClipLanguage)reader.GetInt32(7),
                Genre = (MusicalGenre)reader.GetInt32(8),
                IsBurkinabe = reader.GetBoolean(9),
                Duration = TimeSpan.FromTicks(reader.GetInt64(10)),
                FilePath = reader.GetString(11)
            });
        }

        return entries;
    }
}
