using Npgsql;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class PostgresLanguageCatalog : ILanguageCatalog
{
    private readonly string _connectionString;

    public PostgresLanguageCatalog(string connectionString)
    {
        _connectionString = connectionString;
    }

    public IReadOnlyList<SpokenLanguage> GetAll()
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(
            "SELECT id, code, label, enum_value, is_seeded FROM languages ORDER BY label;",
            connection);
        using var reader = command.ExecuteReader();
        var languages = new List<SpokenLanguage>();
        while (reader.Read())
        {
            languages.Add(Read(reader));
        }

        return languages;
    }

    public SpokenLanguage Add(string label)
    {
        label = label.Trim();
        if (label.Length < 2)
        {
            throw new ArgumentException("Le nom de la langue doit contenir au moins 2 caractères.", nameof(label));
        }

        var existing = FindByNormalizedLabel(label);
        if (existing is not null)
        {
            return existing;
        }

        var language = new SpokenLanguage
        {
            Code = UniqueCode(LanguageCatalog.Slug(label)),
            Label = label,
            EnumValue = LanguageCatalog.MapEnum(label),
            IsSeeded = false
        };

        using var connection = PostgresDatabase.Open(_connectionString);
        using var command = new NpgsqlCommand(
            """
            INSERT INTO languages (id, code, label, enum_value, is_seeded)
            VALUES (@id, @code, @label, @enum, @seeded);
            """,
            connection);
        command.Parameters.AddWithValue("id", language.Id);
        command.Parameters.AddWithValue("code", language.Code);
        command.Parameters.AddWithValue("label", language.Label);
        command.Parameters.AddWithValue("enum", (int)language.EnumValue);
        command.Parameters.AddWithValue("seeded", language.IsSeeded);
        command.ExecuteNonQuery();
        return language;
    }

    public void EnsureSeed()
    {
        using var connection = PostgresDatabase.Open(_connectionString);
        foreach (var seed in LanguageCatalog.BurkinaAndFrench)
        {
            using var command = new NpgsqlCommand(
                """
                INSERT INTO languages (id, code, label, enum_value, is_seeded)
                SELECT @id, @code, @label, @enum, TRUE
                WHERE NOT EXISTS (SELECT 1 FROM languages WHERE code = @code);
                """,
                connection);
            command.Parameters.AddWithValue("id", Guid.NewGuid());
            command.Parameters.AddWithValue("code", seed.Code);
            command.Parameters.AddWithValue("label", seed.Label);
            command.Parameters.AddWithValue("enum", (int)seed.EnumValue);
            command.ExecuteNonQuery();
        }
    }

    private SpokenLanguage? FindByNormalizedLabel(string label)
    {
        return GetAll().FirstOrDefault(item =>
            LanguageCatalog.Normalize(item.Label) == LanguageCatalog.Normalize(label));
    }

    private string UniqueCode(string slug)
    {
        var existing = GetAll().Select(item => item.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var code = slug;
        var index = 2;
        while (existing.Contains(code))
        {
            code = $"{slug}-{index++}";
        }

        return code;
    }

    private static SpokenLanguage Read(NpgsqlDataReader reader) =>
        new()
        {
            Id = reader.GetGuid(0),
            Code = reader.GetString(1),
            Label = reader.GetString(2),
            EnumValue = (ClipLanguage)reader.GetInt32(3),
            IsSeeded = reader.GetBoolean(4)
        };
}
