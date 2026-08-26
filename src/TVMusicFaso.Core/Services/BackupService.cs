using System.Diagnostics;

namespace TVMusicFaso.Core.Services;

public sealed class BackupService
{
    public const int RetentionCount = 14;

    public string BackupNow()
    {
        Directory.CreateDirectory(AppPaths.BackupDirectory);
        var destination = Path.Combine(
            AppPaths.BackupDirectory,
            $"tvmusicfaso-{DateTime.Now:yyyyMMdd-HHmmss}.sql");

        var start = new ProcessStartInfo("docker", "exec tvmusicfaso-postgres pg_dump -U tvmusic tvmusicfaso")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Impossible de lancer docker pour pg_dump.");
        var sql = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(sql))
        {
            throw new InvalidOperationException(
                "Sauvegarde PostgreSQL impossible. Vérifiez que Docker tourne (container tvmusicfaso-postgres).\n" +
                error);
        }

        File.WriteAllText(destination, sql);
        Prune();
        return destination;
    }

    public string? LastBackupPath =>
        Directory.EnumerateFiles(AppPaths.BackupDirectory, "tvmusicfaso-*.sql")
            .Concat(Directory.EnumerateFiles(AppPaths.BackupDirectory, "mediatheque-*.db"))
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .FirstOrDefault();

    public string BackupIfDue(TimeSpan maxAge)
    {
        var last = LastBackupPath;
        if (last is not null && DateTime.Now - File.GetCreationTime(last) < maxAge)
        {
            return last;
        }

        try
        {
            return BackupNow();
        }
        catch (Exception)
        {
            return last ?? string.Empty;
        }
    }

    private static void Prune()
    {
        var files = Directory.EnumerateFiles(AppPaths.BackupDirectory, "tvmusicfaso-*.sql")
            .OrderByDescending(path => path, StringComparer.Ordinal)
            .Skip(RetentionCount)
            .ToList();
        foreach (var file in files)
        {
            File.Delete(file);
        }
    }
}
