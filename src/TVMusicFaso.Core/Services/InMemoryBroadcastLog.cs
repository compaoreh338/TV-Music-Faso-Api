using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class InMemoryBroadcastLog : IBroadcastLog
{
    private readonly List<BroadcastLogEntry> _entries = [];

    public void Record(DaySchedule schedule)
    {
        _entries.RemoveAll(entry => entry.Date == schedule.Date);
        foreach (var playlist in schedule.Playlists)
        {
            var cursor = TimeSlotInfo.For(playlist.Slot).Start;
            foreach (var item in playlist.Items)
            {
                _entries.Add(new BroadcastLogEntry
                {
                    Date = schedule.Date,
                    Slot = playlist.Slot,
                    StartTime = cursor,
                    ClipId = item.Clip.Id,
                    Title = item.Clip.Title,
                    Artist = item.Clip.Artist,
                    Language = item.Clip.Language,
                    Genre = item.Clip.Genre,
                    IsBurkinabe = item.Clip.IsBurkinabe,
                    Duration = item.Clip.Duration,
                    FilePath = item.Clip.FilePath
                });
                cursor = cursor.Add(item.Clip.Duration);
            }
        }
    }

    public IReadOnlyList<BroadcastLogEntry> GetByDate(DateOnly date) =>
        _entries.Where(entry => entry.Date == date).OrderBy(entry => entry.StartTime).ToList();

    public IReadOnlyList<BroadcastLogEntry> GetByMonth(int year, int month) =>
        _entries
            .Where(entry => entry.Date.Year == year && entry.Date.Month == month)
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.StartTime)
            .ToList();

    public IReadOnlyList<BroadcastLogEntry> GetByRange(DateOnly from, DateOnly to)
    {
        var start = from <= to ? from : to;
        var end = from <= to ? to : from;
        return _entries
            .Where(entry => entry.Date >= start && entry.Date <= end)
            .OrderBy(entry => entry.Date)
            .ThenBy(entry => entry.StartTime)
            .ToList();
    }

    public IReadOnlyList<BroadcastLogEntry> GetRecent(int take = 200) =>
        _entries.OrderByDescending(entry => entry.Date).ThenByDescending(entry => entry.StartTime).Take(take).ToList();
}
