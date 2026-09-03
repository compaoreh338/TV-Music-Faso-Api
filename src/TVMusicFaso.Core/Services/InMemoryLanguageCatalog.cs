using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public sealed class InMemoryLanguageCatalog : ILanguageCatalog
{
    private readonly List<SpokenLanguage> _languages = [];

    public InMemoryLanguageCatalog(bool seed = true)
    {
        if (seed)
        {
            EnsureSeed();
        }
    }

    public IReadOnlyList<SpokenLanguage> GetAll() =>
        _languages.OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase).ToList();

    public SpokenLanguage Add(string label)
    {
        label = label.Trim();
        if (label.Length < 2)
        {
            throw new ArgumentException("Le nom de la langue doit contenir au moins 2 caractères.", nameof(label));
        }

        var existing = _languages.FirstOrDefault(item =>
            LanguageCatalog.Normalize(item.Label) == LanguageCatalog.Normalize(label));
        if (existing is not null)
        {
            return existing;
        }

        var language = new SpokenLanguage
        {
            Code = UniqueCode(LanguageCatalog.Slug(label)),
            Label = label,
            EnumValue = LanguageCatalog.MapEnum(label),
            IsSeeded = false
        };
        _languages.Add(language);
        return language;
    }

    public void EnsureSeed()
    {
        foreach (var seed in LanguageCatalog.BurkinaAndFrench)
        {
            if (_languages.Any(item => item.Code == seed.Code))
            {
                continue;
            }

            _languages.Add(new SpokenLanguage
            {
                Id = seed.Id == Guid.Empty ? Guid.NewGuid() : seed.Id,
                Code = seed.Code,
                Label = seed.Label,
                EnumValue = seed.EnumValue,
                IsSeeded = true
            });
        }
    }

    private string UniqueCode(string slug)
    {
        var code = slug;
        var index = 2;
        while (_languages.Any(item => item.Code == code))
        {
            code = $"{slug}-{index++}";
        }

        return code;
    }
}
