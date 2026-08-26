using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public interface IBroadcastLog
{
    void Record(DaySchedule schedule);

    IReadOnlyList<BroadcastLogEntry> GetByDate(DateOnly date);

    IReadOnlyList<BroadcastLogEntry> GetByMonth(int year, int month);

    IReadOnlyList<BroadcastLogEntry> GetByRange(DateOnly from, DateOnly to);

    IReadOnlyList<BroadcastLogEntry> GetRecent(int take = 200);
}
