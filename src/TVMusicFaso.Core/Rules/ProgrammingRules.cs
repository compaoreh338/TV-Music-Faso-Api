namespace TVMusicFaso.Core.Rules;

/// <summary>
/// Paramètres métier du moteur de programmation (offre technique §3.2).
/// </summary>
public sealed class ProgrammingRules
{
    public const double SovereigntyTargetPercent = 90;

    public int DefaultDailyPlayCap { get; init; } = 2;

    public int PremiumDailyPlayCap { get; init; } = 4;

    public bool EnforceCulturalDiversity { get; init; } = true;

    /// <summary>
    /// Un Hit n’est jamais choisi à la place d’un clip encore moins diffusé.
    /// Le Premium n’augmente que le plafond quotidien (offre §3.2).
    /// </summary>
    public bool PreferUnderPlayed { get; init; } = true;

    public bool PreferBurkinabeUntilTarget { get; init; } = true;

    /// <summary>
    /// Null = durée réelle de la tranche (offre). Une valeur courte sert uniquement aux tests / prévisualisation.
    /// </summary>
    public TimeSpan? SlotFillLimit { get; init; }
}
