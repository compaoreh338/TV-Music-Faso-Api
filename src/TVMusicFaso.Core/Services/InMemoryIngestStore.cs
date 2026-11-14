using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Stockage en mémoire (tests et prévisualisation) ; chaque lecture renvoie une copie.</summary>
public sealed class InMemoryIngestStore : IIngestStore
{
    private readonly object _lock = new();
    private readonly Dictionary<Guid, MonitoredSource> _sources = [];
    private readonly Dictionary<Guid, IngestItem> _items = [];
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Scope, string Field), string> _memory = [];

    public IReadOnlyList<MonitoredSource> GetSources()
    {
        lock (_lock)
        {
            return _sources.Values
                .OrderByDescending(source => source.Priority)
                .ThenBy(source => source.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(source => source.Clone())
                .ToList();
        }
    }

    public MonitoredSource? GetSource(Guid id)
    {
        lock (_lock)
        {
            return _sources.TryGetValue(id, out var source) ? source.Clone() : null;
        }
    }

    public void SaveSource(MonitoredSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_lock)
        {
            _sources[source.Id] = source.Clone();
        }
    }

    public bool DeleteSource(Guid id)
    {
        lock (_lock)
        {
            return _sources.Remove(id);
        }
    }

    public IReadOnlyList<IngestItem> GetItems(IReadOnlyCollection<IngestStage>? stages = null, int limit = 500)
    {
        lock (_lock)
        {
            return _items.Values
                .Where(item => stages is null || stages.Contains(item.Stage))
                .OrderByDescending(item => item.DetectedAt)
                .Take(Math.Max(1, limit))
                .Select(item => item.Clone())
                .ToList();
        }
    }

    public IngestItem? GetItem(Guid id)
    {
        lock (_lock)
        {
            return _items.TryGetValue(id, out var item) ? item.Clone() : null;
        }
    }

    public IngestItem? FindByExternalId(string externalId)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return null;
        }

        lock (_lock)
        {
            return _items.Values.FirstOrDefault(item => string.Equals(item.ExternalId, externalId, StringComparison.Ordinal))?.Clone();
        }
    }

    public void AddItem(IngestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(item.ExternalId)
                && _items.Values.Any(existing => string.Equals(existing.ExternalId, item.ExternalId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Vidéo déjà connue : {item.ExternalId}");
            }

            _items[item.Id] = item.Clone();
        }
    }

    public void UpdateItem(IngestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_lock)
        {
            if (!_items.ContainsKey(item.Id))
            {
                throw new InvalidOperationException("Clip arrivé introuvable.");
            }

            _items[item.Id] = item.Clone();
        }
    }

    public IngestItem? ClaimNext(IngestStage from, IngestStage to, DateTimeOffset now)
    {
        lock (_lock)
        {
            var next = _items.Values
                .Where(item => item.Stage == from && (item.NextAttemptAt is null || item.NextAttemptAt <= now))
                .OrderByDescending(item => SourcePriority(item))
                .ThenBy(item => item.DetectedAt)
                .FirstOrDefault();
            if (next is null)
            {
                return null;
            }

            next.Stage = to;
            next.UpdatedAt = now;
            return next.Clone();
        }
    }

    public int ResetStage(IngestStage from, IngestStage to)
    {
        lock (_lock)
        {
            var stuck = _items.Values.Where(item => item.Stage == from).ToList();
            foreach (var item in stuck)
            {
                item.Stage = to;
            }

            return stuck.Count;
        }
    }

    public string? GetValue(string key)
    {
        lock (_lock)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void SetValue(string key, string json)
    {
        lock (_lock)
        {
            _values[key] = json;
        }
    }

    public IReadOnlyDictionary<string, string> GetMemory(string scope)
    {
        lock (_lock)
        {
            return _memory
                .Where(entry => entry.Key.Scope == scope)
                .ToDictionary(entry => entry.Key.Field, entry => entry.Value, StringComparer.Ordinal);
        }
    }

    public void Remember(string scope, string field, string value)
    {
        lock (_lock)
        {
            _memory[(scope, field)] = value;
        }
    }

    public IngestSummary Summarize(DateTimeOffset since)
    {
        lock (_lock)
        {
            return IngestStages.Summarize(_items.Values, since);
        }
    }

    private int SourcePriority(IngestItem item) =>
        item.SourceId is { } id && _sources.TryGetValue(id, out var source) ? source.Priority : 0;
}
