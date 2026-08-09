namespace MogCoach.Core.Model;

/// <summary>
/// A trusted rotation reference for a job (optionally encounter-specific), loaded from
/// curated sources (e.g. The Balance). Used by the rotation lens to compare actual play
/// against intended priority / opener.
/// </summary>
public sealed record RotationReference
{
    public required Job Job { get; init; }

    /// <summary>Optional encounter this reference is tuned for; null = generic.</summary>
    public string? Encounter { get; init; }

    /// <summary>Short provenance string, e.g. "The Balance — Endwalker WAR 6.5".</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Ordered opener, ability names.</summary>
    public IReadOnlyList<string> Opener { get; init; } = [];

    /// <summary>Steady-state priority list, highest first.</summary>
    public IReadOnlyList<string> Priority { get; init; } = [];

    /// <summary>Free-form coaching notes / rules (e.g. "hold gauge for burst windows").</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Buffs that should be kept up, with a target uptime — drives the resource pass.</summary>
    public IReadOnlyList<MaintainedBuff> MaintainedBuffs { get; init; } = [];
}

/// <summary>A self-buff the job should keep up (e.g. Surging Tempest), with its uptime target.</summary>
public sealed record MaintainedBuff
{
    /// <summary>Status id (hex) as it appears in the capture.</summary>
    public required string StatusId { get; init; }
    public string? Name { get; init; }
    /// <summary>Target uptime fraction (0..1). Below this flags a finding.</summary>
    public double TargetUptime { get; init; } = 0.95;
}

/// <summary>
/// Performance benchmark for an encounter+job from FFLogs. The public API exposes a named
/// character's parses (not a generic percentile→dps histogram), so this carries the character's
/// best rDPS and where it and their median sit — e.g. "your best is 8,900 rDPS (p72); median p55".
/// </summary>
public sealed record FightBenchmark
{
    public required string Encounter { get; init; }
    public required Job Job { get; init; }
    public string Source { get; init; } = "FFLogs";

    /// <summary>The character's best rDPS on this fight.</summary>
    public double? BestRdps { get; init; }

    /// <summary>Percentile (0–100) of that best parse.</summary>
    public double? BestPercentile { get; init; }

    /// <summary>The character's median parse percentile (0–100) across their kills.</summary>
    public double? MedianPercentile { get; init; }

    public int? Kills { get; init; }

    /// <summary>A reference rDPS number for reports (the character's best).</summary>
    public double? ReferenceRdps => BestRdps;
}
