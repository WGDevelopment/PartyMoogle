namespace MogCoach.Core.Model;

/// <summary>
/// One attempt at an encounter — the unit of coaching. Produced by the segmenter from a
/// continuous event stream. A prog session is a sequence of pulls at the same encounter.
/// </summary>
public sealed record Pull
{
    /// <summary>Stable id within a capture (e.g. "pull-03").</summary>
    public required string Id { get; init; }

    public required string ZoneName { get; init; }

    /// <summary>Encounter / boss name as reported by the log, when resolvable.</summary>
    public string? EncounterName { get; init; }

    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset EndedAt { get; init; }

    public TimeSpan Duration => EndedAt - StartedAt;

    /// <summary>The local player's job for this pull.</summary>
    public Job Job { get; init; } = Job.Unknown;

    /// <summary>True if the encounter was cleared; false if it ended in a wipe.</summary>
    public bool Cleared { get; init; }

    /// <summary>Fraction of boss HP remaining at wipe (0..1); null if cleared or unknown.</summary>
    public double? WipeAtBossHpFraction { get; init; }

    /// <summary>All events belonging to this pull, in chronological order.</summary>
    public IReadOnlyList<CombatEvent> Events { get; init; } = [];
}
