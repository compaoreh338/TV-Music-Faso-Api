using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Sources lues dans un fichier, et lignes écartées avec leur motif.</summary>
public sealed record SourceListReadResult(IReadOnlyList<MonitoredSource> Sources, IReadOnlyList<string> Errors);

/// <summary>
/// Import d'une liste de sources (M1) depuis Excel (.xlsx) ou CSV. Les colonnes sont reconnues par leur
/// en-tête : Nom, Plateforme, Lien, Propriétaire, Pays, Priorité, Fréquence, Actif, Contact.
/// </summary>
public static partial class SourceListImport
{
    public const int MaxRows = 5000;

    private const long MaxEntryBytes = 50L * 1024 * 1024;

    private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relations = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelations = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly Dictionary<string, string[]> HeaderAliases = new(StringComparer.Ordinal)
    {
        ["name"] = ["nom", "nom de la source", "source", "chaine", "chaine youtube", "name"],
        ["platform"] = ["plateforme", "platform", "type", "reseau"],
        ["url"] = ["lien", "url", "adresse", "link", "lien de la source"],
        ["owner"] = ["proprietaire", "artiste", "label", "owner", "artiste label"],
        ["country"] = ["pays", "country", "origine"],
        ["priority"] = ["priorite", "priority"],
        ["frequency"] = ["frequence", "frequence de passage", "frequence h", "frequence heures", "frequence min", "frequence minutes", "intervalle"],
        ["active"] = ["actif", "active", "statut", "etat"],
        ["contact"] = ["contact", "courriel", "email", "e mail", "mail"]
    };

    /// <summary>Lit le fichier selon son extension (.xlsx, .csv ou .txt).</summary>
    public static SourceListReadResult Read(string fileName, Stream stream, int defaultIntervalMinutes)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        List<string[]> rows;
        try
        {
            rows = extension switch
            {
                ".xlsx" => ReadXlsx(stream),
                ".csv" or ".txt" => ReadCsv(stream),
                ".xls" => throw new InvalidOperationException("Ancien format Excel (.xls) : enregistrez le fichier en .xlsx ou en CSV."),
                _ => throw new InvalidOperationException("Format non pris en charge : choisissez un fichier .xlsx ou .csv.")
            };
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException)
        {
            throw new InvalidOperationException("Fichier Excel illisible ou abîmé.", exception);
        }

        return Map(rows, defaultIntervalMinutes);
    }

    /// <summary>Transforme les lignes (la première = en-têtes) en sources.</summary>
    public static SourceListReadResult Map(IReadOnlyList<string[]> rows, int defaultIntervalMinutes)
    {
        var errors = new List<string>();
        var sources = new List<MonitoredSource>();
        var headerIndex = -1;
        Dictionary<string, int> columns = [];
        for (var index = 0; index < rows.Count && index < 10; index++)
        {
            columns = MatchHeaders(rows[index]);
            if (columns.ContainsKey("url") || columns.ContainsKey("name"))
            {
                headerIndex = index;
                break;
            }
        }

        if (headerIndex < 0 || !columns.ContainsKey("url"))
        {
            return new SourceListReadResult([], ["Colonne « Lien » introuvable : la première ligne doit contenir les en-têtes (Nom, Lien, Pays…)."]);
        }

        var frequencyInMinutes = columns.TryGetValue("frequency", out var frequencyColumn)
            && Normalize(rows[headerIndex][frequencyColumn]).Contains("min", StringComparison.Ordinal);
        for (var index = headerIndex + 1; index < rows.Count && sources.Count < MaxRows; index++)
        {
            var row = rows[index];
            string Cell(string key) => columns.TryGetValue(key, out var column) && column < row.Length ? row[column].Trim() : string.Empty;
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var url = Cell("url");
            var source = new MonitoredSource
            {
                Url = url,
                Name = Cell("name"),
                Owner = Cell("owner"),
                Country = Cell("country") is { Length: > 0 } country ? country : "Burkina Faso",
                ContactEmail = Cell("contact"),
                Priority = ParsePriority(Cell("priority")),
                CheckIntervalMinutes = ParseFrequency(Cell("frequency"), frequencyInMinutes, defaultIntervalMinutes),
                IsActive = ParseActive(Cell("active")),
                Platform = ParsePlatform(Cell("platform"), url)
            };
            if (string.IsNullOrWhiteSpace(source.Name))
            {
                source.Name = !string.IsNullOrWhiteSpace(source.Owner) ? source.Owner : url;
            }

            var problems = source.Validate();
            if (problems.Count > 0)
            {
                errors.Add($"Ligne {index + 1} : {string.Join(" ", problems)}");
                continue;
            }

            sources.Add(source);
        }

        return new SourceListReadResult(sources, errors);
    }

    public static SourcePlatform ParsePlatform(string? text, string? url)
    {
        var value = Normalize(text);
        if (value.Contains("playlist", StringComparison.Ordinal) || value.Contains("liste", StringComparison.Ordinal))
        {
            return SourcePlatform.YouTubePlaylist;
        }

        SourcePlatform? named = value switch
        {
            _ when value.Contains("youtube", StringComparison.Ordinal) => SourcePlatform.YouTubeChannel,
            _ when value.Contains("boomplay", StringComparison.Ordinal) => SourcePlatform.Boomplay,
            _ when value.Contains("tiktok", StringComparison.Ordinal) => SourcePlatform.TikTok,
            _ when value.Contains("facebook", StringComparison.Ordinal) => SourcePlatform.Facebook,
            _ when value.Contains("audiomack", StringComparison.Ordinal) => SourcePlatform.Audiomack,
            _ when value.Contains("depot", StringComparison.Ordinal) || value.Contains("artiste", StringComparison.Ordinal) => SourcePlatform.ArtistUpload,
            _ => null
        };
        if (YouTubeFeedReader.Classify(url) is { } youtube && named is null or SourcePlatform.YouTubeChannel)
        {
            return youtube.Platform;
        }

        if (named is not null)
        {
            return named.Value;
        }

        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : string.Empty;
        return host switch
        {
            _ when host.Contains("boomplay", StringComparison.Ordinal) => SourcePlatform.Boomplay,
            _ when host.Contains("tiktok", StringComparison.Ordinal) => SourcePlatform.TikTok,
            _ when host.Contains("facebook", StringComparison.Ordinal) || host.Contains("fb.", StringComparison.Ordinal) => SourcePlatform.Facebook,
            _ when host.Contains("audiomack", StringComparison.Ordinal) => SourcePlatform.Audiomack,
            _ when host.Contains("youtube", StringComparison.Ordinal) || host.Contains("youtu.be", StringComparison.Ordinal) => SourcePlatform.YouTubeChannel,
            _ => SourcePlatform.Other
        };
    }

    private static int ParsePriority(string text)
    {
        var value = Normalize(text);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return Math.Clamp(number, 1, 5);
        }

        return value switch
        {
            _ when value.StartsWith("tres haute", StringComparison.Ordinal) => 5,
            _ when value.StartsWith("haute", StringComparison.Ordinal) => 4,
            _ when value.StartsWith("basse", StringComparison.Ordinal) => 2,
            _ when value.StartsWith("tres basse", StringComparison.Ordinal) => 1,
            _ => 3
        };
    }

    private static int ParseFrequency(string text, bool minutesColumn, int fallback)
    {
        var value = Normalize(text);
        if (value.Length == 0)
        {
            return fallback;
        }

        if (value.Contains("jour", StringComparison.Ordinal) || value.Contains("quotidien", StringComparison.Ordinal))
        {
            return 24 * 60;
        }

        if (value.Contains("semaine", StringComparison.Ordinal) || value.Contains("hebdo", StringComparison.Ordinal))
        {
            return 7 * 24 * 60;
        }

        var match = NumberPattern().Match(value);
        if (!match.Success || !double.TryParse(match.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            return fallback;
        }

        var minutes = value.Contains("min", StringComparison.Ordinal) || (minutesColumn && !value.Contains('h'))
            ? amount
            : amount * 60;
        return (int)Math.Clamp(Math.Round(minutes), MonitoredSource.MinCheckIntervalMinutes, MonitoredSource.MaxCheckIntervalMinutes);
    }

    private static bool ParseActive(string text) =>
        Normalize(text) is not ("non" or "0" or "faux" or "false" or "no" or "inactif" or "inactive" or "desactive" or "x non");

    private static Dictionary<string, int> MatchHeaders(string[] row)
    {
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var column = 0; column < row.Length; column++)
        {
            var header = Normalize(row[column]);
            if (header.Length == 0)
            {
                continue;
            }

            foreach (var (key, aliases) in HeaderAliases)
            {
                if (!columns.ContainsKey(key) && aliases.Contains(header))
                {
                    columns[key] = column;
                    break;
                }
            }
        }

        return columns;
    }

    private static string Normalize(string? value) => PlatformTitleParser.Key(value);

    private static List<string[]> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var firstLine = text.Split('\n', 2)[0];
        var separator = firstLine.Count(c => c == ';') >= firstLine.Count(c => c == ',') ? ';' : ',';
        var rows = new List<string[]>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                }
                else
                {
                    cell.Append(character);
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case var c when c == separator:
                    cells.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    rows.Add([.. cells]);
                    cells.Clear();
                    if (rows.Count > MaxRows + 10)
                    {
                        return rows;
                    }

                    break;
                default:
                    cell.Append(character);
                    break;
            }
        }

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            rows.Add([.. cells]);
        }

        return rows;
    }

    /// <summary>Première feuille d'un classeur .xlsx (textes partagés, textes en ligne, nombres).</summary>
    private static List<string[]> ReadXlsx(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var shared = new List<string>();
        if (archive.GetEntry("xl/sharedStrings.xml") is { } sharedEntry)
        {
            var document = Load(sharedEntry);
            shared.AddRange(document.Descendants(Sheet + "si").Select(si => string.Concat(si.Descendants(Sheet + "t").Select(t => t.Value))));
        }

        var sheetEntry = FirstSheet(archive) ?? throw new InvalidOperationException("Aucune feuille dans le classeur.");
        var sheet = Load(sheetEntry);
        var rows = new List<string[]>();
        foreach (var row in sheet.Descendants(Sheet + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            var position = 0;
            foreach (var cell in row.Elements(Sheet + "c"))
            {
                var reference = cell.Attribute("r")?.Value;
                position = reference is null ? position : ColumnIndex(reference);
                var type = cell.Attribute("t")?.Value;
                var raw = cell.Element(Sheet + "v")?.Value ?? string.Empty;
                var value = type switch
                {
                    "s" when int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < shared.Count => shared[index],
                    "inlineStr" => string.Concat(cell.Descendants(Sheet + "t").Select(t => t.Value)),
                    "b" => raw == "1" ? "oui" : "non",
                    _ => raw
                };
                cells[position] = value;
                position++;
            }

            if (cells.Count == 0)
            {
                continue;
            }

            var width = cells.Keys.Max() + 1;
            var values = new string[Math.Min(width, 64)];
            for (var column = 0; column < values.Length; column++)
            {
                values[column] = cells.TryGetValue(column, out var text) ? text : string.Empty;
            }

            rows.Add(values);
            if (rows.Count > MaxRows + 10)
            {
                break;
            }
        }

        return rows;
    }

    private static ZipArchiveEntry? FirstSheet(ZipArchive archive)
    {
        if (archive.GetEntry("xl/workbook.xml") is { } workbookEntry && archive.GetEntry("xl/_rels/workbook.xml.rels") is { } relsEntry)
        {
            var firstId = Load(workbookEntry).Descendants(Sheet + "sheet").FirstOrDefault()?.Attribute(Relations + "id")?.Value;
            var target = Load(relsEntry).Descendants(PackageRelations + "Relationship")
                .FirstOrDefault(rel => rel.Attribute("Id")?.Value == firstId)?.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(target))
            {
                var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                if (archive.GetEntry(path) is { } entry)
                {
                    return entry;
                }
            }
        }

        return archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? archive.Entries.FirstOrDefault(entry => entry.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal));
    }

    private static XDocument Load(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxEntryBytes)
        {
            throw new InvalidOperationException("Classeur trop volumineux pour une liste de sources.");
        }

        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var character in reference)
        {
            if (!char.IsAsciiLetter(character))
            {
                break;
            }

            index = index * 26 + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return Math.Max(0, index - 1);
    }

    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex NumberPattern();
}
