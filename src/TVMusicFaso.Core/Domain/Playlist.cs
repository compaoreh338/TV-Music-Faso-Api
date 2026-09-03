namespace TVMusicFaso.Core.Domain;

public sealed class PlaylistItem
{
    public int Position { get; set; }

    public required Clip Clip { get; set; }

    public bool IsLocked { get; set; }

    public string LockLabel => IsLocked ? "Verrouillé" : "Libre";
}

public sealed class Playlist
{
    public TimeSlot Slot { get; init; }

    public DateOnly Date { get; init; }

    public List<PlaylistItem> Items { get; } = [];

    public TimeSpan TotalDuration =>
        Items.Aggregate(TimeSpan.Zero, (sum, item) => sum + item.Clip.Duration);

    public TimeSpan SlotDuration => TimeSlotInfo.For(Slot).Duration;

    public TimeSpan RemainingDuration
    {
        get
        {
            var left = SlotDuration - TotalDuration;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    public bool Fits(Clip clip) => TotalDuration + clip.Duration <= SlotDuration;

    public bool FitsReplacement(Clip outgoing, Clip incoming) =>
        TotalDuration - outgoing.Duration + incoming.Duration <= SlotDuration;

    public double SovereigntyPercent
    {
        get
        {
            if (Items.Count == 0)
            {
                return 0;
            }

            return 100.0 * Items.Count(item => item.Clip.IsBurkinabe) / Items.Count;
        }
    }
}

public sealed class DaySchedule
{
    public DateOnly Date { get; init; }

    public List<Playlist> Playlists { get; } = [];

    public IEnumerable<Clip> AllClips => Playlists.SelectMany(playlist => playlist.Items.Select(item => item.Clip));

    public double SovereigntyPercent
    {
        get
        {
            var clips = AllClips.ToList();
            if (clips.Count == 0)
            {
                return 0;
            }

            return 100.0 * clips.Count(clip => clip.IsBurkinabe) / clips.Count;
        }
    }
}
