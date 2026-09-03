namespace TVMusicFaso.Core.Domain;

/// <summary>
/// Règle paramétrable par tranche horaire (offre §5).
/// </summary>
public sealed class SlotRule
{
    public TimeSlot Slot { get; set; }

    public SlotMood Mood { get; set; }

    public Audience Audience { get; set; }

    public List<MusicalGenre> AllowedGenres { get; set; } = [];

    public List<MusicalGenre> FridayGenres { get; set; } = [];

    public List<MusicalGenre> SundayGenres { get; set; } = [];

    public string Summary =>
        $"{Slot.ToDisplayName()} · {Mood.ToDisplayName()} · {Audience.ToDisplayName()}";

    public string ClockLabel
    {
        get
        {
            var clock = TimeSlotInfo.For(Slot);
            var end = clock.Start.Add(clock.Duration);
            return $"{clock.Start:HH\\hmm}–{end:HH\\hmm} · {clock.Duration.TotalHours:0} h";
        }
    }

    public string GenresLabel => AllowedGenres.Count == 0
        ? "Tous genres"
        : string.Join(" · ", AllowedGenres.Select(genre => genre.ToDisplayName()));
}

public sealed class SlotRuleCatalog
{
    public SlotRuleCatalog(IEnumerable<SlotRule>? rules = null)
    {
        Rules = Normalize(rules);
    }

    public IReadOnlyList<SlotRule> Rules { get; }

    public SlotRule For(TimeSlot slot) =>
        Rules.FirstOrDefault(rule => rule.Slot == slot) ?? CreateDefault().First(rule => rule.Slot == slot);

    public bool Matches(Clip clip, TimeSlot slot, DayOfWeek day)
    {
        var rule = For(slot);
        var allowed = day switch
        {
            DayOfWeek.Friday => rule.FridayGenres,
            DayOfWeek.Sunday => rule.SundayGenres,
            _ => rule.AllowedGenres
        };
        return allowed.Count == 0 || allowed.Contains(clip.Genre);
    }

    public static List<SlotRule> Normalize(IEnumerable<SlotRule>? rules)
    {
        var bySlot = (rules ?? []).GroupBy(rule => rule.Slot).ToDictionary(group => group.Key, group => group.First());
        var defaults = CreateDefault().ToDictionary(rule => rule.Slot);
        return TimeSlotInfo.All.Select(info =>
            bySlot.TryGetValue(info.Slot, out var rule) ? rule : defaults[info.Slot]).ToList();
    }

    public static IReadOnlyList<SlotRule> CreateDefault()
    {
        var douce = new[] { MusicalGenre.Gospel, MusicalGenre.Traditionnel, MusicalGenre.Slam, MusicalGenre.MusiqueMusulmane };
        var energique = new[] { MusicalGenre.CoupeDecale, MusicalGenre.AfroPop, MusicalGenre.Urbain };
        var melancolique = new[] { MusicalGenre.Slam, MusicalGenre.Traditionnel, MusicalGenre.Gospel };
        var friday = new[] { MusicalGenre.MusiqueMusulmane, MusicalGenre.Traditionnel, MusicalGenre.Slam };
        var sunday = new[] { MusicalGenre.Gospel, MusicalGenre.Traditionnel, MusicalGenre.Slam };

        return TimeSlotInfo.All.Select(info => new SlotRule
        {
            Slot = info.Slot,
            Mood = info.Mood,
            Audience = info.DefaultAudience,
            AllowedGenres = (info.Mood switch
            {
                SlotMood.Douce => douce,
                SlotMood.Energique => energique,
                _ => melancolique
            }).ToList(),
            FridayGenres = friday.ToList(),
            SundayGenres = sunday.ToList()
        }).ToList();
    }
}
