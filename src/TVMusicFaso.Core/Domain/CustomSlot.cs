namespace TVMusicFaso.Core.Domain;

/// <summary>
/// Tranches créées par l'utilisateur, en plus des 8 de l'offre. Leur identifiant encode
/// leurs horaires (début et fin à la minute), ce qui les rend illimitées, stables en base
/// (entier) et lisibles partout sans catalogue : « 14h00–16h30 ».
/// </summary>
public static class CustomSlot
{
    private const int Base = 1_000_000;
    private const int MinutesPerDay = 24 * 60;

    public static TimeSlot Create(TimeOnly start, TimeOnly end)
    {
        if (start == end)
        {
            throw new ArgumentException("Le début et la fin d'une tranche doivent être distincts.", nameof(end));
        }

        return (TimeSlot)(Base + MinuteOfDay(start) * MinutesPerDay + MinuteOfDay(end));
    }

    public static bool IsCustom(TimeSlot slot) => TryDecode(slot, out _, out _);

    public static bool TryDecode(TimeSlot slot, out TimeOnly start, out TimeOnly end)
    {
        start = default;
        end = default;
        var code = (int)slot - Base;
        if (code < 0 || code >= MinutesPerDay * MinutesPerDay)
        {
            return false;
        }

        var startMinute = code / MinutesPerDay;
        var endMinute = code % MinutesPerDay;
        if (startMinute == endMinute)
        {
            return false;
        }

        start = TimeOnly.MinValue.AddMinutes(startMinute);
        end = TimeOnly.MinValue.AddMinutes(endMinute);
        return true;
    }

    /// <summary>Tranche de l'offre ou tranche créée valide.</summary>
    public static bool IsKnown(TimeSlot slot) => Enum.IsDefined(slot) || IsCustom(slot);

    public static string Label(TimeOnly start, TimeOnly end) => $"{start:HH\\hmm}–{end:HH\\hmm}";

    /// <summary>
    /// Règle d'une nouvelle tranche : humeur, public et genres repris de la tranche de l'offre
    /// qui couvre son heure de début.
    /// </summary>
    public static SlotRule CreateRule(TimeOnly start, TimeOnly end)
    {
        var model = TimeSlotInfo.Containing(start).Slot;
        var rule = SlotRuleCatalog.CreateDefault().First(candidate => candidate.Slot == model).Clone();
        rule.Slot = Create(start, end);
        rule.ApplyClock(start, end);
        return rule;
    }

    private static int MinuteOfDay(TimeOnly time) => time.Hour * 60 + time.Minute;
}
