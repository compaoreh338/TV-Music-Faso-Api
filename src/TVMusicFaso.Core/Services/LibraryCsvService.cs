using System.Globalization;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class LibraryCsvService
{
    public int Import(string csv, IClipRepository repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csv);
        ArgumentNullException.ThrowIfNull(repository);

        var lines = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            return 0;
        }

        var imported = 0;
        foreach (var line in lines.Skip(1))
        {
            var columns = Split(line);
            if (columns.Length < 3)
            {
                continue;
            }

            repository.Add(new Clip
            {
                Title = columns[0],
                Artist = columns[1],
                Year = ParseDecimal(Get(columns, 2), DateTime.Today.Year),
                Language = LanguageCatalog.MapEnum(Get(columns, 3)),
                LanguageName = string.IsNullOrWhiteSpace(Get(columns, 3))
                    ? LanguageCatalog.LabelFor(LanguageCatalog.MapEnum(Get(columns, 3)))
                    : Get(columns, 3),
                Genre = ParseEnum(Get(columns, 4), MusicalGenre.AfroPop),
                Theme = ParseEnum(Get(columns, 5), ClipTheme.Amour),
                Audience = ParseEnum(Get(columns, 6), Audience.Famille),
                IsBurkinabe = !string.Equals(Get(columns, 7), "Etranger", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Get(columns, 7), "Étranger", StringComparison.OrdinalIgnoreCase),
                Duration = TimeSpan.FromSeconds((double)ParseDecimal(Get(columns, 8), 180)),
                FilePath = Get(columns, 9)
            });
            imported++;
        }

        return imported;
    }

    public string Export(IReadOnlyList<Clip> clips)
    {
        var lines = new List<string>
        {
            "Titre;Artiste;Annee;Langue;Genre;Theme;Cible;Origine;DureeSecondes;Fichier"
        };

        foreach (var clip in clips)
        {
            lines.Add(string.Join(';',
                clip.Title,
                clip.Artist,
                clip.Year.ToString(CultureInfo.InvariantCulture),
                clip.LanguageLabel,
                clip.Genre,
                clip.Theme,
                clip.Audience,
                clip.IsBurkinabe ? "Burkinabe" : "Etranger",
                ((int)clip.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                clip.FilePath));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Get(string[] columns, int index) =>
        index < columns.Length ? columns[index] : string.Empty;

    private static decimal ParseDecimal(string value, decimal fallback) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    private static string[] Split(string line)
    {
        return line.Split(';').Select(column => column.Trim().Trim('"')).ToArray();
    }
}
