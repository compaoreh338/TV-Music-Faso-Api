using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public interface ILanguageCatalog
{
    IReadOnlyList<SpokenLanguage> GetAll();

    SpokenLanguage Add(string label);

    void EnsureSeed();
}
