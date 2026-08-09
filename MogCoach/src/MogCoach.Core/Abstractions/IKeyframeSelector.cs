using MogCoach.Core.Model;

namespace MogCoach.Core.Abstractions;

/// <summary>
/// Chooses the moments in a pull worth looking at visually and attaches the frames for each.
/// This is the bridge from cheap telemetry to expensive vision: it bounds how many images the
/// VLM ever sees.
/// </summary>
public interface IKeyframeSelector
{
    Task<IReadOnlyList<Keyframe>> SelectAsync(
        Pull pull,
        IFrameStore frames,
        CoachingMode mode,
        CancellationToken ct = default);
}
