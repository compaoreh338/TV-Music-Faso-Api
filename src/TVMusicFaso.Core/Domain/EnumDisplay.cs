namespace TVMusicFaso.Core.Domain;

public static class EnumDisplay
{
    public static string ToDisplayName(this Enum value) => value switch
    {
        MusicalGenre.CoupeDecale => "Coupé-décalé",
        MusicalGenre.Slam => "Slam",
        MusicalGenre.Traditionnel => "Traditionnel",
        MusicalGenre.AfroPop => "Afro-pop",
        MusicalGenre.Gospel => "Gospel",
        MusicalGenre.Urbain => "Urbain",
        MusicalGenre.MusiqueMusulmane => "Musique musulmane",
        MusicalGenre.Autre => "Autre",

        ClipLanguage.Moore => "Mooré",
        ClipLanguage.Dioula => "Dioula",
        ClipLanguage.Fulfulde => "Fulfulde",
        ClipLanguage.Francais => "Français",
        ClipLanguage.Autre => "Autre",

        ClipTheme.Patriotisme => "Patriotisme",
        ClipTheme.Amour => "Amour",
        ClipTheme.Travail => "Travail",
        ClipTheme.Fete => "Fête",
        ClipTheme.Paix => "Paix",
        ClipTheme.Securite => "Sécurité",
        ClipTheme.Sante => "Santé",
        ClipTheme.Agriculture => "Agriculture",

        Audience.Jeunesse => "Jeunesse",
        Audience.Adultes => "Adultes",
        Audience.Famille => "Famille",

        VideoQuality.Hd => "HD",
        VideoQuality.Uhd4K => "4K",

        TimeSlot.CinqHeures => "05h00",
        TimeSlot.SixHeures => "06h00",
        TimeSlot.NeufHeures => "09h00",
        TimeSlot.Midi => "12h00",
        TimeSlot.QuatorzeHeures => "14h00",
        TimeSlot.DixSeptHeures => "17h00",
        TimeSlot.VingtDeuxHeures => "22h00",
        TimeSlot.UneHeure => "01h00",

        SlotMood.Douce => "Douce",
        SlotMood.Energique => "Énergique",
        SlotMood.Melancolique => "Mélancolique",

        UserRole.Programmateur => "Programmateur",
        UserRole.Technicien => "Technicien",
        UserRole.Direction => "Direction",

        _ => value.ToString()
    };
}
