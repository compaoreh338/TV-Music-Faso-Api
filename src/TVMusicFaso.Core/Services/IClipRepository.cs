using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public interface IClipRepository
{
    IReadOnlyList<Clip> GetAll();

    Clip? GetById(Guid id);

    void Add(Clip clip);

    void Update(Clip clip);

    void Remove(Guid id);

    IReadOnlyList<Clip> Search(string? query, MusicalGenre? genre, ClipLanguage? language, bool? burkinabeOnly);
}
