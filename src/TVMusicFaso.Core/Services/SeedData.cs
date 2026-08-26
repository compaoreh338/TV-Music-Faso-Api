using TVMusicFaso.Core.Domain;

namespace TVMusicFaso.Core.Services;

/// <summary>
/// Médiathèque de démonstration pour valider les règles sans données réelles Filinfo.
/// </summary>
public static class SeedData
{
    public static IReadOnlyList<Clip> CreateLibrary() =>
    [
        Clip("Yamba", "Awa Méda", 2023, MusicalGenre.AfroPop, ClipLanguage.Moore, ClipTheme.Amour, Audience.Jeunesse, 4.6, true, true, 214),
        Clip("Faso Djamana", "Kady Traoré", 2022, MusicalGenre.Traditionnel, ClipLanguage.Dioula, ClipTheme.Patriotisme, Audience.Famille, 4.8, true, true, 248),
        Clip("Wend Naba", "Issa Sawadogo", 2021, MusicalGenre.Gospel, ClipLanguage.Moore, ClipTheme.Paix, Audience.Famille, 4.2, true, false, 231),
        Clip("Bolo", "Moussa Konaté", 2024, MusicalGenre.CoupeDecale, ClipLanguage.Dioula, ClipTheme.Fete, Audience.Jeunesse, 4.4, true, true, 198),
        Clip("Pulaaku", "Aïcha Diallo", 2023, MusicalGenre.Traditionnel, ClipLanguage.Fulfulde, ClipTheme.Agriculture, Audience.Adultes, 4.1, true, false, 256),
        Clip("Ouaga Nights", "DJ Sahel", 2024, MusicalGenre.Urbain, ClipLanguage.Francais, ClipTheme.Fete, Audience.Jeunesse, 3.9, true, false, 187),
        Clip("Salam Alaykum", "Imam Souleymane", 2020, MusicalGenre.MusiqueMusulmane, ClipLanguage.Dioula, ClipTheme.Paix, Audience.Famille, 4.5, true, false, 272),
        Clip("Terre Rouge", "Nafissatou", 2022, MusicalGenre.Slam, ClipLanguage.Francais, ClipTheme.Patriotisme, Audience.Adultes, 4.7, true, true, 203),
        Clip("Sida Tê", "Collectif Santé Faso", 2019, MusicalGenre.AfroPop, ClipLanguage.Moore, ClipTheme.Sante, Audience.Famille, 3.6, true, false, 225),
        Clip("Barka", "Les Frères de Bobo", 2021, MusicalGenre.Traditionnel, ClipLanguage.Dioula, ClipTheme.Travail, Audience.Adultes, 3.8, true, false, 240),
        Clip("Zoodo", "Aminata Kaboré", 2024, MusicalGenre.AfroPop, ClipLanguage.Moore, ClipTheme.Amour, Audience.Jeunesse, 4.3, true, false, 209),
        Clip("La Paix d'abord", "Voice of Faso", 2023, MusicalGenre.Slam, ClipLanguage.Francais, ClipTheme.Securite, Audience.Adultes, 4.0, true, false, 218),
        Clip("Allahou Akbar Chant", "Groupe Al-Falah", 2022, MusicalGenre.MusiqueMusulmane, ClipLanguage.Moore, ClipTheme.Paix, Audience.Famille, 4.4, true, false, 265),
        Clip("Louange", "Chorale Bethesda", 2021, MusicalGenre.Gospel, ClipLanguage.Francais, ClipTheme.Paix, Audience.Famille, 4.1, true, false, 238),
        Clip("Tô et Bissap", "Mama Awa", 2020, MusicalGenre.Traditionnel, ClipLanguage.Moore, ClipTheme.Agriculture, Audience.Famille, 3.7, true, false, 221),
        Clip("Gardez le cap", "Young Ouaga", 2024, MusicalGenre.Urbain, ClipLanguage.Francais, ClipTheme.Travail, Audience.Jeunesse, 3.5, true, false, 176),
        Clip("Djamana", "Fatoumata Dembélé", 2023, MusicalGenre.AfroPop, ClipLanguage.Dioula, ClipTheme.Patriotisme, Audience.Famille, 4.5, true, true, 233),
        Clip("Nagnala", "Hamidou Barry", 2022, MusicalGenre.Slam, ClipLanguage.Fulfulde, ClipTheme.Paix, Audience.Adultes, 3.9, true, false, 244),
        Clip("Balani Show", "Crew 226", 2024, MusicalGenre.CoupeDecale, ClipLanguage.Francais, ClipTheme.Fete, Audience.Jeunesse, 4.2, true, false, 192),
        Clip("Mogho Naba", "Les Griots de Wogodogo", 2018, MusicalGenre.Traditionnel, ClipLanguage.Moore, ClipTheme.Patriotisme, Audience.Famille, 4.9, true, true, 281),
        Clip("Afro Sunrise", "Lagos Beats", 2023, MusicalGenre.AfroPop, ClipLanguage.Autre, ClipTheme.Fete, Audience.Jeunesse, 4.0, false, true, 205, "Nigeria", "Lagos"),
        Clip("Desert Wind", "Sahara Collective", 2022, MusicalGenre.Traditionnel, ClipLanguage.Autre, ClipTheme.Paix, Audience.Adultes, 3.8, false, false, 249, "Mali", "Bamako"),
        Clip("Midnight City", "Paris Noir", 2024, MusicalGenre.Urbain, ClipLanguage.Francais, ClipTheme.Amour, Audience.Jeunesse, 3.4, false, false, 194, "France", "Paris"),
        Clip("Halleluiah Live", "Gospel Wave", 2021, MusicalGenre.Gospel, ClipLanguage.Francais, ClipTheme.Paix, Audience.Famille, 3.6, false, false, 227, "Côte d'Ivoire", "Abidjan")
    ];

    private static Clip Clip(
        string title,
        string artist,
        int year,
        MusicalGenre genre,
        ClipLanguage language,
        ClipTheme theme,
        Audience audience,
        double score,
        bool burkinabe,
        bool premium,
        int seconds,
        string origin = "Burkina Faso",
        string filming = "Ouagadougou") =>
        new()
        {
            Title = title,
            Artist = artist,
            Year = year,
            Genre = genre,
            Language = language,
            Theme = theme,
            Audience = audience,
            CommitteeRating = (decimal)score,
            PopularityScore = (decimal)Math.Max(1, score - 0.2),
            SocialScore = (decimal)Math.Min(5, score + 0.1),
            ImpactScore = (decimal)score,
            IsBurkinabe = burkinabe,
            IsPremium = premium || score >= 4.5,
            Duration = TimeSpan.FromSeconds(seconds),
            OriginPlace = origin,
            FilmingLocation = filming,
            Quality = score >= 4.5 ? VideoQuality.Uhd4K : VideoQuality.Hd,
            FilePath = $"/mediatheque/{artist}/{title}.mp4".ToLowerInvariant()
        };
}
