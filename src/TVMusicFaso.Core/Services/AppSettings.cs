using System.Text.Json;
using TVMusicFaso.Core.Domain;
using TVMusicFaso.Core.Rules;

namespace TVMusicFaso.Core.Services;

public sealed class AppSettings
{
    public string? PostgresConnectionString { get; set; } =
        "Host=localhost;Port=5432;Database=tvmusicfaso;Username=tvmusic;Password=tvmusic";

    public bool PreferPostgreSql { get; set; } = true;

    public int DefaultDailyPlayCap { get; set; } = 2;

    public int PremiumDailyPlayCap { get; set; } = 4;

    public List<SlotRule> SlotRules { get; set; } = SlotRuleCatalog.CreateDefault().ToList();

    public string Theme { get; set; } = "Dark";

    public static string FilePath => Path.Combine(AppPaths.DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath))
        {
            var created = new AppSettings();
            created.Save();
            return created;
        }

        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions()) ?? new AppSettings();
    }

    public void Save()
    {
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions()));
    }

    public ProgrammingRules ToProgrammingRules() => new()
    {
        DefaultDailyPlayCap = DefaultDailyPlayCap,
        PremiumDailyPlayCap = PremiumDailyPlayCap
    };

    public SlotRuleCatalog ToSlotCatalog()
    {
        SlotRules = SlotRuleCatalog.Normalize(SlotRules);
        return new SlotRuleCatalog(SlotRules);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public sealed record DataPlatformInfo(string Name, bool IsPostgreSql, bool IndexesEnabled, string Details);
