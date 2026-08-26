using System.Globalization;
using System.Text;
using System.Xml.Linq;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class PlaylistExportService
{
    private readonly PlayoutPathMapper _paths;

    public PlaylistExportService(PlayoutPathMapper? paths = null)
    {
        _paths = paths ?? new PlayoutPathMapper();
    }

    public string ToCsv(DaySchedule schedule)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Date;Tranche;HeureDebut;Position;Artiste;Titre;Langue;Genre;Origine;Duree;Fichier");

        foreach (var (item, start) in Flatten(schedule))
        {
            builder.AppendLine(string.Join(';',
                schedule.Date.ToString("yyyy-MM-dd"),
                item.Playlist.Slot.ToDisplayName(),
                start.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                item.Item.Position,
                Quote(item.Item.Clip.Artist),
                Quote(item.Item.Clip.Title),
                item.Item.Clip.Language.ToDisplayName(),
                item.Item.Clip.Genre.ToDisplayName(),
                item.Item.Clip.OriginLabel,
                item.Item.Clip.DurationLabel,
                FileOrFallback(item.Item.Clip)));
        }

        return builder.ToString();
    }

    public string ToM3u(DaySchedule schedule)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#EXTM3U");
        builder.AppendLine("#EXTENC:UTF-8");
        builder.AppendLine("# TV-Music Faso / OBS / serveurs de diffusion");

        foreach (var playlist in schedule.Playlists)
        {
            builder.AppendLine($"# TV-Music Faso — {playlist.Slot.ToDisplayName()}");
            foreach (var item in playlist.Items)
            {
                builder.AppendLine($"#EXTINF:{(int)item.Clip.Duration.TotalSeconds},{item.Clip.Artist} - {item.Clip.Title}");
                builder.AppendLine(FileOrFallback(item.Clip));
            }
        }

        return builder.ToString();
    }

    public string ToMovieJayMpl(DaySchedule schedule)
    {
        var root = new XElement("MovieJayPlaylist",
            new XAttribute("channel", "TV-Music Faso"),
            new XAttribute("date", schedule.Date.ToString("yyyy-MM-dd")),
            new XAttribute("generated", DateTime.Now.ToString("o", CultureInfo.InvariantCulture)));

        var index = 1;
        foreach (var (item, start) in Flatten(schedule))
        {
            root.Add(new XElement("Item",
                new XAttribute("index", index++),
                new XAttribute("start", start.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
                new XAttribute("duration", item.Item.Clip.Duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)),
                new XAttribute("title", item.Item.Clip.Title),
                new XAttribute("artist", item.Item.Clip.Artist),
                new XAttribute("slot", item.Playlist.Slot.ToDisplayName()),
                new XAttribute("file", FileOrFallback(item.Item.Clip))));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root).ToString();
    }

    public string ToVmixXml(DaySchedule schedule)
    {
        var inputs = Flatten(schedule)
            .Select((entry, i) => new XElement("input",
                new XAttribute("number", i + 1),
                new XAttribute("type", "Video"),
                new XAttribute("title", $"{entry.Item.Item.Clip.Artist} - {entry.Item.Item.Clip.Title}"),
                new XAttribute("duration", (int)entry.Item.Item.Clip.Duration.TotalMilliseconds),
                FileOrFallback(entry.Item.Item.Clip)))
            .ToArray();

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("vmix",
                new XAttribute("channel", "TV-Music Faso"),
                new XAttribute("date", schedule.Date.ToString("yyyy-MM-dd")),
                new XElement("inputs", inputs)));

        return document.ToString();
    }

    private static IEnumerable<(FlattenedItem Item, TimeOnly Start)> Flatten(DaySchedule schedule)
    {
        foreach (var playlist in schedule.Playlists)
        {
            var cursor = TimeSlotInfo.For(playlist.Slot).Start;
            foreach (var item in playlist.Items)
            {
                yield return (new FlattenedItem(playlist, item), cursor);
                cursor = cursor.Add(item.Clip.Duration);
            }
        }
    }

    private string FileOrFallback(Clip clip) =>
        _paths.Map(clip.FilePath, $"{clip.Artist} - {clip.Title}.mp4");

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private readonly record struct FlattenedItem(Playlist Playlist, PlaylistItem Item);
}
