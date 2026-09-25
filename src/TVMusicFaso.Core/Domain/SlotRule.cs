namespace TVMusicFaso.Core.Domain;

/// <summary>
/// Règle paramétrable par tranche horaire (offre §5).
/// </summary>
public sealed class SlotRule
{
    public TimeSlot Slot { get; set; }

    public SlotMood Mood { get; set; }

    public Audience Audience { get; set; }

    /// <summary>Heure de début (0–23). Null = horaires de l’offre.</summary>
    public int? StartHour { get; set; }

    /// <summary>Minute de début (0–59).</summary>
    public int? StartMinute { get; set; }

    /// <summary>Heure de fin (0–23). Null = horaires de l’offre.</summary>
    public int? EndHour { get; set; }

    /// <summary>Minute de fin (0–59).</summary>
    public int? EndMinute { get; set; }

    public List<MusicalGenre> AllowedGenres { get; set; } = [];

    public List<MusicalGenre> FridayGenres { get; set; } = [];

    public List<MusicalGenre> SundayGenres { get; set; } = [];

    public string Summary =>
        $"{Slot.ToDisplayName()} · {Mood.ToDisplayName()} · {Audience.ToDisplayName()}";

    public bool HasCustomClock =>
        StartHour is >= 0 and <= 23
        && EndHour is >= 0 and <= 23
        && StartMinute is >= 0 and <= 59
        && EndMinute is >= 0 and <= 59;

    public TimeSlotInfo ResolveInfo()
    {
        var fallback = TimeSlotInfo.For(Slot);
        if (!HasCustomClock)
        {
            return new TimeSlotInfo(Slot, fallback.Start, fallback.Duration, Mood, Audience);
        }

        var start = new TimeOnly(StartHour!.Value, StartMinute ?? 0);
        var end = new TimeOnly(EndHour!.Value, EndMinute ?? 0);
        var duration = end.ToTimeSpan() - start.ToTimeSpan();
        if (duration <= TimeSpan.Zero)
        {
            duration += TimeSpan.FromDays(1);
        }

        return new TimeSlotInfo(Slot, start, duration, Mood, Audience);
    }

    public string ClockLabel
    {
        get
        {
            var clock = ResolveInfo();
            var end = clock.Start.Add(clock.Duration);
            var hours = clock.Duration.TotalHours;
            var hoursLabel = Math.Abs(hours - Math.Round(hours)) < 0.01
                ? $"{hours:0} h"
                : $"{hours:0.#} h";
            return $"{clock.Start:HH\\hmm}–{end:HH\\hmm} · {hoursLabel}";
        }
    }

    public string StartClockText => FormatClock(ResolveInfo().Start);

    public string EndClockText
    {
        get
        {
            var clock = ResolveInfo();
            return FormatClock(clock.Start.Add(clock.Duration));
        }
    }

    public string GenresLabel => AllowedGenres.Count == 0
        ? "Tous genres"
        : string.Join(" · ", AllowedGenres.Select(genre => genre.ToDisplayName()));

    public void ApplyClock(TimeOnly start, TimeOnly end)
    {
        StartHour = start.Hour;
        StartMinute = start.Minute;
        EndHour = end.Hour;
        EndMinute = end.Minute;
    }

    public void ApplyClockFromDefaults()
    {
        var info = TimeSlotInfo.For(Slot);
        ApplyClock(info.Start, info.Start.Add(info.Duration));
    }

    public static bool TryParseClock(string? text, out TimeOnly time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var raw = text.Trim().ToLowerInvariant()
            .Replace('h', ':')
            .Replace('.', ':');
        if (TimeOnly.TryParse(raw, out time))
        {
            return true;
        }

        if (int.TryParse(raw, out var hour) && hour is >= 0 and <= 23)
        {
            time = new TimeOnly(hour, 0);
            return true;
        }

        return false;
    }

    public SlotRule Clone() => new()
    {
        Slot = Slot,
        Mood = Mood,
        Audience = Audience,
        StartHour = StartHour,
        StartMinute = StartMinute,
        EndHour = EndHour,
        EndMinute = EndMinute,
        AllowedGenres = AllowedGenres.ToList(),
        FridayGenres = FridayGenres.ToList(),
        SundayGenres = SundayGenres.ToList()
    };

    private static string FormatClock(TimeOnly value) => $"{value.Hour:00}:{value.Minute:00}";
}

public sealed class SlotRuleCatalog
{
    private readonly List<SlotRule> _rules;

    public SlotRuleCatalog(IEnumerable<SlotRule>? rules = null)
    {
        _rules = rules as List<SlotRule> ?? Sanitize(rules);
        if (_rules.Count == 0)
        {
            _rules.AddRange(CreateDefault());
        }
    }

    public IReadOnlyList<SlotRule> Rules => _rules;

    public IReadOnlyList<TimeSlotInfo> ActiveSlots =>
        _rules.Select(rule => rule.ResolveInfo()).ToList();

    /// <summary>
    /// Déduplique et ordonne les tranches actives. Liste vide → offre complète (8).
    /// Ne réinjecte pas les tranches volontairement supprimées.
    /// </summary>
    public static List<SlotRule> Sanitize(IEnumerable<SlotRule>? rules)
    {
        var list = (rules ?? [])
            .GroupBy(rule => rule.Slot)
            .Select(group => group.First())
            .OrderBy(rule => rule.Slot)
            .ToList();
        return list.Count == 0 ? CreateDefault().ToList() : list;
    }

    /// <summary>Compat : même comportement que <see cref="Sanitize"/>.</summary>
    public static List<SlotRule> Normalize(IEnumerable<SlotRule>? rules) => Sanitize(rules);

    public static SlotRule? NextMissing(IEnumerable<SlotRule> rules)
    {
        var present = rules.Select(rule => rule.Slot).ToHashSet();
        return CreateDefault().FirstOrDefault(rule => !present.Contains(rule.Slot))?.Clone();
    }

    public SlotRule For(TimeSlot slot) =>
        _rules.FirstOrDefault(rule => rule.Slot == slot) ?? CreateDefault().First(rule => rule.Slot == slot);

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

    public static IReadOnlyList<SlotRule> CreateDefault()
    {
        var douce = new[] { MusicalGenre.Gospel, MusicalGenre.Traditionnel, MusicalGenre.Slam, MusicalGenre.MusiqueMusulmane };
        var energique = new[] { MusicalGenre.CoupeDecale, MusicalGenre.AfroPop, MusicalGenre.Urbain };
        var melancolique = new[] { MusicalGenre.Slam, MusicalGenre.Traditionnel, MusicalGenre.Gospel };
        var friday = new[] { MusicalGenre.MusiqueMusulmane, MusicalGenre.Traditionnel, MusicalGenre.Slam };
        var sunday = new[] { MusicalGenre.Gospel, MusicalGenre.Traditionnel, MusicalGenre.Slam };

        return TimeSlotInfo.All.Select(info =>
        {
            var end = info.Start.Add(info.Duration);
            return new SlotRule
            {
                Slot = info.Slot,
                Mood = info.Mood,
                Audience = info.DefaultAudience,
                StartHour = info.Start.Hour,
                StartMinute = info.Start.Minute,
                EndHour = end.Hour,
                EndMinute = end.Minute,
                AllowedGenres = (info.Mood switch
                {
                    SlotMood.Douce => douce,
                    SlotMood.Energique => energique,
                    _ => melancolique
                }).ToList(),
                FridayGenres = friday.ToList(),
                SundayGenres = sunday.ToList()
            };
        }).ToList();
    }
}
