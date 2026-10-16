using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public interface IBroadcastLog
{
    /// <summary>
    /// Valide la diffusion des tranches présentes dans <paramref name="schedule"/>, au jour de la grille.
    /// Seules ces tranches sont remplacées pour ce jour : les autres passages déjà validés restent.
    /// </summary>
    void Record(DaySchedule schedule);

    IReadOnlyList<BroadcastLogEntry> GetByDate(DateOnly date);

    IReadOnlyList<BroadcastLogEntry> GetByMonth(int year, int month);

    IReadOnlyList<BroadcastLogEntry> GetByRange(DateOnly from, DateOnly to);

    IReadOnlyList<BroadcastLogEntry> GetRecent(int take = 200);
}
