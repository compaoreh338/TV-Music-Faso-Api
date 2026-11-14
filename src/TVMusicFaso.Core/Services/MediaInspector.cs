using System.Globalization;
using System.Text;
using System.Text.Json;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Mesure EBU R128 du premier passage de loudnorm, réutilisée pour la normalisation.</summary>
public sealed record LoudnessMeasure(double IntegratedLufs, double TruePeakDb, double RangeLu, double ThresholdLufs, double OffsetLu);

/// <summary>Contrôle technique et mise au format de diffusion.</summary>
public interface ITechnicalInspector
{
    Task<(TechnicalReport Report, LoudnessMeasure? Loudness)> InspectAsync(string path, IngestSettings settings, CancellationToken cancellationToken);

    /// <summary>Copie au format de diffusion (MP4 H.264 / AAC 48 kHz, son normalisé) ; l'original n'est pas touché.</summary>
    Task ConvertAsync(string sourcePath, string outputPath, TechnicalReport report, LoudnessMeasure? loudness, IngestSettings settings, CancellationToken cancellationToken);
}

/// <summary>Contrôle technique par ffprobe et ffmpeg (M4).</summary>
public sealed class MediaInspector : ITechnicalInspector
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MeasureTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ConvertTimeout = TimeSpan.FromHours(2);

    public MediaInspector(string ffmpegPath, string ffprobePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ffmpegPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ffprobePath);
        FfmpegPath = ffmpegPath;
        FfprobePath = ffprobePath;
    }

    public string FfmpegPath { get; }

    public string FfprobePath { get; }

    /// <summary>ffmpeg et ffprobe côte à côte, cherchés comme pour la signature.</summary>
    public static MediaInspector? Locate(string? configuredPath)
    {
        foreach (var candidate in ExternalTool.Candidates("ffmpeg", configuredPath))
        {
            var probe = Path.Combine(Path.GetDirectoryName(candidate) ?? string.Empty, ExternalTool.ExecutableName("ffprobe"));
            if (File.Exists(candidate) && File.Exists(probe))
            {
                return new MediaInspector(candidate, probe);
            }
        }

        return null;
    }

    public async Task<(TechnicalReport Report, LoudnessMeasure? Loudness)> InspectAsync(string path, IngestSettings settings, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(settings);
        var output = new StringBuilder();
        var errors = new ExternalTool.Tail(6);
        int code;
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(ProbeTimeout);
            code = await ExternalTool.RunAsync(
                FfprobePath,
                ["-v", "error", "-print_format", "json", "-show_streams", "-show_format", path],
                null,
                line => output.AppendLine(line),
                errors.Add,
                timeout.Token).ConfigureAwait(false);
        }

        if (code != 0)
        {
            var report = new TechnicalReport();
            report.Issues.Add($"Fichier illisible : {errors}".Trim());
            return (report, null);
        }

        var parsed = ParseProbe(output.ToString());
        LoudnessMeasure? loudness = null;
        if (parsed.HasAudio)
        {
            loudness = await MeasureAsync(path, settings, cancellationToken).ConfigureAwait(false);
            parsed.IntegratedLufs = loudness?.IntegratedLufs;
            parsed.TruePeakDb = loudness?.TruePeakDb;
        }

        Evaluate(parsed, settings);
        return (parsed, loudness);
    }

    public async Task ConvertAsync(
        string sourcePath,
        string outputPath,
        TechnicalReport report,
        LoudnessMeasure? loudness,
        IngestSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
        var folder = Path.GetDirectoryName(outputPath) ?? throw new ArgumentException("Dossier de sortie invalide.", nameof(outputPath));
        Directory.CreateDirectory(folder);
        var partial = Path.Combine(folder, $".{Path.GetFileNameWithoutExtension(outputPath)}.partiel.mp4");
        var errors = new ExternalTool.Tail(12);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ConvertTimeout);
            var code = await ExternalTool.RunAsync(
                FfmpegPath,
                BuildConvertArguments(sourcePath, partial, report, loudness, settings),
                null,
                _ => { },
                errors.Add,
                timeout.Token).ConfigureAwait(false);
            if (code != 0)
            {
                throw new InvalidOperationException($"Conversion impossible (ffmpeg code {code}). {errors}".Trim());
            }

            File.Move(partial, outputPath, overwrite: true);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    /// <summary>Largeur maximale du fichier de diffusion (antenne HD 1080).</summary>
    public const int BroadcastMaxWidth = 1920;

    /// <summary>Hauteur maximale du fichier de diffusion (antenne HD 1080).</summary>
    public const int BroadcastMaxHeight = 1080;

    /// <summary>
    /// Commande de conversion : image recopiée si déjà en H.264 4:2:0 et au plus 1920×1080,
    /// sinon réencodée (et réduite au format d'antenne) ; son normalisé en deux passes.
    /// </summary>
    public static IReadOnlyList<string> BuildConvertArguments(
        string sourcePath,
        string outputPath,
        TechnicalReport report,
        LoudnessMeasure? loudness,
        IngestSettings settings)
    {
        var downscale = report.Width > BroadcastMaxWidth || report.Height > BroadcastMaxHeight;
        var copyVideo = !downscale
            && string.Equals(report.VideoCodec, "h264", StringComparison.OrdinalIgnoreCase)
            && string.Equals(report.PixelFormat, "yuv420p", StringComparison.OrdinalIgnoreCase);
        report.VideoReencoded = !copyVideo;
        report.Downscaled = downscale;
        var arguments = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", sourcePath, "-map", "0:v:0" };
        if (report.HasAudio)
        {
            arguments.AddRange(["-map", "0:a:0"]);
        }

        if (downscale)
        {
            arguments.AddRange(["-vf", $"scale=w={BroadcastMaxWidth}:h={BroadcastMaxHeight}:force_original_aspect_ratio=decrease:force_divisible_by=2:flags=lanczos"]);
        }

        arguments.AddRange(copyVideo
            ? ["-c:v", "copy"]
            : ["-c:v", "libx264", "-preset", "medium", "-crf", "18", "-profile:v", "high", "-pix_fmt", "yuv420p"]);

        if (report.HasAudio)
        {
            if (settings.NormalizeAudio)
            {
                arguments.AddRange(["-af", LoudnormFilter(settings, loudness)]);
                report.Normalized = true;
            }

            arguments.AddRange(["-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-ac", "2"]);
        }
        else
        {
            arguments.Add("-an");
        }

        arguments.AddRange(["-movflags", "+faststart", "-max_muxing_queue_size", "1024", outputPath]);
        return arguments;
    }

    /// <summary>Applique les seuils réglés : la liste des refus est vide si le fichier passe.</summary>
    public static void Evaluate(TechnicalReport report, IngestSettings settings)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(settings);
        if (report.Width <= 0 || report.Height <= 0)
        {
            report.Issues.Add("Aucune image vidéo dans le fichier.");
        }
        else if (report.Height < settings.MinHeight)
        {
            report.Issues.Add($"Résolution trop faible : {report.Width}×{report.Height} (minimum {settings.MinHeight} lignes).");
        }

        if (report.DurationSeconds < settings.MinDurationSeconds)
        {
            report.Issues.Add($"Trop court : {Format(report.DurationSeconds)} (minimum {Format(settings.MinDurationSeconds)}).");
        }
        else if (report.DurationSeconds > settings.MaxDurationSeconds)
        {
            report.Issues.Add($"Trop long : {Format(report.DurationSeconds)} (maximum {Format(settings.MaxDurationSeconds)}).");
        }

        if (report.VideoBitrateKbps > 0 && report.VideoBitrateKbps < settings.MinVideoBitrateKbps)
        {
            report.Issues.Add($"Débit vidéo trop faible : {report.VideoBitrateKbps} kb/s (minimum {settings.MinVideoBitrateKbps}).");
        }

        if (!report.HasAudio && settings.RequireAudio)
        {
            report.Issues.Add("Pas de piste son.");
        }

        if (report.IntegratedLufs is { } lufs && Math.Abs(lufs - settings.TargetLufs) > 1)
        {
            report.Warnings.Add(settings.NormalizeAudio
                ? $"Son à {lufs:0.0} LUFS, ramené à {settings.TargetLufs:0} LUFS."
                : $"Son à {lufs:0.0} LUFS (cible {settings.TargetLufs:0} LUFS).");
        }

        if (report.TruePeakDb is { } peak && peak > settings.MaxTruePeakDb)
        {
            report.Warnings.Add($"Crêtes à {peak:0.0} dBTP (maximum {settings.MaxTruePeakDb:0.0}).");
        }
    }

    /// <summary>Lit la sortie JSON de ffprobe (image, son, débit, conteneur).</summary>
    public static TechnicalReport ParseProbe(string json)
    {
        var report = new TechnicalReport();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        long audioBitrate = 0;
        if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
        {
            foreach (var stream in streams.EnumerateArray())
            {
                var type = ReadString(stream, "codec_type");
                var attachedPicture = stream.TryGetProperty("disposition", out var disposition)
                    && disposition.TryGetProperty("attached_pic", out var attached)
                    && attached.ValueKind == JsonValueKind.Number
                    && attached.GetInt32() == 1;
                if (type == "video" && report.Width == 0 && !attachedPicture)
                {
                    report.Width = ReadInt(stream, "width");
                    report.Height = ReadInt(stream, "height");
                    report.VideoCodec = ReadString(stream, "codec_name");
                    report.PixelFormat = ReadString(stream, "pix_fmt");
                    report.FrameRate = ReadString(stream, "avg_frame_rate") is { Length: > 0 } rate && !rate.StartsWith("0/", StringComparison.Ordinal)
                        ? rate
                        : ReadString(stream, "r_frame_rate");
                    report.VideoBitrateKbps = (int)(ReadLong(stream, "bit_rate") / 1000);
                    report.DurationSeconds = Math.Max(report.DurationSeconds, ReadDouble(stream, "duration"));
                }
                else if (type == "audio" && !report.HasAudio)
                {
                    report.HasAudio = true;
                    report.AudioCodec = ReadString(stream, "codec_name");
                    audioBitrate = ReadLong(stream, "bit_rate");
                }
            }
        }

        if (root.TryGetProperty("format", out var format))
        {
            var duration = ReadDouble(format, "duration");
            if (duration > 0)
            {
                report.DurationSeconds = duration;
            }

            report.Container = ReadString(format, "format_name");
            if (report.VideoBitrateKbps == 0)
            {
                var total = ReadLong(format, "bit_rate");
                if (total > 0)
                {
                    report.VideoBitrateKbps = (int)(Math.Max(0, total - audioBitrate) / 1000);
                }
            }
        }

        return report;
    }

    /// <summary>Bloc JSON imprimé par loudnorm à la fin de la sortie d'erreur de ffmpeg.</summary>
    public static LoudnessMeasure? ParseLoudnorm(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
        {
            return null;
        }

        var start = stderr.LastIndexOf('{');
        var end = stderr.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(stderr[start..(end + 1)]);
            var root = document.RootElement;
            var integrated = ReadDouble(root, "input_i", double.NaN);
            var peak = ReadDouble(root, "input_tp", double.NaN);
            var range = ReadDouble(root, "input_lra", double.NaN);
            var threshold = ReadDouble(root, "input_thresh", double.NaN);
            var offset = ReadDouble(root, "target_offset", 0);
            return double.IsFinite(integrated) && double.IsFinite(peak) && double.IsFinite(range) && double.IsFinite(threshold)
                ? new LoudnessMeasure(integrated, peak, range, threshold, double.IsFinite(offset) ? offset : 0)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<LoudnessMeasure?> MeasureAsync(string path, IngestSettings settings, CancellationToken cancellationToken)
    {
        var errors = new StringBuilder();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(MeasureTimeout);
        var code = await ExternalTool.RunAsync(
            FfmpegPath,
            ["-hide_banner", "-nostdin", "-nostats", "-i", path, "-vn", "-sn", "-dn", "-af", LoudnormFilter(settings, null) + ":print_format=json", "-f", "null", "-"],
            null,
            _ => { },
            line =>
            {
                lock (errors)
                {
                    errors.AppendLine(line);
                }
            },
            timeout.Token).ConfigureAwait(false);
        return code == 0 ? ParseLoudnorm(errors.ToString()) : null;
    }

    private static string LoudnormFilter(IngestSettings settings, LoudnessMeasure? measured)
    {
        var target = $"loudnorm=I={Number(settings.TargetLufs)}:TP={Number(settings.MaxTruePeakDb)}:LRA=11";
        return measured is null
            ? target
            : $"{target}:measured_I={Number(measured.IntegratedLufs)}:measured_TP={Number(measured.TruePeakDb)}" +
              $":measured_LRA={Number(measured.RangeLu)}:measured_thresh={Number(measured.ThresholdLufs)}" +
              $":offset={Number(measured.OffsetLu)}:linear=true";
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(@"m\:ss");

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static int ReadInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;

    private static long ReadLong(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return 0;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetInt64(),
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static double ReadDouble(JsonElement element, string property, double fallback = 0)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => fallback
        };
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nettoyage au mieux : le fichier partiel sera écrasé au prochain essai.
        }
    }
}
