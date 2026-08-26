using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class PostgresScheduleStore : IScheduleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _connectionString;

    public PostgresScheduleStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void Save(DaySchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var record = new DayScheduleRecord
        {
            Date = schedule.Date,
            Playlists = schedule.Playlists
                .Select(playlist => new PlaylistRecord
                {
                    Slot = playlist.Slot,
                    Items = playlist.Items
                        .Select(item => new PlaylistItemRecord { ClipId = item.Clip.Id, IsLocked = item.IsLocked })
                        .ToList()
                })
                .ToList()
        };

        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(
            """
            INSERT INTO day_schedules (date, json) VALUES (@date, @json::jsonb)
            ON CONFLICT (date) DO UPDATE SET json = excluded.json;
            """,
            connection);
        command.Parameters.AddWithValue("date", schedule.Date.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("json", JsonSerializer.Serialize(record, JsonOptions));
        command.ExecuteNonQuery();
    }

    public DaySchedule? Load(DateOnly date, IReadOnlyList<Clip> library)
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand("SELECT json FROM day_schedules WHERE date = @date;", connection);
        command.Parameters.AddWithValue("date", date.ToDateTime(TimeOnly.MinValue));
        var raw = command.ExecuteScalar();
        var json = raw as string ?? raw?.ToString();
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        var record = JsonSerializer.Deserialize<DayScheduleRecord>(json, JsonOptions);
        if (record is null)
        {
            return null;
        }

        var byId = library.ToDictionary(clip => clip.Id);
        var schedule = new DaySchedule { Date = date };
        foreach (var playlistRecord in record.Playlists)
        {
            var playlist = new Playlist { Date = date, Slot = playlistRecord.Slot };
            var rows = playlistRecord.Items.Count > 0
                ? playlistRecord.Items
                : playlistRecord.ClipIds.Select(id => new PlaylistItemRecord { ClipId = id }).ToList();
            var position = 1;
            foreach (var row in rows)
            {
                if (!byId.TryGetValue(row.ClipId, out var clip))
                {
                    continue;
                }

                playlist.Items.Add(new PlaylistItem { Position = position++, Clip = clip, IsLocked = row.IsLocked });
            }

            schedule.Playlists.Add(playlist);
        }

        return schedule;
    }

    private sealed class DayScheduleRecord
    {
        public DateOnly Date { get; set; }

        public List<PlaylistRecord> Playlists { get; set; } = [];
    }

    private sealed class PlaylistRecord
    {
        public TimeSlot Slot { get; set; }

        public List<Guid> ClipIds { get; set; } = [];

        public List<PlaylistItemRecord> Items { get; set; } = [];
    }

    private sealed class PlaylistItemRecord
    {
        public Guid ClipId { get; set; }

        public bool IsLocked { get; set; }
    }
}
