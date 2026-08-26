namespace TVMusicFaso.Core.Domain;

public sealed record TimeSlotInfo(
    TimeSlot Slot,
    TimeOnly Start,
    TimeSpan Duration,
    SlotMood Mood,
    Audience DefaultAudience)
{
    public static IReadOnlyList<TimeSlotInfo> All { get; } =
    [
        new(TimeSlot.CinqHeures, new TimeOnly(5, 0), TimeSpan.FromHours(1), SlotMood.Douce, Audience.Famille),
        new(TimeSlot.SixHeures, new TimeOnly(6, 0), TimeSpan.FromHours(3), SlotMood.Douce, Audience.Famille),
        new(TimeSlot.NeufHeures, new TimeOnly(9, 0), TimeSpan.FromHours(3), SlotMood.Energique, Audience.Jeunesse),
        new(TimeSlot.Midi, new TimeOnly(12, 0), TimeSpan.FromHours(2), SlotMood.Energique, Audience.Adultes),
        new(TimeSlot.QuatorzeHeures, new TimeOnly(14, 0), TimeSpan.FromHours(3), SlotMood.Energique, Audience.Jeunesse),
        new(TimeSlot.DixSeptHeures, new TimeOnly(17, 0), TimeSpan.FromHours(5), SlotMood.Energique, Audience.Jeunesse),
        new(TimeSlot.VingtDeuxHeures, new TimeOnly(22, 0), TimeSpan.FromHours(3), SlotMood.Melancolique, Audience.Adultes),
        new(TimeSlot.UneHeure, new TimeOnly(1, 0), TimeSpan.FromHours(4), SlotMood.Melancolique, Audience.Adultes)
    ];

    public static TimeSlotInfo For(TimeSlot slot) =>
        All.First(info => info.Slot == slot);

    public string Label => $"{Start:HH\\hmm} · {Mood.ToDisplayName()} · {DefaultAudience.ToDisplayName()}";
}
