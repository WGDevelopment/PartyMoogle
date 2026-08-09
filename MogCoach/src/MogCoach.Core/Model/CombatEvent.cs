namespace MogCoach.Core.Model;

/// <summary>
/// One normalised combat-log event, parsed from an IINACT / ACT network log line.
/// Only the fields relevant to coaching are lifted; <see cref="Raw"/> preserves the
/// original line so downstream stages can re-parse anything not modelled here.
/// </summary>
public sealed record CombatEvent
{
    /// <summary>Absolute wall-clock time of the event. This is the join key to screen frames.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    public required CombatEventType Type { get; init; }

    /// <summary>Actor that caused the event (player, pet, or boss). Hex game object id as string.</summary>
    public string? SourceId { get; init; }
    public string? SourceName { get; init; }

    public string? TargetId { get; init; }
    public string? TargetName { get; init; }

    /// <summary>Ability / status id (hex) when applicable.</summary>
    public string? AbilityId { get; init; }
    public string? AbilityName { get; init; }

    /// <summary>Damage / heal amount when applicable.</summary>
    public long? Amount { get; init; }

    /// <summary>True when this event concerns the local player (self).</summary>
    public bool IsSelf { get; init; }

    /// <summary>Original log line, verbatim.</summary>
    public string Raw { get; init; } = string.Empty;
}
