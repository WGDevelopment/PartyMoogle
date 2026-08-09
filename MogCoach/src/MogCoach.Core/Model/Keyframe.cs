namespace MogCoach.Core.Model;

/// <summary>
/// A moment of interest selected from a pull for visual (VLM) review. Telemetry decides
/// <em>when</em> to look; the frame(s) around <see cref="Timestamp"/> are what the vision
/// model actually sees. This is the mechanism that keeps vision cost bounded.
/// </summary>
public sealed record Keyframe
{
    public required DateTimeOffset Timestamp { get; init; }

    public required KeyframeReason Reason { get; init; }

    /// <summary>Human-readable label, e.g. "Death: hit by Exaflare (12,480)".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The telemetry event that triggered selection, if any.</summary>
    public CombatEvent? Trigger { get; init; }

    /// <summary>
    /// Frames handed to the VLM — usually a small window (before/at/after the moment)
    /// so the model can see the lead-up, not just the instant of failure.
    /// </summary>
    public IReadOnlyList<Frame> Frames { get; init; } = [];
}
