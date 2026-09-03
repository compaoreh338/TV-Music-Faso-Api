using Microsoft.Data.Sqlite;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class SqliteLanguageCatalog : ILanguageCatalog
{
    private readonly string _databasePath;

    public SqliteLanguageCatalog(string databasePath)
    {
        _databasePath = databasePath;
    }

    public IReadOnlyList<SpokenLanguage> GetAll()
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Code, Label, EnumValue, IsSeeded FROM languages ORDER BY Label;";
        using var reader = command.ExecuteReader();
        var languages = new List<SpokenLanguage>();
        while (reader.Read())
        {
            languages.Add(new SpokenLanguage
            {
                Id = Guid.Parse(reader.GetString(0)),
                Code = reader.GetString(1),
                Label = reader.GetString(2),
                EnumValue = (ClipLanguage)reader.GetInt32(3),
                IsSeeded = reader.GetInt32(4) == 1
            });
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

        var existing = GetAll().FirstOrDefault(item =>
            LanguageCatalog.Normalize(item.Label) == LanguageCatalog.Normalize(label));
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

        using var connection = SqliteDatabase.Open(_databasePath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO languages (Id, Code, Label, EnumValue, IsSeeded)
            VALUES ($id, $code, $label, $enum, $seeded);
            """;
        command.Parameters.AddWithValue("$id", language.Id.ToString());
        command.Parameters.AddWithValue("$code", language.Code);
        command.Parameters.AddWithValue("$label", language.Label);
        command.Parameters.AddWithValue("$enum", (int)language.EnumValue);
        command.Parameters.AddWithValue("$seeded", 0);
        command.ExecuteNonQuery();
        return language;
    }

    public void EnsureSeed()
    {
        using var connection = SqliteDatabase.Open(_databasePath);
        foreach (var seed in LanguageCatalog.BurkinaAndFrench)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO languages (Id, Code, Label, EnumValue, IsSeeded)
                SELECT $id, $code, $label, $enum, 1
                WHERE NOT EXISTS (SELECT 1 FROM languages WHERE Code = $code);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            command.Parameters.AddWithValue("$code", seed.Code);
            command.Parameters.AddWithValue("$label", seed.Label);
            command.Parameters.AddWithValue("$enum", (int)seed.EnumValue);
            command.ExecuteNonQuery();
        }
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
}
