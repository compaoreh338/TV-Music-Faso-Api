using System.Globalization;
using System.Text;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>
/// Import / export CSV de la médiathèque.
/// À l'import, seule la colonne du chemin du fichier vidéo est obligatoire ;
/// les colonnes sont reconnues par leur en-tête, dans n'importe quel ordre.
/// </summary>
public sealed class LibraryCsvService
{
    /// <summary>Nombre maximum de lignes de données par fichier.</summary>
    public const int MaxRows = 50_000;

    /// <summary>Taille maximale d'un champ texte ; au-delà il est tronqué.</summary>
    private const int MaxTextLength = 300;

    private const int MaxPathLength = 1024;

    private const string UnknownArtist = "Artiste inconnu";

    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(3);

    /// <summary>En-tête de l'export ; les dix premières colonnes gardent l'ordre historique (anciens serveurs).</summary>
    public const string Header =
        "Titre;Artiste;Annee;Langue;Genre;Theme;Cible;Origine;DureeSecondes;Fichier;" +
        "LieuOrigine;LieuTournage;Qualite;Format;Premium;NoteComite;Popularite;Reseaux;Vignette";

    /// <summary>Modèle proposé à l'utilisateur : une ligne complète, des lignes partielles, une ligne avec le seul chemin.</summary>
    public static string SampleCsv { get; } = string.Join(Environment.NewLine,
        "Fichier;Titre;Artiste;Annee;Langue;Genre;Theme;Cible;Origine;DureeSecondes;LieuOrigine;LieuTournage;Qualite;Format;Premium;NoteComite;Popularite;Reseaux;Vignette",
        @"D:\Clips\Floby - Mam Yaab.mp4;Mam Yaab;Floby;2023;Mooré;Afro-pop;Amour;Famille;Burkinabè;245;Burkina Faso;Ouagadougou;HD;MP4;oui;4;5;4;D:\Clips\vignettes\mam-yaab.jpg",
        @"D:\Clips\Smarty - Le Président.mp4;Le Président;Smarty;2019;Français;Slam;Patriotisme;Adultes;Burkinabè;04:12;Burkina Faso;Bobo-Dioulasso;4K;MP4;non;5;4;3;",
        @"D:\Clips\Imilo Lechanceux - Wend Panga.mp4;""Wend Panga; version live"";Imilo Lechanceux;2021;Dioula;Gospel;Paix;Famille;Burkinabè;3:58;;;;;;;;;",
        @"D:\Clips\Magic System - Premier Gaou.mp4;Premier Gaou;Magic System;2000;Français;Coupé-décalé;Fête;Jeunesse;Étranger;300;Côte d'Ivoire;Abidjan;HD;MP4;;;;;",
        @"D:\Clips\Amadou Balaké - Taximen.mp4;;;;;;;;;;;;;;;;;;",
        @"Clips/Nouveaux/Alif Naaba - Foo.mov;;;;;;;;;;;;;;;;;;");

    private enum Column
    {
        FilePath,
        Title,
        Artist,
        Year,
        Language,
        Genre,
        Theme,
        Audience,
        Origin,
        Duration,
        OriginPlace,
        FilmingLocation,
        Quality,
        Format,
        Premium,
        CommitteeRating,
        Popularity,
        Social,
        Thumbnail
    }

    private static readonly Dictionary<string, Column> HeaderAliases = BuildAliases();

    /// <summary>Importe et renvoie le nombre de clips ajoutés (compatibilité avec l'API).</summary>
    public int Import(string csv, IClipRepository repository) => ImportWithReport(csv, repository).Imported;

    /// <summary>Lit le CSV, ignore les doublons et lignes sans chemin, enregistre le reste « À valider ».</summary>
    public ClipImportReport ImportWithReport(string? csv, IClipRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var parsed = Parse(csv, repository.GetAll());
        if (parsed.FatalError is not null || parsed.Clips.Count == 0)
        {
            return ClipImportReport.From(parsed, 0);
        }

        int imported;
        if (repository is IBulkClipImport bulk)
        {
            imported = bulk.AddRange(parsed.Clips);
        }
        else
        {
            foreach (var clip in parsed.Clips)
            {
                repository.Add(clip);
            }

            imported = parsed.Clips.Count;
        }

        parsed.Duplicates += Math.Max(0, parsed.Clips.Count - imported);
        return ClipImportReport.From(parsed, imported);
    }

    /// <summary>Analyse le CSV sans rien enregistrer.</summary>
    /// <param name="csv">Contenu du fichier (séparateur ; , ou tabulation, guillemets acceptés).</param>
    /// <param name="existing">Clips déjà présents, pour écarter les fichiers vidéo en double.</param>
    public ClipCsvParseResult Parse(string? csv, IEnumerable<Clip> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);
        var result = new ClipCsvParseResult();
        var text = (csv ?? string.Empty).TrimStart('\uFEFF');
        if (string.IsNullOrWhiteSpace(text))
        {
            result.FatalError = "Le fichier est vide.";
            return result;
        }

        var records = ReadRecords(text, DetectSeparator(text))
            .Where(record => record.Fields.Any(field => !string.IsNullOrWhiteSpace(field)))
            .ToList();
        if (records.Count == 0)
        {
            result.FatalError = "Le fichier est vide.";
            return result;
        }

        var columns = MapHeader(records[0].Fields);
        if (!columns.ContainsKey(Column.FilePath))
        {
            result.FatalError = "Colonne « Fichier » (chemin du clip) introuvable dans la première ligne. Partez du modèle CSV.";
            return result;
        }

        if (records.Count - 1 > MaxRows)
        {
            result.FatalError = $"Trop de lignes ({records.Count - 1}). Maximum {MaxRows} par fichier : découpez-le.";
            return result;
        }

        var knownPaths = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var clip in existing)
        {
            var key = PathKey(clip.FilePath);
            if (key.Length > 0)
            {
                knownPaths.TryAdd(key, 0);
            }
        }

        foreach (var record in records.Skip(1))
        {
            var row = new Row(record, columns, result.Warnings);
            var path = row.Text(Column.FilePath, MaxPathLength + 1);
            if (path.Length == 0)
            {
                result.Rejected.Add(new ClipImportIssue(record.Line, "chemin du fichier vidéo manquant (colonne Fichier)."));
                continue;
            }

            if (path.Length > MaxPathLength || path.Any(char.IsControl))
            {
                result.Rejected.Add(new ClipImportIssue(record.Line, "chemin du fichier invalide."));
                continue;
            }

            var pathKey = PathKey(path);
            if (knownPaths.TryGetValue(pathKey, out var firstLine))
            {
                result.Duplicates++;
                result.Rejected.Add(new ClipImportIssue(record.Line, firstLine == 0
                    ? "ce fichier est déjà dans la médiathèque."
                    : $"même fichier qu'à la ligne {firstLine}."));
                continue;
            }

            knownPaths[pathKey] = record.Line;
            result.Clips.Add(BuildClip(row, path));
        }

        return result;
    }

    /// <summary>Exporte la médiathèque dans un CSV réimportable.</summary>
    public string Export(IReadOnlyList<Clip> clips)
    {
        ArgumentNullException.ThrowIfNull(clips);
        var lines = new List<string> { Header };
        foreach (var clip in clips)
        {
            lines.Add(string.Join(';', new[]
            {
                clip.Title,
                clip.Artist,
                clip.Year.ToString("0", CultureInfo.InvariantCulture),
                clip.LanguageLabel,
                clip.Genre.ToString(),
                clip.Theme.ToString(),
                clip.Audience.ToString(),
                ExportText.Origin(clip.IsBurkinabe),
                ((int)clip.Duration.TotalSeconds).ToString(CultureInfo.InvariantCulture),
                clip.FilePath,
                clip.OriginPlace,
                clip.FilmingLocation,
                clip.Quality.ToDisplayName(),
                clip.Format,
                clip.IsPremium ? "oui" : "non",
                clip.CommitteeRating.ToString("0.#", CultureInfo.InvariantCulture),
                clip.PopularityScore.ToString("0.#", CultureInfo.InvariantCulture),
                clip.SocialScore.ToString("0.#", CultureInfo.InvariantCulture),
                clip.ThumbnailPath
            }.Select(Escape)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static Clip BuildClip(Row row, string path)
    {
        var stem = FileStem(path);
        var title = row.Text(Column.Title);
        var artist = row.Text(Column.Artist);
        if (title.Length == 0 && artist.Length == 0 && stem.IndexOf(" - ", StringComparison.Ordinal) is var dash and > 0)
        {
            artist = stem[..dash].Trim();
            title = stem[(dash + 3)..].Trim();
        }

        var clip = new Clip
        {
            FilePath = path,
            Title = Truncate(title.Length > 0 ? title : stem.Length > 0 ? stem : path),
            Artist = Truncate(artist.Length > 0 ? artist : UnknownArtist),
            Year = row.Year(),
            Genre = row.Choice(Column.Genre, MusicalGenre.AfroPop),
            Theme = row.Choice(Column.Theme, ClipTheme.Amour),
            Audience = row.Choice(Column.Audience, Audience.Famille),
            IsBurkinabe = row.Origin(),
            Duration = row.Duration(),
            Quality = row.Choice(Column.Quality, VideoQuality.Hd),
            Format = Truncate(FirstNonEmpty(row.Text(Column.Format), FileExtension(path), "MP4").ToUpperInvariant()),
            IsPremium = row.Flag(Column.Premium),
            CommitteeRating = row.Rating(Column.CommitteeRating),
            PopularityScore = row.Rating(Column.Popularity),
            SocialScore = row.Rating(Column.Social),
            ThumbnailPath = row.Text(Column.Thumbnail, MaxPathLength)
        };

        var originPlace = row.Text(Column.OriginPlace);
        if (originPlace.Length > 0)
        {
            clip.OriginPlace = originPlace;
        }
        else if (!clip.IsBurkinabe)
        {
            clip.OriginPlace = string.Empty;
        }

        var filmingLocation = row.Text(Column.FilmingLocation);
        if (filmingLocation.Length > 0)
        {
            clip.FilmingLocation = filmingLocation;
        }
        else if (!clip.IsBurkinabe)
        {
            clip.FilmingLocation = string.Empty;
        }

        var language = row.Text(Column.Language);
        clip.Language = LanguageCatalog.MapEnum(language);
        clip.LanguageName = language.Length == 0 || clip.Language != ClipLanguage.Autre
            ? LanguageCatalog.LabelFor(clip.Language)
            : language;

        clip.RecalculateImpact();
        return clip.SubmitForValidation();
    }

    /// <summary>Accès typé aux cellules d'une ligne ; signale les valeurs non reconnues.</summary>
    private sealed class Row(CsvRecord source, IReadOnlyDictionary<Column, int> columns, List<ClipImportIssue> warnings)
    {
        public string Text(Column column, int maxLength = MaxTextLength)
        {
            if (!columns.TryGetValue(column, out var index) || index >= source.Fields.Count)
            {
                return string.Empty;
            }

            var value = source.Fields[index].Trim();
            return value.Length > maxLength ? value[..maxLength] : value;
        }

        public decimal Year()
        {
            var raw = Text(Column.Year);
            if (raw.Length == 0)
            {
                return DateTime.Today.Year;
            }

            var digits = raw.Length >= 4 ? raw[..4] : raw;
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year >= 1900 && year <= DateTime.Today.Year + 1)
            {
                return year;
            }

            Warn(Column.Year, raw, DateTime.Today.Year.ToString(CultureInfo.InvariantCulture));
            return DateTime.Today.Year;
        }

        public TEnum Choice<TEnum>(Column column, TEnum fallback) where TEnum : struct, Enum
        {
            var raw = Text(column);
            if (raw.Length == 0)
            {
                return fallback;
            }

            var key = LanguageCatalog.Normalize(raw);
            foreach (var value in Enum.GetValues<TEnum>())
            {
                if (LanguageCatalog.Normalize(value.ToString()) == key
                    || LanguageCatalog.Normalize(value.ToDisplayName()) == key)
                {
                    return value;
                }
            }

            if (Synonym(key) is TEnum synonym)
            {
                return synonym;
            }

            Warn(column, raw, fallback.ToDisplayName());
            return fallback;
        }

        public bool Origin()
        {
            var raw = Text(Column.Origin);
            if (raw.Length == 0)
            {
                return true;
            }

            switch (LanguageCatalog.Normalize(raw))
            {
                case "burkinabe" or "burkinabee" or "burkina" or "burkinafaso" or "bf" or "local" or "national" or "oui" or "yes" or "1":
                    return true;
                case "etranger" or "etrangere" or "international" or "import" or "non" or "no" or "0":
                    return false;
                default:
                    Warn(Column.Origin, raw, "Burkinabè");
                    return true;
            }
        }

        public bool Flag(Column column)
        {
            var raw = Text(column);
            if (raw.Length == 0)
            {
                return false;
            }

            switch (LanguageCatalog.Normalize(raw))
            {
                case "oui" or "o" or "yes" or "y" or "true" or "vrai" or "1" or "x" or "hit" or "premium":
                    return true;
                case "non" or "n" or "no" or "false" or "faux" or "0":
                    return false;
                default:
                    Warn(column, raw, "non");
                    return false;
            }
        }

        public decimal Rating(Column column)
        {
            var raw = Text(column);
            if (raw.Length == 0)
            {
                return 3;
            }

            if (decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                && value >= 1 && value <= 5)
            {
                return value;
            }

            Warn(column, raw, "3");
            return 3;
        }

        public TimeSpan Duration()
        {
            var raw = Text(Column.Duration);
            if (raw.Length == 0)
            {
                return DefaultDuration;
            }

            if (TryParseDuration(raw, out var duration))
            {
                return duration;
            }

            Warn(Column.Duration, raw, "03:00");
            return DefaultDuration;
        }

        private void Warn(Column column, string raw, string kept) =>
            warnings.Add(new ClipImportIssue(source.Line, $"{ColumnLabel(column)} « {raw} » non reconnu(e), « {kept} » retenu(e)."));
    }

    private sealed record CsvRecord(int Line, List<string> Fields);

    /// <summary>Lit les enregistrements CSV (guillemets, "" échappés, retours à la ligne dans un champ).</summary>
    private static List<CsvRecord> ReadRecords(string text, char separator)
    {
        var records = new List<CsvRecord>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (character == '\n')
                    {
                        line++;
                    }

                    field.Append(character);
                }

                continue;
            }

            if (character == '"' && string.IsNullOrWhiteSpace(field.ToString()))
            {
                field.Clear();
                inQuotes = true;
            }
            else if (character == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                fields.Add(field.ToString());
                field.Clear();
                records.Add(new CsvRecord(recordLine, fields));
                fields = [];
                line++;
                recordLine = line;
            }
            else
            {
                field.Append(character);
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add(new CsvRecord(recordLine, fields));
        }

        return records;
    }

    /// <summary>Choisit ; , ou tabulation d'après la première ligne (Excel FR écrit des ;).</summary>
    private static char DetectSeparator(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        var candidates = new[] { ';', ',', '\t' };
        var best = candidates.MaxBy(candidate => header.Count(character => character == candidate));
        return header.Contains(best) ? best : ';';
    }

    private static Dictionary<Column, int> MapHeader(IReadOnlyList<string> header)
    {
        var columns = new Dictionary<Column, int>();
        for (var index = 0; index < header.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(header[index]))
            {
                continue;
            }

            if (HeaderAliases.TryGetValue(LanguageCatalog.Normalize(header[index]), out var column))
            {
                columns.TryAdd(column, index);
            }
        }

        return columns;
    }

    private static Dictionary<string, Column> BuildAliases()
    {
        var aliases = new Dictionary<string, Column>(StringComparer.Ordinal);
        void Add(Column column, params string[] names)
        {
            foreach (var name in names)
            {
                aliases[LanguageCatalog.Normalize(name)] = column;
            }
        }

        Add(Column.FilePath, "Fichier", "Chemin", "Chemin du fichier", "Chemin fichier", "Fichier vidéo", "Vidéo", "Path", "File", "FilePath", "Media");
        Add(Column.Title, "Titre", "Title", "Titre du clip");
        Add(Column.Artist, "Artiste", "Artist", "Interprète", "Chanteur");
        Add(Column.Year, "Année", "Annee", "Year", "Année de sortie", "Sortie");
        Add(Column.Language, "Langue", "Language", "Langue du clip");
        Add(Column.Genre, "Genre", "Genre musical", "Style");
        Add(Column.Theme, "Thème", "Theme", "Thématique");
        Add(Column.Audience, "Cible", "Public", "Audience", "Public cible");
        Add(Column.Origin, "Origine", "Origin", "Nationalité", "Burkinabè");
        Add(Column.Duration, "DuréeSecondes", "DureeSecondes", "Durée", "Duree", "Duration", "Durée (s)", "Durée secondes");
        Add(Column.OriginPlace, "LieuOrigine", "Lieu d'origine", "Pays", "Pays d'origine");
        Add(Column.FilmingLocation, "LieuTournage", "Lieu de tournage", "Tournage");
        Add(Column.Quality, "Qualité", "Qualite", "Quality", "Résolution");
        Add(Column.Format, "Format", "Extension");
        Add(Column.Premium, "Premium", "Hit", "Premium / Hit");
        Add(Column.CommitteeRating, "NoteComité", "NoteComite", "Note comité", "Comité");
        Add(Column.Popularity, "Popularité", "Popularite", "Popularity");
        Add(Column.Social, "Réseaux", "Reseaux", "Réseaux sociaux", "Social");
        Add(Column.Thumbnail, "Vignette", "Miniature", "Thumbnail", "Image");
        return aliases;
    }

    private static object? Synonym(string key) => key switch
    {
        "1080p" or "720p" or "fullhd" => VideoQuality.Hd,
        "uhd" or "2160p" or "4kuhd" => VideoQuality.Uhd4K,
        "jeune" or "jeunes" or "enfants" => Audience.Jeunesse,
        "adulte" => Audience.Adultes,
        "tous" or "toutpublic" or "tout" => Audience.Famille,
        "afro" => MusicalGenre.AfroPop,
        "rap" or "hiphop" or "rnb" => MusicalGenre.Urbain,
        "religieux" or "chretien" => MusicalGenre.Gospel,
        _ => null
    };

    private static string ColumnLabel(Column column) => column switch
    {
        Column.Year => "Année",
        Column.Genre => "Genre",
        Column.Theme => "Thème",
        Column.Audience => "Cible",
        Column.Origin => "Origine",
        Column.Duration => "Durée",
        Column.Quality => "Qualité",
        Column.Premium => "Premium",
        Column.CommitteeRating => "Note comité",
        Column.Popularity => "Popularité",
        Column.Social => "Réseaux",
        _ => column.ToString()
    };

    /// <summary>Durée en secondes (« 245 », « 245,5 ») ou en « mm:ss » / « hh:mm:ss », entre 1 s et 4 h.</summary>
    private static bool TryParseDuration(string raw, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (raw.Contains(':'))
        {
            var parts = raw.Split(':');
            if (parts.Length is < 2 or > 3
                || !parts.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                return false;
            }

            var numbers = parts.Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
            var seconds = numbers.Length == 2
                ? numbers[0] * 60 + numbers[1]
                : numbers[0] * 3600 + numbers[1] * 60 + numbers[2];
            duration = TimeSpan.FromSeconds(seconds);
        }
        else if (decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            duration = TimeSpan.FromSeconds((double)Math.Round(value));
        }

        return duration >= TimeSpan.FromSeconds(1) && duration <= TimeSpan.FromHours(4);
    }

    /// <summary>Clé de comparaison des chemins : séparateurs unifiés, casse ignorée.</summary>
    private static string PathKey(string? path) =>
        (path ?? string.Empty).Trim().Replace('\\', '/').ToLowerInvariant();

    /// <summary>Nom du fichier sans dossier ni extension, quel que soit le séparateur (Windows ou macOS).</summary>
    private static string FileStem(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return (dot > 0 ? name[..dot] : name).Trim();
    }

    private static string FileExtension(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..].Trim() : string.Empty;
    }

    private static string FileName(string path) => path[(path.LastIndexOfAny(['/', '\\']) + 1)..];

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string Truncate(string value) =>
        value.Length > MaxTextLength ? value[..MaxTextLength] : value;

    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;
        return text.IndexOfAny([';', '"', '\r', '\n']) >= 0
            ? $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\""
            : text;
    }
}
