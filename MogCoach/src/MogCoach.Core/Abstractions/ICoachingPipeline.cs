using MogCoach.Core.Model;

namespace MogCoach.Core.Abstractions;

/// <summary>
/// The end-to-end orchestrator: ingest → segment → (per pull) telemetry analysis →
/// keyframe select → vision analysis → fuse → report. Returns one report per pull.
/// </summary>
public interface ICoachingPipeline
{
    Task<IReadOnlyList<CoachingReport>> RunAsync(
        CoachingRequest request,
        CancellationToken ct = default);
}
