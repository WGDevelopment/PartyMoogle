namespace MogCoach.Core.Model;

/// <summary>
/// Inputs for one coaching run: which capture to analyse, under which lens, and the
/// player context needed to pick references. A capture bundles a telemetry log with the
/// screen-frame source (see the ingest layer for how these are resolved).
/// </summary>
public sealed record CoachingRequest
{
    /// <summary>Path to the captured telemetry (IINACT NDJSON) for the session.</summary>
    public required string CapturePath { get; init; }

    public CoachingMode Mode { get; init; } = CoachingMode.Rotation;

    /// <summary>Player job. If Unknown, the analyzer attempts to infer it from the log.</summary>
    public Job Job { get; init; } = Job.Unknown;

    /// <summary>Local player name, used to attribute self events when the log lacks the flag.</summary>
    public string? PlayerName { get; init; }

    /// <summary>Restrict to a single pull id; null = analyse every pull in the capture.</summary>
    public string? OnlyPullId { get; init; }

    /// <summary>Enable the vision pass. When false, only telemetry findings are produced.</summary>
    public bool EnableVision { get; init; } = true;
}
