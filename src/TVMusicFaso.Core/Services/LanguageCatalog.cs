using System.Globalization;
using System.Text;
using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

public static class LanguageCatalog
{
    public static IReadOnlyList<SpokenLanguage> BurkinaAndFrench { get; } =
    [
        Entry("fr", "Français", ClipLanguage.Francais),
        Entry("mos", "Mooré", ClipLanguage.Moore),
        Entry("dyu", "Dioula", ClipLanguage.Dioula),
        Entry("fuf", "Fulfulde", ClipLanguage.Fulfulde),
        Entry("bib", "Bissa", ClipLanguage.Bissa),
        Entry("gux", "Gourmanchéma", ClipLanguage.Gourmanche),
        Entry("bqm", "Bwamu", ClipLanguage.Bwamu),
        Entry("dgi", "Dagara", ClipLanguage.Dagara),
        Entry("lob", "Lobi", ClipLanguage.Lobi),
        Entry("lee", "Lyélé", ClipLanguage.Lyele),
        Entry("nnw", "Nuni", ClipLanguage.Nuni),
        Entry("sef", "Sénoufo", ClipLanguage.Senoufo),
        Entry("sbo", "San", ClipLanguage.San),
        Entry("xsm", "Kasséna", ClipLanguage.Kassena),
        Entry("bwq", "Bobo", ClipLanguage.Bobo),
        Entry("rkm", "Marka", ClipLanguage.Marka),
        Entry("taq", "Tamasheq", ClipLanguage.Tamasheq),
        Entry("bfo", "Birifor", ClipLanguage.Birifor),
        Entry("cke", "Cerma", ClipLanguage.Cerma),
        Entry("kfz", "Koromfé", ClipLanguage.Koromfe),
        Entry("sif", "Siamou", ClipLanguage.Siamou),
        Entry("tuz", "Turka", ClipLanguage.Turka),
        Entry("kst", "Winye", ClipLanguage.Winye),
        Entry("dos", "Dogosé", ClipLanguage.Dogose),
        Entry("gur", "Ninkaré", ClipLanguage.Ninkare),
        Entry("hau", "Haoussa", ClipLanguage.Hausa)
    ];

    public static ClipLanguage MapEnum(string? labelOrCode)
    {
        if (string.IsNullOrWhiteSpace(labelOrCode))
        {
            return ClipLanguage.Moore;
        }

        var key = Normalize(labelOrCode);
        var match = BurkinaAndFrench.FirstOrDefault(item =>
            Normalize(item.Code) == key || Normalize(item.Label) == key);
        if (match is not null)
        {
            return match.EnumValue;
        }

        return Enum.TryParse<ClipLanguage>(labelOrCode, true, out var parsed) ? parsed : ClipLanguage.Autre;
    }

    public static string LabelFor(ClipLanguage language)
    {
        var match = BurkinaAndFrench.FirstOrDefault(item => item.EnumValue == language);
        return match?.Label ?? language.ToDisplayName();
    }

    public static SpokenLanguage? FindByLabel(IEnumerable<SpokenLanguage> languages, string? label)
    {
        var key = Normalize(label ?? string.Empty);
        return languages.FirstOrDefault(item => Normalize(item.Label) == key);
    }

    public static string Slug(string label)
    {
        var normalized = label.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var character in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "langue" : slug;
    }

    public static string Normalize(string value) =>
        Slug(value).Replace("-", "", StringComparison.Ordinal);

    private static SpokenLanguage Entry(string code, string label, ClipLanguage mapped) => new()
    {
        Code = code,
        Label = label,
        EnumValue = mapped,
        IsSeeded = true
    };
}
