namespace MogCoach.Core.Model;

/// <summary>Computed, objective metrics for a pull. Populated by the telemetry analyzer.</summary>
public sealed record PullMetrics
{
    /// <summary>GCD uptime as a fraction (0..1): time on-GCD vs total pull time.</summary>
    public double? GcdUptime { get; init; }

    /// <summary>Estimated actual rDPS for the pull.</summary>
    public double? ActualDps { get; init; }

    /// <summary>Benchmark median rDPS for context, if a benchmark was available.</summary>
    public double? BenchmarkDps { get; init; }

    /// <summary>Rough FFLogs-style percentile estimate, if computable.</summary>
    public int? EstimatedPercentile { get; init; }

    /// <summary>Total seconds of clipped GCD (drift) detected.</summary>
    public double? GcdDriftSeconds { get; init; }

    public int DeathCount { get; init; }
}

/// <summary>
/// The deliverable for a single pull: metrics + findings + a short synthesised summary.
/// A session report is a collection of these.
/// </summary>
public sealed record CoachingReport
{
    public required string PullId { get; init; }
    public required CoachingMode Mode { get; init; }
    public required Job Job { get; init; }

    public string? EncounterName { get; init; }
    public TimeSpan PullDuration { get; init; }
    public bool Cleared { get; init; }

    public PullMetrics Metrics { get; init; } = new();

    /// <summary>Findings, ordered most-severe first.</summary>
    public IReadOnlyList<Finding> Findings { get; init; } = [];

    /// <summary>2–4 sentence natural-language wrap-up.</summary>
    public string Summary { get; init; } = string.Empty;
}
