namespace MogCoach.Core.Model;

/// <summary>
/// One actionable coaching observation. Findings come from either the telemetry analyzer
/// or the vision analyzer and are fused into a report. Keep <see cref="Recommendation"/>
/// concrete and singular — "use your 2-minute buffs before the tankbuster", not "play better".
/// </summary>
public sealed record Finding
{
    public required Severity Severity { get; init; }
    public required FindingCategory Category { get; init; }

    /// <summary>One-line statement of the issue.</summary>
    public required string Title { get; init; }

    /// <summary>Explanation with evidence.</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>Single concrete fix.</summary>
    public string Recommendation { get; init; } = string.Empty;

    /// <summary>When in the pull this occurred, if point-in-time.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>Seconds into the pull, for display.</summary>
    public double? PullOffsetSeconds { get; init; }

    /// <summary>"telemetry" or "vision" — provenance for trust / debugging.</summary>
    public string Origin { get; init; } = "telemetry";

    /// <summary>Optional path to a saved evidence frame (for the death/positioning findings).</summary>
    public string? EvidenceImagePath { get; init; }
}
