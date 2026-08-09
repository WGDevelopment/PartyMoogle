using MogCoach.Core.Model;

namespace MogCoach.Core.Abstractions;

/// <summary>
/// Time-addressed access to captured screen frames (Screenpipe). The keyframe selector uses
/// this to pull the frame(s) around a moment of interest and materialise their image bytes.
/// </summary>
public interface IFrameStore
{
    /// <summary>The frame nearest <paramref name="timestamp"/>, with image bytes materialised.</summary>
    Task<Frame?> GetNearestAsync(DateTimeOffset timestamp, CancellationToken ct = default);

    /// <summary>
    /// Frames within [timestamp - before, timestamp + after], oldest first, with image bytes
    /// materialised. Used to give the VLM a short lead-up window around a failure.
    /// </summary>
    Task<IReadOnlyList<Frame>> GetWindowAsync(
        DateTimeOffset timestamp,
        TimeSpan before,
        TimeSpan after,
        CancellationToken ct = default);
}
