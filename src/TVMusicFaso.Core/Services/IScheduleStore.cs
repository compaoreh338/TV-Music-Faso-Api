using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public interface IScheduleStore
{
    void Save(DaySchedule schedule);

    DaySchedule? Load(DateOnly date, IReadOnlyList<Clip> library);
}

/// <summary>
/// Génération côté API partagée (offre §2.2).
/// </summary>
public interface IServerProgramming
{
    DaySchedule GenerateOnServer(DateOnly date, ThematicFilter? theme, IReadOnlyList<Clip> library);
}
