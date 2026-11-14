using System.Diagnostics;
using System.Text;

namespace TVMusicFaso.Core.Services;

/// <summary>Recherche et lancement des programmes externes (ffmpeg, ffprobe, yt-dlp), sans passer par un shell.</summary>
public static class ExternalTool
{
    public static string ExecutableName(string program) =>
        OperatingSystem.IsWindows() ? program + ".exe" : program;

    /// <summary>
    /// Premier exécutable trouvé : chemin des réglages (fichier ou dossier), dossier de l'app, PATH,
    /// puis emplacements courants (Homebrew, /usr/local, C:\Tools, Program Files).
    /// </summary>
    public static string? Find(string program, string? configuredPath, params string[] extraFolders)
    {
        foreach (var candidate in Candidates(program, configuredPath, extraFolders))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static IEnumerable<string> Candidates(string program, string? configuredPath, params string[] extraFolders)
    {
        var name = ExecutableName(program);
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var trimmed = configuredPath.Trim();
            yield return Directory.Exists(trimmed) ? Path.Combine(trimmed, name) : trimmed;
        }

        yield return Path.Combine(AppContext.BaseDirectory, name);
        yield return Path.Combine(AppContext.BaseDirectory, program, name);
        foreach (var folder in extraFolders)
        {
            yield return Path.Combine(folder, name);
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return Path.Combine(folder.Trim('"'), name);
        }

        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(@"C:\Tools", program, name);
            yield return Path.Combine(@"C:\Tools", name);
            yield return Path.Combine($@"C:\{program}\bin", name);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), program, "bin", name);
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), program, name);
        }
        else
        {
            yield return Path.Combine("/opt/homebrew/bin", name);
            yield return Path.Combine("/usr/local/bin", name);
            yield return Path.Combine("/usr/bin", name);
        }
    }

    /// <summary>
    /// Lance le programme avec ses arguments un par un (aucune interprétation par un shell) et renvoie
    /// le code de sortie. Une annulation tue le processus et ses enfants.
    /// </summary>
    public static async Task<int> RunAsync(
        string program,
        IEnumerable<string> arguments,
        string? workingDirectory,
        Action<string> onOutput,
        Action<string> onError,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(program);
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            start.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutput(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onError(e.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Impossible de lancer {Path.GetFileName(program)}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Déjà terminé.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Accès refusé ou processus en cours de fermeture : l'annulation reste signalée.
            }

            throw;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    /// <summary>Garde les dernières lignes non vides d'une sortie, pour les messages d'erreur.</summary>
    public sealed class Tail(int capacity)
    {
        private readonly Queue<string> _lines = new();

        public void Add(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            lock (_lines)
            {
                _lines.Enqueue(line.Trim());
                while (_lines.Count > capacity)
                {
                    _lines.Dequeue();
                }
            }
        }

        public override string ToString()
        {
            lock (_lines)
            {
                return string.Join(Environment.NewLine, _lines);
            }
        }
    }
}
