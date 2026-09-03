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

        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            return null;
        }

        var relative = trimmed.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.Combine(libraryRoot.Trim(), relative);
        if (File.Exists(combined))
        {
            return Path.GetFullPath(combined);
        }

        var byName = Path.Combine(libraryRoot.Trim(), Path.GetFileName(trimmed));
        return File.Exists(byName) ? Path.GetFullPath(byName) : null;
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
