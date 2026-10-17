namespace TVMusicFaso.Core.Services;

public static class ClipMediaPath
{
    public static string? Resolve(string? filePath, string? libraryRoot = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var trimmed = filePath.Trim();
        if (File.Exists(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.IsFile
            && File.Exists(uri.LocalPath))
        {
            return uri.LocalPath;
        }

        foreach (var alias in AliasCandidates(trimmed))
        {
            if (File.Exists(alias))
            {
                return Path.GetFullPath(alias);
            }
        }

        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            return null;
        }

        var root = libraryRoot.Trim();
        foreach (var relative in RelativeCandidates(trimmed))
        {
            var combined = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(combined))
            {
                return Path.GetFullPath(combined);
            }
        }

        return null;
    }

    /// <summary>
    /// Chemin à enregistrer en base : un fichier choisi sous /Volumes/MUSIQUE sur un Mac
    /// est stocké en K:\STOCKAGE\MUSIQUE\... pour que le serveur, MovieJay et les autres
    /// postes le relisent. Hors du disque partagé, rien ne change.
    /// </summary>
    public static string ToSharedDiskPath(string localPath, string? localRoot, string? windowsRoot)
    {
        if (string.IsNullOrWhiteSpace(localPath))
        {
            return localPath;
        }

        var canonical = ToCanonicalWindowsPath(localPath);
        if (!string.Equals(canonical, localPath, StringComparison.OrdinalIgnoreCase))
        {
            return canonical;
        }

        if (string.IsNullOrWhiteSpace(localRoot)
            || string.IsNullOrWhiteSpace(windowsRoot)
            || OperatingSystem.IsWindows())
        {
            return localPath;
        }

        var path = localPath.Trim().Replace('\\', '/');
        var root = localRoot.Trim().Replace('\\', '/').TrimEnd('/') + "/";
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return localPath;
        }

        var tail = path[root.Length..].Trim('/');
        return windowsRoot.Trim().TrimEnd('/', '\\') + "\\" + tail.Replace('/', '\\');
    }

    /// <summary>
    /// Parties relatives possibles d'un chemin enregistré sur un autre poste, de la plus
    /// précise à la plus large : K:\STOCKAGE\a.mp4 et /Volumes/DIFFUSION/STOCKAGE/a.mp4
    /// donnent STOCKAGE/a.mp4, puis le seul nom de fichier en dernier recours.
    /// </summary>
    internal static IEnumerable<string> RelativeCandidates(string path)
    {
        var normalized = path.Replace('\\', '/');
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (TryStripMacVolume(normalized, out var afterVolume) && seen.Add(afterVolume))
        {
            yield return afterVolume;
        }

        if (TryStripDriveLetter(normalized, out var afterDrive) && seen.Add(afterDrive))
        {
            yield return afterDrive;
        }

        if (TryMusiqueTail(normalized, out var afterShare) && seen.Add(afterShare))
        {
            yield return afterShare;
        }

        var afterMarker = RelativeToSharedDisk(path).Replace('\\', '/').TrimStart('/');
        if (afterMarker.Length > 0 && seen.Add(afterMarker))
        {
            yield return afterMarker;
        }

        var asIs = normalized.TrimStart('/');
        if (asIs.Length > 0 && !HasDriveLetter(asIs) && seen.Add(asIs))
        {
            yield return asIs;
        }

        var fileName = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (fileName.Length > 0 && !HasDriveLetter(fileName) && seen.Add(fileName))
        {
            yield return fileName;
        }
    }

    /// <summary>
    /// Un chemin Windows K:\DIFFUSION\SPOT\clip.mp4 devient SPOT\clip.mp4
    /// pour être relu sous /Volumes/DIFFUSION sur un Mac.
    /// </summary>
    internal static string RelativeToSharedDisk(string path)
    {
        var normalized = path.Replace('\\', '/');
        const string marker = "/DIFFUSION/";
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return normalized[(index + marker.Length)..];
        }

        return path;
    }

    private static bool TryStripMacVolume(string normalized, out string tail)
    {
        const string volumes = "/Volumes/";
        tail = string.Empty;
        if (!normalized.StartsWith(volumes, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var afterVolumes = normalized[volumes.Length..];
        var slash = afterVolumes.IndexOf('/');
        if (slash < 0)
        {
            return false;
        }

        tail = afterVolumes[(slash + 1)..].Trim('/');
        return tail.Length > 0;
    }

    private static bool TryStripDriveLetter(string normalized, out string tail)
    {
        tail = string.Empty;
        if (!HasDriveLetter(normalized))
        {
            return false;
        }

        tail = normalized[2..].Trim('/');
        return tail.Length > 0;
    }

    private static bool HasDriveLetter(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    /// <summary>
    /// Le partage SMB MUSIQUE (<c>K:\STOCKAGE\MUSIQUE</c> sur le serveur,
    /// <c>/Volumes/MUSIQUE</c> sur un Mac) désigne le même dossier.
    /// </summary>
    public const string MusiqueWindowsRoot = @"K:\STOCKAGE\MUSIQUE";

    public const string MusiqueMacRoot = "/Volumes/MUSIQUE";

    /// <summary>Réécrit un chemin Mac du partage MUSIQUE vers le chemin Windows du serveur.</summary>
    public static string ToCanonicalWindowsPath(string localPath)
    {
        var normalized = localPath.Trim().Replace('\\', '/');
        if (!normalized.StartsWith(MusiqueMacRoot + "/", StringComparison.OrdinalIgnoreCase)
            || !TryMusiqueTail(normalized, out var tail))
        {
            return localPath;
        }

        return WindowsMusiquePath(tail);
    }

    /// <summary>Les deux chemins absolus du même fichier sur le partage MUSIQUE.</summary>
    public static IEnumerable<string> AliasCandidates(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (!TryMusiqueTail(normalized, out var tail))
        {
            yield break;
        }

        yield return WindowsMusiquePath(tail);
        yield return MacMusiquePath(tail);
    }

    /// <summary>
    /// Vrai si le fichier est dans le dossier configuré sur ce poste, ou dans un partage connu
    /// (MUSIQUE ou DIFFUSION).
    /// </summary>
    public static bool IsServable(string? fullPath, string? libraryRoot) =>
        IsInsideLibrary(fullPath, libraryRoot)
        || IsInsideLibrary(fullPath, MusiqueWindowsRoot)
        || IsInsideLibrary(fullPath, MusiqueMacRoot)
        || IsInsideLibrary(fullPath, @"K:\DIFFUSION")
        || IsInsideLibrary(fullPath, "/Volumes/DIFFUSION");

    private static bool TryMusiqueTail(string normalized, out string tail)
    {
        tail = string.Empty;
        foreach (var prefix in new[] { "K:/STOCKAGE/MUSIQUE/", MusiqueMacRoot + "/" })
        {
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            tail = normalized[prefix.Length..].Trim('/');
            return tail.Length > 0;
        }

        return false;
    }

    private static string WindowsMusiquePath(string tail) =>
        MusiqueWindowsRoot + "\\" + tail.Replace('/', '\\');

    private static string MacMusiquePath(string tail) =>
        MusiqueMacRoot + "/" + tail;

    /// <summary>Vrai seulement si le fichier résolu est dans le dossier médiathèque.</summary>
    public static bool IsInsideLibrary(string? fullPath, string? libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(libraryRoot))
        {
            return false;
        }

        var root = Path.GetFullPath(libraryRoot.Trim());
        var full = Path.GetFullPath(fullPath);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, comparison);
    }

    public static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".webm" => "video/webm",
            ".avi" => "video/x-msvideo",
            ".mxf" => "application/mxf",
            ".mpg" or ".mpeg" => "video/mpeg",
            _ => "application/octet-stream"
        };
}
