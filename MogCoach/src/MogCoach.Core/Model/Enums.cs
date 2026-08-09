namespace MogCoach.Core.Model;

/// <summary>The coaching lens applied to a pull. Selects which references and prompts run.</summary>
public enum CoachingMode
{
    /// <summary>Rotation / DPS optimisation: uptime, drift, opener, resource waste, DPS vs benchmark.</summary>
    Rotation,

    /// <summary>Mechanics &amp; deaths: what killed you / the party, positioning at failure moments.</summary>
    Mechanics,

    /// <summary>General awareness: movement uptime, reaction windows, situational play.</summary>
    Awareness,
}

/// <summary>FFXIV combat job. <see cref="Unknown"/> covers non-combat / unresolved.</summary>
public enum Job
{
    Unknown = 0,

    // Tanks
    Paladin, Warrior, DarkKnight, Gunbreaker,

    // Healers
    WhiteMage, Scholar, Astrologian, Sage,

    // Melee DPS
    Monk, Dragoon, Ninja, Samurai, Reaper, Viper,

    // Physical Ranged DPS
    Bard, Machinist, Dancer,

    // Magical Ranged DPS
    BlackMage, Summoner, RedMage, Pictomancer, BlueMage,
}

/// <summary>Broad role, derived from <see cref="Job"/>.</summary>
public enum Role
{
    Unknown = 0,
    Tank,
    Healer,
    MeleeDps,
    PhysicalRangedDps,
    MagicalRangedDps,
}

/// <summary>Normalised category of a single combat event parsed from the telemetry log.</summary>
public enum CombatEventType
{
    Unknown = 0,
    EncounterStart,
    EncounterEnd,
    Wipe,
    ZoneChange,
    Cast,          // ability begins casting (network 20)
    Ability,       // ability lands / snapshots (network 21/22)
    DamageTaken,
    Death,         // network 25
    StatusApply,   // buff/debuff gained (network 26/30)
    StatusRemove,
    DotTick,       // 24
    Pull,          // combat/engage
    Chat,          // 00 — kept for OCR/context cross-reference
}

/// <summary>Why a moment was chosen for visual (VLM) review.</summary>
public enum KeyframeReason
{
    Death,
    HeavyDamageTaken,
    Wipe,
    EnrageWindow,
    MechanicResolve,
    Manual,
}

/// <summary>Severity ranking for a coaching finding, ascending.</summary>
public enum Severity
{
    Info = 0,
    Minor = 1,
    Major = 2,
    Critical = 3,
}

/// <summary>What area of play a finding concerns.</summary>
public enum FindingCategory
{
    Rotation,
    Uptime,
    Resource,
    Positioning,
    Mechanic,
    Mitigation,
    Death,
    Awareness,
    Other,
}

/// <summary>Maps jobs to roles.</summary>
public static class JobExtensions
{
    public static Role Role(this Job job) => job switch
    {
        Job.Paladin or Job.Warrior or Job.DarkKnight or Job.Gunbreaker => Model.Role.Tank,
        Job.WhiteMage or Job.Scholar or Job.Astrologian or Job.Sage => Model.Role.Healer,
        Job.Monk or Job.Dragoon or Job.Ninja or Job.Samurai or Job.Reaper or Job.Viper => Model.Role.MeleeDps,
        Job.Bard or Job.Machinist or Job.Dancer => Model.Role.PhysicalRangedDps,
        Job.BlackMage or Job.Summoner or Job.RedMage or Job.Pictomancer or Job.BlueMage => Model.Role.MagicalRangedDps,
        _ => Model.Role.Unknown,
    };

    // Note: Model.Role.* is used above (fully qualified) so the enum type is unambiguous with this
    // extension method's name (both are "Role").
}
