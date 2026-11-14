using System.Text.Json;
using System.Text.Json.Serialization;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Stockage des sources suivies, des clips arrivés, des réglages et des corrections mémorisées.</summary>
public interface IIngestStore
{
    IReadOnlyList<MonitoredSource> GetSources();

    MonitoredSource? GetSource(Guid id);

    /// <summary>Ajoute ou remplace la source.</summary>
    void SaveSource(MonitoredSource source);

    bool DeleteSource(Guid id);

    /// <summary>Clips arrivés, les plus récents d'abord ; <paramref name="stages"/> null = toutes les étapes.</summary>
    IReadOnlyList<IngestItem> GetItems(IReadOnlyCollection<IngestStage>? stages = null, int limit = 500);

    IngestItem? GetItem(Guid id);

    /// <summary>Clip déjà connu sous cet identifiant de plateforme (doublon exact).</summary>
    IngestItem? FindByExternalId(string externalId);

    void AddItem(IngestItem item);

    void UpdateItem(IngestItem item);

    /// <summary>
    /// Prend le prochain clip à l'étape <paramref name="from"/> (priorité haute, puis le plus ancien)
    /// et le passe à <paramref name="to"/> ; deux tâches ne prennent jamais le même.
    /// </summary>
    IngestItem? ClaimNext(IngestStage from, IngestStage to, DateTimeOffset now);

    /// <summary>Remet à <paramref name="to"/> les clips restés à <paramref name="from"/> (reprise après arrêt du serveur).</summary>
    int ResetStage(IngestStage from, IngestStage to);

    string? GetValue(string key);

    void SetValue(string key, string json);

    IReadOnlyDictionary<string, string> GetMemory(string scope);

    void Remember(string scope, string field, string value);

    IngestSummary Summarize(DateTimeOffset since);

    /// <summary>Clips repérés et fichiers téléchargés entre <paramref name="from"/> (inclus) et <paramref name="to"/> (exclu).</summary>
    IngestActivity CountActivity(DateTimeOffset from, DateTimeOffset to);
}

/// <summary>Sérialisation commune (base, API, postes) et accès typé aux réglages partagés.</summary>
public static class IngestJson
{
    public const string SettingsKey = "ingest.settings";
    public const string StatusKey = "ingest.status";
    public const string ToolUpdateKey = "ingest.tool-update";

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static IngestSettings LoadSettings(this IIngestStore store) =>
        Deserialize<IngestSettings>(store.GetValue(SettingsKey)) ?? new IngestSettings();

    public static void SaveSettings(this IIngestStore store, IngestSettings settings) =>
        store.SetValue(SettingsKey, Serialize(settings));

    public static IngestPipelineStatus LoadStatus(this IIngestStore store)
    {
        var status = Deserialize<IngestPipelineStatus>(store.GetValue(StatusKey)) ?? new IngestPipelineStatus();
        status.ToolUpdateRequested = string.Equals(store.GetValue(ToolUpdateKey), "true", StringComparison.Ordinal);
        return status;
    }

    public static void SaveStatus(this IIngestStore store, IngestPipelineStatus status) =>
        store.SetValue(StatusKey, Serialize(status));

    public static void SetToolUpdateRequested(this IIngestStore store, bool requested) =>
        store.SetValue(ToolUpdateKey, requested ? "true" : "false");
}
