namespace TVMusicFaso.Core.Services;

/// <summary>
/// Offre §4.3 : mapping médiathèque → arborescence MovieJaySX / playout.
/// </summary>
public sealed class PlayoutPathMapper
{
    public PlayoutPathMapper(string? libraryRoot = null, string? playoutRoot = null)
    {
        LibraryRoot = TrimSlash(libraryRoot);
        PlayoutRoot = TrimSlash(playoutRoot);
    }

    public string LibraryRoot { get; }

    public string PlayoutRoot { get; }

    public bool IsConfigured =>
        LibraryRoot.Length > 0 && PlayoutRoot.Length > 0;

    public string Map(string? filePath, string fallbackFileName)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Path.Combine(PlayoutRoot.Length > 0 ? PlayoutRoot : "", fallbackFileName);
        }

        if (!IsConfigured)
        {
            return filePath;
        }

        var source = filePath.Replace('\\', '/');
        var root = LibraryRoot.Replace('\\', '/');
        if (source.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var tail = source[root.Length..].TrimStart('/', '\\');
            return Path.Combine(PlayoutRoot, tail.Replace('/', Path.DirectorySeparatorChar));
        }

        return filePath;
    }

    private static string TrimSlash(string? path) =>
        (path ?? "").Trim().TrimEnd('/', '\\');
}
