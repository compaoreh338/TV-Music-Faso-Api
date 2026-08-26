using System.Globalization;
using System.Text;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class BbdaReportService
{
    public string ToMonthlyCsv(IReadOnlyList<BroadcastLogEntry> entries, int year, int month) =>
        ToPeriodCsv(entries, $"{month:00}/{year}");

    public string ToPeriodCsv(IReadOnlyList<BroadcastLogEntry> entries, string periodLabel)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Rapport de diffusion - TV-Music Faso / Filinfo Group");
        builder.AppendLine("Organisme;BBDA - Bureau Burkinabe du Droit d'Auteur");
        builder.AppendLine($"Periode;{periodLabel}");
        builder.AppendLine($"Passages;{entries.Count}");
        builder.AppendLine($"DureeTotale;{FormatDuration(entries.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Duration))}");
        builder.AppendLine();
        builder.AppendLine("Date;Heure;Tranche;Titre;Interprete;Duree;Langue;Genre;Origine;Fichier");

        foreach (var entry in entries.OrderBy(item => item.Date).ThenBy(item => item.StartTime))
        {
            builder.AppendLine(string.Join(';',
                entry.Date.ToString("yyyy-MM-dd"),
                entry.StartTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                entry.Slot.ToDisplayName(),
                Quote(entry.Title),
                Quote(entry.Artist),
                entry.DurationLabel,
                entry.Language.ToDisplayName(),
                entry.Genre.ToDisplayName(),
                ExportText.Origin(entry.IsBurkinabe),
                entry.FilePath));
        }

        var grouped = entries
            .GroupBy(entry => (entry.Artist, entry.Title))
            .OrderByDescending(group => group.Count());
        builder.AppendLine();
        builder.AppendLine("Synthese par oeuvre;Passages;DureeCumulee");
        foreach (var group in grouped)
        {
            builder.AppendLine(string.Join(';',
                Quote($"{group.Key.Artist} - {group.Key.Title}"),
                group.Count(),
                FormatDuration(group.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Duration))));
        }

        return builder.ToString();
    }

    private static string FormatDuration(TimeSpan duration) =>
        $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
