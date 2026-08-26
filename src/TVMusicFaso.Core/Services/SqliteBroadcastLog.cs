using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class SqliteBroadcastLog : IBroadcastLog
{
    private readonly string _databasePath;

    public SqliteBroadcastLog(string databasePath)
    {
        _databasePath = databasePath;
    }

    public void Record(DaySchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        using var connection = SqliteDatabase.Open(_databasePath);
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM broadcast_logs WHERE Date = $date;";
        delete.Parameters.AddWithValue("$date", schedule.Date.ToString("yyyy-MM-dd"));
        delete.ExecuteNonQuery();

        foreach (var playlist in schedule.Playlists)
        {
            var cursor = TimeSlotInfo.For(playlist.Slot).Start;
            foreach (var item in playlist.Items)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText =
                    """
                    INSERT INTO broadcast_logs (
                        Id, Date, Slot, StartTime, ClipId, Title, Artist, Language, Genre,
                        IsBurkinabe, DurationTicks, FilePath)
                    VALUES (
                        $Id, $Date, $Slot, $StartTime, $ClipId, $Title, $Artist, $Language, $Genre,
                        $IsBurkinabe, $DurationTicks, $FilePath);
                    """;
                insert.Parameters.AddWithValue("$Id", Guid.NewGuid().ToString());
                insert.Parameters.AddWithValue("$Date", schedule.Date.ToString("yyyy-MM-dd"));
                insert.Parameters.AddWithValue("$Slot", (int)playlist.Slot);
                insert.Parameters.AddWithValue("$StartTime", cursor.ToString("HH:mm:ss"));
                insert.Parameters.AddWithValue("$ClipId", item.Clip.Id.ToString());
                insert.Parameters.AddWithValue("$Title", item.Clip.Title);
                insert.Parameters.AddWithValue("$Artist", item.Clip.Artist);
                insert.Parameters.AddWithValue("$Language", (int)item.Clip.Language);
                insert.Parameters.AddWithValue("$Genre", (int)item.Clip.Genre);
                insert.Parameters.AddWithValue("$IsBurkinabe", item.Clip.IsBurkinabe ? 1 : 0);
                insert.Parameters.AddWithValue("$DurationTicks", item.Clip.Duration.Ticks);
                insert.Parameters.AddWithValue("$FilePath", item.Clip.FilePath);
                insert.ExecuteNonQuery();
                cursor = cursor.Add(item.Clip.Duration);
            }
        }
    }

    public IReadOnlyList<BroadcastLogEntry> GetByDate(DateOnly date) =>
        Query("WHERE Date = $date ORDER BY StartTime", command =>
            command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd")));

    public IReadOnlyList<BroadcastLogEntry> GetByMonth(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to = from.AddMonths(1);
        return Query("WHERE Date >= $from AND Date < $to ORDER BY Date, StartTime", command =>
        {
            command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        });
    }

    public IReadOnlyList<BroadcastLogEntry> GetByRange(DateOnly from, DateOnly to)
    {
        var start = from <= to ? from : to;
        var end = from <= to ? to : from;
        return Query("WHERE Date >= $from AND Date <= $to ORDER BY Date, StartTime", command =>
        {
            command.Parameters.AddWithValue("$from", start.ToString("yyyy-MM-dd"));
            command.Parameters.AddWithValue("$to", end.ToString("yyyy-MM-dd"));
        });
    }

    public IReadOnlyList<BroadcastLogEntry> GetRecent(int take = 200) =>
        Query($"ORDER BY Date DESC, StartTime DESC LIMIT {Math.Clamp(take, 1, 2000)}");

    private IReadOnlyList<BroadcastLogEntry> Query(string suffix, Action<Microsoft.Data.Sqlite.SqliteCommand>? bind = null)
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT Id, Date, Slot, StartTime, ClipId, Title, Artist, Language, Genre,
                   IsBurkinabe, DurationTicks, FilePath
            FROM broadcast_logs
            {suffix};
            """;
        bind?.Invoke(command);

        using var reader = command.ExecuteReader();
        var entries = new List<BroadcastLogEntry>();
        while (reader.Read())
        {
            entries.Add(new BroadcastLogEntry
            {
                Id = Guid.Parse(reader.GetString(0)),
                Date = DateOnly.Parse(reader.GetString(1)),
                Slot = (TimeSlot)reader.GetInt32(2),
                StartTime = TimeOnly.Parse(reader.GetString(3)),
                ClipId = Guid.Parse(reader.GetString(4)),
                Title = reader.GetString(5),
                Artist = reader.GetString(6),
                Language = (ClipLanguage)reader.GetInt32(7),
                Genre = (MusicalGenre)reader.GetInt32(8),
                IsBurkinabe = reader.GetInt32(9) == 1,
                Duration = TimeSpan.FromTicks(reader.GetInt64(10)),
                FilePath = reader.GetString(11)
            });
        }

        return entries;
    }
}
