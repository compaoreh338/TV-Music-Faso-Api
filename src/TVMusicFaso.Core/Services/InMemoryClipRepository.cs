using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class InMemoryClipRepository : IClipRepository
{
    private readonly List<Clip> _clips;

    public InMemoryClipRepository(IEnumerable<Clip> seed)
    {
        _clips = seed.ToList();
    }

    public IReadOnlyList<Clip> GetAll() => _clips.ToList();

    public Clip? GetById(Guid id) => _clips.FirstOrDefault(clip => clip.Id == id);

    public void Add(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        _clips.Add(clip);
    }

    public void Update(Clip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);
        var index = _clips.FindIndex(existing => existing.Id == clip.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"Clip introuvable : {clip.Id}");
        }

        _clips[index] = clip;
    }

    public void Remove(Guid id)
    {
        _clips.RemoveAll(clip => clip.Id == id);
    }

    public void SetValidation(Guid id, ClipValidationStatus status, string? note = null) =>
        ClipValidation.Apply(this, id, status, note);

    public IReadOnlyList<Clip> Search(string? query, MusicalGenre? genre, ClipLanguage? language, bool? burkinabeOnly) =>
        ClipQuery.Filter(_clips, query, genre, language, burkinabeOnly);
}
