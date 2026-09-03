using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class BbdaWorkSummary
{
    public required string Title { get; init; }

    public required string Artist { get; init; }

    public required string Genre { get; init; }

    public TimeSpan Duration { get; init; }

    public int PlayCount { get; init; }

    public string DurationLabel => FormatDuration(Duration);

    public static string FormatDuration(TimeSpan duration)
    {
        var totalSeconds = Math.Max(0, (int)Math.Round(duration.TotalSeconds));
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return $"{hours:00}:{minutes:00}:{seconds:00}";
    }
}

public sealed class BbdaReportService
{
    private const int RowsPerPage = 10;
    private static readonly string BbdaGreen = "#1B7A3D";
    private static readonly string BbdaRed = "#C8102E";
    private static bool _licenseConfigured;

    public string ToMonthlyCsv(IReadOnlyList<BroadcastLogEntry> entries, int year, int month) =>
        ToPeriodCsv(entries, $"{month:00}/{year}");

    public string ToPeriodCsv(IReadOnlyList<BroadcastLogEntry> entries, string periodLabel)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Rapport de diffusion - TV-Music Faso / Filinfo Group");
        builder.AppendLine("Organisme;BBDA - Bureau Burkinabe du Droit d'Auteur");
        builder.AppendLine($"Periode;{periodLabel}");
        builder.AppendLine($"Passages;{entries.Count}");
        builder.AppendLine($"DureeTotale;{BbdaWorkSummary.FormatDuration(entries.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Duration))}");
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
                BbdaWorkSummary.FormatDuration(entry.Duration),
                entry.Language.ToDisplayName(),
                entry.Genre.ToDisplayName(),
                ExportText.Origin(entry.IsBurkinabe),
                entry.FilePath));
        }

        builder.AppendLine();
        builder.AppendLine("Synthese par oeuvre;Passages;DureeCumulee");
        foreach (var work in SummarizeByWork(entries))
        {
            builder.AppendLine(string.Join(';',
                Quote($"{work.Artist} - {work.Title}"),
                work.PlayCount,
                work.DurationLabel));
        }

        return builder.ToString();
    }

    public byte[] ToPeriodPdf(IReadOnlyList<BroadcastLogEntry> entries, string periodLabel)
    {
        _ = periodLabel;
        EnsureLicense();
        var works = SummarizeByWork(entries);
        var pages = Chunk(works, RowsPerPage).ToList();
        if (pages.Count == 0)
        {
            pages.Add([]);
        }

        var logo = LoadLogoBytes();
        var document = Document.Create(container =>
        {
            foreach (var rows in pages)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.MarginHorizontal(26);
                    page.MarginVertical(18);
                    page.DefaultTextStyle(text => text
                        .FontSize(9)
                        .FontColor(Colors.Black)
                        .FontFamily(Fonts.Arial));

                    page.Content().Column(column =>
                    {
                        column.Spacing(10);
                        column.Item().Element(header => ComposeHeader(header, logo));
                        column.Item().Element(ComposeDottedFields);
                        column.Item().Element(table => ComposeTable(table, rows));
                        column.Item().Element(ComposeFooter);
                    });
                });
            }
        });

        return document.GeneratePdf();
    }

    public IReadOnlyList<BbdaWorkSummary> SummarizeByWork(IReadOnlyList<BroadcastLogEntry> entries) =>
        entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Title) || !string.IsNullOrWhiteSpace(entry.Artist))
            .GroupBy(entry => (
                ArtistKey: entry.Artist.Trim().ToUpperInvariant(),
                TitleKey: entry.Title.Trim().ToUpperInvariant()))
            .Select(group =>
            {
                var first = group.First();
                return new BbdaWorkSummary
                {
                    Title = first.Title.Trim(),
                    Artist = first.Artist.Trim(),
                    Genre = "Musique",
                    Duration = group.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Duration),
                    PlayCount = group.Count()
                };
            })
            .OrderBy(work => work.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(work => work.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static void ComposeHeader(IContainer container, byte[]? logo)
    {
        container.Row(row =>
        {
            row.ConstantItem(160).Column(left =>
            {
                if (logo is { Length: > 0 })
                {
                    left.Item().Height(56).Image(logo).FitArea();
                }

                left.Item().PaddingTop(6).Text("SECRETARIAT GENERAL")
                    .FontSize(7).FontColor(BbdaGreen).Bold();
                left.Item().PaddingVertical(1).LineHorizontal(0.6f).LineColor(BbdaGreen);
                left.Item().Text("DIRECTION DE LA REPARTITION DES DROITS")
                    .FontSize(7).FontColor(BbdaGreen);
                left.Item().PaddingVertical(1).LineHorizontal(0.6f).LineColor(BbdaGreen);
                left.Item().Text("SERVICE GESTION DES PROGRAMMES")
                    .FontSize(7).FontColor(BbdaGreen);
            });

            row.RelativeItem().PaddingLeft(16).AlignMiddle().Column(center =>
            {
                center.Item().AlignCenter().Text("RELEVE DE PROGRAMME")
                    .FontSize(15).Bold().FontColor(BbdaGreen);
                center.Item().PaddingTop(8).Border(1.6f).BorderColor(BbdaGreen).PaddingVertical(10).PaddingHorizontal(12)
                    .AlignCenter().Column(box =>
                    {
                        box.Item().AlignCenter().Text("SERVICE DES DROITS D'AUTEURS")
                            .FontSize(12).Bold().FontColor(BbdaGreen);
                        box.Item().AlignCenter().Text("TELEVISION")
                            .FontSize(12).Bold().FontColor(BbdaGreen);
                    });
            });
        });
    }

    private static void ComposeDottedFields(IContainer container)
    {
        // Alignement du formulaire papier :
        // EMISSION (gauche) | Station / Relevé (centre, décalés) | DIFFUSION (droite)
        container.Column(section =>
        {
            section.Item().Text("EMISSION :").Bold().FontColor(BbdaGreen).FontSize(10);

            section.Item().PaddingTop(4).Row(row =>
            {
                // Espace sous EMISSION → Station démarre nettement plus à droite
                row.ConstantItem(95);

                row.RelativeItem(1.35f).Column(center =>
                {
                    center.Spacing(5);
                    center.Item().Text(text =>
                    {
                        text.Span("Station : ").Bold().FontSize(9);
                        text.Span(Dots(58)).FontColor(Colors.Grey.Medium);
                    });
                    center.Item().Text(text =>
                    {
                        text.Span("Relevé établi par ").Bold().FontSize(9);
                        text.Span(Dots(46)).FontColor(Colors.Grey.Medium);
                    });
                    center.Item().PaddingLeft(2).Text("(Nom-Qualité - Signature)")
                        .FontSize(7).Italic().FontColor(Colors.Grey.Darken1);
                });

                row.ConstantItem(24);

                row.RelativeItem(1f).Column(right =>
                {
                    right.Spacing(4);
                    right.Item().Text("DIFFUSION :").Bold().FontColor(BbdaGreen).FontSize(10);
                    right.Item().Text("Date :").Bold().FontSize(9);
                    right.Item().Text(text =>
                    {
                        text.Span("Horaire Début : ").Bold().FontSize(9);
                        text.Span(Dots(28)).FontColor(Colors.Grey.Medium);
                    });
                    right.Item().Text(text =>
                    {
                        text.Span("Horaire Fin / ").Bold().FontSize(9);
                        text.Span(Dots(30)).FontColor(Colors.Grey.Medium);
                    });
                    right.Item().Text(text =>
                    {
                        text.Span("Feuille N° : ").Bold().FontSize(9);
                        text.Span("...../............").FontColor(Colors.Grey.Medium);
                    });
                });
            });
        });
    }

    private static void ComposeTable(IContainer container, IReadOnlyList<BbdaWorkSummary> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(22);
                columns.RelativeColumn(3.1f);
                columns.ConstantColumn(68);
                columns.RelativeColumn(2.1f);
                columns.RelativeColumn(2.5f);
                columns.ConstantColumn(68);
            });

            table.Header(header =>
            {
                header.Cell().Element(c => HeaderCell(c, string.Empty));
                header.Cell().Element(c => HeaderCell(c, "TITRE DES OEUVRES"));
                header.Cell().Element(c => HeaderCell(c, "BBDA\n(Ne rien écrire)"));
                header.Cell().Element(c => HeaderCell(c, "GENRE\n(Musique-Littérature-Théâtre-Films…)"));
                header.Cell().Element(c => HeaderCell(c, "NOM DE L'AUTEUR PRINCIPAL"));
                header.Cell().Element(c => HeaderCell(c, "DUREE"));
            });

            for (var i = 0; i < RowsPerPage; i++)
            {
                var work = i < rows.Count ? rows[i] : null;
                table.Cell().Element(c => BodyCell(c, (i + 1).ToString(), alignCenter: true));
                table.Cell().Element(c => BodyCell(c, work?.Title ?? string.Empty));
                table.Cell().Element(c => BodyCell(c, string.Empty));
                table.Cell().Element(c => BodyCell(c, work?.Genre ?? string.Empty));
                table.Cell().Element(c => BodyCell(c, work?.Artist ?? string.Empty));
                table.Cell().Element(c => BodyCell(c, work?.DurationLabel ?? string.Empty, alignCenter: true));
            }
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Column(column =>
        {
            column.Spacing(2);
            column.Item().Text("N.B.").Bold().FontSize(8).FontColor(BbdaRed);
            column.Item().Text("1. Il est recommandé pour chaque changement d'animateur d'utiliser un nouveau relevé.")
                .FontSize(7).FontColor(Colors.Grey.Darken2);
            column.Item().Text("2. Il est souhaitable que chaque changement d'émission entraîne l'utilisation d'un nouveau relevé.")
                .FontSize(7).FontColor(Colors.Grey.Darken2);
            column.Item().Text("3. Contact BBDA : Tél. 25 30 22 23  —  Adresse électronique : bbda@fasonet.bf")
                .FontSize(7).FontColor(Colors.Grey.Darken2);
            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().AlignCenter().Text("01 BP 3926 Ouagadougou 01")
                    .FontSize(8).FontColor(BbdaGreen);
                row.ConstantItem(170).AlignRight().Text("Tournez S'il Vous Plait.")
                    .FontSize(8).Italic().FontColor(BbdaGreen);
            });
        });
    }

    private static void HeaderCell(IContainer cell, string text) =>
        cell.Border(1).BorderColor(BbdaGreen).Background("#E8F5EC")
            .Padding(3).AlignMiddle().AlignCenter()
            .Text(text).FontSize(6.5f).Bold().FontColor(BbdaGreen);

    private static void BodyCell(IContainer cell, string text, bool alignCenter = false)
    {
        var box = cell.Border(1).BorderColor(Colors.Grey.Medium).MinHeight(26).Padding(3).AlignMiddle();
        if (alignCenter)
        {
            box.AlignCenter().Text(text).FontSize(8);
        }
        else
        {
            box.Text(text).FontSize(8);
        }
    }

    private static IEnumerable<List<BbdaWorkSummary>> Chunk(IReadOnlyList<BbdaWorkSummary> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }

    private static string Dots(int count) => new('.', count);

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"")}\"";

    private static void EnsureLicense()
    {
        if (_licenseConfigured)
        {
            return;
        }

        QuestPDF.Settings.License = LicenseType.Community;
        _licenseConfigured = true;
    }

    private static byte[]? LoadLogoBytes()
    {
        var assembly = typeof(BbdaReportService).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("bbda-logo.jpg", StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
