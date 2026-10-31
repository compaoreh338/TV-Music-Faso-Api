using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>Catalogue en mémoire (mode design, tests) ; reporte les suppressions sur les clips fournis.</summary>
public sealed class InMemoryGenreCatalog : IGenreCatalog
{
    private readonly List<GenreDefinition> _genres;
    private readonly IClipRepository? _clips;

    public InMemoryGenreCatalog(IClipRepository? clips = null, IEnumerable<GenreDefinition>? seed = null)
    {
        _clips = clips;
        _genres = (seed ?? GenreCatalog.Seeds()).Select(item => item.Clone()).ToList();
    }

    public IReadOnlyList<GenreDefinition> GetAll()
    {
        var counts = _clips?.GetAll().GroupBy(clip => (int)clip.Genre).ToDictionary(group => group.Key, group => group.Count()) ?? [];
        return _genres.Select(item =>
        {
            var copy = item.Clone();
            copy.ClipCount = counts.GetValueOrDefault(item.Id);
            return copy;
        }).ToList();
    }

    public GenreDefinition Add(string label)
    {
        var cleaned = GenreCatalog.ValidateLabel(label);
        if (GenreCatalog.FindActiveByLabel(_genres, cleaned) is { } existing)
        {
            throw new InvalidOperationException($"Le genre « {existing.Label} » existe déjà.");
        }

        var genre = new GenreDefinition { Id = GenreCatalog.NextId(_genres), Label = cleaned };
        _genres.Add(genre);
        return genre.Clone();
    }

    public GenreDefinition Rename(int id, string label)
    {
        var cleaned = GenreCatalog.ValidateLabel(label);
        var target = _genres.FirstOrDefault(item => item.Id == id && !item.IsArchived)
            ?? throw new InvalidOperationException("Genre introuvable ou supprimé.");
        if (GenreCatalog.FindActiveByLabel(_genres, cleaned, exceptId: id) is { } existing)
        {
            throw new InvalidOperationException($"Le genre « {existing.Label} » existe déjà.");
        }

        target.Label = cleaned;
        return target.Clone();
    }

    public int Delete(int id, int? replacementId)
    {
        var used = _clips?.GetAll().Where(clip => (int)clip.Genre == id).ToList() ?? [];
        var target = GenreCatalog.CheckDeletion(_genres, id, replacementId, used.Count);
        foreach (var clip in used)
        {
            clip.Genre = (MusicalGenre)replacementId!.Value;
            _clips!.Update(clip);
        }

        target.IsArchived = true;
        return used.Count;
    }

    public void EnsureSeed()
    {
        foreach (var seed in GenreCatalog.Seeds().Where(seed => _genres.All(item => item.Id != seed.Id)))
        {
            _genres.Add(seed);
        }
    }
}
