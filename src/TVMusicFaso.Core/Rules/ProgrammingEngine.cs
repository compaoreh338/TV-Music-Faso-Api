using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Rules;

/// <summary>
/// Offre §3.2 / §5 : files FIFO par seau, équité de rotation (sous-diffusés d'abord),
/// Premium = plafond plus haut seulement, budget étrangers 10 %, réparation 90 %.
/// </summary>
public sealed class ProgrammingEngine
{
    private readonly ProgrammingRules _rules;
    private readonly SlotRuleCatalog _slots;
    private readonly Random _random;

    public ProgrammingEngine(ProgrammingRules? rules = null, int? seed = null, SlotRuleCatalog? slots = null)
    {
        _rules = rules ?? new ProgrammingRules();
        _slots = slots ?? new SlotRuleCatalog();
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public DaySchedule GenerateDay(
        IReadOnlyList<Clip> library,
        DateOnly date,
        ThematicFilter? theme = null,
        DaySchedule? existing = null)
    {
        ArgumentNullException.ThrowIfNull(library);

        var catalog = FilterCatalog(library, theme);
        var state = BucketIndex.Build(catalog, date, _rules, _random);
        var schedule = new DaySchedule { Date = date };

        foreach (var slotInfo in TimeSlotInfo.All)
        {
            var locked = existing?.Playlists
                .FirstOrDefault(playlist => playlist.Slot == slotInfo.Slot)
                ?.Items.Where(item => item.IsLocked)
                .ToList() ?? [];
            schedule.Playlists.Add(FillSlot(slotInfo, date, state, locked, theme, schedule));
        }

        RepairSovereignty(schedule, state, date, theme);
        return schedule;
    }

    public Playlist GenerateSlot(
        IReadOnlyList<Clip> library,
        TimeSlotInfo slotInfo,
        DateOnly date,
        Dictionary<Guid, int> playsToday,
        double currentSovereignty,
        ThematicFilter? theme = null,
        IReadOnlyList<PlaylistItem>? lockedItems = null)
    {
        var catalog = FilterCatalog(library, theme);
        var state = BucketIndex.Build(catalog, date, _rules, _random);
        foreach (var (id, plays) in playsToday)
        {
            var clip = catalog.FirstOrDefault(item => item.Id == id);
            if (clip is null)
            {
                continue;
            }

            state.Remaining[id] = Math.Max(0, MaxPlays(clip) - plays);
            state.PlaysToday[id] = Math.Max(0, plays);
        }

        var schedule = new DaySchedule { Date = date };
        return FillSlot(slotInfo, date, state, lockedItems ?? [], theme, schedule, currentSovereignty);
    }

    private static List<Clip> FilterCatalog(IReadOnlyList<Clip> library, ThematicFilter? theme)
    {
        var catalog = library.Where(clip => clip.IsReadyForProgramming).ToList();
        if (theme is not null && !theme.IsEmpty)
        {
            var filtered = catalog.Where(theme.Matches).ToList();
            if (filtered.Count > 0)
            {
                catalog = filtered;
            }
        }

        return catalog.Count > 0 ? catalog : library.ToList();
    }

    private Playlist FillSlot(
        TimeSlotInfo slotInfo,
        DateOnly date,
        BucketIndex state,
        IReadOnlyList<PlaylistItem> lockedItems,
        ThematicFilter? theme,
        DaySchedule schedule,
        double? sovereigntyOverride = null)
    {
        var playlist = new Playlist { Slot = slotInfo.Slot, Date = date };
        foreach (var locked in lockedItems.OrderBy(item => item.Position))
        {
            playlist.Items.Add(new PlaylistItem
            {
                Position = playlist.Items.Count + 1,
                Clip = locked.Clip,
                IsLocked = true
            });
            state.Consume(locked.Clip, requeue: false);
        }

        var elapsed = playlist.TotalDuration;
        var target = TargetDuration(slotInfo);
        var safety = 0;
        var thematic = theme is not null && !theme.IsEmpty;
        var maxAttempts = Math.Max(800, (int)Math.Ceiling(target.TotalSeconds / 5.0) + 50);

        while (elapsed < target && safety < maxAttempts)
        {
            safety++;
            var last = playlist.Items.LastOrDefault()?.Clip;
            var daySovereignty = sovereigntyOverride
                ?? CombinedSovereignty(schedule, playlist);
            var requireBurkinabe = _rules.PreferBurkinabeUntilTarget
                && (daySovereignty < ProgrammingRules.SovereigntyTargetPercent || state.ForeignBudget <= 0);
            var candidate =
                state.Pick(slotInfo, date.DayOfWeek, last, thematic, _slots, _rules, requireBurkinabe, false, false)
                ?? state.Pick(slotInfo, date.DayOfWeek, last, thematic, _slots, _rules, requireBurkinabe, true, false)
                ?? state.Pick(slotInfo, date.DayOfWeek, last, thematic, _slots, _rules, requireBurkinabe, false, true)
                ?? state.Pick(slotInfo, date.DayOfWeek, last, thematic, _slots, _rules, requireBurkinabe, true, true)
                ?? state.Pick(slotInfo, date.DayOfWeek, last, thematic, _slots, _rules, false, true, true)
                ?? state.Pick(slotInfo, date.DayOfWeek, last, thematic: true, _slots, _rules, false, true, true)
                ?? state.AnyEligible(last);

            if (candidate is null)
            {
                break;
            }

            playlist.Items.Add(new PlaylistItem
            {
                Position = playlist.Items.Count + 1,
                Clip = candidate
            });
            state.Consume(candidate, requeue: true);
            elapsed += candidate.Duration;
        }

        EnsureHitsInSlot(playlist, state);
        return playlist;
    }

    private void EnsureHitsInSlot(Playlist playlist, BucketIndex state)
    {
        if (!_rules.HitsReplayInEverySlot)
        {
            return;
        }

        foreach (var hit in state.Hits)
        {
            if (playlist.Items.Any(item => item.Clip.Id == hit.Id))
            {
                continue;
            }

            if (!TryInsertHit(playlist, hit))
            {
                continue;
            }

            state.Consume(hit, requeue: true);
        }
    }

    private static bool TryInsertHit(Playlist playlist, Clip hit)
    {
        var start = 0;
        while (start < playlist.Items.Count && playlist.Items[start].IsLocked)
        {
            start++;
        }

        var index = -1;
        for (var i = start; i <= playlist.Items.Count; i++)
        {
            var previous = i > 0 ? playlist.Items[i - 1].Clip : null;
            var next = i < playlist.Items.Count ? playlist.Items[i].Clip : null;
            if (RespectsDiversity(hit, previous) && (next is null || RespectsDiversity(hit, next)))
            {
                index = i;
            }
        }

        if (index < 0)
        {
            index = playlist.Items.Count;
        }

        playlist.Items.Insert(index, new PlaylistItem
        {
            Position = index + 1,
            Clip = hit
        });
        for (var i = 0; i < playlist.Items.Count; i++)
        {
            playlist.Items[i].Position = i + 1;
        }

        return true;
    }

    private static double CombinedSovereignty(DaySchedule schedule, Playlist current)
    {
        var clips = schedule.AllClips.Concat(current.Items.Select(item => item.Clip)).ToList();
        return clips.Count == 0 ? 100 : 100.0 * clips.Count(clip => clip.IsBurkinabe) / clips.Count;
    }

    private void RepairSovereignty(DaySchedule schedule, BucketIndex state, DateOnly date, ThematicFilter? theme)
    {
        if (schedule.SovereigntyPercent >= ProgrammingRules.SovereigntyTargetPercent)
        {
            return;
        }

        var skipped = new HashSet<PlaylistItem>();
        var guard = 0;
        while (schedule.SovereigntyPercent < ProgrammingRules.SovereigntyTargetPercent && guard++ < 80)
        {
            var foreign = schedule.Playlists
                .SelectMany(playlist => playlist.Items.Select(item => (playlist, item)))
                .Where(row => !row.item.IsLocked && !row.item.Clip.IsBurkinabe && !skipped.Contains(row.item))
                .LastOrDefault();
            if (foreign.item is null)
            {
                break;
            }

            var slotInfo = TimeSlotInfo.For(foreign.playlist.Slot);
            var index = foreign.playlist.Items.IndexOf(foreign.item);
            var previous = index > 0 ? foreign.playlist.Items[index - 1].Clip : null;
            var next = index + 1 < foreign.playlist.Items.Count ? foreign.playlist.Items[index + 1].Clip : null;
            var replacement = state.Pick(
                slotInfo,
                date.DayOfWeek,
                previous,
                thematic: true,
                _slots,
                _rules,
                requireBurkinabe: true,
                relaxDiversity: false,
                ignorePlayCap: true);
            if (replacement is null || (next is not null && !RespectsDiversity(replacement, next)))
            {
                replacement = state.Pick(slotInfo, date.DayOfWeek, previous, true, _slots, _rules, true, true, true)
                    ?? state.AnyBurkinabe(previous, next, relaxDiversity: false)
                    ?? state.AnyBurkinabe(previous, next, relaxDiversity: true);
            }

            if (replacement is null || !replacement.IsBurkinabe)
            {
                skipped.Add(foreign.item);
                continue;
            }

            foreign.item.Clip = replacement;
            state.Consume(replacement, requeue: state.Remaining.GetValueOrDefault(replacement.Id) > 1);
        }
    }

    private TimeSpan TargetDuration(TimeSlotInfo slotInfo) =>
        _rules.SlotFillLimit is { } limit && limit < slotInfo.Duration ? limit : slotInfo.Duration;

    private int MaxPlays(Clip clip) =>
        clip.IsHit || clip.IsPremium
            ? Math.Max(_rules.PremiumDailyPlayCap, TimeSlotInfo.All.Count)
            : _rules.DefaultDailyPlayCap;

    internal static bool RespectsDiversity(Clip clip, Clip? last)
    {
        if (last is null)
        {
            return true;
        }

        return !string.Equals(clip.LanguageLabel, last.LanguageLabel, StringComparison.CurrentCultureIgnoreCase)
            && clip.Genre != last.Genre
            && clip.Id != last.Id;
    }

    private sealed class BucketIndex
    {
        private readonly Dictionary<BucketKey, Queue<Clip>> _buckets = [];
        private readonly List<Clip> _catalog = [];
        private readonly Dictionary<BucketKey, int> _lastPickedTurn = [];
        private int _pickTurn;

        public Dictionary<Guid, int> Remaining { get; } = [];

        public Dictionary<Guid, int> PlaysToday { get; } = [];

        public IReadOnlyList<Clip> Hits => _catalog.Where(clip => clip.IsHit).ToList();

        public int ForeignBudget { get; private set; }

        public static BucketIndex Build(IReadOnlyList<Clip> catalog, DateOnly date, ProgrammingRules rules, Random random)
        {
            var index = new BucketIndex();
            index._catalog.AddRange(catalog);
            foreach (var group in catalog.GroupBy(clip => new BucketKey(clip.IsBurkinabe, clip.Genre)))
            {
                var list = group.ToList();
                Shuffle(list, random);
                index._buckets[group.Key] = new Queue<Clip>(list);
            }

            foreach (var clip in catalog)
            {
                index.Remaining[clip.Id] = clip.IsHit || clip.IsPremium
                    ? Math.Max(rules.PremiumDailyPlayCap, TimeSlotInfo.All.Count)
                    : rules.DefaultDailyPlayCap;
                index.PlaysToday[clip.Id] = 0;
            }

            var averageTicks = catalog.Count == 0 ? TimeSpan.FromMinutes(3).Ticks : catalog.Average(clip => clip.Duration.Ticks);
            var dayTicks = TimeSlotInfo.All.Sum(slot => (rules.SlotFillLimit is { } limit && limit < slot.Duration ? limit : slot.Duration).Ticks);
            var estimated = (int)Math.Max(1, dayTicks / Math.Max(1, averageTicks));
            index.ForeignBudget = (int)Math.Floor(0.10 * estimated);
            return index;
        }

        public void Consume(Clip clip, bool requeue)
        {
            if (Remaining.ContainsKey(clip.Id))
            {
                Remaining[clip.Id] = Math.Max(0, Remaining[clip.Id] - 1);
            }

            PlaysToday[clip.Id] = PlaysToday.GetValueOrDefault(clip.Id) + 1;

            if (!clip.IsBurkinabe)
            {
                ForeignBudget = Math.Max(0, ForeignBudget - 1);
            }

            if (requeue)
            {
                Enqueue(clip);
            }
        }

        public Clip? Pick(
            TimeSlotInfo slotInfo,
            DayOfWeek day,
            Clip? last,
            bool thematic,
            SlotRuleCatalog slots,
            ProgrammingRules rules,
            bool requireBurkinabe,
            bool relaxDiversity,
            bool ignorePlayCap)
        {
            var scored = new List<(Clip Clip, Queue<Clip> Queue, BucketKey Key, double Score)>();
            foreach (var (key, queue) in _buckets)
            {
                if (queue.Count == 0)
                {
                    continue;
                }

                if (requireBurkinabe && !key.IsBurkinabe)
                {
                    continue;
                }

                if (!thematic && !slots.Matches(new Clip { Genre = key.Genre, IsBurkinabe = key.IsBurkinabe }, slotInfo.Slot, day)
                    && !queue.Any(clip => clip.IsHit))
                {
                    continue;
                }

                var rotated = 0;
                var limit = queue.Count;
                while (rotated < limit)
                {
                    var head = queue.Peek();
                    var remaining = Remaining.GetValueOrDefault(head.Id);
                    var playOk = ignorePlayCap || remaining > 0;
                    var notRepeat = last is null || head.Id != last.Id;
                    var diversityOk = relaxDiversity || !rules.EnforceCulturalDiversity || RespectsDiversity(head, last);
                    if (playOk && notRepeat && diversityOk)
                    {
                        scored.Add((head, queue, key, EquityScore(head, key, slotInfo, rules)));
                        break;
                    }

                    queue.Enqueue(queue.Dequeue());
                    rotated++;
                }
            }

            if (scored.Count == 0)
            {
                return null;
            }

            var best = scored.OrderByDescending(row => row.Score).First();
            DequeueMatching(best.Queue, best.Clip.Id);
            _lastPickedTurn[best.Key] = _pickTurn;
            _pickTurn++;
            return best.Clip;
        }

        public Clip? AnyEligible(Clip? last)
        {
            if (_catalog.Count == 0)
            {
                return null;
            }

            return _catalog
                .Where(clip => last is null || clip.Id != last.Id)
                .OrderBy(clip => PlaysToday.GetValueOrDefault(clip.Id))
                .ThenBy(clip => clip.LifetimePlayCount)
                .FirstOrDefault()
                ?? _catalog
                    .OrderBy(clip => PlaysToday.GetValueOrDefault(clip.Id))
                    .ThenBy(clip => clip.LifetimePlayCount)
                    .First();
        }

        public Clip? AnyBurkinabe(Clip? previous, Clip? next, bool relaxDiversity)
        {
            return _catalog
                .Where(item => item.IsBurkinabe)
                .Where(clip =>
                    relaxDiversity
                    || (RespectsDiversity(clip, previous) && (next is null || RespectsDiversity(clip, next))))
                .OrderBy(clip => PlaysToday.GetValueOrDefault(clip.Id))
                .ThenBy(clip => clip.LifetimePlayCount)
                .FirstOrDefault();
        }

        /// <summary>
        /// Un Hit ne gagne jamais contre un clip moins diffusé. Le score d'impact
        /// n'est qu'un départage minuscule (offre §1.2 équité, §3.2 stock faible).
        /// </summary>
        private double EquityScore(Clip head, BucketKey key, TimeSlotInfo slotInfo, ProgrammingRules rules)
        {
            var playsToday = PlaysToday.GetValueOrDefault(head.Id);
            var lifetime = head.LifetimePlayCount + playsToday;
            var lastTurn = _lastPickedTurn.GetValueOrDefault(key, -1);
            var idle = _pickTurn - lastTurn;
            var audience = head.Audience == slotInfo.DefaultAudience ? 1.0 : 0.0;
            var impact = (double)head.ImpactScore * 0.02;
            if (!rules.PreferUnderPlayed)
            {
                return -playsToday * 10 + audience + impact;
            }

            return -playsToday * 1000.0 - lifetime * 10.0 + idle * 3.0 + audience + impact;
        }

        private void Enqueue(Clip clip) =>
            _buckets.GetValueOrDefault(new BucketKey(clip.IsBurkinabe, clip.Genre))?.Enqueue(clip);

        private static void DequeueMatching(Queue<Clip> queue, Guid id)
        {
            if (queue.Count == 0)
            {
                return;
            }

            if (queue.Peek().Id == id)
            {
                queue.Dequeue();
                return;
            }

            var guard = queue.Count;
            while (guard-- > 0 && queue.Peek().Id != id)
            {
                queue.Enqueue(queue.Dequeue());
            }

            if (queue.Count > 0 && queue.Peek().Id == id)
            {
                queue.Dequeue();
            }
        }

        private static void Shuffle(List<Clip> list, Random random)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    private readonly record struct BucketKey(bool IsBurkinabe, MusicalGenre Genre);
}
