using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public static class ClipQuery
{
    public static IReadOnlyList<Clip> Filter(
        IEnumerable<Clip> clips,
        string? query,
        MusicalGenre? genre,
        ClipLanguage? language,
        bool? burkinabeOnly)
    {
        IEnumerable<Clip> result = clips;

        if (!string.IsNullOrWhiteSpace(query))
        {
            result = result.Where(clip =>
                clip.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || clip.Artist.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        if (genre.HasValue)
        {
            result = result.Where(clip => clip.Genre == genre.Value);
        }

        if (language.HasValue)
        {
            result = result.Where(clip => clip.Language == language.Value);
        }

        if (burkinabeOnly == true)
        {
            result = result.Where(clip => clip.IsBurkinabe);
        }

        return result
            .OrderBy(clip => clip.Artist)
            .ThenBy(clip => clip.Title)
            .ToList();
    }
}
