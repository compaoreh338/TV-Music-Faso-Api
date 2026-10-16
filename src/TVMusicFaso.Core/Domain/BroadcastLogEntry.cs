namespace TVMusicFaso.Core.Domain;

public sealed class BroadcastLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateOnly Date { get; set; }

    public TimeSlot Slot { get; set; }

    public TimeOnly StartTime { get; set; }

    public Guid ClipId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Artist { get; set; } = string.Empty;

    public ClipLanguage Language { get; set; }

    public MusicalGenre Genre { get; set; }

    public bool IsBurkinabe { get; set; }

    public TimeSpan Duration { get; set; }

    public string FilePath { get; set; } = string.Empty;

    public string OriginLabel => IsBurkinabe ? "Burkinabè" : "Étranger";

    public string DurationLabel => Duration.ToString(@"mm\:ss");

    public TimeOnly EndTime => StartTime.Add(Duration);

    public string SlotLabel => Slot.ToDisplayName();

    /// <summary>Horaire du passage : « 14:00:00 – 14:03:45 ».</summary>
    public string TimeRangeLabel => $"{StartTime:HH\\:mm\\:ss} – {EndTime:HH\\:mm\\:ss}";
}
